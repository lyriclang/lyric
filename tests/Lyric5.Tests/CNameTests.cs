using System.Text.RegularExpressions;
using Lyric5.Compiler;
using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// What the emitted C calls a program's types and globals (design/v5/spec/01 L8, the review's
/// B13): by the module and the name they have in Lyric. Until the review a type was called by its
/// number in the type table, which holds every type of the standard library — a new type there
/// renamed every type of every program, and with them every golden.
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
    /// declares it (07 K2) — a function's instance, a type's, and a type's inside another's; a
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
