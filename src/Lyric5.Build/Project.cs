using Lyric5.Toolchain;

namespace Lyric5.Build;

/// <summary>A package without <c>src/main.lyr</c>: a library, which <c>build</c> checks
/// and nothing runs.</summary>
public sealed class LibraryException(Manifest manifest, PackageGraph graph)
    : InvalidOperationException($"package '{manifest.Name}' is a library — it has no src/main.lyr and no [[bin]], so there is no program")
{
    public Manifest Manifest { get; } = manifest;
    public PackageGraph Graph { get; } = graph;
}

/// <summary>
/// What is being built and where its files go.
///
/// <para>A <b>package</b> has a manifest, <c>lyric.toml</c>: its modules live under <c>src/</c>,
/// each named by its path with the package's name in front — <c>src/net/http.lyr</c> in
/// <c>app</c> is <c>app.net.http</c>; <c>src/lib.lyr</c> alone is the package's root module,
/// <c>app</c> —, and its program is <c>src/main.lyr</c>, or the module a
/// command names: a module is a library and a program at once. A <b>single file</b> outside
/// every package is a package of its own, named after the file, which imports only <c>std</c>.</para>
///
/// <para><c>out/</c> lies by the manifest — for a single file too, where one stands above it —;
/// without one, by the nearest <c>.git</c>, else in the file's own directory. Inside it:
/// <c>out/&lt;profile&gt;/&lt;target&gt;/&lt;name&gt;</c> and <c>out/cache/</c>.</para>
/// </summary>
/// <param name="Source">The entry module's file.</param>
/// <param name="Name">The package's name — the executable's, too.</param>
/// <param name="Module">The entry module's path.</param>
/// <param name="Root">Where <c>out/</c> goes.</param>
/// <param name="Manifest">The package's manifest; <c>null</c> for a single file.</param>
public sealed record Project(string Source, string Name, string Module, string Root, Manifest? Manifest)
{
    /// <summary>The packages the program is built from: the package's own and its
    /// dependencies; <c>null</c> for a single file, which has std alone.</summary>
    public PackageGraph? Graph { get; init; }

    /// <summary>The name a <c>[[bin]]</c> gives the program; <c>null</c> for every other.</summary>
    public string? Binary { get; init; }

    /// <summary>The package's <c>tests/</c>, where the program is <c>lyric test</c>'s;
    /// <c>null</c> for every other.</summary>
    public string? TestRoot { get; init; }

    /// <summary>The entry module's text where the toolchain writes it — the test program —;
    /// <c>null</c> where <see cref="Source"/> holds it.</summary>
    public string? EntryText { get; init; }

    public string OutDir => Path.Combine(Root, "out");

    public string CacheDir => Path.Combine(OutDir, "cache");

    /// <summary>The binary: named after the package for its <c>src/main.lyr</c>, as its <c>[[bin]]</c>
    /// says for another program, after the module's last segment for another module run as a
    /// program — beside it, not over it; the test program's under <c>test/</c>, where no
    /// program's name can reach it.</summary>
    public string Executable(BuildProfile profile, Target target) => TestRoot is null
        ? Path.Combine(OutDir, profile.Name, target.Triple, BinaryName + target.ExecutableSuffix)
        : Path.Combine(OutDir, profile.Name, target.Triple, "test", BinaryName + target.ExecutableSuffix);

    public string BinaryName =>
        Library is not null ? "lib" + Name
        : Binary ?? (Manifest is null || Module == $"{Name}.main" ? Name : Module[(Module.LastIndexOf('.') + 1)..]);

    /// <summary>For a library C links (<c>lyric build --lib</c>), which kind; <c>null</c> for a
    /// program. Its entry is the package's root module, <c>src/lib.lyr</c>.</summary>
    public LibraryKind? Library { get; init; }

    /// <summary>The library C links, beside where the package's binaries go: <c>lib&lt;name&gt;.a</c>,
    /// or shared — <c>lib&lt;name&gt;.so</c>, <c>lib&lt;name&gt;.dylib</c>, <c>&lt;name&gt;.dll</c>.</summary>
    public string LibraryFile(BuildProfile profile, Target target) =>
        Path.Combine(OutDir, profile.Name, target.Triple, Library != LibraryKind.Shared ? $"lib{Name}.a" : target.Os switch
        {
            TargetOs.Windows => $"{Name}.dll",
            TargetOs.MacOs => $"lib{Name}.dylib",
            _ => $"lib{Name}.so",
        });

