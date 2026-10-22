using System.Text;
using Lyric5.Toolchain;

namespace Lyric5.Build;

/// <summary>
/// <c>liblyr.a</c> for a target and profile, built from the runtime's C source into the user's
/// cache once per toolchain version, target and profile, and reused by every project (
/// Zig builds its libc the same way). The source is found above the running toolchain: in the
/// repository during development, in the archive's own directory once a release ships it.
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
        string Build()
        {
            var built = RuntimeLayout.BuildArchive(new CBuild(compiler, target, profile, cache), root, Path.Combine(cache, "lib"));
            // A new archive is rare — a new version, target or profile —: then the versions nobody
            // used for a while go (a development build's version carries its commit,
            // and the directories had grown to 14 GB).
            PruneVersions(Path.Combine(UserCache.Root, "runtime"), toolchainVersion, DateTime.UtcNow - VersionAge);
            return built;
        }
        var archive = UserCache.Memo("runtime-archive", key, Build);
        MarkUsed(cache);
        // The memo may outlive the archive (a cleaned cache): then build again.
        return File.Exists(archive) ? archive : Build();
    }

    /// <summary>How long a version's archives may go unused before a new archive's build removes them.</summary>
    public static readonly TimeSpan VersionAge = TimeSpan.FromDays(14);

    /// <summary>Every use of a version's archives stamps <c>.used</c> in its directory.</summary>
    private static void MarkUsed(string versionDir)
    {
        try
        {
            var stamp = Path.Combine(versionDir, ".used");
            if (File.Exists(stamp)) File.SetLastWriteTimeUtc(stamp, DateTime.UtcNow);
            else if (Directory.Exists(versionDir)) File.WriteAllText(stamp, "");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Removes the version directories under <paramref name="runtimeRoot"/> last used —
    /// their <c>.used</c> stamp, or the directory's own time where it has none — before
    /// <paramref name="limit"/>, all but <paramref name="keep"/>; how many went. A directory in use
    /// elsewhere is passed over; a toolchain that finds its archive gone builds it again.</summary>
    public static int PruneVersions(string runtimeRoot, string keep, DateTime limit)
    {
        if (!Directory.Exists(runtimeRoot)) return 0;
        var removed = 0;
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(runtimeRoot))
            {
                if (Path.GetFileName(dir) == keep) continue;
                try
                {
                    var stamp = Path.Combine(dir, ".used");
                    var used = File.Exists(stamp) ? File.GetLastWriteTimeUtc(stamp) : Directory.GetLastWriteTimeUtc(dir);
                    if (used >= limit) continue;
                    Directory.Delete(dir, recursive: true);
                    removed++;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return removed;
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
