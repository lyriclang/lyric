using System.Runtime.CompilerServices;
using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Tests.Sema;

/// <summary>
/// A <c>try</c> contributes what its body assigns when every <c>catch</c> leaves (§7.7): the body
/// may have thrown mid-way, but a throw then leads into a clause that returns, throws, breaks or
/// continues — so the one way to the statement after the <c>try</c> is the body's own end. With a
/// catch that falls through, nothing counts, as before.
/// </summary>
public class TryDefiniteAssignmentTests
{
    private static string RepoRoot([CallerFilePath] string thisFile = "")
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

    private const string Head =
        """
        import std.core { Exception };
        fn parse(s: string): int throws Exception {
            if (s == "") { throw Exception { text = "empty" }; }
            return 1;
        }

        """;

    [Fact]
    public void A_try_whose_every_catch_returns_assigns_what_its_body_assigned()
    {
        var de = Check(Head + """
            fn f(s: string): int {
                var n: int;
                try {
                    n = parse(s);
                } catch (e: Exception) {
                    return -1;
                }
                return n;
            }
            """);
        Assert.False(de.HasErrors, string.Join("\n", de.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Fact]
    public void A_catch_that_throws_or_continues_leaves_too()
    {
        var de = Check(Head + """
            class Fatal :: [Throwable] { fn message(): string { return "f"; } }
            fn f(xs: string[]): int throws Fatal {
                var total = 0;
                for (s in xs) {
                    var n: int;
                    try { n = parse(s); }
                    catch (e: Exception) { continue; }
                    catch (_) { throw Fatal { }; }
                    total += n;
                }
                return total;
            }
            """);
        Assert.False(de.HasErrors, string.Join("\n", de.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Fact]
    public void A_catch_that_falls_through_contributes_nothing()
    {
        var de = Check(Head + """
            import std.io.console { println };
            fn f(s: string): int {
                var n: int;
                try { n = parse(s); } catch (e: Exception) { println(e.message()); }
                return n;
            }
            """);
        Assert.Contains(de.Diagnostics, d => d.Code == "LYR-SEM0018");
    }

    [Fact]
    public void Only_what_the_body_assigns_on_its_own_end_counts()
    {
        // 'n' is assigned after a possible throw site inside the body: still fine, because a throw
        // there lands in the returning catch. What is NOT assigned on the body's end does not count.
        var de = Check(Head + """
            fn f(s: string): int {
                var n: int;
                var m: int;
                try {
                    n = parse(s);
                    if (n > 0) { m = 2; }
                } catch (e: Exception) {
                    return -1;
                }
                return n + m;
            }
            """);
        var error = Assert.Single(de.Diagnostics, d => d.Code == "LYR-SEM0018");
        Assert.Contains("m", error.Message);
    }
}
