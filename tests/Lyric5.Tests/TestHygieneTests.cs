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
