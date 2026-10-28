using System.Text.RegularExpressions;
using Lyric5.Compiler;
using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// What the emitted C calls a program's types and globals: by the module and the name they have
/// in Lyric, not by a number in the type table, which holds every type of the standard library —
/// a new type there would rename every type of every program, and with them every golden.
/// </summary>
[Collection("console")]
public class CNameTests
{
    private static string Emit(string source)
    {
        var result = TestCompiler.Lower("names.lyr", source);
        var rendered = new StringWriter();
        result.Diagnostics.RenderText(rendered);
        Assert.True(result.Ok && result.Ir is not null, rendered.ToString());
        return CEmitter.Join(CEmitter.Emit(result.Ir!, result.Sources, TestCompiler.Stdlib));
    }

    private const string Point = "struct Point { x: int, y: int }\n";

    private static string Main(string sum) =>
        $"fn main(): int {{\n    let p = Point {{ x = 1, y = 2 }};\n    return {sum};\n}}\n";

    /// <summary>A type declared before another one took a number below it and moved the other's
    /// name: <c>lyr_ty0_Point</c> became <c>lyr_ty1_Point</c>.</summary>
    [Fact]
    public void A_type_s_name_does_not_move_with_a_type_declared_before_it()
    {
        var alone = Emit(Point + Main("p.x + p.y"));
        var behind = Emit("struct Earlier { n: int }\nfn early(): int { let e = Earlier { n = 3 }; return e.n; }\n"
            + Point + Main("p.x + p.y + early()"));

        Assert.Contains("struct lyr_ty_app_main_Point {", alone);
        Assert.Contains("struct lyr_ty_app_main_Point {", behind);
        Assert.Contains("struct lyr_ty_app_main_Earlier {", behind);
    }

    /// <summary>
    /// A field and a local are named in C as the program names them, so that every debugger shows
    /// Lyric's names: <c>x</c>, not <c>f_x</c>; <c>total</c>, not <c>l3_total</c>. A name C or its
    /// headers keep (<c>errno</c>, <c>size_t</c>, a macro's capitals, <c>fmod</c>), one in a form the
    /// emitter keeps for its own (<c>t3</c>, <c>f_x</c>), <c>header</c> beside a class's fields, and
    /// two locals of one name in a function stay mangled — and the program with all of them builds
    /// and runs.
    /// </summary>
    [Fact]
    public void Fields_and_locals_carry_their_Lyric_names_where_C_can_take_them()
    {
        const string program = """
            struct Point { x: int, y: int }
            struct Odd { errno: int, size_t: int, MAX: int, f_x: int, fmod: int }
            class Box { header: int, value: int }
            fn main(): int {
                let p = Point { x = 1, y = 2 };
                let o = Odd { errno = 3, size_t = 4, MAX = 5, f_x = 6, fmod = 7 };
                let b = Box { header = 8, value = 9 };
                let errno = p.x;
                let t3 = p.y;
                var total = 0;
                if (true) { let s = o.errno; total = total + s; }
                if (true) { let s = o.size_t; total = total + s; }
                return errno + t3 + total + o.MAX + o.f_x + o.fmod + b.header + b.value;
            }
            """;
        // the emitter writes the platform's line ends; the C is read here with '\n'
        var c = Emit(program).Replace("\r\n", "\n");

        Assert.Contains("    int64_t x;\n    int64_t y;\n", c);
        Assert.Contains("    int64_t f_errno;\n    int64_t f_size_t;\n    int64_t f_MAX;\n    int64_t f_f_x;\n    int64_t f_fmod;\n", c);
        Assert.Contains("    LyrObj header;\n    int64_t f_header;\n    int64_t value;\n", c);
        Assert.Matches(@"\n    lyr_ty_app_main_Point p = ", c);
        Assert.Matches(@"\n    int64_t total = 0;", c);
        Assert.Matches(@"\n    int64_t l\d+_errno = 0;", c);
        Assert.Matches(@"\n    int64_t l\d+_t3 = 0;", c);
        Assert.Equal(2, Regex.Matches(c, @"\n    int64_t l\d+_s = 0;").Count);

        var root = Package(("lyric.toml", AppManifest), ("src/main.lyr", program));
        var (exit, _, error) = Run("run", "-C", root);
        Assert.True(exit == 45, $"{exit}: {error}");
    }

    /// <summary>The environments of two lambdas in one function are both named after the function
    /// in the IR; in C they are two structs, the second with a 2 behind its name.</summary>
    [Fact]
    public void Two_lambdas_of_one_function_have_two_environments()
    {
        var c = Emit("""
            fn main(): int {
                var a = 1;
                var b = 2;
                let f = () => { a = a + 1; return a; };
                let g = () => { b = b + 2; return b; };
                return f() + g();
            }
            """);

        var environments = Regex.Matches(c, @"^struct (lyr_ty_0_env_app_main_main_\w+) \{", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value).Distinct().ToArray();
        Assert.Equal(2, environments.Length);
        Assert.EndsWith("_2", environments[1]);
    }

    /// <summary>A variant is named under its enum, a tuple by what it holds.</summary>
    [Fact]
    public void A_variant_is_its_enum_s_and_a_tuple_is_what_it_holds()
    {
        var c = Emit("""
            enum Shape { Dot, Circle(int) }
            fn pair(): (int, string) { return (1, "a"); }
            fn main(): int {
                let s = Shape.Circle(2);
                let (n, _) = pair();
                return match (s) { Shape.Circle(r) => r + n, Shape.Dot => 0 };
            }
            """);

        Assert.Contains("struct lyr_ty_14_app_main_Shape_Circle {", c);
        Assert.Contains("struct lyr_ty_0tup_i64_str {", c);
    }

