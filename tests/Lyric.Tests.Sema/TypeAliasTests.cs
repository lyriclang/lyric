using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;
using Xunit;

namespace Lyric.Tests.Sema;

/// <summary>A type alias is the type it names, and <c>opaque type</c> is no form of the language.</summary>
public class TypeAliasTests
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

    [Fact]
    public void A_plain_alias_stays_transparent()
    {
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

    [Fact]
    public void An_opaque_type_is_refused_once_and_then_reads_as_an_alias()
    {
        var de = Check(
            """
            opaque type Entity = int;

            fn main(): int {
                let e: Entity = 42;
                return e + 1;
            }
            """);
        var error = Assert.Single(de.Diagnostics, d => d.Severity == Severity.Error);
        Assert.Equal("LYR-SEM0175", error.Code);
    }
}
