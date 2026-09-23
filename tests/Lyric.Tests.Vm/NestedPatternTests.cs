using Lyric.Bytecode;
using Lyric.Core;
using Lyric.Ir.Lowering;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;
using Lyric.Vm;

namespace Lyric.Tests.Vm;

/// <summary>
/// The pattern compiler (4.5): patterns that nest to any depth, compiled in one recursive
/// test-and-bind pass. Every shape here was refused (LYR-IR0001) or crashed the compiler before —
/// tuples with variants and literals, variants inside variants, or-patterns that bind, field
/// patterns that test, literals adapted to the scrutinee's width, a binding over a struct that
/// used to alias it, and a binding arm written before the null arm.
///
/// <para>Every form appears at least twice: once matching, once missing — a test that checks
/// only the hit stays green when every pattern matches everything.</para>
/// </summary>
public class NestedPatternTests
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

    private const string Machine = """
        enum State { Idle, Connecting { attempt: int }, Connected(int), Closed { reason: int }, }
        enum Event { Dial(int), Timeout, Hangup, Data(int), }
        fn step(s: State, ev: Event): State {
            return match ((s, ev)) {
                (Idle, Dial(_)) => State.Connecting { attempt = 1 },
                (Connecting { attempt }, Timeout) if attempt < 3 => State.Connecting { attempt = attempt + 1 },
                (Connecting { attempt = 3 }, Timeout) => State.Closed { reason = 7 },
                (Connecting { attempt }, Dial(h)) => State.Connected(h),
                (Connected(_), Hangup) => State.Closed { reason = 6 },
                _ => s,
            };
        }
        fn code(s: State): int {
            return match (s) {
                Idle => 0,
                Connecting { attempt } => 10 + attempt,
                Connected(h) => 20 + h,
                Closed { reason } => 30 + reason,
            };
        }
        """;

    // --- tuples with variant and literal sub-patterns: the state-machine idiom ---

    [Fact]
    public void A_tuple_of_enums_drives_a_state_machine() =>
        Assert.Equal(23, Run(Machine + """
            fn main(): int {
                var s = State.Idle;
                s = step(s, Event.Dial(1));       // Connecting(1)
                s = step(s, Event.Timeout);       // Connecting(2)
                s = step(s, Event.Dial(3));       // Connected(3)
                return code(s);
            }
            """));

    [Fact]
    public void A_tuple_arm_that_misses_falls_to_the_next() =>
        Assert.Equal(37, Run(Machine + """
            fn main(): int {
                var s = State.Connecting { attempt = 3 };
                s = step(s, Event.Timeout);       // the guard fails, the literal arm takes it
                return code(s);                   // Closed(7) = 30 + 7
            }
            """));

    [Fact]
    public void A_tuple_with_no_matching_arm_takes_the_wildcard() =>
        Assert.Equal(0, Run(Machine + "fn main(): int { return code(step(State.Idle, Event.Hangup)); }"));

    [Theory]
    [InlineData("0, 0", 1)]
    [InlineData("0, 3", 2)]
    [InlineData("2, 0", 3)]
    [InlineData("4, 4", 4)]
    [InlineData("1, 2", 5)]
    public void Literals_in_a_tuple_pattern_are_tested_element_by_element(string args, long expected) =>
        Assert.Equal(expected, Run($$"""
            fn q(x: int, y: int): int {
                return match ((x, y)) {
                    (0, 0) => 1,
                    (0, _) => 2,
                    (_, 0) => 3,
                    (a, b) if a == b => 4,
                    _ => 5,
                };
            }
            fn main(): int { return q({{args}}); }
            """));

    // --- variants inside variants ---

    private const string Simplifier = """
        enum Expr { Lit(int), Neg(Expr), Add(Expr, Expr), }
        fn simplify(e: Expr): int {
            return match (e) {
                Neg(Neg(x)) => simplify(x),
                Neg(Lit(0)) => 0,
                Add(Lit(0), r) => simplify(r),
                Add(l, r) => simplify(l) + simplify(r),
                Neg(x) => 0 - simplify(x),
                Lit(n) => n,
            };
        }
        """;

    [Theory]
    [InlineData("Expr.Neg(Expr.Neg(Expr.Lit(7)))", 7)]   // two levels deep
    [InlineData("Expr.Add(Expr.Lit(0), Expr.Lit(5))", 5)]  // a literal in a payload
    [InlineData("Expr.Neg(Expr.Lit(0))", 0)]
    [InlineData("Expr.Neg(Expr.Lit(3))", -3)]               // the nested arms miss, the plain one takes it
    [InlineData("Expr.Add(Expr.Lit(2), Expr.Lit(5))", 7)]
    public void Nested_variant_patterns_match_to_any_depth(string input, long expected) =>
        Assert.Equal(expected, Run(Simplifier + $"fn main(): int {{ return simplify({input}); }}"));

    // --- or-patterns that bind, and or-patterns whose alternatives carry '_' ---

    [Theory]
    [InlineData("E.A(3)", 3)]
    [InlineData("E.B(4)", 4)]   // the second alternative stores into the same slot
    [InlineData("E.C", -1)]
    public void An_or_pattern_binds_the_same_name_from_either_alternative(string input, long expected) =>
        Assert.Equal(expected, Run($$"""
            enum E { A(int), B(int), C, }
            fn f(e: E): int { return match (e) { A(x) | B(x) => x, C => -1, }; }
            fn main(): int { return f({{input}}); }
            """));

    [Theory]
    [InlineData("Event.Dial(\"x\")", 1)]
    [InlineData("Event.Hangup", 1)]
    [InlineData("Event.Ack", 0)]
    public void An_or_pattern_with_a_wildcard_payload_binds_nothing_and_is_accepted(string input, long expected) =>
        Assert.Equal(expected, Run($$"""
            enum Event { Dial(string), Ack, Data(int), Hangup, }
            fn f(ev: Event): int { return match (ev) { Dial(_) | Hangup => 1, _ => 0, }; }
            fn main(): int { return f({{input}}); }
            """));

    [Fact]
    public void An_or_pattern_nested_in_a_tuple_binds_into_the_arm() =>
        Assert.Equal(12, Run("""
            enum E { A(int), B(int), }
            fn f(e: E, k: int): int { return match ((e, k)) { (A(x) | B(x), 1) => x + 10, _ => 0, }; }
            fn main(): int { return f(E.B(2), 1) + f(E.A(2), 2); }
            """));

    // --- field patterns that test ---

    [Theory]
    [InlineData("Shape.Rect { w = 0, h = 5 }", 5)]   // the literal field matches: the height alone
    [InlineData("Shape.Rect { w = 2, h = 5 }", 10)]  // it misses: the next arm multiplies
    [InlineData("Shape.Dot", 0)]
    public void A_literal_in_a_variant_field_pattern_is_a_test(string input, long expected) =>
        Assert.Equal(expected, Run($$"""
            enum Shape { Rect { w: int, h: int }, Dot, }
            fn f(s: Shape): int { return match (s) { Rect { w = 0, h } => h, Rect { w, h } => w * h, Dot => 0, }; }
            fn main(): int { return f({{input}}); }
            """));

    [Theory]
    [InlineData("P { x = 0, y = 7 }", 7)]
    [InlineData("P { x = 2, y = 7 }", 14)]
    public void A_literal_in_a_struct_field_pattern_is_a_test(string input, long expected) =>
        Assert.Equal(expected, Run($$"""
            struct P { x: int, y: int, }
            fn g(p: P): int { return match (p) { P { x = 0, y } => y, P { x, y } => x * y, }; }
            fn main(): int { return g({{input}}); }
            """));

    // --- literal adaptation: the pattern literal takes the scrutinee's width ---

    [Theory]
    [InlineData("1", 10)]
    [InlineData("3", 20)]
    [InlineData("9", 0)]
    public void A_literal_pattern_adapts_to_a_narrow_int(string input, long expected) =>
        Assert.Equal(expected, Run($$"""
            fn f(b: int8): int { return match (b) { 1 => 10, 2..=5 => 20, _ => 0, }; }
            fn main(): int { return f({{input}}); }
            """));

    [Theory]
    [InlineData("'a'", 1)]
    [InlineData("'q'", 2)]
    [InlineData("'A'", 0)]
    public void A_char_range_pattern_compares_code_points(string input, long expected) =>
        Assert.Equal(expected, Run($$"""
            fn g(c: char): int { return match (c) { 'a' => 1, 'b'..='z' => 2, _ => 0, }; }
            fn main(): int { return g({{input}}); }
            """));

    [Theory]
    [InlineData("\"hi\"", 1)]
    [InlineData("\"hey\"", 2)]
    [InlineData("\"x\"", 0)]
    public void A_string_literal_pattern_compares_contents(string input, long expected) =>
        Assert.Equal(expected, Run($$"""
            fn h(s: string): int { return match (s) { "hi" => 1, "yo" | "hey" => 2, _ => 0, }; }
            fn main(): int { return h({{input}}); }
            """));

    // --- struct bindings copy ---

    [Fact]
    public void A_binding_over_a_struct_is_a_copy_not_an_alias() =>
        Assert.Equal(1, Run("""
            struct P { x: int, }
            fn main(): int {
                var p = P { x = 1 };
                var seen = 0;
                match (p) { q => { p.x = 99; seen = q.x; } }
                return seen;
            }
            """));

    // --- optionals: a binding arm before the null arm, and bindings nested over ?T ---

    [Theory]
    [InlineData("5", 5)]
    [InlineData("null", -1)]   // the binding arm tests presence and lets null pass
    public void A_binding_arm_before_the_null_arm_tests_presence(string input, long expected) =>
        Assert.Equal(expected, Run($$"""
            fn f(o: ?int): int { return match (o) { n => n, null => -1, }; }
            fn main(): int { return f({{input}}); }
            """));

    [Theory]
    [InlineData("n, 2", -2)]   // 'null' in a tuple element is a presence test
    [InlineData("o, 3", 4)]    // 'a' nested over '?int' binds the '?int' and covers it
    public void A_name_nested_over_an_optional_binds_the_whole_optional(string input, long expected) =>
        Assert.Equal(expected, Run($$"""
            fn g(t: (?int, int)): int { return match (t) { (null, b) => 0 - b, (a, b) => (a ?? 0) + b, }; }
            fn main(): int { let n: ?int = null; let o: ?int = 1; return g(({{input}})); }
            """));

    [Theory]
    [InlineData("Result.Ok(4)", 4)]
    [InlineData("Result.Ok(n)", -1)]   // 'Ok(null)' tests the optional payload
    [InlineData("Result.Err(2)", 2)]
    public void A_generic_enum_with_an_optional_payload_is_matched_completely(string input, long expected) =>
        Assert.Equal(expected, Run($$"""
            enum Result<T, E> { Ok(T), Err(E), }
            fn f(r: Result<?int, int>): int {
                return match (r) { Ok(null) => -1, Ok(v) => v ?? -2, Err(e) => e, };
            }
            fn main(): int { let n: ?int = null; let r: Result<?int, int> = {{input}}; return f(r); }
            """));

    // --- guards ---

    [Theory]
    [InlineData("3", 1)]
    [InlineData("-3", 0)]
    public void A_parenthesized_guard_is_a_guard_and_not_a_lambda(string input, long expected) =>
        Assert.Equal(expected, Run($$"""
            fn f(n: int): int { return match (n) { x if (x > 0) => 1, _ => 0, }; }
            fn main(): int { return f({{input}}); }
            """));

    [Fact]
    public void A_guard_may_still_contain_a_lambda() =>
        Assert.Equal(1, Run("""
            fn apply(f: fn(int) -> bool, v: int): bool { return f(v); }
            fn f(n: int): int { return match (n) { x if apply((y) => y > 0, x) => 1, _ => 0, }; }
            fn main(): int { return f(3); }
            """));

    // --- destructuring shares the compiler ---

    [Fact]
    public void A_mutable_destructuring_captured_by_a_lambda_lives_in_a_cell() =>
        Assert.Equal(7, Run("""
            fn main(): int {
                var (a, b) = (1, 2);
                let bump = () => { a = a + 5; };
                bump();
                return a + b - 1;
            }
            """));
}