    /// <summary>An instance writes a declared type among its arguments with the module that
    /// declares it — a function's instance, a type's, and a type's inside another's; a
    /// built-in type has no module to write. A type's own entry carries its module beside its
    /// name, once.</summary>
    [Fact]
    public void An_instance_names_its_arguments_by_their_full_path()
    {
        var result = TestCompiler.Lower("names.lyr", """
            struct Cat { lives: int }
            struct Box<T> { held: T }
            fn first<T>(xs: T[]): T { return xs[0]; }

            fn main(): int {
                let cats = [Cat { lives = 9 }];
                let boxes = [Box<Cat> { held = cats[0] }];
                return first(cats).lives - first(boxes).held.lives + first([0]);
            }
            """);
        var rendered = new StringWriter();
        result.Diagnostics.RenderText(rendered);
        Assert.True(result.Ok && result.Ir is not null, rendered.ToString());

        var functions = result.Ir!.Functions.Select(f => f.Name).ToArray();
        Assert.Contains("app.main.first<app.main.Cat>", functions);
        Assert.Contains("app.main.first<app.main.Box<app.main.Cat>>", functions);
        Assert.Contains("app.main.first<int>", functions);
        Assert.Contains(result.Ir.Types, t => t.Name == "Box<app.main.Cat>" && t.Module == "app.main");
        Assert.DoesNotContain(result.Ir.Types, t => t.Name.StartsWith("app.main.", StringComparison.Ordinal));
    }

    /// <summary>The name of an instance is the same whatever else the program declares. It was
    /// <c>first&lt;Cat&gt;</c> until another module declared a <c>Cat</c> too, and then
    /// <c>first&lt;app.a.Cat&gt;</c>: a type added in one module renamed the frames, the cache
    /// units and the C names of another's.</summary>
    [Fact]
    public void A_second_type_of_the_name_renames_no_instance()
    {
        const string a = "pub struct Cat { pub lives: int }\npub fn first<T>(xs: T[]): T { return xs[0]; }\n"
            + "pub fn nine(): int { return first([Cat { lives = 9 }]).lives; }\n";

        string Units(string main, params (string Path, string Text)[] more)
        {
            var dir = Package([("lyric.toml", AppManifest), ("src/a.lyr", a), ("src/main.lyr", main), .. more]);
            var (exit, c, error) = Run("build", "-C", dir, "--emit", "c");
            Assert.True(exit == 0, error);
            return string.Join("\n", Regex.Matches(c, @"==== unit: (app\.a\.first<.*>) ====").Select(m => m.Groups[1].Value));
        }

        var alone = Units("import app.a;\n\nfn main(): int { return a.nine() - 9; }\n");
        var beside = Units("import app.a;\nimport app.b;\n\n"
                + "fn main(): int { let other = b.Cat { claws = 1 }; return a.nine() - 9 + other.claws - 1; }\n",
            ("src/b.lyr", "pub struct Cat { pub claws: int }\n"));
        Assert.Equal("app.a.first<app.a.Cat>", alone);
        Assert.Equal(alone, beside);
    }

    /// <summary>Two modules' generic types of one name: an instance's entry has its module once.
    /// Under the rule before, such a type's name carried the module as well, and the entry read
    /// <c>app.a.app.a.Crate&lt;int&gt;</c>.</summary>
    [Fact]
    public void Two_modules_generic_types_of_one_name_have_one_module_each()
    {
        var dir = Package(("lyric.toml", AppManifest),
            ("src/a.lyr", "pub class Crate<T> { pub var held: T }\n"
                + "pub fn make(n: int): Crate<int> { return Crate<int> { held = n }; }\n"),
            ("src/b.lyr", "pub class Crate<T> { pub var kept: T, pub var count: int }\n"
                + "pub fn make(n: int): Crate<int> { return Crate<int> { kept = n, count = 1 }; }\n"),
            ("src/main.lyr", "import std.io { println };\nimport app.a;\nimport app.b;\n\n"
                + "fn main(): void {\n    let x = a.make(1);\n    let y = b.make(2);\n"
                + "    println(f\"{x.held} {y.kept} {y.count}\");\n}\n"));
        Assert.Equal("1 2 1\n", BuildAndRun(dir, "app", "build", "-C", dir));

        var (exit, c, error) = Run("build", "-C", dir, "--emit", "c");
        Assert.True(exit == 0, error);
        Assert.Contains("\"app.a.Crate<int>\"", c);
        Assert.Contains("\"app.b.Crate<int>\"", c);
        Assert.DoesNotContain("app.a.app.a.", c);
    }

    /// <summary>Two modules may each have a global of one name: the C tells them apart by their
    /// modules — and the program reads each one's own value.</summary>
    [Fact]
    public void Two_modules_globals_of_one_name_are_two()
    {
        var dir = Package(("lyric.toml", AppManifest),
            ("src/a.lyr", "pub let count: int = one();\nfn one(): int { return 1; }\n"),
            ("src/b.lyr", "pub let count: int = two();\nfn two(): int { return 2; }\n"),
            ("src/main.lyr", "import std.io { println };\nimport app.a;\nimport app.b;\n\n"
                + "fn main(): void {\n    println(f\"{a.count} {b.count}\");\n}\n"));
        Assert.Equal("1 2\n", BuildAndRun(dir, "app", "build", "-C", dir));

        var (exit, c, error) = Run("build", "-C", dir, "--emit", "c");
        Assert.True(exit == 0, error);
        Assert.Contains("lyr_g_app_a_count", c);
        Assert.Contains("lyr_g_app_b_count", c);
    }
}
