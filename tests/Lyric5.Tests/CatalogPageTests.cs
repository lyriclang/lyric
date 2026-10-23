using Lyric.Core;
using Lyric5.Doc;
using Lyric5.Site;
using Lyric5.Toolchain;

namespace Lyric5.Tests;

/// <summary>
/// The specification and the diagnostic catalog as pages of the site: a page for every code and
/// the index that lists them, the URL a tool names for a code, a chapter of the specification with
/// its links, and the refusal of a specification checkout the toolchain is not pinned to. The
/// goldens are <c>site/golden/code.html</c> and <c>site/golden/spec.html</c>, made from fixtures,
/// so neither moves with the catalog or the specification.
/// </summary>
public class CatalogPageTests
{
    private static readonly string Root = RuntimeLayout.FindRoot(AppContext.BaseDirectory);

    private static IReadOnlyList<CatalogEntry> Catalog() =>
        DiagnosticCatalog.Read(File.ReadAllText(Path.Combine(Root, SiteBuilder.CatalogFile)));

    /// <summary>The catalog is read on the site and by <c>lyric explain</c>: like a comment, none of
    /// its lines names a design label, a milestone, a slice or a section of the specification.</summary>
    [Fact]
    public void No_line_of_the_catalog_cites_the_design()
    {
        var lines = File.ReadAllLines(Path.Combine(Root, SiteBuilder.CatalogFile));
        var offenders = lines.Select((line, i) => (Line: i + 1, Text: line))
            .Where(l => CommentCitationTests.Cites(l.Text) || l.Text.Contains('§'))
            .Select(l => $"  {l.Line}: {l.Text}")
            .ToList();
        Assert.True(offenders.Count == 0, "catalog lines that cite the design:\n" + string.Join('\n', offenders));
    }

    [Fact]
    public void Every_code_of_the_catalog_has_its_page()
    {
        var catalog = Catalog();
        var pages = SiteBuilder.Build(Root, "dev", "main").Select(p => p.Path).ToHashSet();

        var codePages = pages.Where(p => p.StartsWith(CatalogPages.Directory + "/LYR-", StringComparison.Ordinal)).ToList();
        Assert.Equal(catalog.Count, codePages.Count);
        Assert.All(catalog, e => Assert.Contains(SiteContext.FileOf(DiagnosticCatalog.PagePath(e.Code)), pages));
        Assert.Contains(CatalogPages.IndexPath, pages);
    }

    [Fact]
    public void The_index_links_every_code_by_its_family()
    {
        var catalog = Catalog();
        var index = CatalogPages.Index(catalog).Html;
        Assert.All(catalog, e => Assert.Contains($"<a href=\"../diagnostics/{e.Code}/\"><code>{e.Code}</code></a>", index));
        Assert.Contains("<h2 id=\"sem\">Checking <span class=\"family\">SEM</span></h2>", index);
    }

    private static readonly CatalogEntry Entry = new("LYR-SEM0999", "a value that is never read", "warning",
        "07-statements", "Reported where a `let` names a value nothing reads.")
    {
        Explanation = "A value no one reads is work for nothing, or a name that was meant elsewhere.",
        Wrong = "fn main(): void {\n    let unused = 1;\n}",
        Right = "fn main(): void {\n    let _ = 1;\n}",
        Lint = "unused-value",
        Group = "unused",
        Level = LintLevel.Warn,
    };

    [Fact]
    public void A_code_page_matches_its_golden()
    {
        var path = SiteContext.FileOf(DiagnosticCatalog.PagePath(Entry.Code));
        var site = new SiteContext("dev", [SiteBuilder.HomePath, CatalogPages.IndexPath, path, "spec/07-statements/index.html"],
            SiteContext.DefaultAreas);
        SiteTests.MatchesGolden("code", Pages.Reference(site, path, CatalogPages.CodePage(site, path, Entry, [Entry])));
    }

    [Theory]
    [InlineData("5.0.2", "5.0")]
    [InlineData("5.1.0", "5.1")]
    [InlineData("5.0.0-dev.20261023+6d7bd38", "dev")]
    [InlineData("5.0.0+6d7bd38", "dev")]
    [InlineData("5.0.0", "5.0")]
    public void A_toolchain_names_the_page_of_its_own_version(string version, string site)
    {
        Assert.Equal(site, DiagnosticCatalog.SiteVersion(version));
        Assert.Equal($"https://lyric.oil1i.de/{site}/diagnostics/LYR-SEM0001/", DiagnosticCatalog.PageUrl("LYR-SEM0001", version));
    }

