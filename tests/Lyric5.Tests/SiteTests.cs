using System.Globalization;
using System.Text.RegularExpressions;
using Lyric5.Doc;
using Lyric5.Site;
using Lyric5.Toolchain;

namespace Lyric5.Tests;

/// <summary>
/// The website's renderer and its tool: each page form against its golden
/// (<c>site/golden/*.html</c>; <c>LYRIC_UPDATE_SNAPSHOTS=1</c> rewrites them), the built site with
/// no link that leads nowhere and no request to another host, the palette's contrast, and the
/// highlighting of Lyric code by the compiler's lexer.
/// </summary>
public partial class SiteTests
{
    private static readonly string Root = RuntimeLayout.FindRoot(AppContext.BaseDirectory);
    private static readonly string Golden = Path.Combine(Root, "tests", "Lyric5.Tests", "site", "golden");

    private const string Program = "fn add(a: int, b: int): int {\n    return a + b; // the sum\n}";

    private const string Changelog = """
        # Changelog

        What this file is.

        ## v2.0.0 — today

        - **A change** ([the plan](design/plan.md)), shown:

        ```lyric
        let total = 1 + 2;
        ```

        ## v1.0.0

        The first. See [README](README.md#versioning), [Rust](https://www.rust-lang.org/) and [above](#v2.0.0-today).
        """;

    private static SiteContext Site(params string[] pages) => new("dev", pages, SiteContext.DefaultAreas);

    internal static void MatchesGolden(string name, string actual)
    {
        var path = Path.Combine(Golden, name + ".html");
        if (Environment.GetEnvironmentVariable("LYRIC_UPDATE_SNAPSHOTS") == "1")
        {
            Directory.CreateDirectory(Golden);
            File.WriteAllText(path, actual);
        }
        Assert.True(File.Exists(path), $"no golden at {path}; set LYRIC_UPDATE_SNAPSHOTS=1 to write it");
        Assert.Equal(File.ReadAllText(path).ReplaceLineEndings("\n"), actual);
    }

    [Fact]
    public void The_start_page_matches_its_golden()
    {
        var site = Site(SiteBuilder.HomePath, SiteBuilder.ReleasesPath);
        MatchesGolden("home", Pages.Home(site, SiteBuilder.HomePath, SiteBuilder.Home(Program)));
    }

    [Fact]
    public void An_article_page_matches_its_golden()
    {
        var site = Site(SiteBuilder.HomePath, SiteBuilder.ReleasesPath);
        MatchesGolden("article", Pages.Article(site, SiteBuilder.ReleasesPath, SiteBuilder.Releases(Changelog, "v2.0.0")));
    }

    [Fact]
    public void A_reference_page_matches_its_golden()
    {
        const string path = "std/collections/List/index.html";
        var site = Site(SiteBuilder.HomePath, path);
        var text = Markdown.Render("# List\n\nA growable array.\n\n## Methods\n\n### push\n\nAppends a value.\n\n```lyric\nxs.push(1);\n```\n", dropTitle: true);
        var page = new ReferenceContent("List", text.Html,
            [new Link("std.collections", "std/collections/"), new Link("List", "std/collections/List/")],
            "std.collections", text.Headings);
        MatchesGolden("reference", Pages.Reference(site, path, page));
    }

    private static readonly BookSummary Summary =
        Book.ReadSummary("# The Book\n\n- [Start](start.md)\n- [Next steps](next.md)\n- [Last](last.md)\n", "SUMMARY.md");

    private const string Chapter = """
        # Next steps

        After [the start](start.md#install), and before [the last](last.md); the
        [grammar](../Grammar.md) is elsewhere.

        ```lyric,no_run
        let x = 1;
        ```

        ```lyric
        println(2);
        ```

        ```output
        2
        ```
        """;

    [Fact]
    public void A_book_page_matches_its_golden()
    {
        var paths = new[] { SiteBuilder.HomePath, SiteBuilder.BookPath }
            .Concat(Summary.Chapters.Select(c => SiteContext.FileOf(Book.PageOf(c))));
        var site = Site([.. paths]);
        var chapter = Summary.Chapters[1];
        var path = SiteContext.FileOf(Book.PageOf(chapter));
        MatchesGolden("book", Pages.Article(site, path, SiteBuilder.BookChapter(Summary, chapter, Chapter, "main")));
    }

