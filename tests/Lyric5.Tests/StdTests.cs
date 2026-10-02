using Lyric5.Toolchain;
using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// The standard library's own tests (design/v5/spec/13 M8a: <c>lyric test</c> runs them): the
/// package <c>tests/std</c>, copied out of the repository so its <c>out/</c> lands elsewhere, and
/// run by <c>lyric test</c> as a user runs a package's. Through <c>Main</c>, so in the console
/// collection.
/// </summary>
[Collection("console")]
public class StdTests
{
    [Fact]
    public void The_standard_library_holds_its_own_tests()
    {
        var source = Path.Combine(RuntimeLayout.FindRoot(AppContext.BaseDirectory), "tests", "std");
        var dir = Package(("x", ""));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            if (relative.StartsWith("out", StringComparison.Ordinal)) continue;
            var target = Path.Combine(dir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
        var (exit, output, error) = Run("test", "-C", dir);
        Assert.True(exit == 0, output + error);
        Assert.Matches(@"\n\d+ tests: \d+ passed, 0 failed, 0 skipped\n$", output);
    }
}
