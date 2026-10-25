using System.IO.Compression;
using Lyric5.Doc.Api;
using Lyric5.Toolchain;

namespace Lyric5.Build;

/// <summary>
/// <c>lyric api</c>: a package's public surface as text or JSON, and with <c>--diff</c> what changed
/// against the package at a git revision — read from the repository, never from the working tree.
/// </summary>
public static class ApiRun
{
    /// <summary>The surface of the package whose manifest lies in <paramref name="root"/> as the
    /// revision <paramref name="revision"/> of its repository holds it: the package's directory taken
    /// out with <c>git archive</c> into a fresh one and checked there. <c>null</c> after saying why —
    /// no repository, a revision git does not know, a package that did not check.</summary>
    public static Surface? SurfaceAt(string root, string revision, TextWriter error)
    {
        // Where the package lies in its repository, as git says it: a path computed against the
        // top-level would climb out of it where the two are spelled through different links
        // (macOS's /var is /private/var).
        var top = Git(root, ["rev-parse", "--show-toplevel", "--show-prefix"]);
        if (top is null || top.ExitCode != 0)
        {
            Say.Error(error, "LYR-CLI0022", $"'{root}' is in no git repository: --diff reads the package at a revision of its repository");
            return null;
        }
        var answer = top.Stdout.Replace("\r\n", "\n").Split('\n');
        var repository = answer[0].Trim();
        var prefix = answer.Length > 1 ? answer[1].Trim().TrimEnd('/') : "";
        var inside = prefix.Length == 0 ? "." : prefix;
        var scratch = Directory.CreateTempSubdirectory("lyric-api-").FullName;
        try
        {
            var archive = Path.Combine(scratch, "package.zip");
            var listed = Git(repository, ["archive", "--format=zip", "-o", archive, revision, "--", inside == "." ? "." : inside]);
            if (listed is null || listed.ExitCode != 0)
            {
                Say.Error(error, "LYR-CLI0022", $"git cannot read '{revision}': {(listed?.Stderr ?? "").Trim()}");
                return null;
            }
            var tree = Path.Combine(scratch, "tree");
            ZipFile.ExtractToDirectory(archive, tree);
            var old = inside == "." ? tree : Path.Combine(tree, inside);
            if (!File.Exists(Path.Combine(old, "lyric.toml")))
            {
                Say.Error(error, "LYR-CLI0022", $"'{revision}' holds no lyric.toml at '{inside}'");
                return null;
            }
            return PackageSurface(old, error);
        }
        finally
        {
            try { Directory.Delete(scratch, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>The surface of the package found from <paramref name="directory"/>; <c>null</c>
    /// after saying why.</summary>
    public static Surface? PackageSurface(string directory, TextWriter error)
    {
        Manifest manifest;
        PackageGraph graph;
        try
        {
            if (Project.ProgramsOf(directory, offline: true) is not { } programs)
            {
                Say.Error(error, "LYR-CLI0004", "no lyric.toml here or above", "lyric5 api --std prints the standard library's surface");
                return null;
            }
            manifest = programs[0].Manifest!;
            graph = programs[0].Graph!;
        }
        catch (ManifestException refused)
        {
            Say.Manifest(error, refused);
            return null;
        }
        catch (LibraryException library)
        {
            manifest = library.Manifest;
            graph = library.Graph;
        }
        return DocRun.PackageSurface(manifest, graph, error, warnings: false);
    }

    private static ProcessRunner.Result? Git(string directory, IEnumerable<string> arguments) =>
        ProcessRunner.TryRun("git", ["-C", directory, .. arguments], TimeSpan.FromMinutes(2));
}
