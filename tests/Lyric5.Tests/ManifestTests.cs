using Lyric5.Build;

namespace Lyric5.Tests;

/// <summary>
/// <c>lyric.toml</c> (design/v5/spec/07 V7, 11 W2): the TOML subset a manifest is written in, and
/// what the toolchain reads of it — <c>[package]</c> with its name, version and edition — and
/// refuses: what TOML refuses, a manifest without its fields, a section it does not know or does not
/// read yet.
/// </summary>
public class ManifestTests
{
    [Fact]
    public void Toml_reads_tables_arrays_and_inline_tables()
    {
        var doc = Toml.Parse("""
            # a comment
            title = "a \"quoted\" \u00e9 string"   # and one at the end
            literal = 'C:\path'
            count = 1_000
            hex = 0xff
            negative = -7
            yes = true
            list = [1, 2,
              3, ]          # a trailing comma, a line break

            [a.b]
            inline = { path = "../geo", tag = 'v1' }
            "quoted key" = false

            [[bin]]
            name = "one"
            [[bin]]
            name = "two"
            """);
        Assert.Equal("a \"quoted\" é string", Get(doc, "title"));
        Assert.Equal(@"C:\path", Get(doc, "literal"));
        Assert.Equal(1000L, Get(doc, "count"));
        Assert.Equal(255L, Get(doc, "hex"));
        Assert.Equal(-7L, Get(doc, "negative"));
        Assert.Equal(true, Get(doc, "yes"));
        Assert.Equal([1L, 2L, 3L], ((TomlArray)Get(doc, "list")).Cast<long>());
        var b = (TomlTable)Get((TomlTable)Get(doc, "a"), "b");
        var inline = (TomlTable)Get(b, "inline");
        Assert.Equal("../geo", Get(inline, "path"));
        Assert.Equal(false, Get(b, "quoted key"));
        var bins = (TomlArray)Get(doc, "bin");
        Assert.Equal(["one", "two"], bins.Cast<TomlTable>().Select(t => (string)Get(t, "name")));
    }

    [Theory]
    [InlineData("a = 1\na = 2", 2, "defined twice")]
    [InlineData("[t]\n[t]", 2, "defined twice")]
    [InlineData("a = \"open", 1, "not closed")]
    [InlineData("a = 1.5", 1, "floats")]
    [InlineData("a = 1979-05-27", 1, "dates")]
    [InlineData("a = \"\"\"multi\"\"\"", 1, "multi-line")]
    [InlineData("a = 007", 1, "not a value")]
    [InlineData("a = 1 b = 2", 1, "end of the line")]
    [InlineData("a = 1\n[a.b]", 2, "not a table")]
    public void Toml_refuses_with_a_line(string text, int line, string why)
    {
        var e = Assert.Throws<TomlException>(() => Toml.Parse(text));
        Assert.Equal(line, e.Line);
        Assert.Contains(why, e.Message);
    }

    [Fact]
    public void A_manifest_names_its_package()
    {
        var m = Manifest.Read(Write("[package]\nname = \"geo_kit\"\nversion = \"1.2.3-beta.1\"\nedition = \"5\"\ndescription = \"shapes\"\n"
                                    + "license = \"MIT\"\nrepository = \"https://example.org/geo\"\nauthors = [\"a\", \"b\"]\n"));
        Assert.Equal("geo_kit", m.Name);
        Assert.Equal("1.2.3-beta.1", m.Version);
        Assert.Equal("5", m.Edition);
        Assert.Equal(Path.Combine(m.Root, "src"), m.SourceRoot);
    }

