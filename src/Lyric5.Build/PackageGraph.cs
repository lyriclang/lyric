namespace Lyric5.Build;

/// <summary>
/// The packages a program is built from (design/v5/spec/07 V7, 11 W2): its own and, through
/// <c>[dependencies]</c>, every package those depend on — read from its directory, or from the
/// revision of a git repository it names, by way of the user's cache (P9).
///
/// <para>One package of each name (P3). Versions of one repository — tags that are semantic
/// versions of one line — are one package: of every version the graph asks for, directly or
/// through a version read on the way, the build takes the greatest, the least that satisfies them
/// all (minimal version selection, P4). Any other second source for a name — a directory, another
/// repository, another revision, another line — is refused, unless the root's <c>[override]</c>
/// picks the one: read wherever the graph asks for the package, the replaced one never read, not
/// even fetched (P7). A dependency names its package: the manifest it finds says the same name.</para>
///
/// <para>What is read from git is read at the commit <c>lyric.lock</c> holds for it, and must be
/// the content the lock holds (P5); <see cref="Locked"/> is what the lock holds afterwards.</para>
///
/// <para>No package depends on itself, at once or through others (the review's M7-6): a cycle is
/// refused with its path. A tag that is a semantic version names the version the repository's
/// manifest gives there — the selection chooses by the tag, the lock writes the manifest's.</para>
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

    /// <summary>Every revision of a repository the resolution read — the chosen versions and the
    /// ones read on the way — with its commit and content hash: the lock's entries (07 P5).</summary>
    public IReadOnlyList<Locked> Locked { get; init; } = [];

    /// <summary>The build scripts' own graphs (11 W3 BS3), by the package whose script it is — for
    /// a script with <c>[build-dependencies]</c>.</summary>
    public IReadOnlyDictionary<string, PackageGraph> Scripts { get; init; } = new Dictionary<string, PackageGraph>();

    /// <summary>The revision each package read from git is, by name — the chosen one.</summary>
    public IReadOnlyDictionary<string, Locked> Revisions { get; init; } = new Dictionary<string, Locked>();

    /// <summary>One source of a package, as read: where from, and what was found there.</summary>
    private sealed record Node(Dependency At, Manifest Manifest, Locked? Revision);

    /// <summary>Reads the graph from <paramref name="root"/>: what comes from git through
    /// <paramref name="git"/> — the user's cache, fetching allowed, when none is given — at the
    /// commits <paramref name="locked"/> holds.</summary>
    /// <exception cref="ManifestException">A dependency is refused: no package there, another name,
    /// a second source for one name, a revision that cannot be read, content that is not what the
    /// lock holds.</exception>
    /// <param name="frozen">The lock is taken as it is (<c>--locked</c>, the review's M7-9): a
    /// revision it does not hold is refused, before anything is fetched.</param>
    public static PackageGraph Resolve(Manifest root, GitCache? git = null, IReadOnlyList<Locked>? locked = null,
        bool frozen = false)
    {
        var overrides = root.Overrides.ToDictionary(o => o.Name, StringComparer.Ordinal);
        var locks = (locked ?? []).GroupBy(l => (l.Name, l.Git)).ToDictionary(g => g.Key, g => g.First());
        // Every source the graph asks for, by name, each read once.
        var read = new Dictionary<string, List<Node>>(StringComparer.Ordinal)
        {
            [root.Name] = [new Node(new Dependency(root.Name, root.Root, null, 0), root, null)],
        };
        var pending = new Queue<Node>(read[root.Name]);
        while (pending.TryDequeue(out var node))
        {
            var package = node.Manifest;
            foreach (var dependency in package.Dependencies)
            {
                // An override answers for the package wherever it is asked for; what the manifest
                // names is then not read at all (P7).
                var at = overrides.TryGetValue(dependency.Name, out var replaced) ? replaced : dependency;
                var declaring = ReferenceEquals(at, replaced) ? root.File : package.File;
                // A package from git is its repository's package content (11 W2 P8): a directory
                // beside it is no part of it.
                if (at.Path is not null && !ReferenceEquals(at, replaced) && node.Revision is not null)
                    throw new ManifestException("LYR-PKG0004", declaring, at.Line, 1,
                        $"'{package.Name}' is read from git and names '{dependency.Name}' by a directory, which is no part of "
                        + $"its package — a package from git names its dependencies by git, or [override] in '{root.Name}' replaces '{dependency.Name}'");
                if (read.TryGetValue(dependency.Name, out var sources))
                {
                    if (sources.Any(s => SameSource(s.At, at))) continue;
                    if (!OneLine(sources[0].At, at))
                        throw new ManifestException("LYR-PKG0005", declaring, at.Line, 1, Conflict(dependency.Name, sources[0].At, at, root.Name));
                }
                var found = Read(dependency.Name, at, declaring, git ??= GitCache.ForUser(offline: false), locks, frozen);
                (read.TryGetValue(dependency.Name, out var list) ? list : read[dependency.Name] = []).Add(found);
                pending.Enqueue(found);
            }
        }

        // One package of each name: the one source, or the greatest version asked for.
        var chosen = read.ToDictionary(r => r.Key,
            r => r.Value.MaxBy(n => n.At.Git is { Kind: GitRefKind.Tag } g ? SemVer.OfTag(g.Ref!) : null)!,
            StringComparer.Ordinal);
        RefuseCycles(root.Name, chosen.ToDictionary(c => c.Key, c => c.Value.Manifest, StringComparer.Ordinal));
        var revisions = read.Values.SelectMany(nodes => nodes).Select(n => n.Revision).OfType<Locked>();
        return new PackageGraph(root, chosen.ToDictionary(c => c.Key, c => c.Value.Manifest, StringComparer.Ordinal))
        {
            Locked = LockFile.Sorted(revisions).ToList(),
            Revisions = chosen.Where(c => c.Value.Revision is not null)
                .ToDictionary(c => c.Key, c => c.Value.Revision!, StringComparer.Ordinal),
        };
    }

    /// <summary>The package <paramref name="at"/> names, from its directory or its revision.</summary>
    private static Node Read(string name, Dependency at, string declaring, GitCache git,
        Dictionary<(string, GitSource), Locked> locks, bool frozen)
    {
        Locked? revision = null;
        string directory;
        if (at.Git is not null)
        {
            if (frozen && !locks.ContainsKey((name, at.Git)))
                throw new ManifestException("LYR-PKG0011", declaring, at.Line, 1,
                    $"'{name}' is read from {at.Git}, which {LockFile.FileName} does not hold — --locked takes the lock "
                    + "as it is; a build without it writes the lock");
            var (checkout, commit, hash) = git.Checkout(at, declaring, locks.GetValueOrDefault((name, at.Git)));
            directory = checkout;
            revision = new Locked(name, "", at.Git, commit, hash);
        }
        else directory = at.Path!;
        var file = Path.Combine(directory, "lyric.toml");
        if (!File.Exists(file))
            throw new ManifestException("LYR-PKG0004", declaring, at.Line, 1,
                $"there is no package at {at.Describe()} for '{name}' — no lyric.toml there");
        var manifest = Manifest.Read(file);
        if (manifest.Name != name)
            throw new ManifestException("LYR-PKG0004", declaring, at.Line, 1,
                $"the package at {at.Describe()} is named '{manifest.Name}', not '{name}'");
        // A tag that is a version is the version the manifest there gives: the selection reads the
        // one, the lock the other, and they must be the same.
        if (at.Git is { Kind: GitRefKind.Tag, Ref: { } tag } && SemVer.OfTag(tag) is { } tagged
            && SemVer.Parse(manifest.Version) is { } carried && tagged.CompareTo(carried) != 0)
            throw new ManifestException("LYR-PKG0006", declaring, at.Line, 1,
                $"tag '{tag}' of {at.Git.Url} holds '{name}' at version {manifest.Version} — a tag that is a version "
                + "names the version the package's manifest gives, by which the build chooses among versions (07 P4)");
        return new Node(at, manifest, revision is null ? null : revision with { Version = manifest.Version });
    }

    /// <summary>No package of the graph depends on itself, at once or through others (the review's
    /// M7-6): the first cycle a walk from the root meets, refused at the dependency that closes it,
    /// with its path — <c>app -> geo -> app</c>. A package's dependencies are built before it, and a
    /// cycle has no first one.</summary>
    private static void RefuseCycles(string root, IReadOnlyDictionary<string, Manifest> packages)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal) { root };
        var path = new List<string>();
        void Visit(string name)
        {
            var manifest = packages[name];
            path.Add(name);
            foreach (var dependency in manifest.Dependencies)
            {
                if (!packages.ContainsKey(dependency.Name)) continue;
                var at = path.IndexOf(dependency.Name);
                if (at >= 0)
                    throw new ManifestException("LYR-PKG0010", manifest.File, dependency.Line, 1,
                        $"the packages depend on each other in a cycle: {string.Join(" -> ", path.Skip(at).Append(dependency.Name))} "
                        + "— a package's dependencies are built before it, and a cycle has no first one");
                if (seen.Add(dependency.Name)) Visit(dependency.Name);
            }
            path.RemoveAt(path.Count - 1);
        }
        Visit(root);
    }

    /// <summary>One directory, or one revision of one repository, as written.</summary>
    private static bool SameSource(Dependency a, Dependency b) =>
        a.Path is not null && b.Path is not null ? SameDirectory(a.Path, b.Path) : a.Git is not null && a.Git == b.Git;

    /// <summary>Two versions of one line of one repository (07 P2, P4): tags that are semantic
    /// versions, one major version — below 1, one minor.</summary>
    private static bool OneLine(Dependency a, Dependency b) =>
        a.Git is { Kind: GitRefKind.Tag } x && b.Git is { Kind: GitRefKind.Tag } y && x.Url == y.Url
        && SemVer.OfTag(x.Ref!) is { } u && SemVer.OfTag(y.Ref!) is { } v && u.Line == v.Line;

    private static string Conflict(string name, Dependency first, Dependency second, string root)
    {
        var versions = first.Git is { Kind: GitRefKind.Tag } x && second.Git is { Kind: GitRefKind.Tag } y && x.Url == y.Url
                       && SemVer.OfTag(x.Ref!) is not null && SemVer.OfTag(y.Ref!) is not null;
        return versions
            ? $"'{name}' is asked for at tag '{first.Git!.Ref}' and at tag '{second.Git!.Ref}' of {first.Git.Url}: two incompatible "
              + $"versions — a program holds one package of each name (07 P3); [override] in '{root}' picks the one"
            : $"'{name}' is read from {first.Describe()} and from {second.Describe()} — a program holds one "
              + $"package of each name (07 P3); [override] in '{root}' picks the one";
    }

    private static bool SameDirectory(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