    private static readonly IReadOnlyList<SpecChapter> Chapters =
    [
        new("01-first.md", """
            # First things

            The start. See [the second chapter](02-second.md#2-the-second-rule), [its start](02-second.md),
            [a rule here](#1-the-first-rule), the [suite](../conformance/README.md) and the
            [design](https://github.com/lyriclang/lyric/blob/main/design/v5/spec/08-syntax.md).

            ## 1. The first rule

            1. A rule: `let x = 1;`

            ```lyric
            fn main(): void {
            }
            ```
            """),
        new("02-second.md", """
            # Second things

            ## 1. A rule

            Back to [the first](01-first.md#1-the-first-rule).

            ## 2. The second rule

            The end.
            """),
    ];

    private const string Commit = "0123456789abcdef0123456789abcdef01234567";

    private static List<Page> SpecSite(IReadOnlyList<SpecChapter> chapters)
    {
        var site = new SiteContext("dev", SpecPages.Paths(chapters).Prepend(SiteBuilder.HomePath), SiteContext.DefaultAreas);
        return [.. SpecPages.Build(site, chapters, Commit)];
    }

    [Fact]
    public void A_spec_page_matches_its_golden()
    {
        SiteTests.MatchesGolden("spec", SpecSite(Chapters).Single(p => p.Path == "spec/01-first/index.html").Html);
    }

    [Fact]
    public void A_chapter_links_the_others_by_page_and_the_repository_on_GitHub()
    {
        var html = SpecSite(Chapters).Single(p => p.Path == "spec/01-first/index.html").Html;
        Assert.Contains("<a href=\"../../spec/02-second/#2-the-second-rule\">the second chapter</a>", html);
        Assert.Contains("<a href=\"#1-the-first-rule\">a rule here</a>", html);
        Assert.Contains($"<a href=\"https://github.com/lyriclang/lyric-spec/blob/{Commit}/conformance/README.md\">suite</a>", html);
        Assert.Contains("<h2 id=\"1-the-first-rule\">", html);
    }

    /// <summary>The site with the specification's fixture and the repository's own catalog.</summary>
    private static (List<string> Broken, int Checked) LinksOfSiteWith(IReadOnlyList<SpecChapter> chapters)
    {
        var spec = Path.Combine(Path.GetTempPath(), "lyric5-spec-" + Guid.NewGuid().ToString("N"));
        var output = Path.Combine(Path.GetTempPath(), "lyric5-site-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(spec, SpecPages.Directory));
            foreach (var chapter in chapters)
                File.WriteAllText(Path.Combine(spec, SpecPages.Directory, chapter.File), chapter.Text);
            SiteWriter.Write(Path.Combine(output, "dev"), SiteBuilder.Build(Root, "dev", "main", new SpecSource(spec, Commit)));
            return SiteTests.LinksOf(output);
        }
        finally
        {
            Directory.Delete(spec, recursive: true);
            if (Directory.Exists(output)) Directory.Delete(output, recursive: true);
        }
    }

    [Fact]
    public void The_site_with_spec_and_catalog_has_no_link_that_leads_nowhere()
    {
        var (broken, checkedLinks) = LinksOfSiteWith(Chapters);
        Assert.True(broken.Count == 0, "links that lead nowhere:\n  " + string.Join("\n  ", broken));
        Assert.True(checkedLinks > 1000, $"only {checkedLinks} links were checked");
    }

    [Fact]
    public void A_link_to_a_chapter_that_is_not_there_is_found()
    {
        var chapters = Chapters.Select(c => c.File == "02-second.md" ? c with { Text = c.Text + "\nSee [nothing](03-none.md).\n" } : c).ToList();
        var (broken, _) = LinksOfSiteWith(chapters);
        Assert.Equal(["dev/spec/02-second/index.html: ../../spec/03-none/"], broken.Select(b => b.Replace('\\', '/')));
    }

    [Fact]
    public void A_specification_at_another_commit_than_the_pin_is_refused()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lyric5-spec-git-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Git(dir, "init", "-q");
            File.WriteAllText(Path.Combine(dir, "README"), "spec\n");
            Git(dir, "add", "README");
            Git(dir, "-c", "user.name=t", "-c", "user.email=t@t", "commit", "-q", "-m", "spec");
            var head = Git(dir, "rev-parse", "HEAD");

            Assert.Equal(head, SpecSource.Open(dir, head).Commit);
            var refused = Assert.Throws<InvalidOperationException>(() => SpecSource.Open(dir, Commit));
            Assert.Contains($"is at {head[..12]}, the toolchain is pinned to {Commit[..12]}", refused.Message);
        }
        finally
        {
            // git writes its objects read-only, which Windows does not delete.
            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(dir, recursive: true);
        }
    }

    private static string Git(string dir, params string[] args)
    {
        var ran = ProcessRunner.Run("git", ["-C", dir, .. args], TimeSpan.FromMinutes(1));
        Assert.True(ran.ExitCode == 0, ran.Stderr);
        return ran.Stdout.Trim();
    }
}
