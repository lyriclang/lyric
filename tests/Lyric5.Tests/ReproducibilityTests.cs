using Lyric5.Toolchain;
using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// Reproducibility (design/v5/spec/11 W2 P6): the same source, options, toolchain and C compiler
/// give the same bytes — wherever the package lies. Two copies of one package, built from two
/// directories, are compared byte for byte, a path dependency and a generic instance's unit included.
/// Through <c>Main</c>, so in the console collection.
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
    [InlineData("debug")]
    [InlineData("release")]
    public void A_package_built_in_two_places_is_the_same_binary(string profile)
    {
        var first = Package(Files());
        var second = Package(Files());
        Assert.NotEqual(first, second);
        var host = Target.Host;
        byte[] Built(string dir)
        {
            var (exit, _, error) = Run("build", "-C", dir, "--profile", profile);
            Assert.True(exit == 0, error);
            return File.ReadAllBytes(Path.Combine(dir, "out", profile, host.Triple, "app" + host.ExecutableSuffix));
        }
        var a = Built(first);
        var b = Built(second);
        Assert.True(a.AsSpan().SequenceEqual(b), $"the binaries differ: {a.Length} and {b.Length} bytes, first at byte {First(a, b)}");
    }

    private static int First(byte[] a, byte[] b)
    {
        for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
            if (a[i] != b[i]) return i;
        return Math.Min(a.Length, b.Length);
    }
}
