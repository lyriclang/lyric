using System.Runtime.CompilerServices;
using Lyric.Compiler;
using Lyric.Core;
using Xunit;

namespace Lyric.Tests.Sema;

/// <summary>
/// The repository holds itself to its own diagnostics: every real Lyric file — the standard
/// library, the examples, the project templates — checks in complete silence. The formatter's
/// corpus rule, applied to warnings: a new warning that fires on the corpus either found real
/// dirt (clean it in the same commit) or is wrong (fix it before it ships).
/// </summary>
public class CorpusWarningTests
{
    private static string RepoRoot([CallerFilePath] string thisFile = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));

    public static TheoryData<string> Files()
    {
        var data = new TheoryData<string>();
        foreach (var root in new[] { "stdlib", "stdlib-tests", "examples", "templates" })
        foreach (var file in Directory.GetFiles(Path.Combine(RepoRoot(), root), "*.lyr",
                     SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(RepoRoot(), file);

            // The corpus is what the repository TRACKS; build output is not in it — the same
            // rule the formatter's corpus follows, for the same reason.
            var segments = relative.Split(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (segments.Any(s => s is "bin" or "obj" or "out")) continue;

            data.Add(relative);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void The_repository_checks_in_silence(string relativePath)
    {
        // The roots the file is actually compiled with. A corpus file belongs to a project —
        // the templates have a lyric.json, and their test files import their source root — so
        // checking it without one would report an import that resolves perfectly well in every
        // real compile, which is how a corpus rule stops meaning anything.
        var project = ProjectFile.Discover(
            Path.GetDirectoryName(Path.Combine(RepoRoot(), relativePath))!);

        var options = new CompilerOptions
        {
            StdlibRoot = Path.Combine(RepoRoot(), "stdlib"),
            SourceRoot = project?.SourceRoot,
            NativeRoots = project?.NativeRoots,
            DependencyRoots = project?.Dependencies,
        };

        // A standard library file is not an entry file — as one it may not even declare its
        // natives. It is checked the way it is ever compiled: loaded through the std root, by a
        // probe that imports it.
        //
        // The test is on the FIRST SEGMENT and not on the prefix, which is what it was until
        // 4.5: 'stdlib-tests\…' starts with 'stdlib', so every one of the standard library's
        // own test files went through the probe as 'import tests.math_tests' — a path that
        // resolves nowhere, whose error sat in the probe file and was dropped as the harness's
        // noise. Sixteen files were in the corpus and none of them was read.
        var result = SegmentsOf(relativePath)[0] == "stdlib"
            ? SourceCompiler.Check(ScriptSource.FromBuffer(
                    Path.Combine(RepoRoot(), "corpus_probe.lyr"),
                    $"import {ModulePathOf(relativePath)};\n"),
                options)
            : SourceCompiler.Check(Path.Combine(RepoRoot(), relativePath), options);

        // The probe's own line may warn — a bare 'import std.string;' shadows the builtin type,
        // by design. That is the harness's noise, not the module's; only diagnostics OUTSIDE the
        // probe file speak about the corpus.
        var findings = result.Diagnostics.Diagnostics.Where(d =>
                !d.Span.File.IsValid
                || !result.Sources.GetPath(d.Span.File)
                    .EndsWith("corpus_probe.lyr", StringComparison.Ordinal))
            .ToList();

        Assert.True(findings.Count == 0,
            $"{relativePath} does not check in silence:\n" + string.Join("\n",
                findings.Select(d =>
                    $"{d.Severity.ToDisplayString()}[{d.Code}]: {d.Message}")));
    }

    /// <summary>stdlib/std/io/file.lyr → std.io.file</summary>
    private static string ModulePathOf(string relativePath) =>
        string.Join('.', SegmentsOf(relativePath).Skip(1)); // drop the 'stdlib' root segment

    private static string[] SegmentsOf(string relativePath) =>
        Path.ChangeExtension(relativePath, null)!
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
