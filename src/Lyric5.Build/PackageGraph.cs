namespace Lyric5.Build;

/// <summary>
/// The packages a program is built from (design/v5/spec/07 V7, 11 W2): its own and, through
/// <c>[dependencies]</c>, every package those depend on, each read once — from its directory, or
/// from the revision of a git repository it names, by way of the user's cache (P9).
///
/// <para>One package of each name (P3): two sources for one name — directories, repositories, or
/// revisions of one — are refused, unless the root's <c>[override]</c> picks the one: read
/// wherever the graph asks for the package, the replaced one never read, not even fetched (P7). A
/// dependency names its package: the manifest it finds says the same name.</para>
/// </summary>
/// <param name="Root">The package the program is built from.</param>
/// <param name="Packages">Every package of the graph by name, the root's included.</param>
public sealed record PackageGraph(Manifest Root, IReadOnlyDictionary<string, Manifest> Packages)
{
    /// <summary>Each package's modules (07 M1): its name to its <c>src/</c>.</summary>
    public IReadOnlyDictionary<string, string> SourceRoots =>
        Packages.ToDictionary(p => p.Key, p => p.Value.SourceRoot, StringComparer.Ordinal);

    /// <summary>What each package declares it imports from (P6) — not what those import.</summary>
    public IReadOnlyDictionary<string, IReadOnlySet<string>> Declared =>
        Packages.ToDictionary(p => p.Key,
            p => (IReadOnlySet<string>)p.Value.Dependencies.Select(d => d.Name).ToHashSet(StringComparer.Ordinal),
            StringComparer.Ordinal);

    /// <summary>Reads the graph from <paramref name="root"/>; what comes from git, through
    /// <paramref name="git"/> — the user's cache, fetching allowed, when none is given.</summary>
    /// <exception cref="ManifestException">A dependency is refused: no package there, another name,
    /// a second source for one name, a revision that cannot be read.</exception>
    public static PackageGraph Resolve(Manifest root, GitCache? git = null)
    {
        var overrides = root.Overrides.ToDictionary(o => o.Name, StringComparer.Ordinal);
        var packages = new Dictionary<string, Manifest>(StringComparer.Ordinal) { [root.Name] = root };
        // Where each package was read from: a second source for its name is refused (P3).
        var sources = new Dictionary<string, Dependency>(StringComparer.Ordinal)
        {
            [root.Name] = new Dependency(root.Name, root.Root, null, 0),
        };
        var fromGit = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<Manifest>([root]);
        while (pending.TryDequeue(out var package))
        {
            foreach (var dependency in package.Dependencies)
            {
                // An override answers for the package wherever it is asked for; what the manifest
                // names is then not read at all (P7).
                var at = overrides.TryGetValue(dependency.Name, out var replaced) ? replaced : dependency;
                var declaring = ReferenceEquals(at, replaced) ? root.File : package.File;
                // A package from git is its repository's package content (11 W2 P8): a directory
                // beside it is no part of it.
                if (at.Path is not null && !ReferenceEquals(at, replaced) && fromGit.Contains(package.Name))
                    throw new ManifestException("LYR-PKG0004", declaring, at.Line, 1,
                        $"'{package.Name}' is read from git and names '{dependency.Name}' by a directory, which is no part of "
                        + $"its package — a package from git names its dependencies by git, or [override] in '{root.Name}' replaces '{dependency.Name}'");
                if (sources.TryGetValue(dependency.Name, out var known))
                {
                    if (!SameSource(known, at))
                        throw new ManifestException("LYR-PKG0005", declaring, at.Line, 1,
                            $"'{dependency.Name}' is read from {known.Describe()} and from {at.Describe()} — a program holds one "
                            + $"package of each name (07 P3); [override] in '{root.Name}' picks the one");
                    continue;
                }
                string directory;
                if (at.Git is not null)
                {
                    directory = (git ??= GitCache.ForUser(offline: false)).Checkout(at, declaring);
                    fromGit.Add(dependency.Name);
                }
                else directory = at.Path!;
                var file = Path.Combine(directory, "lyric.toml");
                if (!File.Exists(file))
                    throw new ManifestException("LYR-PKG0004", declaring, at.Line, 1,
                        $"there is no package at {at.Describe()} for '{dependency.Name}' — no lyric.toml there");
                var manifest = Manifest.Read(file);
                if (manifest.Name != dependency.Name)
                    throw new ManifestException("LYR-PKG0004", declaring, at.Line, 1,
                        $"the package at {at.Describe()} is named '{manifest.Name}', not '{dependency.Name}'");
                packages[dependency.Name] = manifest;
                sources[dependency.Name] = at;
                pending.Enqueue(manifest);
            }
        }
        return new PackageGraph(root, packages);
    }

    /// <summary>One directory, or one revision of one repository, as written.</summary>
    private static bool SameSource(Dependency a, Dependency b) =>
        a.Path is not null && b.Path is not null ? SameDirectory(a.Path, b.Path) : a.Git is not null && a.Git == b.Git;

    private static bool SameDirectory(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
