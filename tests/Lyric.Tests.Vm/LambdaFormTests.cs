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
/// The three lambda forms (4.5) beside the parenthesized one: the bare <c>x =&gt; …</c>, the
/// trailing <c>f { it * 2 }</c> with its implicit <c>it</c>, and pattern parameters
/// <c>((k, v)) =&gt; …</c>; plus the pattern in a for-loop head, which shares the compiler. And a
/// match expression whose arms all leave, which has the type never and no longer crashes.
/// </summary>
public class LambdaFormTests
{
    private static string RepoRoot([CallerFilePath] string thisFile = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));

    private static long Run(string source)
    {
        var sm = new SourceManager();
        var id = sm.AddVirtual("test.lyr", source);
        var de = new DiagnosticEngine(sm);
        // The stdlib is loaded because iterating an ARRAY needs the adapter from std.iter —
        // a for-loop over 'int[]' has no declaration a conformance could hang on.
        var comp = new Compilation(sm, de)
        {
            ModuleLoader = StdlibLoader.ForRoot(Path.Combine(RepoRoot(), "stdlib"), sm, de),
        };
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

    private const string Helpers = """
        fn apply(f: fn(int) -> int, v: int): int { return f(v); }
        fn applyTo(v: int, f: fn(int) -> int): int { return f(v); }
        fn run(f: fn() -> int): int { return f(); }
        fn each(xs: int[], f: fn(int) -> void): void { for (x in xs) { f(x); } }
        fn twice<T>(f: fn(T) -> T, v: T): T { return f(f(v)); }
        fn foldPairs(ps: (int, int)[], seed: int, f: fn(int, (int, int)) -> int): int {
            var acc = seed; for (p in ps) { acc = f(acc, p); } return acc;
        }
        """;

    // --- bare ---

    [Fact]
    public void A_bare_lambda_takes_its_type_from_the_context() =>
        Assert.Equal(42, Run(Helpers + "fn main(): int { return apply(x => x * 2, 21); }"));

    [Fact]
    public void A_bare_lambda_binds_a_generic_parameter_from_the_other_argument() =>
        Assert.Equal(18, Run(Helpers + "fn main(): int { return twice(n => n * 3, 2); }"));

    [Fact]
    public void A_bare_lambda_may_have_a_block_body() =>
        Assert.Equal(8, Run(Helpers + "fn main(): int { return apply(x => { let y = x + 1; return y * 2; }, 3); }"));

    [Fact]
    public void A_bare_name_before_the_arrow_of_a_guard_is_the_guard() =>
        Assert.Equal(1, Run("fn main(): int { let n = 5; return match (n) { x if x > 3 => 1, _ => 0 }; }"));

    [Fact]
    public void A_bare_lambda_inside_a_guard_call_is_still_a_lambda() =>
        Assert.Equal(1, Run(Helpers + "fn main(): int { return match (5) { x if apply(y => y - 5, x) == 0 => 1, _ => 0 }; }"));

    // --- trailing ---

    [Fact]
    public void A_trailing_lambda_is_the_last_argument_and_it_is_its_parameter() =>
        Assert.Equal(42, Run(Helpers + "fn main(): int { return applyTo(41) { it + 1 }; }"));

    [Fact]
    public void A_trailing_lambda_alone_needs_no_parentheses() =>
        Assert.Equal(7, Run(Helpers + "fn main(): int { return run { 7 }; }"));

    [Fact]
    public void A_trailing_lambda_with_statements_is_a_block_and_ends_the_statement() =>
        Assert.Equal(3, Run(Helpers + """
            fn main(): int {
                var sum = 0;
                each([1, 2]) { sum = sum + it; }
                return sum;
            }
            """));

    [Fact]
    public void A_trailing_lambda_chains_through_members() =>
        Assert.Equal(60, Run("""
            class Box { v: int, fn map(f: fn(int) -> int): Box { return Box { v = f(this.v) }; } }
            fn main(): int { return Box { v = 3 }.map { it * 10 }.map { it * 2 }.v; }
            """));

    [Fact]
    public void A_struct_initializer_is_not_a_trailing_lambda() =>
        Assert.Equal(3, Run("""
            struct P { x: int, y: int, }
            fn main(): int { let p = P { x = 1, y = 2 }; let e = P { x = 0, y = 0 }; return p.x + p.y + e.x; }
            """));

    // --- pattern parameters and for-loop heads ---

    [Fact]
    public void A_tuple_pattern_parameter_takes_the_argument_apart() =>
        Assert.Equal(50, Run(Helpers + "fn main(): int { return foldPairs([(1, 10), (2, 20)], 0, (acc, (a, b)) => acc + a * b); }"));

    [Fact]
    public void A_tuple_pattern_in_a_for_head_binds_both_names() =>
        Assert.Equal(33, Run("""
            fn main(): int {
                var total = 0;
                for ((a, b) in [(1, 10), (2, 20)]) { total = total + a + b; }
                return total;
            }
            """));

    [Fact]
    public void A_wildcard_in_a_for_head_pattern_skips_the_element() =>
        Assert.Equal(30, Run("""
            fn main(): int {
                var total = 0;
                for ((_, b) in [(1, 10), (2, 20)]) { total = total + b; }
                return total;
            }
            """));

    // --- never ---

    [Fact]
    public void A_match_expression_whose_arms_all_leave_is_never_and_compiles() =>
        Assert.Equal(1, Run("""
            enum V { A, B }
            fn f(v: V): bool {
                return match (v) {
                    A => { return false; },
                    B => { return true; },
                };
            }
            fn main(): int { return if (f(V.B)) 1 else 0; }
            """));
}
