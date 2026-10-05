using Lyric5.Compiler;

namespace Lyric5.Tests;

/// <summary>
/// The emitted C holds the types the program reaches (design/v5/spec/01 L11, the review's B13) —
/// not every type the standard library declares.
/// </summary>
public class CReachTests
{
    private static string Emit(string source)
    {
        var result = TestCompiler.Lower("reach.lyr", source);
        var rendered = new StringWriter();
        result.Diagnostics.RenderText(rendered);
        Assert.True(result.Ok && result.Ir is not null, rendered.ToString());
        return CEmitter.Join(CEmitter.Emit(result.Ir!, result.Sources, TestCompiler.Stdlib));
    }

    private const string Builder = "lyr_ty_std_core_StringBuilder";

    [Fact]
    public void A_type_the_program_does_not_reach_is_not_in_its_C()
    {
        var hello = Emit("import std.io { println };\n\nfn main(): int {\n    println(\"hi\");\n    return 0;\n}\n");
        Assert.DoesNotContain(Builder, hello);

        // … and the name is right: a program that builds a string has it.
        var built = Emit("""
            import std.core { StringBuilder };
            import std.io { println };

            fn main(): int {
                var b = StringBuilder.new();
                b.appendStr("hi");
                println(b.toString());
                return 0;
            }
            """);
        Assert.Contains($"struct {Builder} {{", built);
    }

    /// <summary>What is reached only through another type is there: a field's type, an enum's
    /// variants, the interface a value was lifted to.</summary>
    [Fact]
    public void What_a_reached_type_holds_is_there()
    {
        var c = Emit("""
            import std.io { println };

            struct Inner { n: int }
            struct Outer { inner: Inner }
            enum Kind { One, Two(Inner) }
            interface Named { fn name(): string; }
            struct Thing :: [Named] { fn name(): string { return "thing"; } }
            struct Unused { n: int }

            // Lowered, as every function of the module is, and then cut: nothing calls it. Its
            // type is in the table and not in the program.
            fn dead(): int { let u = Unused { n = 1 }; return u.n; }

            fn show(n: Named): string { return n.name(); }

            fn main(): int {
                let o = Outer { inner = Inner { n = 1 } };
                let k = Kind.Two(o.inner);
                println(show(Thing { }));
                return match (k) { Kind.Two(i) => i.n - 1, Kind.One => 1 };
            }
            """);

        Assert.Contains("struct lyr_ty_app_main_Inner {", c);
        Assert.Contains("struct lyr_ty_app_main_Outer {", c);
        Assert.Contains("lyr_ty_13_app_main_Kind_Two", c);
        Assert.Contains("lyr_vt_ty_app_main_Named", c);
        Assert.Contains("lyr_box_ty_app_main_Thing", c);
        Assert.DoesNotContain("lyr_ty_app_main_Unused", c);
    }
}
