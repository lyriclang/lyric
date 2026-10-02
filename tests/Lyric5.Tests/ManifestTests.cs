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
    [InlineData("name = \"app\"\nversion = \"0.1.0\"\ntoolchain = \">=5.1\"", "LYR-PKG0003", "'toolchain' comes with M7 S6")]
    [InlineData("name = \"app\"\nversion = \"0.1.0\"\ninclude = [\"src\"]", "LYR-PKG0003", "'include' comes with M7 S5")]
    public void A_package_table_is_checked(string body, string code, string why)
    {
        var e = Assert.Throws<ManifestException>(() => Manifest.Read(Write("[package]\n" + body + "\n")));
        Assert.Equal(code, e.Code);
        Assert.Contains(why, e.Message);
    }

    [Theory]
    [InlineData("[dependencies]\ngeo = { path = \"../geo\" }", "comes with M7 S4")]
    [InlineData("[[bin]]\nname = \"tool\"", "comes with M7 S6")]
    [InlineData("[workspace]\nmembers = []", "no part of a manifest")]
    public void A_section_the_toolchain_does_not_read_is_refused(string section, string why)
    {
        var e = Assert.Throws<ManifestException>(() =>
            Manifest.Read(Write("[package]\nname = \"app\"\nversion = \"0.1.0\"\n\n" + section + "\n")));
        Assert.Equal("LYR-PKG0003", e.Code);
        Assert.Contains(why, e.Message);
        Assert.Equal(5, e.Line);
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
        var dir = Path.Combine(Path.GetTempPath(), "lyric5-manifest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "lyric.toml");
        File.WriteAllText(file, text);
        return file;
    }
}
