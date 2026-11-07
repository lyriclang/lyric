using Lyric.Core;
using Lyric.Ir;
using Lyric.Ir.Lowering;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Tests.Ir;

/// <summary>
/// The pub-roots rule (§4.6 of the specification, since 2.0): a compile WITHOUT an entry point —
/// a library — takes the `pub` functions of its compiled modules as reachability roots, so a
/// library's surface decides its contents.
///
/// <para>The rule rides an OPT-IN of the drivers (<c>libraryRoots: true</c> through
/// <c>SourceCompiler</c>): a test lowering a bare snippet through the raw API keeps every function
/// it wrote, which the last test here pins — hundreds of existing fixtures depend on it.</para>
/// </summary>
public class ExportRootTests
{
    private static string RepoRoot([System.Runtime.CompilerServices.CallerFilePath] string thisFile = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));

    private static IrModule Lower(string source, bool libraryRoots)
    {
        var sm = new SourceManager();
        var id = sm.AddVirtual("lib.lyr", source);
        var de = new DiagnosticEngine(sm);

        // With the stdlib: an attribute marker comes from 'std.core', and the pub-roots cases do
        // not mind carrying a loader they never reach for.
        var comp = new Compilation(sm, de)
        {
            ModuleLoader = StdlibLoader.ForRoot(Path.Combine(RepoRoot(), "stdlib5"), sm, de),
        };
        comp.AddModule(new Parser(sm, id, de).ParseModule());
        var binding = comp.Resolve();
        var types = Semantics.Analyze(comp, binding, de);
        if (de.HasErrors)
        {
            var writer = new StringWriter();
            de.RenderText(writer);
            Assert.Fail("source did not compile: " + writer);
        }

        // optimize:false — these tests pin WHICH functions survive, and the inliner folding a
        // single-caller helper into its caller would blur exactly that.
        var ir = ModuleLowerer.Lower(comp, binding, types, de, verify: true, optimize: false,
            libraryRoots: libraryRoots);
        Assert.NotNull(ir);
        return ir!;
    }

    private const string Library = """
        pub fn surface(): int { return helper(); }
        fn helper(): int { return 7; }
        fn orphan(): int { return 8; }
        """;

    [Fact]
    public void A_library_prunes_from_its_pub_surface()
    {
        var module = Lower(Library, libraryRoots: true);

        // 'surface' is the surface, 'helper' is reachable through it — 'orphan' is neither and
        // does not ship.
        Assert.Contains(module.Functions, f => f.Name.EndsWith(".surface"));
        Assert.Contains(module.Functions, f => f.Name.EndsWith(".helper"));
        Assert.DoesNotContain(module.Functions, f => f.Name.EndsWith(".orphan"));
    }

    [Fact]
    public void The_export_roots_follow_the_renumbering()
    {
        var module = Lower(Library, libraryRoots: true);

        // After the prune the recorded roots must still point at the pub functions — an index
        // into the pre-prune numbering would name an arbitrary survivor.
        var root = Assert.Single(module.ExportRoots);
        Assert.EndsWith(".surface", module.Functions[root.Value].Name);
    }

    /// <summary>An attribute is inert: what it marks stays only when something calls it, or when it
    /// is pub in a library.</summary>
    private const string Attributed = """
        import std.core { OnFunction };

        pub struct Hook :: [OnFunction] { }

        fn dead_before(): int { return 41; }

        @Hook
        fn hooked(): int { return 42; }

        pub fn surface(): int { return 7; }
        """;

    [Fact]
    public void An_attribute_keeps_no_function_alive()
    {
        var module = Lower(Attributed, libraryRoots: true);

        // Not pub and called by nobody, so it goes with 'dead_before'; 'surface' is the control.
        Assert.DoesNotContain(module.Functions, f => f.Name.EndsWith(".hooked"));
        Assert.DoesNotContain(module.Functions, f => f.Name.EndsWith(".dead_before"));
        Assert.Contains(module.Functions, f => f.Name.EndsWith(".surface"));
    }

    [Fact]
    public void A_bare_snippet_through_the_raw_api_keeps_everything()
    {
        var module = Lower(Library, libraryRoots: false);

        // The pre-2.0 behavior, deliberately kept for the raw API: no entry point and no export
        // roots means no pruning at all.
        Assert.Contains(module.Functions, f => f.Name.EndsWith(".orphan"));
    }
}
