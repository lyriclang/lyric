using System.Text;
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

    /// <summary><c>~/.cache/lyric/runtime/&lt;version&gt;</c>, where a build of the runtime never
    /// collides with another version's.</summary>
    public static string CacheDir(string toolchainVersion) => Path.Combine(UserCache.Root, "runtime", toolchainVersion);

    /// <summary>
    /// The archive's path, building it when the cache has none. Keying every runtime unit hashes
    /// megabytes of collector source, so the answer is memoized under the toolchain version, the
    /// compiler, the target, the profile and its flags, and a stamp of the runtime's own files (their names,
    /// sizes and times): a development tree that edits the runtime gets a new archive, a warm
    /// build of a program gets the path.
    /// </summary>
    public static string For(CCompiler compiler, Target target, BuildProfile profile, string toolchainVersion)
    {
        var root = SourceRoot()
            ?? throw new CBuildException("this toolchain carries no runtime source (runtime/include/lyr not found above it)");
        var cache = CacheDir(toolchainVersion);
        // The flags, not the name alone: '--lto' on 'release' is another archive than 'release'.
        var key = $"{toolchainVersion}|{compiler.Identity}|{target.Triple}|{profile.Name}|{string.Join(' ', profile.Codegen)}|{SourceStamp(root)}";
        string Build() => RuntimeLayout.BuildArchive(new CBuild(compiler, target, profile, cache), root, Path.Combine(cache, "lib"));
        var archive = UserCache.Memo("runtime-archive", key, Build);
        // The memo may outlive the archive (a cleaned cache): then build again.
        return File.Exists(archive) ? archive : Build();
    }

    private static string SourceStamp(string root)
    {
        var stamp = new StringBuilder();
        foreach (var part in new[] { "include", "src", "third_party" })
        {
            var dir = Path.Combine(root, "runtime", part);
            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                var info = new FileInfo(file);
                stamp.Append(Path.GetRelativePath(root, file)).Append('|').Append(info.Length).Append('|').Append(info.LastWriteTimeUtc.Ticks).Append('\n');
            }
        }
        return stamp.ToString();
    }
}
