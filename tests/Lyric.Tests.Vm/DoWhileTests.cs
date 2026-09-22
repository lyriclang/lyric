using Lyric.Bytecode;
using Lyric.Core;
using Lyric.Ir.Lowering;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;
using Lyric.Vm;

namespace Lyric.Tests.Vm;

/// <summary>
/// <c>do { … } while (…)</c> — the only loop whose condition stands BEHIND the body and can therefore
/// be unreachable.
///
/// <para><c>do { return 1; } while (true);</c> used to be a compiler crash. Body, condition and exit
/// were all three created in advance; if the body terminated, two blocks without predecessors stayed
/// behind, and the verifier rejects that, as there is no SimplifyCfg pass.</para>
///
/// <para>THE TRAP IS IN THE SECOND TEST. The case was long described as "the body terminates", and it
/// cannot be decided on that: <c>do { if (c) { break; } return 2; }</c> does not fall through and
/// reaches the exit all the same. "Is the block reachable" and "does the body fall through" are two
/// questions. A fix asking only the second would be green with the first test and red here.</para>
/// </summary>
public class DoWhileTests
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

        var diagnostics = new StringWriter();
        de.RenderText(diagnostics);
        Assert.False(de.HasErrors, "source did not compile: " + diagnostics);

        // verify: true — the verifier IS the test here. Only it notices an unreachable block, and that was
        // exactly the crash.
        var ir = ModuleLowerer.Lower(comp, binding, types, de, verify: true);
        Assert.NotNull(ir);

        return Interpreter.Run(BytecodeReader.ReadOrThrow(BytecodeWriter.Write(ir!)),
            NativeRegistry.CreateDefault(TextWriter.Null, TextWriter.Null)).AsI64;
    }

    /// <summary>The reported case: the body leaves the function and nobody reaches the condition or the
    /// exit.</summary>
    [Fact]
    public void A_body_that_always_returns_leaves_no_unreachable_blocks() =>
        Assert.Equal(1, Run("""
            fn main(): int {
                do { return 1; } while (true);
            }
            """));

    /// <summary>
    /// The case a fix that is too simple fails at: the body does NOT fall through and the exit is
    /// reachable all the same, through the <c>break</c>.
    /// </summary>
    [Fact]
    public void A_break_reaches_the_exit_even_from_a_body_that_never_falls_through() =>
        Assert.Equal(3, Run("""
            fn los(c: bool): int {
                do { if (c) { break; } return 2; } while (true);
                return 3;
            }

            fn main(): int { return los(true); }
            """));

    /// <summary>The same function with the other answer; otherwise it would stay open whether the body
    /// runs at all.</summary>
    [Fact]
    public void The_same_loop_still_returns_from_inside_the_body() =>
        Assert.Equal(2, Run("""
            fn los(c: bool): int {
                do { if (c) { break; } return 2; } while (true);
                return 3;
            }

            fn main(): int { return los(false); }
            """));

    /// <summary>A <c>continue</c> reaches the condition even when the body does not fall through.</summary>
    [Fact]
    public void A_continue_reaches_the_condition_from_a_body_that_never_falls_through() =>
        Assert.Equal(3, Run("""
            fn main(): int {
                var i = 0;
                do {
                    i = i + 1;
                    if (i < 3) { continue; }
                    return i;
                } while (i < 100);
                return -1;
            }
            """));

    /// <summary>
    /// The counter-check. Without it everything above would stay green if the ordinary loop had broken in
    /// the process, and that is the form standing in real code.
    /// </summary>
    [Fact]
    public void An_ordinary_do_while_still_loops() =>
        Assert.Equal(5, Run("""
            fn main(): int {
                var i = 0;
                do { i = i + 1; } while (i < 5);
                return i;
            }
            """));

    /// <summary>And the endless loop with a <c>break</c> stays the form <c>do-while</c> exists for.
    /// </summary>
    [Fact]
    public void A_loop_that_only_leaves_through_break_works() =>
        Assert.Equal(4, Run("""
            fn main(): int {
                var i = 0;
                do {
                    i = i + 1;
                    if (i == 4) { break; }
                } while (true);
                return i;
            }
            """));

    // --- the jump target's POSITION: a protected region is a contiguous range of block ids ---

    /// <summary>
    /// A <c>break</c> out of a <c>try</c> used to create the exit block INSIDE the handler's block
    /// range, because the target arose where the jump stood. Everything after the loop was then
    /// protected by a <c>catch</c> that belongs inside it: the <c>throw</c> behind the loop landed
    /// in the loop's own handler, control returned to the condition, and the program looped forever
    /// while running the code after the loop again on every turn.
    /// </summary>
    [Fact]
    public void A_break_out_of_a_try_does_not_put_the_code_after_the_loop_under_its_handler() =>
        Assert.Equal(7, Run("""
            class Boom :: [Throwable] {
                n: int,
                fn message(): string { return "boom"; }
            }

            fn work(): int throws Boom {
                do {
                    try { break; }
                    catch (e: Boom) { return 99; }
                } while (false);
                throw Boom { n = 1 };
            }

            fn main(): int {
                try { return work(); }
                catch (e: Boom) { return 7; }
            }
            """));

    /// <summary>The same for <c>continue</c>, whose target is the CONDITION: the condition's own
    /// exception was caught by the handler in the body it had just left.</summary>
    [Fact]
    public void A_continue_out_of_a_try_does_not_put_the_condition_under_its_handler() =>
        Assert.Equal(7, Run("""
            class Boom :: [Throwable] {
                n: int,
                fn message(): string { return "boom"; }
            }

            fn again(calls: int): bool throws Boom {
                if (calls >= 2) { throw Boom { n = calls }; }
                return true;
            }

            fn work(): int throws Boom {
                var calls = 0;
                do {
                    try { calls = calls + 1; continue; }
                    catch (e: Boom) { return 99; }
                } while (again(calls));
                return 0;
            }

            fn main(): int {
                try { return work(); }
                catch (e: Boom) { return 7; }
            }
            """));

    /// <summary>A <c>defer</c> scope opens the same kind of region, and there the price was a body
    /// that ran TWICE: once at the <c>break</c>, once more while the <c>throw</c> behind the loop
    /// unwound through a finally region that should never have covered it.</summary>
    [Fact]
    public void A_break_out_of_a_defer_scope_runs_the_defer_once() =>
        Assert.Equal(1, Run("""
            class Boom :: [Throwable] {
                n: int,
                fn message(): string { return "boom"; }
            }

            class Counter { n: int }

            fn work(c: Counter): int throws Boom {
                do {
                    {
                        defer { c.n = c.n + 1; }
                        break;
                    }
                } while (false);
                throw Boom { n = 1 };
            }

            fn main(): int {
                let c = Counter { n = 0 };
                try { return work(c); }
                catch (e: Boom) { return c.n; }
            }
            """));
}
