using Lyric.Bytecode;
using Lyric.Core;
using Lyric.Ir.Lowering;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;
using Lyric.Vm;

namespace Lyric.Tests.Vm;

/// <summary>
/// Array patterns (4.5): <c>[]</c>, <c>[a]</c>, <c>[a, b]</c>, <c>[first, ..]</c>,
/// <c>[.., last]</c>, <c>[a, .., z]</c>. The length is the first test — exactly when no rest
/// stands among the elements, at least when one does — and the positions after a rest are
/// counted from the back, so <c>[first, .., last]</c> needs no arithmetic over the rest.
///
/// <para>Each shape is checked on a hit AND on a miss of every length class it borders, because
/// an off-by-one in the length test is invisible when only the hit is tried.</para>
/// </summary>
public class ArrayPatternTests
{
    private static long Run(string source)
    {
        var sm = new SourceManager();
        var id = sm.AddVirtual("test.lyr", source);
        var de = new DiagnosticEngine(sm);
        var comp = new Compilation(sm, de);
        comp.AddModule(new Parser(sm, id, de).ParseModule());
        var binding = comp.Resolve();
        var types = Semantics.Analyze(comp, binding, de);

        var writer = new StringWriter();
        de.RenderText(writer);
        Assert.False(de.HasErrors, "source did not compile:\n" + writer);

        var ir = ModuleLowerer.Lower(comp, binding, types, de, verify: true);
        Assert.NotNull(ir);

        return Interpreter.Run(BytecodeReader.ReadOrThrow(BytecodeWriter.Write(ir!)),
            NativeRegistry.CreateDefault(TextWriter.Null, TextWriter.Null)).AsI64;
    }

    private static long Shape(string literal) => Run($$"""
        fn shape(xs: int[]): int {
            return match (xs) {
                []          => 0,
                [x]         => 100 + x,
                [0, y]      => 200 + y,
                [a, b]      => 300 + a * 10 + b,
                [first, ..] => 400 + first,
            };
        }
        fn main(): int { return shape({{literal}}); }
        """);

    [Theory]
    [InlineData("[]", 0)]
    [InlineData("[7]", 107)]
    [InlineData("[0, 5]", 205)]      // the literal position decides between two length-two arms
    [InlineData("[3, 4]", 334)]
    [InlineData("[9, 8, 7]", 409)]   // longer than every fixed arm: the rest takes it
    public void An_array_pattern_tests_the_length_first(string literal, long expected) =>
        Assert.Equal(expected, Shape(literal));

    [Theory]
    [InlineData("[1, 2, 3]", 4)]   // first + last
    [InlineData("[5, 6]", 11)]     // two elements: first and last are both there
    [InlineData("[5]", 5)]         // one element is too short for '[a, .., z]'
    [InlineData("[]", -1)]
    public void A_rest_between_two_positions_counts_the_second_from_the_back(string literal, long expected) =>
        Assert.Equal(expected, Run($$"""
            fn ends(xs: int[]): int {
                return match (xs) {
                    [a, .., z] => a + z,
                    [only]     => only,
                    _          => -1,
                };
            }
            fn main(): int { return ends({{literal}}); }
            """));

    [Theory]
    [InlineData("[1, 2, 3]", 3)]
    [InlineData("[9]", 9)]
    [InlineData("[]", -1)]
    public void A_position_after_a_rest_alone_is_the_last_element(string literal, long expected) =>
        Assert.Equal(expected, Run($$"""
            fn last(xs: int[]): int { return match (xs) { [.., z] => z, _ => -1 }; }
            fn main(): int { return last({{literal}}); }
            """));

    [Fact]
    public void A_bare_rest_matches_every_array_and_needs_no_other_arm() =>
        Assert.Equal(1, Run("fn f(xs: int[]): int { return match (xs) { [..] => 1 }; } fn main(): int { return f([]); }"));

    [Fact]
    public void An_array_pattern_nests_into_its_elements() =>
        Assert.Equal(2, Run("""
            enum E { A(int), B, }
            fn f(xs: E[]): int { return match (xs) { [A(1), B] => 2, _ => 0 }; }
            fn main(): int { return f([E.A(1), E.B]); }
            """));

    [Fact]
    public void An_array_pattern_that_nests_and_misses_falls_through() =>
        Assert.Equal(0, Run("""
            enum E { A(int), B, }
            fn f(xs: E[]): int { return match (xs) { [A(1), B] => 2, _ => 0 }; }
            fn main(): int { return f([E.A(2), E.B]); }
            """));

    [Fact]
    public void An_array_pattern_binds_through_a_let_else() =>
        Assert.Equal(30, Run("""
            fn f(xs: int[]): int { let [a, b] = xs else { return -1; }; return a * b; }
            fn main(): int { return f([5, 6]); }
            """));

    [Fact]
    public void An_array_pattern_in_an_if_let_binds_the_positions() =>
        Assert.Equal(12, Run("""
            fn f(xs: int[]): int { if (let [a, .., z] = xs) { return a * z; } return -1; }
            fn main(): int { return f([3, 9, 4]); }
            """));
}
