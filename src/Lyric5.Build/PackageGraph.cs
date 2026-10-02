namespace Lyric5.Build;

/// <summary>
/// The packages a program is built from (design/v5/spec/07 V7, 11 W2): its own and, through
/// <c>[dependencies]</c>, every package those depend on, each read once from its directory.
///
/// <para>One package of each name (P3): two directories for one name are refused, unless the root's
/// <c>[override]</c> picks the one — read wherever the graph asks for the package, the replaced one
/// never read (P7). A dependency names its package: the directory's manifest says the same name.</para>
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

    /// <summary>Reads the graph from <paramref name="root"/>.</summary>
    /// <exception cref="ManifestException">A dependency is refused: no package there, another name,
    /// a second directory for one name.</exception>
    public static PackageGraph Resolve(Manifest root)
    {
        var overrides = root.Overrides.ToDictionary(o => o.Name, StringComparer.Ordinal);
        var packages = new Dictionary<string, Manifest>(StringComparer.Ordinal) { [root.Name] = root };
        var pending = new Queue<Manifest>([root]);
        while (pending.TryDequeue(out var package))
        {
            foreach (var dependency in package.Dependencies)
            {
                // An override answers for the package wherever it is asked for; the directory the
                // manifest names is then not read at all (P7).
                var at = overrides.TryGetValue(dependency.Name, out var replaced) ? replaced : dependency;
                if (packages.TryGetValue(dependency.Name, out var known))
                {
                    if (!SameDirectory(known.Root, at.Root))
                        throw new ManifestException("LYR-PKG0005", package.File, dependency.Line, 1,
                            $"'{dependency.Name}' is read from '{known.Root}' and from '{at.Root}' — a program holds one "
                            + $"package of each name (07 P3); [override] in '{root.Name}' picks the one");
                    continue;
                }
                var file = Path.Combine(at.Root, "lyric.toml");
                var declaring = ReferenceEquals(at, replaced) ? root.File : package.File;
                if (!File.Exists(file))
                    throw new ManifestException("LYR-PKG0004", declaring, at.Line, 1,
                        $"there is no package at '{at.Root}' for '{dependency.Name}' — no lyric.toml there");
                var manifest = Manifest.Read(file);
                if (manifest.Name != dependency.Name)
                    throw new ManifestException("LYR-PKG0004", declaring, at.Line, 1,
                        $"the package at '{at.Root}' is named '{manifest.Name}', not '{dependency.Name}'");
                packages[dependency.Name] = manifest;
                pending.Enqueue(manifest);
            }
        }
        return new PackageGraph(root, packages);
    }

    private static bool SameDirectory(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