    [Fact]
    public void A_book_page_links_its_neighbours_and_shows_no_fence_kind()
    {
        var html = SiteBuilder.BookChapter(Summary, Summary.Chapters[1], Chapter, "main").Html;
        Assert.Contains("<a href=\"../../book/start/#install\">the start</a>", html);
        Assert.Contains("<a href=\"../../book/last/\">the last</a>", html);
        Assert.Contains("<a href=\"https://github.com/lyriclang/lyric/blob/main/docs/Grammar.md\">grammar</a>", html);
        Assert.Contains("<pre class=\"output\"><code>2</code></pre>", html);
        Assert.DoesNotContain("no_run", html);

        var first = SiteBuilder.BookChapter(Summary, Summary.Chapters[0], "# Start\n", "main");
        Assert.Equal(new Link("The Book", "book/"), first.Previous);
        Assert.Equal(new Link("Next steps", "book/next/"), first.Next);
        Assert.Null(SiteBuilder.BookChapter(Summary, Summary.Chapters[2], "# Last\n", "main").Next);
    }

    [Fact]
    public void The_built_start_page_leads_into_the_Book()
    {
        var home = SiteBuilder.Build(Root, "dev", "main").Single(p => p.Path == SiteBuilder.HomePath).Html;
        Assert.Contains("<a class=\"button primary\" href=\"book/getting-started/\">Get started</a>", home);
        Assert.Contains("<a class=\"button\" href=\"book/\">Read the Book</a>", home);
        Assert.Contains("<a href=\"book/\">Book</a>", home);
    }

    /// <summary>The site of the repository, built into a fresh directory.</summary>
    private static string BuildSite()
    {
        var output = Path.Combine(Path.GetTempPath(), "lyric5-site-" + Guid.NewGuid().ToString("N"));
        SiteWriter.Write(Path.Combine(output, "dev"), SiteBuilder.Build(Root, "dev", "main"));
        return output;
    }

    /// <summary>Every relative link of a built site that reaches no written file or no anchor
    /// there, and how many links were looked at.</summary>
    internal static (List<string> Broken, int Checked) LinksOf(string output) => SiteCheck.Links(output);

    [Fact]
    public void The_built_site_has_no_link_that_leads_nowhere()
    {
        var output = BuildSite();
        try
        {
            var (broken, checkedLinks) = LinksOf(output);
            Assert.True(broken.Count == 0, "links that lead nowhere:\n  " + string.Join("\n  ", broken));
            Assert.True(checkedLinks > 20, $"only {checkedLinks} links were checked");
        }
        finally
        {
            Directory.Delete(output, recursive: true);
        }
    }

    /// <summary>The check the publishing workflow runs over the built tree: a good tree passes, a
    /// link to a missing page, a missing anchor and a stylesheet from another host are each found.</summary>
    [Fact]
    public void The_site_check_finds_what_would_break_on_the_published_site()
    {
        var output = Directory.CreateTempSubdirectory("lyric5-site-check").FullName;
        try
        {
            void Write(string path, string text)
            {
                var file = Path.Combine(output, path);
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllText(file, text);
            }
            Write("index.html", "<a href=\"book/\">Book</a> <a href=\"book/#start\">start</a> <a href=\"/legacy/\">old</a> <a href=\"https://example.org/\">x</a>");
            Write("book/index.html", "<h1 id=\"start\">Start</h1> <a href=\"../\">home</a>");
            Write("site.css", "body { font-family: Inter; }");

            var good = SiteCheck.Run(output);
            Assert.True(good.Passed, string.Join("\n", good.Broken.Concat(good.External)));
            Assert.Equal(3, good.Checked);

            Write("book/index.html", "<h1 id=\"begin\">Start</h1> <a href=\"../spec/\">spec</a> <link rel=\"stylesheet\" href=\"https://fonts.example/a.css\">");
            Write("site.css", "@import url(\"https://fonts.example/b.css\");");
            var bad = SiteCheck.Run(output);
            Assert.False(bad.Passed);
            Assert.Equal(["book/index.html: ../spec/", "index.html: book/#start (no such anchor)"],
                bad.Broken.Select(b => b.Replace('\\', '/')).Order(StringComparer.Ordinal));
            // The link element, and in the stylesheet both the @import and its url().
            Assert.Equal(3, bad.External.Count);
        }
        finally
        {
            Directory.Delete(output, recursive: true);
        }
    }

