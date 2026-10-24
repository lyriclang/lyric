namespace Lyric5.Tests;

/// <summary>
/// Whether this run builds for the Tier 1 targets other than the host. Such a build is the same
/// on every machine — zig cross-compiles alike from each — so CI asks for it on one runner, Linux
/// x86-64, and the others set <c>LYRIC5_CROSS_TARGETS=0</c>; a developer's run, which sets
/// nothing, builds them.
/// </summary>
internal static class CrossTargets
{
    public static bool Enabled => Environment.GetEnvironmentVariable("LYRIC5_CROSS_TARGETS") != "0";

    public const string Reason = "builds for other targets; this run sets LYRIC5_CROSS_TARGETS=0 (CI builds them on Linux x86-64)";
}

/// <summary>A fact that builds for other targets: skipped, and said so, where the run leaves them out.</summary>
public sealed class CrossTargetFactAttribute : FactAttribute
{
    public CrossTargetFactAttribute()
    {
        if (!CrossTargets.Enabled) Skip = CrossTargets.Reason;
    }
}

/// <summary>A theory whose every case builds for other targets: skipped, and said so, where the
/// run leaves them out.</summary>
public sealed class CrossTargetTheoryAttribute : TheoryAttribute
{
    public CrossTargetTheoryAttribute()
    {
        if (!CrossTargets.Enabled) Skip = CrossTargets.Reason;
    }
}
