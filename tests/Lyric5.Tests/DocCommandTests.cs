using Lyric5.Doc.Api;
using Lyric5.Site;
using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// <c>lyric doc</c>: a package's reference apart from the site — an index, a page per module and per
/// type, each member under an anchor that says what it is —, and the standard library's with
/// <c>--std</c>. The goldens are <c>site/golden/doc-module.html</c> and <c>doc-type.html</c>.
/// </summary>
[Collection("console")]
public class DocCommandTests
{
    private const string Manifest = "[package]\nname = \"geo\"\nversion = \"0.2.0\"\n";

    private const string Lib = """
        //! Shapes and the arithmetic of the plane.
        //!
        //! A [Point] is a place, a [Shape] has an area.

        /// The largest coordinate a point takes.
        pub let limit: int = 1000;

        /// A place in the plane.
        pub struct Point {
            /// Across.
            pub x: int,
            /// Up.
            pub y: int,

            /// The point moved by `dx` and `dy`.
            pub fn moved(dx: int, dy: int): Point {
                return Point { x = this.x + dx, y = this.y + dy };
            }
        }

        /// A direction.
        pub enum Dir {
            /// Towards the top.
            Up,
            /// Towards the bottom.
            Down,
        }

        /// What has an area.
        pub interface Shape {
            /// The area, in square units.
            fn area(): int;

            /// Whether the area is zero.
            fn isEmpty(): bool {
                return this.area() == 0;
            }
        }

        /// The larger of two values.
        ///
        /// # Panics
        ///
        /// Never.
        pub fn larger<T :: [Ordered]>(a: T, b: T): T {
            return if (a > b) a else b;
        }

        extend Point :: [Shape] {
            fn area(): int {
                return 0;
            }
        }

        """;

    private const string Units = """
        //! Units of length.

        /// A length in meters.
        pub type Meters = int;

        """;

    private static string Geo() => Package(("lyric.toml", Manifest), ("src/lib.lyr", Lib), ("src/units.lyr", Units));

    [Fact]
    public void Doc_writes_an_index_a_page_per_module_and_per_type_and_no_link_leads_nowhere()
    {
        var root = Geo();
        var (exit, output, error) = RunRaw("doc", "-C", root);
        Assert.True(exit == 0, error);
        Assert.Contains("documented geo 0.2.0: 2 modules, 3 types", output);

        var doc = Path.Combine(root, "out", "doc");
        foreach (var page in new[] { "index.html", "geo/index.html", "geo/units/index.html", "geo/Point/index.html", "geo/Dir/index.html",
                     "geo/Shape/index.html", "site.css" })
            Assert.True(File.Exists(Path.Combine(doc, page)), $"no {page}");

        var check = SiteCheck.Run(doc);
        Assert.True(check.Broken.Count == 0, "links leading nowhere:\n" + string.Join('\n', check.Broken));
        Assert.Empty(check.External);
        Assert.True(check.Checked > 20, $"only {check.Checked} links");
    }

    [Fact]
    public void A_member_stands_under_an_anchor_that_says_what_it_is()
    {
        var root = Geo();
        Assert.Equal(0, RunRaw("doc", "-C", root).Exit);
        string Read(string page) => File.ReadAllText(Path.Combine(root, "out", "doc", page));

        var point = Read("geo/Point/index.html");
        Assert.Contains("id=\"field.x\"", point);
        Assert.Contains("id=\"method.moved\"", point);
        Assert.Contains("id=\"method.area\"", point);
        Assert.Contains("Conforms to <code>Shape</code>.", point);
        Assert.Contains("id=\"variant.Up\"", Read("geo/Dir/index.html"));
        Assert.Contains("id=\"method.isEmpty\"", Read("geo/Shape/index.html"));

        var module = Read("geo/index.html");
        Assert.Contains("id=\"fn.larger\"", module);
        Assert.Contains("id=\"const.limit\"", module);
        Assert.Contains("<h4 id=\"panics\">Panics</h4>", module);
        Assert.Contains("A [Point] is a place", module);
        Assert.Contains("id=\"type.Meters\"", Read("geo/units/index.html"));
    }

    [Fact]
    public void The_module_page_and_the_type_page_match_their_goldens()
    {
        var root = Geo();
        Assert.Equal(0, RunRaw("doc", "-C", root).Exit);
        SiteTests.MatchesGolden("doc-module", File.ReadAllText(Path.Combine(root, "out", "doc", "geo", "index.html")));
        SiteTests.MatchesGolden("doc-type", File.ReadAllText(Path.Combine(root, "out", "doc", "geo", "Point", "index.html")));
    }

