namespace Lyric5.Tests;

/// <summary>
/// What keeps a test machine's temporary directory from filling: the fixtures' directories go with
/// their process or, where it was killed, with a later one; the cache of built programs loses what
/// nobody used for a week.
/// </summary>
public class TestHygieneTests
{
    [Fact]
    public void The_cache_loses_what_went_unused_and_keeps_the_rest()
    {
        var cache = TestDirectories.Fresh("lyric5-prune-");
        var old = Path.Combine(cache, "emitted", "gone", "u0.c");
        var read = Path.Combine(cache, "emitted", "read", "u0.c");
        var fresh = Path.Combine(cache, "bin", "kept");
        foreach (var file in new[] { old, read, fresh })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, "x");
        }
        var long_ago = DateTime.UtcNow - TimeSpan.FromDays(30);
        File.SetLastWriteTimeUtc(old, long_ago);
        File.SetLastAccessTimeUtc(old, long_ago);
        // Written long ago and read yesterday: in use.
        File.SetLastWriteTimeUtc(read, long_ago);
        File.SetLastAccessTimeUtc(read, DateTime.UtcNow - TimeSpan.FromDays(1));

        var removed = RuntimeBuildTests.PruneCache(cache, DateTime.UtcNow - TimeSpan.FromDays(7));

        Assert.Equal(1, removed);
        Assert.False(File.Exists(old));
        Assert.False(Directory.Exists(Path.GetDirectoryName(old)), "the directory that emptied stays");
        Assert.True(File.Exists(read));
        Assert.True(File.Exists(fresh));
    }

    /// <summary>The user cache's runtime archives, one directory per toolchain version: what
    /// went unused for the age goes, by its stamp or by the directory's time; the version
    /// building keeps its own.</summary>
    [Fact]
    public void A_version_s_archives_go_when_unused_and_the_current_one_stays()
    {
        var runtime = TestDirectories.Fresh("lyric5-versions-");
        var long_ago = DateTime.UtcNow - TimeSpan.FromDays(30);
        string Version(string name, DateTime? stamped, DateTime written)
        {
            var dir = Directory.CreateDirectory(Path.Combine(runtime, name)).FullName;
            File.WriteAllText(Path.Combine(dir, "liblyr-x.a"), "x");
            if (stamped is { } at)
            {
                File.WriteAllText(Path.Combine(dir, ".used"), "");
                File.SetLastWriteTimeUtc(Path.Combine(dir, ".used"), at);
            }
            Directory.SetLastWriteTimeUtc(dir, written);
            return dir;
        }
        var stale = Version("5.0.0+aaaaaaa", long_ago, long_ago);
        var used = Version("5.0.0+bbbbbbb", DateTime.UtcNow - TimeSpan.FromDays(1), long_ago);
        var unstamped = Version("5.0.0+ccccccc", null, long_ago);
        var current = Version("5.0.0+ddddddd", long_ago, long_ago);

        var removed = Lyric5.Build.RuntimeArchive.PruneVersions(runtime, "5.0.0+ddddddd", DateTime.UtcNow - TimeSpan.FromDays(14));

        Assert.Equal(2, removed);
        Assert.False(Directory.Exists(stale));
        Assert.False(Directory.Exists(unstamped));
        Assert.True(Directory.Exists(used), "used yesterday");
        Assert.True(Directory.Exists(current), "the version building");
    }

    [Fact]
    public void A_dead_process_s_directories_go_and_a_living_one_s_stay()
    {
        var roots = TestDirectories.Fresh("lyric5-roots-");
        var dead = Directory.CreateDirectory(Path.Combine(roots, "dead")).FullName;
        var living = Directory.CreateDirectory(Path.Combine(roots, "living")).FullName;
        File.WriteAllText(Path.Combine(dead, "lyric.toml"), "x");
        Directory.SetLastWriteTimeUtc(dead, DateTime.UtcNow - TimeSpan.FromDays(3));

        var removed = TestDirectories.RemoveOlder(roots, DateTime.UtcNow - TimeSpan.FromDays(1));

        Assert.Equal(1, removed);
        Assert.False(Directory.Exists(dead));
        Assert.True(Directory.Exists(living));
    }
}