    [Fact]
    public void No_page_asks_another_host_for_anything()
    {
        var output = BuildSite();
        try
        {
            var offenders = SiteCheck.External(output);
            Assert.True(offenders.Count == 0, "requests to another host:\n  " + string.Join("\n  ", offenders));
        }
        finally
        {
            Directory.Delete(output, recursive: true);
        }
    }

    [GeneratedRegex("url\\(\\s*\"(?<path>[^\"]+)\"\\s*\\)")]
    private static partial Regex CssUrl();

    [Fact]
    public void Every_font_the_stylesheet_names_is_shipped_with_its_licence()
    {
        var files = Theme.Files().Select(f => f.Path).ToHashSet();
        var urls = CssUrl().Matches(Theme.Text(Theme.Stylesheet)).Select(m => m.Groups["path"].Value).ToList();

        Assert.True(urls.Count >= 5, $"the stylesheet names {urls.Count} files");
        Assert.All(urls, url => Assert.Contains(url, files));
        Assert.Contains("fonts/Inter-LICENSE.txt", files);
        Assert.Contains("fonts/JetBrainsMono-OFL.txt", files);
    }

    [GeneratedRegex("--(?<name>[a-z]+):\\s*(?<value>#[0-9a-f]{6});")]
    private static partial Regex Colour();

    /// <summary>The colours of the block that opens with <paramref name="selector"/>.</summary>
    private static Dictionary<string, string> Palette(string css, string selector)
    {
        var start = css.IndexOf(selector + " {", StringComparison.Ordinal);
        Assert.True(start >= 0, $"no block {selector}");
        var end = css.IndexOf('}', start);
        return Colour().Matches(css[start..end]).ToDictionary(m => m.Groups["name"].Value, m => m.Groups["value"].Value);
    }

    private static readonly string Css = Theme.Text(Theme.Stylesheet);

    [Fact]
    public void The_palette_is_the_one_of_the_design()
    {
        var light = Palette(Css, ":root");
        var dark = Palette(Css, ":root[data-theme=\"dark\"]");
        Assert.Equal(("#ffffff", "#16181d", "#535a69", "#e1e4e9", "#f4f5f7", "#6440c4"),
            (light["bg"], light["text"], light["muted"], light["line"], light["surface"], light["accent"]));
        Assert.Equal(("#111317", "#e7e9ee", "#a3aab6", "#2b2f37", "#181b21", "#b29aff"),
            (dark["bg"], dark["text"], dark["muted"], dark["line"], dark["surface"], dark["accent"]));
        // The system's dark and the switch's dark are one palette.
        Assert.Equal(dark, Palette(Css, ":root:not([data-theme=\"light\"])"));
    }

    [Theory]
    [InlineData(":root")]
    [InlineData(":root[data-theme=\"dark\"]")]
    public void Every_text_colour_reads_at_4_5_to_1_on_its_grounds(string selector)
    {
        var palette = Palette(Css, selector);
        (string Fore, string Back)[] pairs =
        [
            ("text", "bg"), ("muted", "bg"), ("accent", "bg"), ("literal", "bg"),
            ("text", "surface"), ("muted", "surface"), ("accent", "surface"), ("literal", "surface"),
            ("bg", "accent"),
        ];
        var low = pairs
            .Select(p => (p.Fore, p.Back, Ratio: Contrast(palette[p.Fore], palette[p.Back])))
            .Where(p => p.Ratio < 4.5)
            .Select(p => $"{p.Fore} on {p.Back}: {p.Ratio.ToString("0.00", CultureInfo.InvariantCulture)}:1")
            .ToList();
        Assert.True(low.Count == 0, "below 4.5:1:\n  " + string.Join("\n  ", low));
    }

