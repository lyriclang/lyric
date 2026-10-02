using Lyric5.Toolchain;

namespace Lyric5.Build;

/// <summary>
/// What is being built and where its files go (design/v5/spec/07 V1, V7; 11 C8, P2, P4, P5).
///
/// <para>A <b>package</b> has a manifest, <c>lyric.toml</c>: its modules live under <c>src/</c>,
/// each named by its path with the package's name in front (07 M1) — <c>src/net/http.lyr</c> in
/// <c>app</c> is <c>app.net.http</c> —, and its program is <c>src/main.lyr</c> (P2), or the module a
/// command names: a module is a library and a program at once (07 M7a). A <b>single file</b> outside
/// every package is a package of its own, named after the file, which imports only <c>std</c> (C8).</para>
///
/// <para><c>out/</c> lies by the manifest — for a single file too, where one stands above it —;
/// without one, by the nearest <c>.git</c>, else in the file's own directory (P5). Inside it:
/// <c>out/&lt;profile&gt;/&lt;target&gt;/&lt;name&gt;</c> (P4) and <c>out/cache/</c> (L7).</para>
/// </summary>
/// <param name="Source">The entry module's file.</param>
/// <param name="Name">The package's name — the executable's, too.</param>
/// <param name="Module">The entry module's path (07 M1).</param>
/// <param name="Root">Where <c>out/</c> goes.</param>
/// <param name="Manifest">The package's manifest; <c>null</c> for a single file.</param>
public sealed record Project(string Source, string Name, string Module, string Root, Manifest? Manifest)
{
    public string OutDir => Path.Combine(Root, "out");

    public string CacheDir => Path.Combine(OutDir, "cache");

    /// <summary>The binary: named after the package for its <c>src/main.lyr</c> (P2), after the
    /// module's last segment for another module run as a program (07 M7a) — beside it, not over it.</summary>
    public string Executable(Profile profile, Target target) =>
        Path.Combine(OutDir, profile.Name(), target.Triple, BinaryName + target.ExecutableSuffix);

    public string BinaryName =>
        Manifest is null || Module == $"{Name}.main" ? Name : Module[(Module.LastIndexOf('.') + 1)..];

    /// <summary>The package's source roots by name, for the module loader — none for a single file,
    /// whose only module is its own.</summary>
    public IReadOnlyDictionary<string, string> PackageRoots =>
        Manifest is { } m ? new Dictionary<string, string> { [m.Name] = m.SourceRoot } : new Dictionary<string, string>();

    /// <summary>The project a file belongs to: the package whose <c>src/</c> holds it — the file is
    /// its entry then —, else the file alone. A file beside a package's modules rather than among
    /// them — a script next to <c>src/</c> — is a single file whose <c>out/</c> still lies by the
    /// manifest; the manifest is read only for a module of the package.</summary>
    /// <exception cref="ManifestException">The package's manifest is refused.</exception>
    public static Project ForFile(string path)
    {
        var full = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(full)!;
        var stem = Path.GetFileNameWithoutExtension(full);
        if (FindManifest(directory) is not { } manifestFile)
            return new Project(full, stem, stem, SingleFileRoot(directory), null);

        var root = Path.GetDirectoryName(manifestFile)!;
        if (!IsUnder(Path.Combine(root, "src"), full))
            return new Project(full, stem, stem, root, null);
        var manifest = Manifest.Read(manifestFile);
        return ModulePathOf(manifest, full) is { } module
            ? new Project(full, manifest.Name, module, manifest.Root, manifest)
            : new Project(full, stem, stem, root, null);
    }

    /// <summary>The package of a directory: the nearest manifest at or above it, built from its
    /// <c>src/main.lyr</c> (P2). <c>null</c> where there is no manifest.</summary>
    /// <exception cref="ManifestException">The manifest is refused.</exception>
    /// <exception cref="InvalidOperationException">The package has no program.</exception>
    public static Project? ForDirectory(string directory)
    {
        if (FindManifest(Path.GetFullPath(directory)) is not { } manifestFile) return null;
        var manifest = Manifest.Read(manifestFile);
        var entry = Path.Combine(manifest.SourceRoot, "main.lyr");
        if (!File.Exists(entry))
            throw new InvalidOperationException(
                $"package '{manifest.Name}' has no program: its src/main.lyr is missing, so it is a library (11 P2), "
                + "and building a library by itself comes with M7 S4");
        return new Project(entry, manifest.Name, $"{manifest.Name}.main", manifest.Root, manifest);
    }

    /// <summary>The module path of a file under a package's <c>src/</c> (07 M1), or <c>null</c>
    /// where the file lies elsewhere.</summary>
    public static string? ModulePathOf(Manifest manifest, string file)
    {
        if (!IsUnder(manifest.SourceRoot, file)) return null;
        var relative = Path.GetRelativePath(manifest.SourceRoot, file);
        if (!relative.EndsWith(".lyr", StringComparison.Ordinal)) return null;
        var segments = relative[..^4].Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Join('.', [manifest.Name, .. segments]);
    }

    /// <summary>Whether <paramref name="file"/> lies below <paramref name="directory"/>.</summary>
    private static bool IsUnder(string directory, string file)
    {
        var relative = Path.GetRelativePath(directory, file);
        return !relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative);
    }

    /// <summary>The nearest <c>lyric.toml</c> at or above <paramref name="start"/>.</summary>
    private static string? FindManifest(string start)
    {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "lyric.toml");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>Where a single file's <c>out/</c> goes: the nearest directory with <c>.git</c>,
    /// else the file's own (P5).</summary>
    private static string SingleFileRoot(string start)
    {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")) || File.Exists(Path.Combine(dir.FullName, ".git")))
                return dir.FullName;
        }
        return start;
    }
}
