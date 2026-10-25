using Lyric.Compiler;
using Lyric5.Build;

namespace Lyric.Lsp.Analysis;

/// <summary>
/// A package as the editor analyses it: the nearest <c>lyric.toml</c> above a file, and every
/// module under its <c>src/</c> and <c>tests/</c> in one compilation — the compilation
/// <c>lyric test</c> reads, so the editor and the command line answer the same question.
///
/// <para>The graph is read the way a build reads it, with two differences: packages from git come
/// from the user's cache alone, and <c>lyric.lock</c> is left as it is. A keystroke fetches
/// nothing and writes nothing.</para>
/// </summary>
public sealed class PackageUnit
{
    public const string ManifestName = "lyric.toml";
    public const string LockName = "lyric.lock";

    private readonly Manifest _manifest;

    private PackageUnit(Manifest manifest, string testRoot, IReadOnlyList<(string File, string Module)> files,
        CompilerOptions options)
    {
        _manifest = manifest;
        TestRoot = testRoot;
        Files = files;
        Options = options;
    }

    /// <summary>The directory of the manifest: the unit's key.</summary>
    public string Root => _manifest.Root;

    public string ManifestFile => _manifest.File;

    public string SourceRoot => _manifest.SourceRoot;

    public string TestRoot { get; }

    /// <summary>The modules on disk, by file and module path.</summary>
    public IReadOnlyList<(string File, string Module)> Files { get; }

    /// <summary>What the unit is checked under, without the editor's buffers.</summary>
    public CompilerOptions Options { get; }

    /// <summary>
    /// The package of a file, or <c>null</c> where no manifest lies above it; <paramref
    /// name="refused"/> says why a manifest that is there could not be read — the file is then
    /// analysed alone, and the reason goes to the log rather than to a document that is not at
    /// fault.
    /// </summary>
    public static PackageUnit? Find(string path, out string? refused)
    {
        refused = null;
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (directory is null || Project.FindManifest(directory) is null) return null;
        try
        {
            if (Project.TestsOf(directory, offline: true, write: false) is not { } tests) return null;
            var (files, options) = TestRun.PackageUnit(tests);
            return new PackageUnit(tests.Manifest!, tests.TestRoot!, files, options);
        }
        catch (ManifestException broken)
        {
            refused = $"{broken.File}:{broken.Line}:{broken.Column}: {broken.Code}: {broken.Message}";
            return null;
        }
    }

    /// <summary>The module path of a file of this package — under <c>src/</c> or <c>tests/</c> —,
    /// saved or not; <c>null</c> for any other file, which is not one of its modules.</summary>
    public string? ModuleOf(string path)
    {
        var full = Path.GetFullPath(path);
        if (!full.EndsWith(".lyr", StringComparison.Ordinal)) return null;
        if (Project.ModulePathOf(_manifest, full) is { } module) return module;
        return IsUnder(full, TestRoot) ? TestRun.TestModuleOf(_manifest, TestRoot, full) : null;
    }

    /// <summary>Whether a change to this file changes what the unit is: its manifest or its lock.</summary>
    public static bool IsManifest(string path) =>
        Path.GetFileName(path) is ManifestName or LockName;

    /// <summary>The options a file outside every package is checked under: the file alone, with
    /// std, as <c>lyric check</c> checks it.</summary>
    public static CompilerOptions SingleFileOptions(string path)
    {
        var full = Path.GetFullPath(path);
        var stem = Path.GetFileNameWithoutExtension(full);
        return Pipeline.CheckOptions(new Project(full, stem, stem, Path.GetDirectoryName(full)!, null));
    }

    private static bool IsUnder(string full, string directory)
    {
        var relative = Path.GetRelativePath(directory, full);
        return !relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative);
    }
}