    /// <summary>The contrast ratio of two colours as WCAG 2 defines it.</summary>
    private static double Contrast(string a, string b)
    {
        var (la, lb) = (Luminance(a), Luminance(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance(string hex)
    {
        double Channel(int at)
        {
            var c = int.Parse(hex.AsSpan(at, 2), NumberStyles.HexNumber) / 255.0;
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(1) + 0.7152 * Channel(3) + 0.0722 * Channel(5);
    }

    [Fact]
    public void Lyric_code_is_marked_by_its_tokens()
    {
        const string source = "fn main(): int throws Error {\n    let s = \"hi\"; // greet\n    return 42;\n}";
        var html = Highlighter.Highlight(source);

        Assert.Contains("<span class=\"kw\">fn</span>", html);
        Assert.Contains("<span class=\"kw\">throws</span>", html);
        Assert.Contains("<span class=\"ty\">int</span>", html);
        Assert.Contains("<span class=\"ty\">Error</span>", html);
        Assert.Contains("<span class=\"str\">\"hi\"</span>", html);
        Assert.Contains("<span class=\"com\">// greet</span>", html);
        Assert.Contains("<span class=\"num\">42</span>", html);
        Assert.Equal(source, Unmarked(html));
    }

    [Fact]
    public void Code_the_lexer_refuses_stays_unmarked()
    {
        const string source = "let s = \"never closed <b>";
        Assert.Equal(Html.Text(source), Highlighter.Highlight(source));
    }

    private static string Unmarked(string html) =>
        Regex.Replace(html, "<[^>]+>", "").Replace("&lt;", "<").Replace("&gt;", ">").Replace("&amp;", "&");

    [Fact]
    public void Only_a_lyric_fence_is_highlighted()
    {
        var html = Markdown.Render("```lyric,no_run\nlet x = 1;\n```\n\n```toml\n[package]\nname = \"a\"\n```\n").Html;
        Assert.Contains("<pre><code class=\"language-lyric\"><span class=\"kw\">let</span> x = <span class=\"num\">1</span>;</code></pre>", html);
        Assert.Contains("<pre><code class=\"language-toml\">[package]\nname = \"a\"</code></pre>", html);
    }

    [Fact]
    public void A_snippet_that_must_fail_says_so_and_with_which_code()
    {
        var fails = Markdown.Render("```lyric,compile_fail=LYR-SEM0138\nlet x = f();\n```\n").Html;
        Assert.Contains("<p class=\"snippet-note fails\">Does not compile — the compiler reports <code>LYR-SEM0138</code></p>\n<pre class=\"fails\">", fails);

        var noRun = Markdown.Render("```lyric,no_run\nlet x = 1;\n```\n").Html;
        Assert.Contains("<p class=\"snippet-note no-run\">Compiles; not run here</p>", noRun);

        var runs = Markdown.Render("```lyric\nlet x = 1;\n```\n").Html;
        Assert.DoesNotContain("snippet-note", runs);
    }

    [Theory]
    [InlineData("design/plan.md", "https://github.com/lyriclang/lyric/blob/main/design/plan.md")]
    [InlineData("README.md#versioning", "https://github.com/lyriclang/lyric/blob/main/README.md#versioning")]
    [InlineData("./docs/Grammar.md", "https://github.com/lyriclang/lyric/blob/main/docs/Grammar.md")]
    [InlineData("#v1-0-0", "#v1-0-0")]
    [InlineData("https://www.rust-lang.org/", "https://www.rust-lang.org/")]
    public void A_link_into_the_repository_points_at_its_file_on_GitHub(string target, string expected) =>
        Assert.Equal(expected, SiteBuilder.RepositoryLink(target, "main"));

    [Fact]
    public void A_page_the_version_lacks_is_named_not_linked()
    {
        var book = new Link("Book", "book/");
        Assert.Equal("<span class=\"pending\">Book</span>", Layout.Anchor(Site("index.html"), "index.html", book));
        var site = Site("index.html", "book/index.html", "releases/index.html");
        Assert.Equal("<a href=\"book/\">Book</a>", Layout.Anchor(site, "index.html", book));
        Assert.Equal("<a href=\"../book/\">Book</a>", Layout.Anchor(site, "releases/index.html", book));
        Assert.Equal("<a href=\"../\">Home</a>", Layout.Anchor(site, "releases/index.html", new Link("Home", "")));
    }

    [Fact]
    public void The_start_page_shows_a_function_of_the_server_example()
    {
        var source = File.ReadAllText(Path.Combine(Root, SiteBuilder.HomeProgramFile));
        var function = SiteBuilder.FunctionOf(source, SiteBuilder.HomeProgramFunction);
        Assert.StartsWith("fn handle(", function);
        Assert.EndsWith("\n}", function);
        Assert.Contains("throws", function);
        Assert.Contains("<span class=\"kw\">fn</span>", Highlighter.Highlight(function));
    }
}
