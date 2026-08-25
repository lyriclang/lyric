using System.Runtime.CompilerServices;
using Lyric.Bytecode;
using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;
using Lyric.Ir.Lowering;
using Lyric.Vm;

namespace Lyric.Tests.Vm;

/// <summary>
/// The chain machinery behind coroutines (format 4.0): frames captured at a suspension, pushed
/// back at the next pull. The whole pre-4.0 suite pins that the SEMANTICS did not move; what
/// stands here are the edges the state machine never had — a chain nested in a chain, the state
/// after a throw has crossed the pull, and the one-driver rule.
/// </summary>
public class CoroutineChainTests
{
    private static string RepoRoot([CallerFilePath] string thisFile = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));

    private static LyrValue Run(string source)
    {
        var sm = new SourceManager();
        var id = sm.AddVirtual("test.lyr", source);
        var de = new DiagnosticEngine(sm);
        var comp = new Compilation(sm, de);
        comp.ModuleLoader = StdlibLoader.ForRoot(Path.Combine(RepoRoot(), "stdlib"), sm, de);
        comp.AddModule(new Parser(sm, id, de).ParseModule());
        var binding = comp.Resolve();
        var types = Semantics.Analyze(comp, binding, de);

        var writer = new StringWriter();
        de.RenderText(writer);
        Assert.False(de.HasErrors, "source did not compile:\n" + writer);

        var ir = ModuleLowerer.Lower(comp, binding, types, de, verify: true);
        Assert.NotNull(ir);

        var module = BytecodeReader.ReadOrThrow(BytecodeWriter.Write(ir!));
        return Interpreter.Run(module,
            NativeRegistry.CreateDefault(TextWriter.Null, TextWriter.Null));
    }

    private static LyricPanic Panics(string source) =>
        Assert.Throws<LyricPanic>(() => Run(source));

    [Fact]
    public void A_chain_nested_in_a_chain_delivers_to_its_own_resumer()
    {
        // outer drives inner: inner's yields arrive at OUTER's resume, outer's at main's.
        var result = Run("""
            fn inner(): Coroutine<int> {
                yield 1;
                yield 2;
            }

            fn outer(): Coroutine<int> {
                let co = inner();
                yield (resume co) * 10;
                yield (resume co) * 10;
                yield 3;
            }

            fn main(): int {
                let co = outer();
                let a = resume co;
                let b = resume co;
                let c = resume co;
                return a * 100 + b + c;
            }
            """);

        Assert.Equal(1023, result.AsI64); // 10*100 + 20 + 3
    }

    [Fact]
    public void A_throw_that_crossed_the_pull_ends_the_chain()
    {
        // After the exception leaves the resume, the chain is DONE: a lenient pull answers
        // null, and the strict pull panics as on any finished coroutine. The state machine
        // left this edge undefined — the throw skipped its done-exit — and nothing pinned it.
        var result = Run("""
            import std.core { Exception };

            fn gen(): Coroutine<int> throws Exception {
                yield 1;
                throw Exception { text = "boom" };
            }

            fn main(): int {
                let co = gen();
                var sum = 0;
                var after: ?int = null;
                try {
                    sum += resume co;
                    sum += resume co;
                } catch (_: Exception) {
                    sum += 100;
                }
                try {
                    after = co.next();
                } catch (_: Exception) {
                    sum += 999999; // must not happen: the chain is done, next answers null
                }
                if (after == null) {
                    sum += 1000;
                }
                return sum;
            }
            """);

        Assert.Equal(1101, result.AsI64);
    }

    [Fact]
    public void A_throw_handled_inside_the_body_keeps_the_chain_alive()
    {
        var result = Run("""
            import std.core { Exception };

            fn gen(): Coroutine<int> {
                try {
                    throw Exception { text = "inside" };
                } catch (_: Exception) {
                    yield 7;
                }
                yield 8;
            }

            fn main(): int {
                let co = gen();
                return (resume co) * 10 + resume co;
            }
            """);

        Assert.Equal(78, result.AsI64);
    }

    [Fact]
    public void A_defer_in_the_body_runs_when_the_throw_unwinds()
    {
        var result = Run("""
            import std.core { Exception };

            class Log {
                n: int,
            }

            let log = Log { n = 0 };

            fn gen(): Coroutine<int> throws Exception {
                defer { log.n += 5; }
                yield 1;
                throw Exception { text = "boom" };
            }

            fn main(): int {
                let co = gen();
                var sum = 0;
                try {
                    sum += resume co;
                    sum += resume co;
                } catch (_: Exception) {
                    sum += log.n * 100;
                }
                return sum;
            }
            """);

        Assert.Equal(501, result.AsI64); // the defer ran before the catch saw log.n
    }

    [Fact]
    public void Resuming_a_running_chain_panics()
    {
        var panic = Panics("""
            class Box {
                co: ?Coroutine<int>,
            }

            let box = Box { co = null };

            fn gen(): Coroutine<int> {
                let again = resume box.co!;
                yield again;
            }

            fn main(): int {
                box.co = gen();
                return resume box.co!;
            }
            """);

        Assert.Contains("already running", panic.Message);
    }

    [Fact]
    public void A_long_generator_survives_many_suspensions()
    {
        // The capture array is reused across suspensions; a hundred round trips would surface
        // a stale frame or a dropped slot immediately.
        var result = Run("""
            fn counter(): Coroutine<int> {
                var n = 0;
                while (true) {
                    yield n;
                    n += 1;
                }
            }

            fn main(): int {
                let co = counter();
                var sum = 0;
                var i = 0;
                while (i < 100) {
                    sum += resume co;
                    i += 1;
                }
                return sum;
            }
            """);

        Assert.Equal(4950, result.AsI64);
    }
}