    [Theory]
    [InlineData("name = \"app\"", "LYR-PKG0002", "[package] needs a 'version'")]
    [InlineData("version = \"0.1.0\"", "LYR-PKG0002", "[package] needs a 'name'")]
    [InlineData("name = \"App\"\nversion = \"0.1.0\"", "LYR-PKG0002", "no package name")]
    [InlineData("name = \"std\"\nversion = \"0.1.0\"", "LYR-PKG0002", "standard library")]
    [InlineData("name = \"app\"\nversion = \"1.0\"", "LYR-PKG0002", "no semantic version")]
    [InlineData("name = \"app\"\nversion = \"0.1.0\"\nedition = \"4\"", "LYR-PKG0002", "edition")]
    [InlineData("name = \"app\"\nversion = \"0.1.0\"\nauthor = \"x\"", "LYR-PKG0003", "no key 'author'")]
    [InlineData("name = 7\nversion = \"0.1.0\"", "LYR-PKG0002", "a string, not the number 7")]
    [InlineData("name = \"app\"\nversion = \"0.1.0\"\ndescription = 7", "LYR-PKG0002", "'description' is a string, not the number 7")]
    [InlineData("name = \"app\"\nversion = \"0.1.0\"\nauthors = \"me\"", "LYR-PKG0002", "'authors' is an array of strings")]
    [InlineData("name = \"app\"\nversion = \"0.1.0\"\nauthors = [\"me\", 7]", "LYR-PKG0002", "'authors' is an array of strings")]
    [InlineData("name = \"app\"\nversion = \"0.1.0\"\ntoolchain = \"~5.1\"", "LYR-PKG0002", "is no toolchain requirement")]
    [InlineData("name = \"app\"\nversion = \"0.1.0\"\ninclude = \"src\"", "LYR-PKG0002", "'include' is an array of patterns")]
    [InlineData("name = \"app\"\nversion = \"0.1.0\"\nexclude = [\"../notes\"]", "LYR-PKG0002", "'../notes' is no pattern of the package's files")]
    public void A_package_table_is_checked(string body, string code, string why)
    {
        var e = Assert.Throws<ManifestException>(() => Manifest.Read(Write("[package]\n" + body + "\n")));
        Assert.Equal(code, e.Code);
        Assert.Contains(why, e.Message);
    }

    [Theory]
    [InlineData("[lints]\ndeny = []", "comes with M12")]
    [InlineData("[workspace]\nmembers = []", "no part of a manifest")]
    public void A_section_the_toolchain_does_not_read_is_refused(string section, string why)
    {
        var e = Assert.Throws<ManifestException>(() =>
            Manifest.Read(Write("[package]\nname = \"app\"\nversion = \"0.1.0\"\n\n" + section + "\n")));
        Assert.Equal("LYR-PKG0003", e.Code);
        Assert.Contains(why, e.Message);
        Assert.Equal(5, e.Line);
    }

    /// <summary>A build script's sections (11 W3 BS3, BS5; M8b S13): its own dependencies — the name
    /// `build` its own module, no package's —, and the packages whose scripts the root trusts.</summary>
    [Fact]
    public void A_build_scripts_dependencies_and_the_trust_rule_are_read()
    {
        var m = Manifest.Read(Write("[package]\nname = \"app\"\nversion = \"0.1.0\"\n\n[build-dependencies]\ntools = { path = \"../tools\" }\n\n[trust]\nbuild-scripts = [\"geo\", \"geo\"]\n"));
        Assert.Equal("tools", Assert.Single(m.BuildDependencies).Name);
        Assert.Empty(m.Dependencies);
        Assert.Equal(["geo"], m.TrustedScripts);
        var e = Assert.Throws<ManifestException>(() =>
            Manifest.Read(Write("[package]\nname = \"app\"\nversion = \"0.1.0\"\n\n[build-dependencies]\nbuild = { path = \"../b\" }\n")));
        Assert.Equal("LYR-PKG0002", e.Code);
        Assert.Contains("the build script's own module", e.Message);
        var t = Assert.Throws<ManifestException>(() =>
            Manifest.Read(Write("[package]\nname = \"app\"\nversion = \"0.1.0\"\n\n[trust]\nscripts = []\n")));
        Assert.Equal("LYR-PKG0003", t.Code);
    }

    [Fact]
    public void A_dependency_is_a_path_relative_to_the_manifest()
    {
        var m = Manifest.Read(Write("[package]\nname = \"app\"\nversion = \"0.1.0\"\n\n[dependencies]\ngeo = { path = \"../geo\" }\n"));
        var geo = Assert.Single(m.Dependencies);
        Assert.Equal("geo", geo.Name);
        Assert.Equal(Path.GetFullPath(Path.Combine(m.Root, "..", "geo")), geo.Path);
        Assert.Null(geo.Git);
        Assert.Equal(6, geo.Line);
    }

    [Fact]
    public void A_git_dependency_names_a_repository_and_a_revision()
    {
        var m = Manifest.Read(Write("[package]\nname = \"app\"\nversion = \"0.1.0\"\n\n[dependencies]\n"
            + "geo = { git = \"https://example.org/geo.git\", tag = \"v1.2.0\" }\n"
            + "units = { git = \"git@example.org:units.git\", branch = \"release/1\" }\n"
            + "io = { git = \"file:///srv/io\", rev = \"0123abc\" }\n"
            + "fmt = { git = \"ssh://example.org/fmt\" }\n"));
        // read in the normal form (M7-10): the '.git' at the end dropped
        Assert.Equal(
            [new GitSource("https://example.org/geo", GitRefKind.Tag, "v1.2.0"),
             new GitSource("git@example.org:units", GitRefKind.Branch, "release/1"),
             new GitSource("file:///srv/io", GitRefKind.Rev, "0123abc"),
             new GitSource("ssh://example.org/fmt", GitRefKind.Default, null)],
            m.Dependencies.Select(d => d.Git));
        Assert.All(m.Dependencies, d => Assert.Null(d.Path));
    }

