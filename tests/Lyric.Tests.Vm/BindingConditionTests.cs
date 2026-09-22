using Lyric.Bytecode;
using Lyric.Core;
using Lyric.Ir.Lowering;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;
using Lyric.Vm;

namespace Lyric.Tests.Vm;

/// <summary>
/// <c>if (let P = e)</c>, <c>while (let P = e)</c> and <c>let P = e else { … };</c> (4.5): the
/// binding condition, lowered through the pattern compiler with the else branch, the loop exit or
/// the else block as the failure target.
///
/// <para>Each form is exercised on the hit AND on the miss, and with an enum payload as well as
/// an optional, because those are the two things the forms exist for.</para>
/// </summary>
public class BindingConditionTests
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

    private const string Shapes = "enum Shape { Circle(int), Rect { w: int, h: int }, Empty, }\n";

    // --- if let ---

    [Theory]
    [InlineData("Shape.Circle(4)", 4)]
    [InlineData("Shape.Empty", 0)]
    public void If_let_binds_a_payload_in_the_then_branch(string input, long expected) =>
        Assert.Equal(expected, Run(Shapes + $$"""
            fn radius(s: Shape): int { if (let Circle(r) = s) { return r; } return 0; }
            fn main(): int { return radius({{input}}); }
            """));

    [Theory]
    [InlineData("Shape.Rect { w = 2, h = 3 }", 6)]
    [InlineData("Shape.Circle(1)", -1)]
    public void If_let_takes_the_else_branch_on_a_miss(string input, long expected) =>
        Assert.Equal(expected, Run(Shapes + $$"""
            fn area(s: Shape): int { if (let Rect { w, h } = s) { return w * h; } else { return -1; } }
            fn main(): int { return area({{input}}); }
            """));

    [Theory]
    [InlineData("5", 5)]
    [InlineData("null", 0)]
    public void If_let_over_an_optional_binds_the_present_value(string input, long expected) =>
        Assert.Equal(expected, Run($$"""
            fn f(o: ?int): int { var r = 0; if (let v = o) { r = v; } return r; }
            fn main(): int { return f({{input}}); }
            """));

    [Fact]
    public void If_let_names_are_not_visible_after_the_statement() =>
        Assert.Equal(3, Run("""
            fn main(): int { let r = 3; if (let v = f()) { return v + 100; } return r; }
            fn f(): ?int { return null; }
            """));

    // --- while let ---

    [Fact]
    public void While_let_pulls_until_the_source_is_empty() =>
        Assert.Equal(6, Run("""
            class Counter { n: int, fn next(): ?int { if (this.n == 0) { return null; } this.n = this.n - 1; return this.n + 1; } }
            fn main(): int {
                let c = Counter { n = 3 };
                var sum = 0;
                while (let v = c.next()) { sum = sum + v; }
                return sum;
            }
            """));

    [Fact]
    public void While_let_over_an_enum_stops_at_the_first_other_variant() =>
        Assert.Equal(2, Run(Shapes + """
            class Feed { i: int, fn pull(): Shape { this.i = this.i + 1; return if (this.i < 3) Shape.Circle(this.i) else Shape.Empty; } }
            fn main(): int {
                let f = Feed { i = 0 };
                var last = 0;
                while (let Circle(r) = f.pull()) { last = r; }
                return last;
            }
            """));

    [Fact]
    public void While_let_honours_break_and_continue() =>
        Assert.Equal(4, Run("""
            class Counter { n: int, fn next(): ?int { if (this.n == 0) { return null; } this.n = this.n - 1; return this.n + 1; } }
            fn main(): int {
                let c = Counter { n = 10 };
                var sum = 0;
                while (let v = c.next()) {
                    if (v > 8) { continue; }   // skips 10 and 9
                    if (v < 7) { break; }      // stops at 6
                    sum = sum + v;             // 8 and 7 → 15
                }
                return sum - 11;
            }
            """));

    // --- let … else ---

    [Theory]
    [InlineData("Shape.Circle(9)", 9)]
    [InlineData("Shape.Empty", -1)]
    public void Let_else_binds_or_leaves(string input, long expected) =>
        Assert.Equal(expected, Run(Shapes + $$"""
            fn radius(s: Shape): int { let Circle(r) = s else { return -1; }; return r; }
            fn main(): int { return radius({{input}}); }
            """));

    [Theory]
    [InlineData("7", 7)]
    [InlineData("null", -1)]
    public void Let_else_narrows_an_optional_into_a_plain_binding(string input, long expected) =>
        Assert.Equal(expected, Run($$"""
            fn f(o: ?int): int { let v = o else { return -1; }; return v + 0; }
            fn main(): int { return f({{input}}); }
            """));

    [Fact]
    public void Let_else_chains_through_fields_without_copies() =>
        Assert.Equal(42, Run("""
            class Profile { score: ?int, }
            class User { profile: ?Profile, }
            fn score(u: User): int {
                let p = u.profile else { return -1; };
                let s = p.score else { return -2; };
                return s;
            }
            fn main(): int {
                let a = score(User { profile = Profile { score = 42 } });
                let b = score(User { profile = Profile { score = null } });
                let c = score(User { profile = null });
                return a + b + c + 3;
            }
            """));

    [Theory]
    [InlineData("Result.Ok(7)", 7)]
    [InlineData("Result.Ok(n)", -1)]     // present variant, absent payload: '??' answers
    [InlineData("Result.Err(3)", -1)]
    public void Let_else_over_a_generic_enum_with_an_optional_payload(string input, long expected) =>
        Assert.Equal(expected, Run($$"""
            enum Result<T, E> { Ok(T), Err(E), }
            fn unwrapOr(r: Result<?int, int>, d: int): int { let Ok(v) = r else { return d; }; return v ?? d; }
            fn main(): int { let n: ?int = null; let r: Result<?int, int> = {{input}}; return unwrapOr(r, -1); }
            """));

    [Fact]
    public void Let_else_in_a_loop_may_continue_or_break() =>
        Assert.Equal(6, Run(Shapes + """
            fn main(): int {
                let xs = [Shape.Circle(1), Shape.Empty, Shape.Circle(2), Shape.Rect { w = 1, h = 1 }, Shape.Circle(3)];
                var sum = 0;
                var i = 0;
                while (i < 5) {
                    let s = xs[i];
                    i = i + 1;
                    let Circle(r) = s else { continue; };
                    sum = sum + r;
                }
                return sum;
            }
            """));

    [Fact]
    public void A_let_pattern_without_else_destructures_a_struct() =>
        Assert.Equal(7, Run("""
            struct Point { x: int, y: int, }
            fn main(): int { let Point { x, y } = Point { x = 3, y = 4 }; return x + y; }
            """));

    [Fact]
    public void A_mutable_let_pattern_binds_var() =>
        Assert.Equal(11, Run("""
            struct Point { x: int, y: int, }
            fn main(): int { var Point { x, y } = Point { x = 3, y = 4 }; x = x + 4; return x + y; }
            """));
}
