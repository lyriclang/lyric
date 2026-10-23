using System.Text;

namespace Lyric5.Doc;

/// <summary>One of the three statements under the start page's headline.</summary>
public sealed record Claim(string Title, string Text);

/// <summary>One of the start page's ways in: a labelled target with a line on what is there.</summary>
public sealed record Entry(Link Link, string Text);

/// <summary>What the start page says.</summary>
/// <param name="Program">Lyric source, shown highlighted beside the headline.</param>
public sealed record HomeContent(
    string Headline,
    string Lead,
    Link Primary,
    Link Secondary,
    string Program,
    IReadOnlyList<Claim> Claims,
    IReadOnlyList<Entry> Entries);

/// <summary>A page of text with a navigation beside it: a chapter of the Book, the releases.</summary>
/// <param name="Html">The text's HTML, its title not included.</param>
/// <param name="Navigation">The links of the side navigation.</param>
/// <param name="Where">The side navigation's own name, shown in its bar on a narrow window.</param>
/// <param name="Previous">The page before this one in reading order, linked below the text.</param>
/// <param name="Next">The page after it.</param>
public sealed record ArticleContent(
    string Title,
    string Html,
    IReadOnlyList<Link> Navigation,
    string Where,
    Link? Previous = null,
    Link? Next = null);

/// <summary>A reference page: navigation, text, and the page's own contents beside it.</summary>
public sealed record ReferenceContent(
    string Title,
    string Html,
    IReadOnlyList<Link> Navigation,
    string Where,
    IReadOnlyList<Heading> OnThisPage);

/// <summary>The forms of a page, each written as HTML around the shell of <see cref="Layout"/>.</summary>
public static class Pages
{
    /// <summary>The start page: headline, a paragraph and two buttons beside a program, then
    /// three claims and three ways in.</summary>
    public static string Home(SiteContext site, string path, HomeContent home)
    {
        var sb = new StringBuilder();
        sb.Append("<main class=\"home-main\">\n");
        sb.Append("<section class=\"hero\">\n");
        sb.Append("<div class=\"pitch\">\n");
        sb.Append($"<h1>{Html.Text(home.Headline)}</h1>\n");
        sb.Append($"<p class=\"lead\">{Html.Text(home.Lead)}</p>\n");
        sb.Append("<p class=\"actions\">");
        sb.Append(Layout.Anchor(site, path, home.Primary, "button primary"));
        sb.Append(Layout.Anchor(site, path, home.Secondary, "button"));
        sb.Append("</p>\n");
        sb.Append("</div>\n");
        sb.Append($"<pre class=\"program\"><code class=\"language-lyric\">{Highlighter.Highlight(home.Program)}</code></pre>\n");
        sb.Append("</section>\n");

        sb.Append("<section class=\"claims\">\n");
        foreach (var claim in home.Claims)
            sb.Append($"<div class=\"claim\"><h2>{Html.Text(claim.Title)}</h2><p>{Html.Text(claim.Text)}</p></div>\n");
        sb.Append("</section>\n");

        sb.Append("<section class=\"entries\">\n");
        foreach (var entry in home.Entries)
            sb.Append($"<div class=\"entry\"><h2>{Layout.Anchor(site, path, entry.Link)}</h2><p>{Html.Text(entry.Text)}</p></div>\n");
        sb.Append("</section>\n");
        sb.Append("</main>\n");
        return Layout.Shell(site, path, "Lyric", "home", sb.ToString());
    }

    /// <summary>A page of text in two columns: the navigation and the text.</summary>
    public static string Article(SiteContext site, string path, ArticleContent article)
    {
        var sb = new StringBuilder();
        sb.Append("<div class=\"columns\">\n");
        Sidebar(sb, site, path, article.Navigation, article.Where);
        sb.Append("<main class=\"text\">\n");
        sb.Append($"<h1>{Html.Text(article.Title)}</h1>\n");
        sb.Append(article.Html);
        if (article.Previous is not null || article.Next is not null)
        {
            sb.Append("<nav class=\"pager\" aria-label=\"Previous and next\">");
            if (article.Previous is { } previous)
                sb.Append(Layout.Anchor(site, path, previous with { Label = "← " + previous.Label }, "previous"));
            if (article.Next is { } next)
                sb.Append(Layout.Anchor(site, path, next with { Label = next.Label + " →" }, "next"));
            sb.Append("</nav>\n");
        }
        sb.Append("</main>\n");
        sb.Append("</div>\n");
        return Layout.Shell(site, path, $"{article.Title} — Lyric", "article", sb.ToString());
    }

    /// <summary>A reference page in three columns: the navigation, the text, and the page's
    /// headings under "On this page".</summary>
    public static string Reference(SiteContext site, string path, ReferenceContent reference)
    {
        var sb = new StringBuilder();
        sb.Append("<div class=\"columns\">\n");
        Sidebar(sb, site, path, reference.Navigation, reference.Where);
        sb.Append("<main class=\"text\">\n");
        sb.Append($"<h1>{Html.Text(reference.Title)}</h1>\n");
        sb.Append(reference.Html);
        sb.Append("</main>\n");
        sb.Append("<aside class=\"on-this-page\" aria-label=\"On this page\">\n");
        sb.Append("<p class=\"aside-title\">On this page</p>\n<ul>\n");
        foreach (var heading in reference.OnThisPage.Where(h => h.Level is 2 or 3))
            sb.Append($"<li class=\"level-{heading.Level}\"><a href=\"#{Html.Attr(heading.Anchor)}\">{Html.Text(heading.Text)}</a></li>\n");
        sb.Append("</ul>\n</aside>\n");
        sb.Append("</div>\n");
        return Layout.Shell(site, path, $"{reference.Title} — Lyric", "reference", sb.ToString());
    }

    /// <summary>The side navigation. On a narrow window it is a bar — where the reader is, and a
    /// button that opens the links as a box; without scripts the box stays open.</summary>
    private static void Sidebar(StringBuilder sb, SiteContext site, string path, IReadOnlyList<Link> links, string where)
    {
        sb.Append("<nav class=\"sidebar\" aria-label=\"Contents\">\n");
        sb.Append("<div class=\"sidebar-bar\">");
        sb.Append($"<span class=\"where\">{Html.Text(where)}</span>");
        sb.Append("<button class=\"contents-toggle\" type=\"button\" aria-expanded=\"false\" aria-controls=\"sidebar-box\" hidden=\"hidden\">Contents</button>");
        sb.Append("</div>\n");
        sb.Append("<ul class=\"sidebar-box\" id=\"sidebar-box\">\n");
        foreach (var link in links)
            sb.Append($"<li>{Layout.Anchor(site, path, link, IsCurrent(path, link.Target) ? "current" : null)}</li>\n");
        sb.Append("</ul>\n");
        sb.Append("</nav>\n");
    }

    private static bool IsCurrent(string path, string target) =>
        !target.StartsWith('#') && SiteContext.FileOf(target) == path;
}
