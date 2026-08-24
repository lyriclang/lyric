using System.Runtime.CompilerServices;
using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Tests.Sema;

/// <summary>
/// An OPTIONAL element cannot be walked, and the reason is the protocol rather than the container.
///
/// <para><c>Iterator&lt;T&gt;.next()</c> answers <c>?T</c> and uses null to mean "the end". An
/// element that is itself optional would need <c>??T</c> to be told apart from that end, and
/// <c>?</c> does not nest — so <c>Iterator&lt;?T&gt;</c> is a type the language cannot express,
/// however ordinary <c>(?T)[]</c> is as a table.</para>
///
/// <para>It used to crash. In debug the IR verifier threw on <c>optnone of ?i64</c> with a stack
/// trace and no source position; in release the verifier does not run, <c>check</c> answered "ok",
/// and the build wrote a module the loader refuses. The plainest form of it —
/// <c>for (x in xs)</c> over an array of optionals — is a loop anybody would write.</para>
/// </summary>
public class IterateOptionalTests
{
    private static string RepoRoot([CallerFilePath] string thisFile = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));

    private static IReadOnlyList<Diagnostic> Check(string source)
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
        return de.Diagnostics;
    }

    [Fact]
    public void An_array_of_optionals_cannot_be_walked()
    {
        var diagnostics = Check("""
            fn main(): int {
                let xs: (?int)[] = [1, null, 3];
                var n = 0;
                for (v in xs) { if (v != null) { n = n + v; } }
                return n;
            }
            """);

        var diagnostic = Assert.Single(diagnostics, d => d.Code == "LYR-SEM0091");
        Assert.Equal(Severity.Error, diagnostic.Severity);
        Assert.True(diagnostic.Span.File.IsValid, "the message has no source position");
        Assert.NotNull(diagnostic.Notes);
        Assert.Contains(diagnostic.Notes!, n => n.Message.Contains("walk the indices"));
    }

    [Fact]
    public void The_same_holds_for_an_explicit_iterator_of_optionals()
    {
        // Not the array's fault: an iterator handed in directly runs into the same protocol.
        var diagnostics = Check("""
            import std.iter { ArrayIterator };

            fn main(): int {
                let xs: (?int)[] = [1, null, 3];
                let it = ArrayIterator<?int> { source = xs, index = 0 };
                var n = 0;
                for (v in it) { if (v != null) { n = n + v; } }
                return n;
            }
            """);

        Assert.Contains(diagnostics, d => d.Code == "LYR-SEM0091");
    }

    /// <summary>
    /// The message stands alone. The loop variable answers ErrorType afterwards, and whatever the
    /// body does with it says nothing further — a sentence about an operator and an
    /// <c>&lt;error&gt;[]</c> is noise trailing the one mistake.
    /// </summary>
    [Fact]
    public void Nothing_in_the_body_reports_a_second_time()
    {
        var diagnostics = Check("""
            pub fn compact<T>(xs: (?T)[]): T[] {
                var kept: T[] = [];
                for (x in xs) { if (x != null) { kept = kept + [x]; } }
                return kept;
            }

            fn main(): int { return 0; }
            """);

        Assert.Single(diagnostics);
        Assert.Equal("LYR-SEM0091", diagnostics[0].Code);
    }

    /// <summary>The counter-check: an ordinary array is walked as it always was, and an OPTIONAL
    /// ARRAY is a different thing from an array of optionals — that one is refused for not being
    /// iterable at all, which is a different message.</summary>
    [Fact]
    public void An_ordinary_array_is_unaffected() =>
        Assert.Empty(Check("""
            fn main(): int {
                let xs = [1, 2, 3];
                var n = 0;
                for (v in xs) { n = n + v; }
                return n;
            }
            """));

    [Fact]
    public void An_array_of_optional_objects_is_refused_the_same_way()
    {
        var diagnostics = Check("""
            class House { rooms: int, }

            fn main(): int {
                let places: (?House)[] = [null];
                var n = 0;
                for (h in places) { if (h != null) { n = n + h.rooms; } }
                return n;
            }
            """);

        Assert.Contains(diagnostics, d => d.Code == "LYR-SEM0091");
    }
}
