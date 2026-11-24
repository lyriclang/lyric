namespace Lyric5.Build;

/// <summary>
/// The toolchain a package asks for: <c>[package] toolchain =
/// "5.1"</c> — or <c>">=5.1"</c> — is the least toolchain that builds the package. A toolchain below
/// what any package of the program asks for refuses the program: the environment's failure, exit 2.
/// </summary>
public static class ToolchainCheck
{
    /// <summary>Checks every package of <paramref name="graph"/>, the root's first, against the
    /// running toolchain's version (<see cref="Satisfies"/>).</summary>
    /// <exception cref="ManifestException"><c>LYR-PKG0009</c>, exit 2.</exception>
    public static void Check(PackageGraph? graph, string running)
    {
        if (graph is null) return;
        var packages = graph.Packages.Values.Where(m => !ReferenceEquals(m, graph.Root)).OrderBy(m => m.Name, StringComparer.Ordinal);
        foreach (var manifest in packages.Prepend(graph.Root))
        {
            if (manifest.Toolchain is not { } pin || Satisfies(running, pin.Minimum)) continue;
            var install = pin.Minimum.Pre is null ? "lyric5 toolchain install stable installs the newest" : "lyric5 toolchain install dev installs the newest development build";
            throw new ManifestException("LYR-PKG0009", manifest.File, pin.Line, 1,
                $"package '{manifest.Name}' needs lyric {pin.Minimum} or later, and this is lyric {running} ({install})", exit: 2);
        }
    }

    /// <summary>
    /// Whether <paramref name="running"/> is <paramref name="minimum"/> or later. A pin of a version,
    /// <c>5.0</c>, takes a development build of that version as the version. A pin of a development
    /// build, <c>5.0.0-dev.20261119</c>, takes that day's build or a later one and every release of
    /// the version or after it; the commit behind <c>+</c> is not ordered and does not count.
    /// </summary>
    public static bool Satisfies(string running, SemVer minimum) =>
        minimum.CompareTo(minimum.Pre is null ? Numbers(running) : SemVer.Parse(running) ?? Numbers(running)) <= 0;

    /// <summary>The version's numbers, without its pre-release and build parts.</summary>
    public static SemVer Numbers(string running)
    {
        var end = running.IndexOfAny(['-', '+']);
        return SemVer.Parse(end < 0 ? running : running[..end]) ?? new SemVer(0, 0, 0, null);
    }
}
