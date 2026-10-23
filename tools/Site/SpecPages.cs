using System.Diagnostics;
using Lyric5.Doc;

namespace Lyric5.Site;

/// <summary>A checkout of the specification's repository and the commit it is at.</summary>
/// <param name="Directory">The checkout; its chapters are <c>spec/*.md</c>.</param>
/// <param name="Commit">The commit its links into the repository point at.</param>
public sealed record SpecSource(string Directory, string Commit)
{
    /// <summary>The checkout at <paramref name="directory"/>, refused unless it is at
    /// <paramref name="pinned"/>: a version's site shows the rules its toolchain was built against.</summary>
    /// <exception cref="InvalidOperationException">The checkout is at another commit, or no git checkout.</exception>
    public static SpecSource Open(string directory, string pinned)
    {
        var head = Git(directory, "rev-parse", "HEAD");
        if (head != pinned)
            throw new InvalidOperationException(
                $"the specification at {directory} is at {Short(head)}, the toolchain is pinned to {Short(pinned)}: "
                + $"check out {Short(pinned)} there (git -C {directory} checkout {pinned})");
        return new SpecSource(Path.GetFullPath(directory), head);
    }

    private static string Short(string commit) => commit.Length > 12 ? commit[..12] : commit;

    private static string Git(string directory, params string[] args)
    {
        var start = new ProcessStartInfo("git") { RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("-C");
        start.ArgumentList.Add(directory);
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var git = Process.Start(start) ?? throw new InvalidOperationException("git did not start");
        var output = git.StandardOutput.ReadToEnd().Trim();
        var error = git.StandardError.ReadToEnd().Trim();
        git.WaitForExit();
        if (git.ExitCode != 0)
            throw new InvalidOperationException($"{directory} is no git checkout of the specification: {error}");
        return output;
    }
}

/// <summary>One chapter of the specification: its file under <c>spec/</c> and its text.</summary>
public sealed record SpecChapter(string File, string Text)
{
    /// <summary>The chapter's name in its page's path: the file's name without its extension.</summary>
    public string Slug => Path.GetFileNameWithoutExtension(File);

    /// <summary>The chapter's page under the version root.</summary>
    public string Target => $"{SpecPages.Directory}/{Slug}/";
}

/// <summary>
/// The specification as pages: an index of its chapters and one reference page per chapter, the
/// chapters as the navigation, the chapter's headings beside the text. A link from one chapter to
/// another leads to that chapter's page; a link to another file of the specification's repository
/// leads to it on GitHub at the commit the pages were made from.
/// </summary>
public static class SpecPages
{
    public const string Directory = "spec";
    public const string IndexPath = "spec/index.html";
    public const string Repository = "https://github.com/lyriclang/lyric-spec";

    /// <summary>The chapters of a checkout, in the order of their file names.</summary>
    public static IReadOnlyList<SpecChapter> Chapters(SpecSource source)
    {
        var dir = Path.Combine(source.Directory, Directory);
        return System.IO.Directory.EnumerateFiles(dir, "*.md")
            .Select(f => Path.GetFileName(f))
            .Order(StringComparer.Ordinal)
            .Select(f => new SpecChapter(f, File.ReadAllText(Path.Combine(dir, f))))
            .ToList();
    }

    /// <summary>The paths of the pages <see cref="Build"/> writes.</summary>
    public static IEnumerable<string> Paths(IReadOnlyList<SpecChapter> chapters) =>
        chapters.Select(c => SiteContext.FileOf(c.Target)).Prepend(IndexPath);

    /// <summary>The index and every chapter's page.</summary>
    public static IEnumerable<Page> Build(SiteContext site, IReadOnlyList<SpecChapter> chapters, string commit)
    {
        var rendered = chapters.ToDictionary(c => c, c => Render(c, commit));
        var navigation = Navigation(chapters, rendered);

        yield return new Page(IndexPath, Pages.Article(site, IndexPath, Index(chapters, rendered, navigation)));
        foreach (var chapter in chapters)
        {
            var path = SiteContext.FileOf(chapter.Target);
            var text = rendered[chapter];
            var content = new ReferenceContent(Title(chapter, text), text.Html, navigation, "Specification", text.Headings);
            yield return new Page(path, Pages.Reference(site, path, content));
        }
    }

    /// <summary>The specification's first page: its chapters, each by its title.</summary>
    public static ArticleContent Index(IReadOnlyList<SpecChapter> chapters,
        IReadOnlyDictionary<SpecChapter, RenderedMarkdown> rendered, IReadOnlyList<Link> navigation)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("<p>The rules of the language: what a program means, what the toolchain must refuse, and "
                  + "what it reports. A second implementation can be built from them.</p>\n<ol class=\"chapters\">\n");
        foreach (var chapter in chapters)
            sb.Append($"<li><a href=\"{Html.Attr(Layout.Href(IndexPath, chapter.Target))}\">{Html.Text(Title(chapter, rendered[chapter]))}</a></li>\n");
        sb.Append("</ol>\n");
        return new ArticleContent("Specification", sb.ToString(), navigation, "Specification");
    }

    private static IReadOnlyList<Link> Navigation(IReadOnlyList<SpecChapter> chapters,
        IReadOnlyDictionary<SpecChapter, RenderedMarkdown> rendered) =>
        [new Link("Specification", Directory + "/"), .. chapters.Select(c => new Link(Title(c, rendered[c]), c.Target))];

    private static string Title(SpecChapter chapter, RenderedMarkdown text) =>
        text.Headings.FirstOrDefault(h => h.Level == 1)?.Text ?? chapter.Slug;

    private static RenderedMarkdown Render(SpecChapter chapter, string commit) =>
        Markdown.Render(chapter.Text, target => Link(SiteContext.FileOf(chapter.Target), target, commit),
            dropTitle: true, gitHubAnchors: true);

    /// <summary>
    /// Where a link of the chapter page at <paramref name="pagePath"/> points: a chapter's file to
    /// its page, an anchor kept; another file of the repository, relative to <c>spec/</c>, to that
    /// file on GitHub at <paramref name="commit"/>; a web address or an anchor as written. Every
    /// Markdown file in <c>spec/</c> is a chapter, so a link to one that is none leads to a page
    /// that does not exist, where the site's link check finds it.
    /// </summary>
    public static string Link(string pagePath, string target, string commit)
    {
        if (target.StartsWith('#') || target.Contains("://", StringComparison.Ordinal)
            || target.StartsWith("mailto:", StringComparison.Ordinal))
            return target;
        var hash = target.IndexOf('#');
        var file = hash < 0 ? target : target[..hash];
        var anchor = hash < 0 ? "" : target[hash..];
        var path = Normalize(Directory + "/" + file);
        if (path.StartsWith(Directory + "/", StringComparison.Ordinal) && !path[(Directory.Length + 1)..].Contains('/')
            && path.EndsWith(".md", StringComparison.Ordinal))
            return Layout.Href(pagePath, $"{Directory}/{Path.GetFileNameWithoutExtension(path)}/") + anchor;
        return $"{Repository}/blob/{commit}/{path}{anchor}";
    }

    /// <summary>A repository path with its <c>.</c> and <c>..</c> steps taken.</summary>
    private static string Normalize(string path)
    {
        var parts = new List<string>();
        foreach (var part in path.Split('/'))
        {
            if (part is "" or ".") continue;
            if (part == "..") { if (parts.Count > 0) parts.RemoveAt(parts.Count - 1); }
            else parts.Add(part);
        }
        return string.Join('/', parts);
    }
}
