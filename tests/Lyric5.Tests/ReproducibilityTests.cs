using System.Text;
using Lyric5.Build;
using Lyric5.Toolchain;
using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// Reproducibility (design/v5/spec/11 W2 P6): the same source, options, toolchain and C compiler
/// give the same bytes — wherever the package lies, and whenever it is built. Two copies of one
/// package, built from two directories a second apart, are compared byte for byte, a path
/// dependency and a generic instance's unit included: for the host in both built-in profiles, and
/// for each operating system's format from whichever host runs the tests — a PE image's time
/// stamps and PDB GUID, a Mach-O image's debug map are each a linker's. Through <c>Main</c>, so in
/// the console collection.
/// </summary>
[Collection("console")]
public class ReproducibilityTests
{
    private static (string Path, string Text)[] Files() =>
    [
        ("lyric.toml", AppManifest + "\n[dependencies]\ngeo = { path = \"deps/geo\" }\n"),
        ("src/main.lyr", "import std.io { println };\nimport geo.shapes { area };\nimport app.util { twice, first };\n\n"
                         + "fn main(): void {\n    let sides = [3, 4];\n    println(f\"{twice(area(first(sides), sides[1]))}\");\n}\n"),
        ("src/util.lyr", "pub fn twice(x: int): int {\n    return x * 2;\n}\n\npub fn first<T>(xs: T[]): T {\n    return xs[0];\n}\n"),
        ("deps/geo/lyric.toml", "[package]\nname = \"geo\"\nversion = \"0.1.0\"\n"),
        ("deps/geo/src/shapes.lyr", "pub fn area(w: int, h: int): int {\n    return w * h;\n}\n"),
    ];

    [Theory]
    [InlineData("debug", null)]
    [InlineData("release", null)]
    [InlineData("debug", "x86_64-linux-gnu")]
    [InlineData("debug", "x86_64-windows-gnu")]
    [InlineData("debug", "aarch64-macos")]
    public void A_package_built_in_two_places_is_the_same_binary(string profile, string? triple)
    {
        var target = triple is null ? Target.Host : Target.Parse(triple);
        var first = Package(Files());
        var second = Package(Files());
        Assert.NotEqual(first, second);
        byte[] Built(string dir)
        {
            var (exit, _, error) = Run("build", "-C", dir, "--profile", profile, "--target", target.Triple);
            Assert.True(exit == 0, error);
            return File.ReadAllBytes(Path.Combine(dir, "out", profile, target.Triple, "app" + target.ExecutableSuffix));
        }
        var a = Built(first);
        // A second later: a linker's clock reads whole seconds, and a stamp from it would differ.
        Thread.Sleep(TimeSpan.FromSeconds(1.1));
        var b = Built(second);
        Assert.True(a.AsSpan().SequenceEqual(b), $"the binaries differ: {a.Length} and {b.Length} bytes, first at byte {First(a, b)}");
        foreach (var place in Places(first))
            Assert.True(a.AsSpan().IndexOf(Encoding.UTF8.GetBytes(place)) < 0, $"the binary names {place}");
    }

    /// <summary>
    /// What a build must not write into a binary: where the package lies, and the directory the
    /// build ran in — which a C compiler records as the compilation directory unless told
    /// otherwise — as a path under the toolchain's root is written (<c>lyric/…</c>; the tests run
    /// inside it). As it is, that directory may come with zig's libunwind on Linux, which zig
    /// built wherever it was first asked to: that is the C compiler's, not the build's.
    /// </summary>
    private static IEnumerable<string> Places(string package)
    {
        yield return package;
        var relative = Path.GetRelativePath(RuntimeArchive.SourceRoot()!, Environment.CurrentDirectory);
        if (relative != "." && !relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative))
            yield return "lyric" + Path.DirectorySeparatorChar + relative;
    }

    private static int First(byte[] a, byte[] b)
    {
        for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
            if (a[i] != b[i]) return i;
        return Math.Min(a.Length, b.Length);
    }
}
