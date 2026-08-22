using System.Runtime.CompilerServices;
using Lyric.Bytecode;
using Lyric.Core;
using Lyric.Ir.Lowering;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;
using Lyric.Vm;

namespace Lyric.Tests.Vm;

/// <summary>
/// Several conformances to one arithmetic interface (v3.0): <c>Vec2 :: [Mul&lt;Vec2, Vec2&gt;]</c>
/// beside <c>extend Vec2 :: [Mul&lt;float, Vec2&gt;]</c>, and the operator picking between them by
/// the type of its right operand.
///
/// <para>The two implementations share a NAME, so nothing about the call site distinguishes them
/// except the operand: these tests pin that the choice is made, that it survives the lowering, and
/// that a dispatch through an interface value makes the same one.</para>
/// </summary>
public class MultiConformanceTests
{
    private static string RepoRoot([CallerFilePath] string thisFile = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));

    private static long Run(string source)
    {
        var sm = new SourceManager();
        var id = sm.AddVirtual("test.lyr", source);
        var de = new DiagnosticEngine(sm);
        var comp = new Compilation(sm, de)
        {
            ModuleLoader = StdlibLoader.ForRoot(Path.Combine(RepoRoot(), "stdlib"), sm, de),
        };
        comp.AddModule(new Parser(sm, id, de).ParseModule());
        var binding = comp.Resolve();
        var types = Semantics.Analyze(comp, binding, de);

        var writer = new StringWriter();
        de.RenderText(writer);
        Assert.False(de.HasErrors, "source did not compile: " + writer);

        var ir = ModuleLowerer.Lower(comp, binding, types, de, verify: true);
        Assert.NotNull(ir);
        return Interpreter.Run(BytecodeReader.ReadOrThrow(BytecodeWriter.Write(ir!)),
            NativeRegistry.CreateDefault(TextWriter.Null, TextWriter.Null)).AsI64;
    }

    /// <summary>A scale factor is not a vector: the second conformance takes an int and gives a
    /// Vec2 back, which is the shape a one-parameter interface could not express.</summary>
    private const string Vec2 = """
        import std.core { Mul };

        struct Vec2 :: [Mul<Vec2, Vec2>] {
            x: int,
            y: int,
            fn mul(other: Vec2): Vec2 {
                return Vec2 { x = this.x * other.x, y = this.y * other.y };
            }
        }

        extend Vec2 :: [Mul<int, Vec2>] {
            fn mul(other: int): Vec2 {
                return Vec2 { x = this.x * other, y = this.y * other };
            }
        }
        """;

    [Fact]
    public void The_right_operand_picks_the_conformance()
    {
        Assert.Equal(12, Run(Vec2 + """

            fn main(): int {
                let a = Vec2 { x = 2, y = 3 };
                let square = a * a;          // Mul<Vec2, Vec2>: 4, 9
                let scaled = a * 4;          // Mul<int, Vec2>:  8, 12
                return square.x + scaled.x;  // 4 + 8
            }
            """));
    }

    [Fact]
    public void Both_conformances_keep_their_own_result()
    {
        Assert.Equal(27, Run(Vec2 + """

            fn main(): int {
                let a = Vec2 { x = 2, y = 3 };
                return (a * a).y + (a * 3).x + (a * 4).y;  // 9 + 6 + 12
            }
            """));
    }

    [Fact]
    public void A_chain_alternates_between_the_two()
    {
        // 'a * 2 * a' is '(a * 2) * a': the first call goes to the extension, the second to the
        // own method, and the result of the first is what the second dispatches on.
        Assert.Equal(8, Run(Vec2 + """

            fn main(): int {
                let a = Vec2 { x = 2, y = 3 };
                return (a * 2 * a).x;
            }
            """));
    }

    [Fact]
    public void A_compound_assignment_picks_the_same_way()
    {
        Assert.Equal(24, Run(Vec2 + """

            fn main(): int {
                var v = Vec2 { x = 2, y = 3 };
                v *= 3;   // 6, 9
                v *= v;   // 36, 81
                return v.x - 12;
            }
            """));
    }

    [Fact]
    public void An_interface_value_dispatches_to_the_conformance_it_names()
    {
        // The rows are keyed by the INSTANCE: 'Mul<int, Vec2>' and 'Mul<Vec2, Vec2>' are two of
        // them on one type, and a row resolved by method name would give both the same target.
        Assert.Equal(18, Run(Vec2 + """

            fn scaled(m: Mul<int, Vec2>): Vec2 {
                return m.mul(3);
            }

            fn squared(m: Mul<Vec2, Vec2>, by: Vec2): Vec2 {
                return m.mul(by);
            }

            fn main(): int {
                let a = Vec2 { x = 2, y = 3 };
                return scaled(a).y + squared(a, a).y;  // 9 + 9
            }
            """));
    }

    [Fact]
    public void A_constraint_names_which_conformance_it_wants()
    {
        // Monomorphization, not dispatch: 'T :: [Mul<int, T>]' promises the scaling one, and the
        // call in the body has to find it rather than the same-typed one.
        Assert.Equal(12, Run(Vec2 + """

            fn twice<T :: [Mul<int, T>]>(value: T): T {
                return value * 2;
            }

            fn main(): int {
                let a = Vec2 { x = 3, y = 6 };
                return twice(a).y;
            }
            """));
    }

    [Fact]
    public void An_untyped_literal_adapts_to_the_conformance()
    {
        // 'f * 2' has no 'Mul<int, …>' to reach; the literal fits a float, and the rule that lets
        // it fit everywhere else in the language applies here too.
        Assert.Equal(7, Run("""
            import std.core { Mul };

            struct Scale :: [Mul<float, Scale>] {
                factor: float,
                fn mul(other: float): Scale {
                    return Scale { factor = this.factor * other };
                }
            }

            fn main(): int {
                let s = Scale { factor = 3.5 };
                return (s * 2).factor as int;
            }
            """));
    }

    [Fact]
    public void The_homogeneous_case_needs_no_second_conformance()
    {
        // The shape that existed before multi-conformance, unchanged apart from the second type
        // argument: one conformance, one implementation, no selection to make.
        Assert.Equal(30, Run("""
            import std.core { Mul };

            struct Money :: [Mul<Money, Money>] {
                cents: int,
                fn mul(other: Money): Money {
                    return Money { cents = this.cents * other.cents };
                }
            }

            fn main(): int {
                let m = Money { cents = 5 };
                let n = Money { cents = 6 };
                return (m * n).cents;
            }
            """));
    }
}
