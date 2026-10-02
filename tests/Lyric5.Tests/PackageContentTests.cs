using Lyric5.Build;

namespace Lyric5.Tests;

/// <summary>
/// What a package is made of (design/v5/spec/11 W2 P8): the default set, <c>include</c> and
/// <c>exclude</c>, and the patterns they are written in.
/// </summary>
public class PackageContentTests
{
    [Theory]
    [InlineData("src/", "src/main.lyr", true)]
    [InlineData("src/", "src/net/http.lyr", true)]
    [InlineData("src/", "srcs/main.lyr", false)]
    [InlineData("src", "src/main.lyr", true)]
    [InlineData("README*", "README.md", true)]
    [InlineData("README*", "docs/README.md", false)]
    [InlineData("*.md", "notes.md", true)]
    [InlineData("*.md", "docs/notes.md", false)]
    [InlineData("**/*.md", "docs/notes.md", true)]
    [InlineData("**/*.md", "notes.md", true)]
    [InlineData("src/**/test_*.lyr", "src/a/b/test_x.lyr", true)]
    [InlineData("src/**/test_*.lyr", "src/test_x.lyr", true)]
    [InlineData("src/**/test_*.lyr", "src/a/x.lyr", false)]
    [InlineData("src/?.lyr", "src/a.lyr", true)]
    [InlineData("src/?.lyr", "src/ab.lyr", false)]
    [InlineData("assets", "assets/img/logo.png", true)]
    [InlineData("a.b", "aXb", false)]
    [InlineData("**", "anything/at/all", true)]
    public void A_pattern_holds_a_path_or_what_lies_below_it(string pattern, string path, bool held)
    {
        Assert.Equal(held, PackageContent.Matches(pattern, path));
    }

    [Theory]
    [InlineData(null, null, "src/main.lyr", true)]
    [InlineData(null, null, "native/zlib.c", true)]
    [InlineData(null, null, "build.lyr", true)]
    [InlineData(null, null, "README.md", true)]
    [InlineData(null, null, "LICENSE-MIT", true)]
    [InlineData(null, null, "tests/shapes_test.lyr", false)]
    [InlineData(null, null, "out/debug/app", false)]
    [InlineData(null, null, "docs/guide.md", false)]
    [InlineData("src/;assets/", null, "assets/logo.png", true)]
    [InlineData("src/;assets/", null, "README.md", false)]
    [InlineData(null, "src/draft.lyr", "src/draft.lyr", false)]
    [InlineData(null, "src/draft.lyr", "src/shapes.lyr", true)]
    [InlineData("src/", "**", "lyric.toml", true)]
    public void A_package_is_made_of_its_content(string? include, string? exclude, string path, bool held)
    {
        var manifest = new Manifest("/p/lyric.toml", "geo", "1.0.0", "5")
        {
            Include = include?.Split(';'),
            Exclude = exclude?.Split(';') ?? [],
        };
        Assert.Equal(held, PackageContent.Holds(manifest, path));
    }

    [Theory]
    [InlineData("src/", true)]
    [InlineData("**/*.md", true)]
    [InlineData("LICENSE*", true)]
    [InlineData("", false)]
    [InlineData("/src", false)]
    [InlineData("../shared", false)]
    [InlineData("src/./main.lyr", false)]
    [InlineData("src\\main.lyr", false)]
    [InlineData("src//main.lyr", false)]
    public void A_pattern_is_relative_to_the_package(string pattern, bool valid)
    {
        Assert.Equal(valid, PackageContent.IsPattern(pattern));
    }
}
