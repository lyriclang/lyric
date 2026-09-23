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

    // --- the NAMED rest: the elements it covers, copied into an array of their own ---

    /// <summary>
    /// A copy, not a view — the language has no slice, and a view would alias the array the
    /// pattern matched. The length is known only at runtime, which is what
    /// <c>std.core.rawArrayAlloc</c> is for; before it existed this form was `LYR-IR0001`.
    /// </summary>
    [Fact]
    public void A_named_rest_binds_the_elements_it_covers() =>
        // 234 rather than a sum: the digits pin the ORDER and the offset, which a sum hides.
        Assert.Equal(234, Run("""
            fn f(xs: int[]): int {
                return match (xs) {
                    [_, ..rest] => rest[0] * 100 + rest[1] * 10 + rest[2],
                    _ => 0,
                };
            }
            fn main(): int { return f([1, 2, 3, 4]); }
            """));

    /// <summary>The edge the allocation has to survive: a rest covering NOTHING. An array of
    /// length zero, not an out-of-range read of the element that would have been first.</summary>
    [Fact]
    public void A_named_rest_may_cover_nothing() =>
        Assert.Equal(0, Run("""
            fn f(xs: int[]): int { return match (xs) { [_, ..rest] => rest.length, _ => -1 }; }
            fn main(): int { return f([7]); }
            """));

    /// <summary>Between two fixed positions, so the copy starts at an offset and stops before the
    /// tail rather than running to the end.</summary>
    [Fact]
    public void A_named_rest_between_two_positions_copies_the_middle() =>
        // 23, so the 9s at both ends would show if the copy started or stopped one off.
        Assert.Equal(23, Run("""
            fn f(xs: int[]): int {
                return match (xs) {
                    [_, ..mid, _] => mid[0] * 10 + mid[1],
                    _ => -1,
                };
            }
            fn main(): int { return f([9, 2, 3, 9]); }
            """));

    /// <summary>The copy is INDEPENDENT: writing through the binding does not reach the array the
    /// pattern matched. A view would, and that is the difference this test exists for.</summary>
    [Fact]
    public void A_named_rest_is_a_copy_rather_than_a_view() =>
        Assert.Equal(2, Run("""
            fn f(xs: int[]): int {
                return match (xs) { [_, ..rest] => { rest[0] = 99; xs[1] } , _ => -1 };
            }
            fn main(): int { return f([1, 2, 3]); }
            """));

    /// <summary>At a second element type, which is what makes the native GENERIC: two import rows
    /// under one name, bound by one host function.</summary>
    [Fact]
    public void A_named_rest_works_at_another_element_type() =>
        Assert.Equal(3, Run("""
            fn f(xs: string[]): int { return match (xs) { [_, ..rest] => rest.length, _ => -1 }; }
            fn main(): int { return f(["a", "b", "c", "d"]); }
            """));
}
