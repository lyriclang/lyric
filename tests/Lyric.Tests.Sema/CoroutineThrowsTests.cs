using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;
using Xunit;

namespace Lyric.Tests.Sema;

/// <summary>
/// Throwability as part of the coroutine TYPE (v3.0, issue #73).
///
/// <para>Until 3.0 a coroutine function's <c>throws</c> was checked at its CALL — an event that
/// runs no body and cannot throw. The demand therefore appeared to follow the local variable and
/// vanished at the first field or optional, which is precisely the idiom coroutines exist for.
/// It belongs to the type now, and the PULL is where it is asked for.</para>
/// </summary>
public class CoroutineThrowsTests
{
    private static string RepoRoot([System.Runtime.CompilerServices.CallerFilePath] string thisFile = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));

    private static DiagnosticEngine Check(string source)
    {
        var sm = new SourceManager();
        var id = sm.AddVirtual("test.lyr", source);
        var de = new DiagnosticEngine(sm);
        var comp = new Compilation(sm, de)
        {
            ModuleLoader = StdlibLoader.ForRoot(Path.Combine(RepoRoot(), "stdlib"), sm, de),
        };

        comp.AddModule(new Parser(sm, id, de).ParseModule());
        Semantics.Analyze(comp, comp.Resolve(), de);
        return de;
    }

    private const string Gen = """
        import std.core { Exception };

        fn gen(): Coroutine<int> throws Exception {
            yield 1;
            throw Exception { text = "mid" };
        }

        fn plain(): Coroutine<int> {
            yield 1;
        }

        """;

    [Fact]
    public void The_call_of_a_coroutine_function_demands_nothing()
    {
        // It builds a suspended frame. Nothing of the body has run, so there is nothing to handle
        // yet — and demanding a 'try' here was what made the check look like it followed the local.
        var de = Check(Gen + """
            fn main(): int {
                let c = gen();
                return 0;
            }
            """);
        Assert.DoesNotContain(de.Diagnostics, d => d.Code == "LYR-SEM0034");
    }

    [Fact]
    public void A_resume_of_a_throwing_coroutine_demands_handling()
    {
        var de = Check(Gen + """
            fn main(): int {
                let c = gen();
                return resume c;
            }
            """);
        var error = Assert.Single(de.Diagnostics, d => d.Code == "LYR-SEM0034");
        Assert.Contains("'resume'", error.Message, StringComparison.Ordinal);
        Assert.Contains("Exception", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_try_around_the_pull_is_enough()
    {
        var de = Check(Gen + """
            fn main(): int {
                let c = gen();
                try {
                    return resume c;
                } catch (e: Exception) {
                    return 0;
                }
            }
            """);
        Assert.False(de.HasErrors);
    }

    [Fact]
    public void The_demand_survives_an_optional()
    {
        // The repro of #73. Before 3.0 this compiled clean and aborted at runtime with LYR-VM0010.
        var de = Check(Gen + """
            fn main(): int {
                var co: ?Coroutine<int> throws Exception = null;
                co = gen();
                let c = co!;
                return resume c;
            }
            """);
        Assert.Contains(de.Diagnostics, d => d.Code == "LYR-SEM0034");
    }

    [Fact]
    public void The_demand_survives_a_field()
    {
        // The shape v2.2.0 built coroutines for: a driver holding one across calls.
        var de = Check(Gen + """
            class Driver {
                co: ?Coroutine<int> throws Exception = null,

                fn start() { this.co = gen(); }
            }

            fn main(): int {
                let d = Driver { };
                d.start();
                return resume d.co!;
            }
            """);
        Assert.Contains(de.Diagnostics, d => d.Code == "LYR-SEM0034");
    }

    [Fact]
    public void The_safe_pull_is_not_safe_about_throwing()
    {
        // 'next()' is lenient about EXHAUSTION — it answers null for a finished coroutine — and
        // says nothing about an exception from the body, which passes straight through it.
        var de = Check(Gen + """
            fn main(): int {
                let c = gen();
                let v = c.next();
                return 0;
            }
            """);
        var error = Assert.Single(de.Diagnostics, d => d.Code == "LYR-SEM0034");
        Assert.Contains("'next()'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_coroutine_that_cannot_throw_demands_nothing()
    {
        var de = Check(Gen + """
            fn main(): int {
                let c = plain();
                let v = c.next();
                return resume c;
            }
            """);
        Assert.False(de.HasErrors);
    }

    [Fact]
    public void A_throwing_coroutine_does_not_fit_a_plain_slot()
    {
        // The hole itself, as a type error: the plain type promises its readers that no pull
        // throws, and this value cannot keep that promise.
        var de = Check(Gen + """
            fn main(): int {
                var co: ?Coroutine<int> = null;
                co = gen();
                return 0;
            }
            """);
        var error = Assert.Single(de.Diagnostics, d => d.Code == "LYR-SEM0001");
        Assert.Contains("Coroutine<int> throws Exception", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_plain_coroutine_fits_a_throwing_slot()
    {
        // The safe direction: the slot promises a 'try' at the pull, and a value that never throws
        // keeps that promise.
        var de = Check(Gen + """
            fn main(): int {
                var co: ?Coroutine<int> throws Exception = null;
                co = plain();
                return 0;
            }
            """);
        Assert.False(de.HasErrors);
    }

    [Fact]
    public void A_typeless_throws_carries_over_too()
    {
        var de = Check("""
            import std.core { Exception };

            fn gen(): Coroutine<int> throws {
                yield 1;
                throw Exception { text = "mid" };
            }

            fn main(): int {
                var co: ?Coroutine<int> throws = null;
                co = gen();
                return resume co!;
            }
            """);
        Assert.Contains(de.Diagnostics, d => d.Code == "LYR-SEM0034");
    }

    [Fact]
    public void Throws_on_anything_but_a_coroutine_is_refused()
    {
        var de = Check("""
            fn f(x: int throws): int {
                return x;
            }

            fn main(): int {
                return f(1);
            }
            """);
        var error = Assert.Single(de.Diagnostics, d => d.Code == "LYR-SEM0084");
        Assert.Contains("coroutine", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_ordinary_function_keeps_its_own_throws()
    {
        // The regression guard for the parser: 'fn f(): MyType throws E' is the FUNCTION's clause
        // and has been since 1.0. Reading it as a throwing type would silently retype every
        // existing signature — and here it would drop the demand at the call.
        var de = Check("""
            import std.core { Exception };

            struct Result { value: int }

            fn risky(): Result throws Exception {
                throw Exception { text = "no" };
            }

            fn main(): int {
                let r = risky();
                return r.value;
            }
            """);
        var error = Assert.Single(de.Diagnostics, d => d.Code == "LYR-SEM0034");
        Assert.Contains("call to 'risky'", error.Message, StringComparison.Ordinal);
    }
}
