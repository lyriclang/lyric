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

    [Theory]
    [InlineData("[Sink<int>, Sink<string>]")]
    [InlineData("[Sink<string>, Sink<int>]")]
    public void Two_conformances_refuse_the_inference_in_either_order(string list)
    {
        var de = Check(Tag(list) + """
            fn main(): int {
                let t = Tag { id = 1 };
                return pick(t, 42);
            }
            """);

        // ONE sentence: no SEM0060 about the unbound T behind it, and no SEM0001 about the
        // probe argument — the cause is reported, the consequences stay quiet.
        var error = Assert.Single(de.Diagnostics, d => d.Severity == Severity.Error);
        Assert.Equal("LYR-SEM0092", error.Code);
        Assert.Contains("Sink<int>", error.Message, StringComparison.Ordinal);
        Assert.Contains("Sink<string>", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_written_type_argument_settles_it()
    {
        var de = Check(Tag("[Sink<int>, Sink<string>]") + """
            fn main(): int {
                let t = Tag { id = 1 };
                return pick<int>(t, 42);
            }
            """);

        Assert.False(de.HasErrors);
    }

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

    [Fact]
    public void A_conformance_repeated_across_declarations_is_still_one()
    {
        // The extend repeats the instance the class already declares. InterfacesOf deduplicates
        // by instance across the whole walk, so this must NOT read as two conformances — the
        // refusal is about a choice, and here there is none.
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

        Assert.False(de.HasErrors);
    }
}
