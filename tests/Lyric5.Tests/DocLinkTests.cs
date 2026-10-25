using Lyric5.Build;
using Lyric5.Doc;
using Lyric5.Doc.Api;
using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// The links of documentation comments on the reference's pages: <c>[Type]</c>, <c>[Type.member]</c>,
/// <c>[module.Type]</c>, a function, a variant, a member of a generic type, a member named from its
/// own type's documentation. A link is a link on the page exactly where the <c>broken-doc-link</c>
/// lint has nothing to say of it; what the lint calls broken stays text.
/// </summary>
public class DocLinkTests
{
    private const string Manifest = "[package]\nname = \"geo\"\nversion = \"0.1.0\"\n";

    private const string Lib = """
        //! Shapes: a [Point], a [Shape], [area], [Kind.Round], [Crate.get], [shapes.Corner] and
        //! [missing]. Throws [Point]: before a colon in a sentence. `[InCode]` is code.

        import geo.shapes;

        /// A place on the plane; [Point.nope] names nothing.
        pub struct Point {
            pub x: int,
            pub y: int,
        }

        /// A shape with an [area]; see [Point.x].
        pub interface Shape {
            fn area(): int;
        }

        /// The kinds.
        pub enum Kind {
            Round,
            Square,
        }

        /// A box of a [T]; [Crate.get] gives it back.
        pub struct Crate<T> {
            pub value: T,

            /// Gives the [value] back.
            pub fn get(): T {
                return this.value;
            }
        }

        /// The area of a [Point], or of [Nope].
        pub fn area(p: Point): int {
            return p.x * p.y;
        }

        """;

    private const string Shapes = """
        //! More shapes.

        /// A corner.
        pub struct Corner {
            pub at: int,
        }

        """;

    private static (string[] Broken, Dictionary<string, string> Pages) Build()
    {
        var root = Package(("lyric.toml", Manifest), ("src/lib.lyr", Lib), ("src/shapes.lyr", Shapes));
        var manifest = Lyric5.Build.Manifest.Read(Path.Combine(root, "lyric.toml"));
        var graph = Project.Resolve(manifest, offline: true);
        var result = Pipeline.CheckPackage(manifest, graph);
        var broken = result.Diagnostics.Diagnostics.Where(d => d.Code == "LYR-SEM0224").Select(d => d.Message).ToArray();
        var compilation = result.Model!.Compilation;
        var modules = compilation.Modules.Where(m => m.FullName == "geo" || m.FullName.StartsWith("geo.", StringComparison.Ordinal));
        var surface = SurfaceReader.Read(compilation, modules, "geo", "0.1.0");
        var pages = DocRun.Pages(surface).ToDictionary(p => p.Path, p => p.Html, StringComparer.Ordinal);
        return (broken, pages);
    }

    [Fact]
    public void Each_form_links_to_its_page_and_anchor()
    {
        var (_, pages) = Build();
        var module = pages["geo/index.html"];
        Assert.Contains("<a href=\"../geo/Point/\">Point</a>", module);
        Assert.Contains("<a href=\"../geo/Shape/\">Shape</a>", module);
        Assert.Contains("<a href=\"../geo/#fn.area\">area</a>", module);
        Assert.Contains("<a href=\"../geo/Kind/#variant.Round\">Kind.Round</a>", module);
        Assert.Contains("<a href=\"../geo/Crate/#method.get\">Crate.get</a>", module);
        Assert.Contains("<a href=\"../geo/shapes/Corner/\">shapes.Corner</a>", module);
        Assert.Contains("Throws <a href=\"../geo/Point/\">Point</a>:", module);

        var shape = pages["geo/Shape/index.html"];
        Assert.Contains("<a href=\"../../geo/#fn.area\">area</a>", shape);
        Assert.Contains("<a href=\"../../geo/Point/#field.x\">Point.x</a>", shape);

        var crate = pages["geo/Crate/index.html"];
        Assert.Contains("<a href=\"../../geo/Crate/#method.get\">Crate.get</a>", crate);
        Assert.Contains("<a href=\"../../geo/Crate/#field.value\">value</a>", crate);
    }

    [Fact]
    public void What_names_nothing_stays_text_and_the_lint_says_so()
    {
        var (broken, pages) = Build();
        var all = string.Join('\n', pages.Values);
        foreach (var target in new[] { "missing", "Point.nope", "Nope" })
        {
            Assert.Contains($"[{target}]", all);
            Assert.Contains(broken, m => m.Contains($"'[{target}]'", StringComparison.Ordinal));
        }
        // a type parameter is a name, and no page
        Assert.Contains("A box of a [T]", pages["geo/Crate/index.html"]);
        Assert.DoesNotContain(broken, m => m.Contains("'[T]'", StringComparison.Ordinal));
        Assert.Contains("<code>[InCode]</code>", pages["geo/index.html"]);
        Assert.DoesNotContain(broken, m => m.Contains("InCode", StringComparison.Ordinal));
    }

    /// <summary>The page and the lint read one resolution: every link the lint lets pass is a link
    /// on the page or names a page-less name, every one it reports is text.</summary>
    [Fact]
    public void The_page_and_the_lint_agree_on_every_link()
    {
        var (broken, pages) = Build();
        var all = string.Join('\n', pages.Values);
        var reported = broken.Select(m => m.Split("'[")[1].Split("]'")[0]).ToHashSet(StringComparer.Ordinal);
        foreach (var target in Lyric.Sema.DocLinks.LinksIn(Lib + Shapes).Distinct())
        {
            var linked = all.Contains($">{target}</a>", StringComparison.Ordinal);
            if (target == "T") continue; // the type parameter: neither linked nor reported
            Assert.True(linked != reported.Contains(target), $"[{target}]: linked {linked}, reported {reported.Contains(target)}");
        }
        Assert.Equal(3, reported.Count);
    }
}
