using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;
using Xunit;

namespace Lyric.Tests.Sema;

/// <summary>
/// <c>opaque type</c> (v1.15): the alias keeps its identity — nothing converts implicitly, the
/// explicit <c>as</c> to exactly the underlying and back is the one door, equality works within
/// one alias, and everything else (arithmetic, ordering, constraints, f-strings) stays walled
/// off. The wall is the point: a forged or leaked handle is a compile error, not a bug report.
/// </summary>
public class OpaqueTypeTests
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
            ModuleLoader = StdlibLoader.ForRoot(Path.Combine(RepoRoot(), "stdlib5"), sm, de),
        };

        comp.AddModule(new Parser(sm, id, de).ParseModule());
        Semantics.Analyze(comp, comp.Resolve(), de);
        return de;
    }

    private const string Entity = "opaque type Entity = int;\n\n";

    [Fact]
    public void A_plain_alias_stays_transparent()
    {
        // The counter-check: without 'opaque' nothing changed.
        var de = Check(
            """
            type Meters = int;

            fn main(): int {
                let m: Meters = 42;
                return m + 1;
            }
            """);
        Assert.False(de.HasErrors, string.Join("\n", de.Diagnostics));
    }
}
