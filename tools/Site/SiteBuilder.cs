using Lyric.Core;
using Lyric5.Doc;
using Lyric5.Doc.Api;
using Lyric5.Build;

namespace Lyric5.Site;

/// <summary>The pages of one version of the site, from the repository's files.</summary>
public static class SiteBuilder
{
    public const string HomePath = "index.html";
    public const string ReleasesPath = "releases/index.html";

    /// <summary>The program beside the start page's headline: the request handler of the JSON
    /// server example, a function that throws and the code that calls what throws.</summary>
    public const string HomeProgramFile = "examples/jsonserver/src/main.lyr";
    public const string HomeProgramFunction = "handle";

    /// <summary>The repository above <paramref name="start"/>: the first directory that holds
    /// <c>CHANGELOG.md</c> and <c>stdlib5/</c>.</summary>
    public static string FindRepository(string start)
    {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "CHANGELOG.md")) && Directory.Exists(Path.Combine(dir.FullName, "stdlib5")))
                return dir.FullName;
        throw new DirectoryNotFoundException($"no repository above {start}");
    }

    /// <summary>The Book's sources, its contents in <c>SUMMARY.md</c>.</summary>
    public const string BookDirectory = "docs/book";
    public const string BookPath = "book/index.html";
    public const string SummaryFile = "SUMMARY.md";

    /// <summary>The diagnostic catalog of the repository.</summary>
    public const string CatalogFile = "src/Lyric.Core/diagnostics.toml";

    /// <summary>The commit of the specification the toolchain is built against.</summary>
    public const string SpecPinFile = "spec.pin";

    /// <summary>The commit <c>spec.pin</c> names.</summary>
    public static string SpecPin(string root) => File.ReadAllText(Path.Combine(root, SpecPinFile)).Trim();

    /// <summary>The version's pages: the start page, the Book, the diagnostic catalog, the
    /// releases, and the specification where a checkout of it is given.</summary>
    /// <param name="gitRef">The branch or tag a link into the repository's files points at.</param>
    public static List<Page> Build(string root, string version, string gitRef, SpecSource? spec = null)
    {
        var bookDir = Path.Combine(root, BookDirectory);
        var summaryText = File.ReadAllText(Path.Combine(bookDir, SummaryFile));
        var summary = Book.ReadSummary(summaryText, SummaryFile);
        var chapterTexts = summary.Chapters.ToDictionary(c => c, c => File.ReadAllText(Path.Combine(bookDir, c.File)));
        var catalog = DiagnosticCatalog.Read(File.ReadAllText(Path.Combine(root, CatalogFile)));
        IReadOnlyList<SpecChapter> specChapters = spec is null ? [] : SpecPages.Chapters(spec);
        var std = StdOf(root, version);

        var paths = new List<string> { HomePath, ReleasesPath, BookPath };
        paths.AddRange(summary.Chapters.Select(c => SiteContext.FileOf(Book.PageOf(c))));
        paths.AddRange(CatalogPages.Paths(catalog));
        if (spec is not null) paths.AddRange(SpecPages.Paths(specChapters));
        paths.AddRange(DocPages.PathsOf(std));
        var site = new SiteContext(version, paths, SiteContext.DefaultAreas);

        var program = FunctionOf(File.ReadAllText(Path.Combine(root, HomeProgramFile)), HomeProgramFunction);
        var changelog = File.ReadAllText(Path.Combine(root, "CHANGELOG.md"));
        var pages = new List<Page>
        {
            new(HomePath, Pages.Home(site, HomePath, Home(program))),
            new(ReleasesPath, Pages.Article(site, ReleasesPath, Releases(changelog, gitRef))),
            new(BookPath, Pages.Article(site, BookPath, BookIndex(summary, summaryText, gitRef))),
        };
        foreach (var chapter in summary.Chapters)
        {
            var path = SiteContext.FileOf(Book.PageOf(chapter));
            pages.Add(new Page(path, Pages.Article(site, path, BookChapter(summary, chapter, chapterTexts[chapter], gitRef))));
        }
        pages.AddRange(CatalogPages.Build(site, catalog));
        if (spec is not null) pages.AddRange(SpecPages.Build(site, specChapters, spec.Commit));
        pages.AddRange(DocPages.Build(std, site));
        return pages;
    }

    /// <summary>The standard library's surface from the repository's <c>stdlib5</c>, read once per
    /// repository and version — every build of the site in one process takes the same.</summary>
    private static Surface StdOf(string root, string version) =>
        StdSurfaces.GetOrAdd((Path.GetFullPath(root), version), key =>
            DocRun.StdSurface(key.Version, Console.Error, Path.Combine(key.Root, "stdlib5"))
            ?? throw new InvalidOperationException($"the standard library under {key.Root} does not check"));

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(string Root, string Version), Surface> StdSurfaces = new();

    /// <summary>The Book's first page: its contents as <c>SUMMARY.md</c> writes them, each
    /// chapter a link, and the first chapter as the way on.</summary>
    public static ArticleContent BookIndex(BookSummary summary, string summaryText, string gitRef)
    {
        var rendered = Markdown.Render(summaryText, target => BookLink(summary, BookPath, target, gitRef), dropTitle: true);
        var first = summary.Chapters.Count > 0 ? ChapterLink(summary.Chapters[0]) : null;
        return new ArticleContent(summary.Title, rendered.Html, BookNavigation(summary), summary.Title, Next: first);
    }

    /// <summary>A chapter's page: its text under its own title, the Book's contents beside it,
    /// the chapters before and after it below.</summary>
    public static ArticleContent BookChapter(BookSummary summary, Chapter chapter, string text, string gitRef)
    {
        var path = SiteContext.FileOf(Book.PageOf(chapter));
        var rendered = Markdown.Render(text, target => BookLink(summary, path, target, gitRef), dropTitle: true);
        var title = rendered.Headings.FirstOrDefault(h => h.Level == 1)?.Text ?? chapter.Label;
        var at = summary.Chapters.ToList().IndexOf(chapter);
        var previous = at == 0 ? new Link(summary.Title, "book/") : ChapterLink(summary.Chapters[at - 1]);
        var next = at + 1 < summary.Chapters.Count ? ChapterLink(summary.Chapters[at + 1]) : null;
        return new ArticleContent(title, rendered.Html, BookNavigation(summary), chapter.Label, previous, next);
    }

    private static Link ChapterLink(Chapter chapter) => new(chapter.Label, Book.PageOf(chapter));

    private static IReadOnlyList<Link> BookNavigation(BookSummary summary) =>
        [new Link(summary.Title, "book/"), .. summary.Chapters.Select(ChapterLink)];

    /// <summary>
    /// Where a link of the Book's page at <paramref name="pagePath"/> points on the site: a
    /// chapter's file to the chapter's page, relative to the page, an anchor kept; another file of
    /// the repository, relative to the Book's directory, to that file on GitHub; a web address or
    /// an anchor as written.
    /// </summary>
    public static string BookLink(BookSummary summary, string pagePath, string target, string gitRef)
    {
        if (target.StartsWith('#') || target.Contains("://", StringComparison.Ordinal)
            || target.StartsWith("mailto:", StringComparison.Ordinal))
            return target;
        var hash = target.IndexOf('#');
        var file = hash < 0 ? target : target[..hash];
        var anchor = hash < 0 ? "" : target[hash..];
        if (summary.Chapters.FirstOrDefault(c => c.File == file) is { } chapter)
            return Layout.Href(pagePath, Book.PageOf(chapter)) + anchor;
        var repositoryPath = Path.GetRelativePath(".", Path.Combine(BookDirectory, file)).Replace('\\', '/');
        return RepositoryLink(repositoryPath + anchor, gitRef);
    }

    /// <summary>What the start page says, around <paramref name="program"/>.</summary>
    public static HomeContent Home(string program) => new(
        Headline: "Lyric: native programs, plainly written.",
        Lead: "A statically typed language for applications, command-line tools and servers. "
              + "It compiles through C to one binary per program, runs its tasks on coroutines with "
              + "their own stacks, and says in a function's signature what it throws.",
        Primary: new Link("Get started", "book/getting-started/"),
        Secondary: new Link("Read the Book", "book/"),
        Program: program,
        Claims:
        [
            new Claim("Native binaries",
                "A program builds into one executable for Linux, macOS or Windows — no virtual machine to install beside it."),
            new Claim("Concurrency without colouring",
                "A task runs on a stack of its own, so any function may wait for input or output; none is marked async."),
            new Claim("Errors in the signature",
                "A function names what it throws; a caller handles it or passes it on with try — no error leaves unseen."),
        ],
        Entries:
        [
            new Entry(new Link("The Book", "book/"), "The language from the first program to a server, chapter by chapter."),
            new Entry(new Link("Standard library", "std/"), "Every module, type and function of std."),
            new Entry(new Link("Specification", "spec/"), "The rules of the language, precisely."),
        ]);

    /// <summary>The releases page: every version's section of the changelog, from the first
    /// <c>## </c> heading on, with the versions as the side navigation.</summary>
    public static ArticleContent Releases(string changelog, string gitRef)
    {
        var start = changelog.IndexOf("\n## ", StringComparison.Ordinal);
        var sections = start < 0 ? "" : changelog[(start + 1)..];
        var rendered = Markdown.Render(sections, target => RepositoryLink(target, gitRef));
        var versions = rendered.Headings
            .Where(h => h.Level == 2)
            .Select(h => new Link(h.Text, "#" + h.Anchor))
            .ToList();
        return new ArticleContent("Releases", rendered.Html, versions, "Releases");
    }

    /// <summary>
    /// Where a link of a repository file points on the site: a web address or an anchor as
    /// written, a path into the repository to that file on GitHub at <paramref name="gitRef"/>.
    /// The repository's documents are not pages of the site, and a version's page keeps
    /// pointing at the files as they were for it.
    /// </summary>
    public static string RepositoryLink(string target, string gitRef)
    {
        if (target.StartsWith('#') || target.Contains("://", StringComparison.Ordinal)
            || target.StartsWith("mailto:", StringComparison.Ordinal))
            return target;
        var path = target.StartsWith("./", StringComparison.Ordinal) ? target[2..] : target.TrimStart('/');
        return $"{Layout.Repository}/blob/{gitRef}/{path}";
    }

    /// <summary>The top-level function <paramref name="name"/> of a Lyric file, from its
    /// <c>fn</c> line to the closing brace at the start of a line.</summary>
    public static string FunctionOf(string source, string name)
    {
        var lines = source.ReplaceLineEndings("\n").Split('\n');
        var first = Array.FindIndex(lines, l => l.StartsWith($"fn {name}(", StringComparison.Ordinal));
        if (first < 0) throw new InvalidDataException($"no top-level fn {name}");
        var last = Array.FindIndex(lines, first, l => l == "}");
        if (last < 0) throw new InvalidDataException($"fn {name} has no closing brace at the start of a line");
        return string.Join('\n', lines[first..(last + 1)]);
    }
}
