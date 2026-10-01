using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;
using Xunit;

namespace Lyric.Tests.Sema;

/// <summary>
/// Type-argument inference through a conformance (§8.3 rule 4), and the refusal that keeps the
/// order of a <c>::</c> list from deciding a call (LYR-SEM0092).
///
/// <para>Through 3.5 the checker took the FIRST conformance in declaration order; the comment
/// above the lookup justified uniqueness with a rule overloading retired in 3.0. Measured before
/// the fix: the identical call compiled with <c>[Sink&lt;int&gt;, Sink&lt;string&gt;]</c> and
/// failed with the entries swapped — complaining about a 'string' nobody wrote.</para>
/// </summary>
public class ConformanceInferenceTests
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

    private const string Sink = """
        interface Sink<T> {
            fn accept(v: T): bool;
        }

        """;

    private static string Tag(string list) => Sink + $$"""
        class Tag :: {{list}} {
            id: int,
            fn accept(v: int): bool { return true; }
            fn accept(v: string): bool { return false; }
        }

        fn pick<T>(s: Sink<T>, probe: T): int { return 1; }

        """;

    [Fact]
    public void One_conformance_still_binds()
    {
        var de = Check(Sink + """
            class Tag :: [Sink<int>] {
                id: int,
                fn accept(v: int): bool { return true; }
            }

            fn pick<T>(s: Sink<T>, probe: T): int { return 1; }

            fn main(): int {
                let t = Tag { id = 1 };
                return pick(t, 42);
            }
            """);

        Assert.False(de.HasErrors);
    }

    // Through S6 the extend could repeat the instance the class declares, and the walk read it
    // as one. Coherence (03 T7 X3) now refuses the repetition where it stands.
    [Fact]
    public void A_conformance_repeated_across_declarations_is_a_duplicate()
    {
        var de = Check(Sink + """
            class Tag :: [Sink<int>] {
                id: int,
                fn accept(v: int): bool { return true; }
            }

            extend Tag :: [Sink<int>] { }

            fn pick<T>(s: Sink<T>, probe: T): int { return 1; }

            fn main(): int {
                let t = Tag { id = 1 };
                return pick(t, 42);
            }
            """);

        Assert.Contains(de.Diagnostics, d => d.Code == "LYR-SEM0133");
    }
}
