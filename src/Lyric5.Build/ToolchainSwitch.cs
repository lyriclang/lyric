namespace Lyric5.Build;

/// <summary>What the command line is to do before its verb: run here, or hand the command to an
/// installed toolchain.</summary>
public abstract record SwitchDecision
{
    /// <summary>This toolchain runs the command.</summary>
    public sealed record Here : SwitchDecision;

    /// <summary>The installed <paramref name="Version"/> runs it, for the reason <paramref name="Why"/>.</summary>
    public sealed record Installed(string Version, string Why) : SwitchDecision;

    /// <summary>The package asks for <paramref name="Minimum"/>, and neither this toolchain nor an
    /// installed one is that or later.</summary>
    public sealed record Missing(SemVer Minimum, string Package, string Manifest, int Line) : SwitchDecision;
}

/// <summary>
/// Which toolchain runs a command: <c>LYRIC_TOOLCHAIN</c> — <c>local</c> for this one, or a
/// version — over the <c>toolchain</c> the package here asks for, a least version. This toolchain
/// runs it where it is what is asked for; else the default, where it is; else the newest installed
/// that is. A command handed on carries <c>LYRIC_TOOLCHAIN_FROM</c>, and is never handed on again.
/// Only the package the command runs in is read: a dependency's pin is checked by the build
/// (<see cref="ToolchainCheck"/>).
/// </summary>
public static class ToolchainSwitch
{
    public const string Variable = "LYRIC_TOOLCHAIN";
    public const string FromVariable = "LYRIC_TOOLCHAIN_FROM";

    /// <param name="running">This toolchain's version.</param>
    /// <param name="variable">The environment, by name.</param>
    /// <param name="manifest">The <c>lyric.toml</c> of the package the command runs in, if any.</param>
    /// <exception cref="ToolchainException"><c>LYR-CLI0025</c>: <c>LYRIC_TOOLCHAIN</c> names a version not installed.</exception>
    public static SwitchDecision Decide(string running, Func<string, string?> variable, ToolchainHome home, string? manifest)
    {
        if (!string.IsNullOrEmpty(variable(FromVariable))) return new SwitchDecision.Here();
        var asked = variable(Variable);
        if (!string.IsNullOrWhiteSpace(asked))
        {
            if (asked == "local" || asked == running || asked == ToolchainHome.WithoutBuild(running)) return new SwitchDecision.Here();
            return home.Resolve(asked) is { } version
                ? new SwitchDecision.Installed(version, $"{Variable}={asked}")
                : throw new ToolchainException("LYR-CLI0025", $"{Variable} names lyric {asked}, which is not installed in {home.Toolchains}",
                    $"lyric5 toolchain install {asked} — or {Variable}=local runs this toolchain");
        }
        if (manifest is null) return new SwitchDecision.Here();
        Manifest package;
        // a manifest that does not read is the verb's to report
        try { package = Manifest.Read(manifest); }
        catch (Exception e) when (e is ManifestException or IOException) { return new SwitchDecision.Here(); }
        if (package.Toolchain is not { } pin || Satisfies(running, pin.Minimum)) return new SwitchDecision.Here();
        var why = $"package '{package.Name}' needs lyric {pin.Minimum} or later";
        if (home.Default() is { } chosen && home.IsInstalled(chosen) && Satisfies(chosen, pin.Minimum))
            return new SwitchDecision.Installed(chosen, why);
        return home.Installed().FirstOrDefault(v => Satisfies(v, pin.Minimum)) is { } newest
            ? new SwitchDecision.Installed(newest, why)
            : new SwitchDecision.Missing(pin.Minimum, package.Name, manifest, pin.Line);
    }

    /// <summary>Whether <paramref name="version"/> is <paramref name="minimum"/> or later; a
    /// development build of a version counts as that version.</summary>
    public static bool Satisfies(string version, SemVer minimum) => minimum.CompareTo(ToolchainCheck.Numbers(version)) <= 0;

    /// <summary>
    /// The offer at a terminal: the least stable version the package's pin admits, for this machine,
    /// installed where the answer is yes. The installed version, or <c>null</c> where there is none
    /// to offer or the answer is no — then the verb refuses the package.
    /// </summary>
    public static string? Offer(ToolchainFetch fetch, ToolchainHome home, SwitchDecision.Missing missing, string running, TextWriter prompt, Func<string?> answer)
    {
        if (Least(fetch.Index(ChannelIndex.Stable), missing.Minimum, Lyric5.Toolchain.Target.Host.Triple) is not { } release) return null;
        prompt.Write($"package '{missing.Package}' needs lyric {missing.Minimum} or later, and this is lyric {running}; install lyric {release.Version}? [y/N] ");
        prompt.Flush();
        if (answer()?.Trim().ToLowerInvariant() is not ("y" or "yes")) return null;
        fetch.Install(home, release, ChannelIndex.Stable);
        prompt.WriteLine($"installed lyric {release.Version} in {home.DirectoryOf(release.Version)}");
        return release.Version;
    }

    /// <summary>The least stable version of <paramref name="index"/> that is <paramref name="minimum"/>
    /// or later and has an archive for <paramref name="triple"/>.</summary>
    public static ChannelRelease? Least(ChannelIndex index, SemVer minimum, string triple) =>
        index.Releases
            .Where(r => r.Number.Pre is null && minimum.CompareTo(r.Number) <= 0 && r.Archives.ContainsKey(triple))
            .MinBy(r => r.Number);
}
