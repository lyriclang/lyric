using Lyric.AST;
using Lyric.Resolver;

namespace Lyric.Lsp.Analysis;

/// <summary>A test of a package as the editor shows it: its name as <c>lyric test</c> runs it,
/// whether it is a function or a documentation block, and the line it stands on.</summary>
public sealed record EditorTest(string Name, string Kind, int Line);

/// <summary>
/// The tests a file of a package holds: each <c>@Test</c> function — named <c>module.function</c>,
/// as <c>lyric test</c> names it — and each runnable block of its documentation, with the name
/// <c>lyric test</c> gives it. Found, not run: the client runs <c>lyric test -f</c> with the name.
/// </summary>
public static class TestLensProvider
{
    public const string Function = "test";
    public const string DocTest = "doc-test";

    public static IReadOnlyList<EditorTest> Of(AnalysisSnapshot snapshot, PackageUnit unit, string path)
    {
        var tests = new List<EditorTest>();
        var model = snapshot.Model;
        var module = model.Compilation.Modules.FirstOrDefault(m => ReferenceEquals(model.Compilation.AstOf(m), snapshot.Root));
        var mark = model.Compilation.FindModule(["std", "core"])?.Members.LookupLocal("Test");
        if (module is not null && mark is not null)
        {
            foreach (var fn in snapshot.Root.Declarations.OfType<FunctionDecl>())
                if (fn.Attributes.Any(a => ReferenceEquals(model.Types.RefOf(a), mark)))
                    tests.Add(new EditorTest($"{module.FullName}.{fn.Name}", Function,
                        SpanMapper.ToRange(snapshot.Sources, fn.NameSpan).Start.Line));
        }

        // the documentation's blocks: of a module under src/, as lyric test reads them
        if (unit.ModuleOf(path) is { } name && Lyric5.Build.Project.ModulePathOf(unit.Manifest, path) is not null)
        {
            var relative = Path.GetRelativePath(unit.Root, path).Replace('\\', '/');
            try
            {
                foreach (var doc in Lyric5.Doc.DocTests.Extract(relative, name, snapshot.Text))
                    tests.Add(new EditorTest(doc.Name, DocTest, doc.Line - 1));
            }
            catch (InvalidDataException)
            {
                // a block the Book's form refuses: lyric test says so, and there is nothing to run
            }
        }
        return tests;
    }
}
