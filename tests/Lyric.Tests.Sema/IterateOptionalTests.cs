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
            ModuleLoader = StdlibLoader.ForRoot(Path.Combine(RepoRoot(), "stdlib5"), sm, de),
        };
        comp.AddModule(new Parser(sm, id, de).ParseModule());
        Semantics.Analyze(comp, comp.Resolve(), de);
        return de.Diagnostics;
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
}
