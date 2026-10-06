using Lyric5.Toolchain;

namespace Lyric5.Build;

/// <summary>A package without <c>src/main.lyr</c> (11 P2): a library, which <c>build</c> checks
/// (07 B6) and nothing runs.</summary>
public sealed class LibraryException(Manifest manifest, PackageGraph graph)
    : InvalidOperationException($"package '{manifest.Name}' is a library — it has no src/main.lyr and no [[bin]], so there is no program (11 P2)")
{
    public Manifest Manifest { get; } = manifest;
    public PackageGraph Graph { get; } = graph;
}

/// <summary>
/// What is being built and where its files go (design/v5/spec/07 V1, V7; 11 C8, P2, P4, P5).
///
/// <para>A <b>package</b> has a manifest, <c>lyric.toml</c>: its modules live under <c>src/</c>,
/// each named by its path with the package's name in front (07 M1) — <c>src/net/http.lyr</c> in
/// <c>app</c> is <c>app.net.http</c>; <c>src/lib.lyr</c> alone is the package's root module,
/// <c>app</c> —, and its program is <c>src/main.lyr</c> (P2), or the module a
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
    /// <summary>The packages the program is built from (07 V7): the package's own and its
    /// dependencies; <c>null</c> for a single file, which has std alone.</summary>
    public PackageGraph? Graph { get; init; }

    /// <summary>The name a <c>[[bin]]</c> gives the program (11 W2 P2); <c>null</c> for every other.</summary>
    public string? Binary { get; init; }

    /// <summary>The package's <c>tests/</c> (07 V4), where the program is <c>lyric test</c>'s;
    /// <c>null</c> for every other.</summary>
    public string? TestRoot { get; init; }

    /// <summary>The entry module's text where the toolchain writes it — the test program —;
    /// <c>null</c> where <see cref="Source"/> holds it.</summary>
    public string? EntryText { get; init; }

    public string OutDir => Path.Combine(Root, "out");

    public string CacheDir => Path.Combine(OutDir, "cache");

    /// <summary>The binary: named after the package for its <c>src/main.lyr</c>, as its <c>[[bin]]</c>
    /// says for another program (P2), after the module's last segment for another module run as a
    /// program (07 M7a) — beside it, not over it; the test program's under <c>test/</c>, where no
    /// program's name can reach it.</summary>
    public string Executable(BuildProfile profile, Target target) => TestRoot is null
        ? Path.Combine(OutDir, profile.Name, target.Triple, BinaryName + target.ExecutableSuffix)
        : Path.Combine(OutDir, profile.Name, target.Triple, "test", BinaryName + target.ExecutableSuffix);

    public string BinaryName =>
        Binary ?? (Manifest is null || Module == $"{Name}.main" ? Name : Module[(Module.LastIndexOf('.') + 1)..]);

    /// <summary>Every package's source root by name, for the module loader — none for a single file,
    /// whose only module is its own.</summary>
    public IReadOnlyDictionary<string, string> PackageRoots =>
        Graph?.SourceRoots ?? new Dictionary<string, string>();

    /// <summary>What each package declares it imports from (07 P6).</summary>
    public IReadOnlyDictionary<string, IReadOnlySet<string>> DeclaredDependencies =>
        Graph?.Declared ?? new Dictionary<string, IReadOnlySet<string>>();

    /// <summary>The project a file belongs to: the package whose <c>src/</c> holds it — the file is
    /// its entry then —, else the file alone. A file beside a package's modules rather than among
    /// them — a script next to <c>src/</c> — is a single file whose <c>out/</c> still lies by the
    /// manifest; the manifest is read only for a module of the package.</summary>
    /// <param name="offline">Whether packages from git come from the user's cache alone (P9).</param>
    /// <exception cref="ManifestException">The package's manifest is refused.</exception>
    public static Project ForFile(string path, bool offline = false, bool locked = false)
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
            ? new Project(full, manifest.Name, module, manifest.Root, manifest) { Graph = Resolve(manifest, offline, locked: locked) }
            : new Project(full, stem, stem, root, null);
    }

    /// <summary>The programs of the package of a directory — the nearest manifest at or above it
    /// —: its <c>src/main.lyr</c>, named after the package, then each <c>[[bin]]</c> (P2).
    /// <c>null</c> where there is no manifest.</summary>
    /// <param name="offline">Whether packages from git come from the user's cache alone (P9).</param>
    /// <exception cref="ManifestException">The manifest, or one of the graph's, is refused.</exception>
    /// <exception cref="LibraryException">The package has no program.</exception>
    /// <param name="locked">Whether the lock is taken as it is (<c>--locked</c>).</param>
    public static IReadOnlyList<Project>? ProgramsOf(string directory, bool offline = false, bool locked = false)
    {
        if (FindManifest(Path.GetFullPath(directory)) is not { } manifestFile) return null;
        var manifest = Manifest.Read(manifestFile);
        var graph = Resolve(manifest, offline, locked: locked);
        var programs = new List<Project>();
        var main = Path.Combine(manifest.SourceRoot, "main.lyr");
        if (File.Exists(main))
            programs.Add(new Project(main, manifest.Name, $"{manifest.Name}.main", manifest.Root, manifest) { Graph = graph });
        foreach (var binary in manifest.Binaries)
        {
            if (!File.Exists(binary.Entry))
                throw new ManifestException("LYR-PKG0002", manifest.File, binary.Line, 1,
                    $"[[bin]] '{binary.Name}' enters at '{Path.GetRelativePath(manifest.Root, binary.Entry).Replace('\\', '/')}', and there is no such file");
            programs.Add(new Project(binary.Entry, manifest.Name, ModulePathOf(manifest, binary.Entry)!, manifest.Root, manifest)
            {
                Graph = graph,
                Binary = binary.Name,
            });
        }
        if (programs.Count == 0) throw new LibraryException(manifest, graph);
        return programs;
    }

    /// <summary>The test program of the package of a directory — the nearest manifest at or above
    /// it — (07 V4): named after the package, its entry the module <c>lyric test</c> writes. A
    /// library has one as well. <c>null</c> where there is no manifest.</summary>
    /// <param name="offline">Whether packages from git come from the user's cache alone (P9).</param>
    /// <exception cref="ManifestException">The manifest, or one of the graph's, is refused.</exception>
    /// <param name="locked">Whether the lock is taken as it is (<c>--locked</c>).</param>
    public static Project? TestsOf(string directory, bool offline = false, bool locked = false)
    {
        if (FindManifest(Path.GetFullPath(directory)) is not { } manifestFile) return null;
        var manifest = Manifest.Read(manifestFile);
        return new Project(Path.Combine(manifest.Root, "tests", TestRun.DriverModule + ".lyr"), manifest.Name,
            $"{manifest.Name}.{TestRun.DriverModule}", manifest.Root, manifest)
        {
            Graph = Resolve(manifest, offline, locked: locked),
            Binary = manifest.Name,
            TestRoot = Path.Combine(manifest.Root, "tests"),
        };
    }

    /// <summary>The graph of <paramref name="manifest"/>'s program, read at the commits its
    /// <c>lyric.lock</c> holds; the lock is written afterwards with what the graph read (07 P5) —
    /// or, <paramref name="locked"/>, taken as it is and not written (the review's M7-9).</summary>
    /// <exception cref="ManifestException">The graph, or the lock, is refused.</exception>
    public static PackageGraph Resolve(Manifest manifest, bool offline, Func<string, bool>? refresh = null,
        Func<Locked, bool>? keep = null, bool locked = false)
    {
        var lockFile = LockFile.For(manifest);
        var held = LockFile.Read(lockFile).Where(keep ?? (_ => true)).ToList();
        var graph = PackageGraph.Resolve(manifest, GitCache.ForUser(offline, refresh), held, frozen: locked);
        if (locked) LockFile.RefuseChange(lockFile, graph.Locked);
        else LockFile.Write(lockFile, graph.Locked);
        return graph;
    }

    /// <summary>The module path of a file under a package's <c>src/</c> (07 M1), or <c>null</c>
    /// where the file lies elsewhere. <c>src/lib.lyr</c> is the root module, named by the package
    /// alone (the review's M7-1).</summary>
    public static string? ModulePathOf(Manifest manifest, string file)
    {
        if (!IsUnder(manifest.SourceRoot, file)) return null;
        var relative = Path.GetRelativePath(manifest.SourceRoot, file);
        if (!relative.EndsWith(".lyr", StringComparison.Ordinal)) return null;
        var segments = relative[..^4].Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments is [Lyric.Resolver.Compilation.RootModuleFile]
            ? manifest.Name
            : string.Join('.', [manifest.Name, .. segments]);
    }

    /// <summary>Whether <paramref name="file"/> lies below <paramref name="directory"/>.</summary>
    private static bool IsUnder(string directory, string file)
    {
        var relative = Path.GetRelativePath(directory, file);
        return !relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative);
    }

    /// <summary>The nearest <c>lyric.toml</c> at or above <paramref name="start"/>.</summary>
    public static string? FindManifest(string start)
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