    [Fact]
    public void An_override_reads_a_directory()
    {
        var e = Assert.Throws<ManifestException>(() => Manifest.Read(Write(
            "[package]\nname = \"app\"\nversion = \"0.1.0\"\n\n[override]\ngeo = { git = \"https://example.org/geo\" }\n")));
        Assert.Equal("LYR-PKG0003", e.Code);
        Assert.Contains("an override reads a directory (07 P7)", e.Message);
    }

    [Fact]
    public void Include_and_exclude_name_the_package_content()
    {
        var m = Manifest.Read(Write("[package]\nname = \"app\"\nversion = \"0.1.0\"\ninclude = [\"src/\", \"assets/**\"]\nexclude = [\"src/draft.lyr\"]\n"));
        Assert.Equal(["src/", "assets/**"], m.Include);
        Assert.Equal(["src/draft.lyr"], m.Exclude);
        var plain = Manifest.Read(Write("[package]\nname = \"app\"\nversion = \"0.1.0\"\n"));
        Assert.Null(plain.Include);
        Assert.Empty(plain.Exclude);
    }

    [Theory]
    [InlineData("geo = \"1.2\"", "LYR-PKG0003", "asks a registry for a version")]
    [InlineData("geo = { path = 3 }", "LYR-PKG0002", "'path' of 'geo' is a string, not the number 3")]
    [InlineData("geo = { git = 7 }", "LYR-PKG0002", "'git' of 'geo' is a string, not the number 7")]
    [InlineData("geo = { git = \"https://example.org/geo\", path = \"../geo\" }", "LYR-PKG0002", "'geo' names a directory and a repository")]
    [InlineData("geo = { tag = \"v1\" }", "LYR-PKG0002", "'geo' needs a path or a git repository")]
    [InlineData("geo = { path = \"../geo\", tag = \"v1\" }", "LYR-PKG0002", "'tag' of 'geo' names a revision of a repository")]
    [InlineData("geo = { git = \"ftp://example.org/geo\" }", "LYR-PKG0002", "'ftp://example.org/geo' is no git repository")]
    [InlineData("geo = { git = \"--upload-pack=touch\" }", "LYR-PKG0002", "is no git repository")]
    [InlineData("geo = { git = \"https://example.org/geo\", tag = \"v1\", branch = \"main\" }", "LYR-PKG0002", "'geo' names a tag and a branch: one revision")]
    [InlineData("geo = { git = \"https://example.org/geo\", rev = \"main\" }", "LYR-PKG0002", "'main' is no commit")]
    [InlineData("geo = { git = \"https://example.org/geo\", tag = \"-x\" }", "LYR-PKG0002", "'-x' is no tag name")]
    [InlineData("geo = { git = \"https://example.org/geo\", branch = \"a..b\" }", "LYR-PKG0002", "'a..b' is no branch name")]
    [InlineData("Geo = { path = \"../geo\" }", "LYR-PKG0002", "'Geo' is no package name")]
    [InlineData("geo = { path = \"../geo\", features = [] }", "LYR-PKG0003", "'geo' has no key 'features'")]
    public void A_dependency_is_checked(string entry, string code, string why)
    {
        var e = Assert.Throws<ManifestException>(() =>
            Manifest.Read(Write("[package]\nname = \"app\"\nversion = \"0.1.0\"\n\n[dependencies]\n" + entry + "\n")));
        Assert.Equal(code, e.Code);
        Assert.Contains(why, e.Message);
    }

    [Fact]
    public void Text_that_is_not_toml_is_refused_where_it_breaks()
    {
        var e = Assert.Throws<ManifestException>(() => Manifest.Read(Write("[package]\nname = \"app\"\nversion = 0.1\n")));
        Assert.Equal("LYR-PKG0001", e.Code);
        Assert.Equal(3, e.Line);
        Assert.Contains("lyric.toml:3:", e.Render());
    }

    private static object Get(TomlTable table, string key)
    {
        Assert.True(table.TryGet(key, out var value), $"no '{key}'");
        return value;
    }

    private static string Write(string text)
    {
        var dir = TestDirectories.Fresh("lyric5-manifest-");
        var file = Path.Combine(dir, "lyric.toml");
        File.WriteAllText(file, text);
        return file;
    }
}