    /// <summary>The library's header, <c>&lt;name&gt;.h</c>, beside it.</summary>
    public string HeaderFile(BuildProfile profile, Target target) => Path.Combine(OutDir, profile.Name, target.Triple, $"{Name}.h");

    /// <summary>The library of the package of a directory — the nearest manifest at or above it
    /// —: its root module, <c>src/lib.lyr</c>, which may be missing. <c>null</c> where there is no
    /// manifest.</summary>
    /// <exception cref="ManifestException">The manifest, or one of the graph's, is refused.</exception>
    public static Project? LibraryOf(string directory, LibraryKind kind, bool offline = false, bool locked = false)
    {
        if (FindManifest(Path.GetFullPath(directory)) is not { } manifestFile) return null;
        var manifest = Manifest.Read(manifestFile);
        var root = Path.Combine(manifest.SourceRoot, "lib.lyr");
        return new Project(root, manifest.Name, manifest.Name, manifest.Root, manifest)
        {
            Graph = Resolve(manifest, offline, locked: locked),
            Library = kind,
        };
    }

    /// <summary>Every package's source root by name, for the module loader — none for a single file,
    /// whose only module is its own.</summary>
    public IReadOnlyDictionary<string, string> PackageRoots =>
        Graph is null ? ModuleRoots ?? new Dictionary<string, string>()
        : ModuleRoots is null ? Graph.SourceRoots
        : Graph.SourceRoots.Where(r => !ModuleRoots.ContainsKey(r.Key)).Concat(ModuleRoots).ToDictionary(r => r.Key, r => r.Value, StringComparer.Ordinal);

    /// <summary>Each package's lint levels by name (spec 16 §5): its <c>[lints]</c>, and for the root
    /// package — or the file built alone — the command line's in front (<c>-A</c>, <c>-W</c>,
    /// <c>-D</c>).</summary>
    /// <summary>What the command line says of the lints for this build.</summary>
    public LintArgs LintArgs { get; init; } = LintArgs.None;

    public IReadOnlyDictionary<string, Lyric.Core.LintSettings> LintLevels
    {
        get
        {
            var commandLine = LintArgs.Rules;
            var levels = new Dictionary<string, Lyric.Core.LintSettings>(StringComparer.Ordinal);
            foreach (var (name, manifest) in Graph?.Packages ?? new Dictionary<string, Manifest>())
                levels[name] = new Lyric.Core.LintSettings(name == Name ? commandLine : [], manifest.Lints);
            if (!levels.ContainsKey(Name)) levels[Name] = new Lyric.Core.LintSettings(commandLine, Manifest?.Lints ?? []);
            return levels;
        }
    }

    /// <summary>What each package declares it imports from — a module space of a
    /// build script's own imports from none.</summary>
    public IReadOnlyDictionary<string, IReadOnlySet<string>> DeclaredDependencies =>
        Graph?.Declared ?? ModuleRoots?.ToDictionary(m => m.Key, _ => (IReadOnlySet<string>)new HashSet<string>(), StringComparer.Ordinal)
        ?? new Dictionary<string, IReadOnlySet<string>>();

    /// <summary>A single file's own module spaces by name — a build script's <c>build/</c>
    /// —; <c>null</c> for every other.</summary>
    public IReadOnlyDictionary<string, string>? ModuleRoots { get; init; }

