using Lyric5.Toolchain;

namespace Lyric5.Build;

/// <summary>
/// <c>liblyr.a</c> for a target and profile, built from the runtime's C source into the user's
/// cache once per toolchain version, target and profile, and reused by every project (11 T3;
/// Zig builds its libc the same way). The source is found above the running toolchain: in the
/// repository during development, in the archive's own directory once M15 ships it.
/// </summary>
public static class RuntimeArchive
{
    /// <summary>The runtime's source root, or <c>null</c> when this toolchain carries none.</summary>
    public static string? SourceRoot()
    {
        try { return RuntimeLayout.FindRoot(AppContext.BaseDirectory); }
        catch (DirectoryNotFoundException) { return null; }
    }

    /// <summary>
    /// <c>~/.cache/lyric/runtime/&lt;version&gt;</c> (the platform's local application data on
    /// Windows), where a build of the runtime never collides with another version's.
    /// </summary>
    public static string CacheDir(string toolchainVersion)
    {
        var configured = Environment.GetEnvironmentVariable("LYRIC_CACHE");
        var root = !string.IsNullOrWhiteSpace(configured)
            ? configured
            : OperatingSystem.IsWindows()
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "lyric", "cache")
                : Path.Combine(Environment.GetEnvironmentVariable("XDG_CACHE_HOME") is { Length: > 0 } xdg
                    ? xdg
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache"), "lyric");
        return Path.Combine(root, "runtime", toolchainVersion);
    }

    /// <summary>The archive's path, building it when the cache has none.</summary>
    public static string For(CCompiler compiler, Target target, Profile profile, string toolchainVersion)
    {
        var root = SourceRoot()
            ?? throw new CBuildException("this toolchain carries no runtime source (runtime/include/lyr not found above it)");
        var cache = CacheDir(toolchainVersion);
        var build = new CBuild(compiler, target, profile, cache);
        return RuntimeLayout.BuildArchive(build, root, Path.Combine(cache, "lib"));
    }
}
