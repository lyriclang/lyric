namespace Lyric5.Build;

/// <summary>
/// The toolchain a package asks for (design/v5/spec/11 W2 P12; 07 V9): <c>[package] toolchain =
/// "5.1"</c> — or <c>">=5.1"</c> — is the least toolchain that builds the package. A toolchain below
/// what any package of the program asks for refuses the program: the environment's failure, exit 2.
/// </summary>
public static class ToolchainCheck
{
    /// <summary>Checks every package of <paramref name="graph"/>, the root's first, against the
    /// running toolchain's version; a development build of 5.0.0 is 5.0.0 for a pin.</summary>
    /// <exception cref="ManifestException"><c>LYR-PKG0009</c>, exit 2.</exception>
    public static void Check(PackageGraph? graph, string running)
    {
        if (graph is null) return;
        var version = Numbers(running);
        var packages = graph.Packages.Values.Where(m => !ReferenceEquals(m, graph.Root)).OrderBy(m => m.Name, StringComparer.Ordinal);
        foreach (var manifest in packages.Prepend(graph.Root))
        {
            if (manifest.Toolchain is not { } pin || pin.Minimum.CompareTo(version) <= 0) continue;
            throw new ManifestException("LYR-PKG0009", manifest.File, pin.Line, 1,
                $"package '{manifest.Name}' needs lyric {pin.Minimum} or later, and this is lyric {running}", exit: 2);
        }
    }

    /// <summary>The version's numbers, without its pre-release and build parts.</summary>
    private static SemVer Numbers(string running)
    {
        var end = running.IndexOfAny(['-', '+']);
        return SemVer.Parse(end < 0 ? running : running[..end]) ?? new SemVer(0, 0, 0, null);
    }
}