    [Fact]
    public void A_signature_is_the_head_the_source_writes()
    {
        var root = Geo();
        Assert.Equal(0, RunRaw("doc", "-C", root).Exit);
        var module = Unmarked(File.ReadAllText(Path.Combine(root, "out", "doc", "geo", "index.html")));
        Assert.Contains("pub fn larger<T :: [Ordered]>(a: T, b: T): T\n", module);
        Assert.Contains("pub let limit: int = 1000\n", module);
        var point = Unmarked(File.ReadAllText(Path.Combine(root, "out", "doc", "geo", "Point", "index.html")));
        Assert.Contains("pub struct Point\n", point);
        Assert.Contains("pub fn moved(dx: int, dy: int): Point\n", point);
        Assert.Contains("pub x: int\n", point);
    }

    private static string Unmarked(string html) =>
        System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", ""));

    [Fact]
    public void Out_names_where_the_reference_goes()
    {
        var root = Geo();
        var elsewhere = TestDirectories.Fresh("doc-out-");
        Assert.Equal(0, RunRaw("doc", "-C", root, "--out", elsewhere).Exit);
        Assert.True(File.Exists(Path.Combine(elsewhere, "geo", "Point", "index.html")));
        Assert.False(Directory.Exists(Path.Combine(root, "out", "doc")));
    }

    [Fact]
    public void Without_a_package_doc_says_so()
    {
        var (exit, _, error) = RunRaw("doc", "-C", TestDirectories.Fresh("doc-none-"));
        Assert.Equal(2, exit);
        Assert.Contains("LYR-CLI0004", error);
    }

    [Fact]
    public void A_package_that_does_not_check_gets_no_reference()
    {
        var root = Package(("lyric.toml", Manifest), ("src/lib.lyr", "/// Broken.\npub fn f(): int { return \"no\"; }\n"));
        var (exit, _, error) = RunRaw("doc", "-C", root);
        Assert.Equal(1, exit);
        Assert.Contains("error[", error);
        Assert.False(Directory.Exists(Path.Combine(root, "out", "doc")));
    }

    [Fact]
    public void The_standard_library_has_a_page_for_every_module_and_no_link_leads_nowhere()
    {
        var output = TestDirectories.Fresh("doc-std-");
        var (exit, said, error) = RunRaw("doc", "--std", "--out", output);
        Assert.True(exit == 0, error);
        Assert.Equal("", error);

        // the library beside the compiler the tests run: the repository's, and the test-only std.notyet
        var std = Path.Combine(AppContext.BaseDirectory, "stdlib5", "std");
        var modules = Directory.EnumerateFiles(std, "*.lyr", SearchOption.AllDirectories).Count();
        Assert.Contains($": {modules} modules,", said);
        foreach (var file in Directory.EnumerateFiles(std, "*.lyr", SearchOption.AllDirectories))
        {
            var segment = Path.GetRelativePath(std, file)[..^4].Replace(Path.DirectorySeparatorChar, '.');
            Assert.True(File.Exists(Path.Combine(output, "std", segment, "index.html")), $"no page for std.{segment}");
        }
        Assert.True(File.Exists(Path.Combine(output, "std", "collections", "List", "index.html")));

        var check = SiteCheck.Run(output);
        Assert.True(check.Broken.Count == 0, "links leading nowhere:\n" + string.Join('\n', check.Broken.Take(20)));
        Assert.Empty(check.External);
    }

    [Fact]
    public void Every_module_of_the_standard_library_opens_with_its_documentation()
    {
        var std = Path.Combine(Lyric5.Toolchain.RuntimeLayout.FindRoot(AppContext.BaseDirectory), "stdlib5", "std");
        var bare = Directory.EnumerateFiles(std, "*.lyr", SearchOption.AllDirectories)
            .Where(f => SurfaceReader.ModuleDoc(File.ReadAllText(f)) is null)
            .Select(f => Path.GetRelativePath(std, f))
            .ToList();
        Assert.True(bare.Count == 0, "std modules without //! lines at the top:\n" + string.Join('\n', bare));
    }

    [Theory]
    [InlineData("//! The module.\n\nimport std.io;\n", "The module.")]
    [InlineData("//! The module.\nimport std.io { println };\n", "The module.")]
    [InlineData("//! The module.\n//!\n//! More.\n\npub fn f(): void {}\n", "The module.\n\nMore.")]
    [InlineData("/// The function.\npub fn f(): void {}\n", null)]
    [InlineData("/// Not the module's: a /// documents what follows it.\n\nimport std.io;\n", null)]
    [InlineData("pub fn f(): void {}\n", null)]
    public void A_module_doc_is_the_inner_doc_lines_that_open_the_file(string text, string? doc) =>
        Assert.Equal(doc, SurfaceReader.ModuleDoc(text));
}