    /// <summary>The project a file belongs to: the package whose <c>src/</c> holds it — the file is
    /// its entry then —, else the file alone. A file beside a package's modules rather than among
    /// them — a script next to <c>src/</c> — is a single file whose <c>out/</c> still lies by the
    /// manifest; the manifest is read only for a module of the package.</summary>
    /// <param name="offline">Whether packages from git come from the user's cache alone.</param>
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
    /// —: its <c>src/main.lyr</c>, named after the package, then each <c>[[bin]]</c>.
    /// <c>null</c> where there is no manifest.</summary>
    /// <param name="offline">Whether packages from git come from the user's cache alone.</param>
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
    /// it —: named after the package, its entry the module <c>lyric test</c> writes. A
    /// library has one as well. <c>null</c> where there is no manifest.</summary>
    /// <param name="offline">Whether packages from git come from the user's cache alone.</param>
    /// <exception cref="ManifestException">The manifest, or one of the graph's, is refused.</exception>
    /// <param name="locked">Whether the lock is taken as it is (<c>--locked</c>).</param>
    /// <param name="write">Whether the lock is written; the editor reads a package and writes nothing.</param>
    public static Project? TestsOf(string directory, bool offline = false, bool locked = false, bool write = true)
    {
        if (FindManifest(Path.GetFullPath(directory)) is not { } manifestFile) return null;
        var manifest = Manifest.Read(manifestFile);
        return new Project(Path.Combine(manifest.Root, "tests", TestRun.DriverModule + ".lyr"), manifest.Name,
            $"{manifest.Name}.{TestRun.DriverModule}", manifest.Root, manifest)
        {
            Graph = Resolve(manifest, offline, locked: locked, write: write),
            Binary = manifest.Name,
            TestRoot = Path.Combine(manifest.Root, "tests"),
        };
    }

    /// <summary>The graph of <paramref name="manifest"/>'s program, read at the commits its
    /// <c>lyric.lock</c> holds; the lock is written afterwards with what the graph read —
    /// or, <paramref name="locked"/>, taken as it is and not written; not <paramref name="write"/>,
    /// left as it is. The source trees are read later, by the build that needs them
    /// (<see cref="PackageGraph.Trees"/>); the lock keeps those a manifest of the graph still names so.</summary>
    /// <exception cref="ManifestException">The graph, or the lock, is refused.</exception>
    public static PackageGraph Resolve(Manifest manifest, bool offline, Func<string, bool>? refresh = null,
        Func<Locked, bool>? keep = null, bool locked = false, bool write = true)
    {
        var lockFile = LockFile.For(manifest);
        var all = LockFile.Read(lockFile).Where(keep ?? (_ => true)).ToList();
        var held = all.Where(l => !l.IsTree).ToList();
        var git = GitCache.ForUser(offline, refresh);
        var graph = PackageGraph.Resolve(manifest, git, held, frozen: locked);
        // Each build script with dependencies of its own: a graph of its own, read
        // at the commits the same lock holds, and held in it beside the program's.
        var scripts = new Dictionary<string, PackageGraph>(StringComparer.Ordinal);
        foreach (var package in graph.Packages.Values.OrderBy(p => p.Name, StringComparer.Ordinal))
            if (package.BuildScript is not null && package.BuildDependencies.Count > 0)
                scripts[package.Name] = PackageGraph.Resolve(package.ScriptRoot(manifest), git, held, frozen: locked);
        var owners = scripts.Values.Prepend(graph).SelectMany(g => g.Packages.Values).GroupBy(p => p.Name)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var trees = all.Where(l => l.IsTree && owners.TryGetValue(l.Of!, out var owner)
                                   && owner.Vendor.Any(t => t.Name == l.Name && t.Git == l.Git)).ToList();
        var entries = LockFile.Sorted(graph.Locked.Concat(scripts.Values.SelectMany(s => s.Locked)).Distinct().Concat(trees)).ToList();
        if (locked) LockFile.RefuseChange(lockFile, entries);
        else if (write) LockFile.Write(lockFile, entries);
        return graph with
        {
            Scripts = scripts,
            Trees = new SourceTrees(lockFile, git, Lyric5.Toolchain.UserCache.Root, offline, frozen: locked, write, trees),
        };
    }

    /// <summary>The module path of a file under a package's <c>src/</c>, or <c>null</c>
    /// where the file lies elsewhere. <c>src/lib.lyr</c> is the root module, named by the package
    /// alone.</summary>
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
    /// else the file's own.</summary>
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

/// <summary>What <c>lyric build --lib</c> makes: an archive C links into its program, or a library
/// the system's loader loads.</summary>
public enum LibraryKind { Static, Shared }
