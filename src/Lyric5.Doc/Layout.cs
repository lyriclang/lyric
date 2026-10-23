using System.Text;

namespace Lyric5.Doc;

/// <summary>
/// The shell around every page: the head, the header as a toolbar — the wordmark with the version,
/// the areas, the search, the theme switch, GitHub — and the footer. The page forms in
/// <see cref="Pages"/> write what goes between.
///
/// <para>Every link is relative to the page, so a version's directory can be served from any
/// place; the root-absolute <c>/legacy/</c> is the one exception, since it lives beside the
/// versions. Void elements are written self-closing, so a page also reads as XML.</para>
/// </summary>
public static class Layout
{
    public const string Repository = "https://github.com/lyriclang/lyric";
    public const string LegacyLabel = "Lyric 1.0.0–4.5.0 (Legacy)";
    public const string LegacyTarget = "/legacy/";

    /// <summary>A full page.</summary>
    /// <param name="path">The page's path under the version root.</param>
    /// <param name="kind">The layout's class on <c>body</c>: <c>home</c>, <c>article</c>, <c>reference</c>.</param>
    /// <param name="body">What stands between header and footer.</param>
    public static string Shell(SiteContext site, string path, string title, string kind, string body)
    {
        var root = SiteContext.RootOf(path);
        var sb = new StringBuilder();
        sb.Append("<!doctype html>\n");
        sb.Append($"<html lang=\"en\" data-root=\"{Html.Attr(root)}\" data-version=\"{Html.Attr(site.Version)}\">\n");
        sb.Append("<head>\n");
        sb.Append("<meta charset=\"utf-8\" />\n");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />\n");
        sb.Append($"<title>{Html.Text(title)}</title>\n");
        sb.Append($"<link rel=\"stylesheet\" href=\"{root}{Theme.Stylesheet}\" />\n");
        sb.Append($"<script src=\"{root}{Theme.ThemeScript}\"></script>\n");
        sb.Append("</head>\n");
        sb.Append($"<body class=\"{Html.Attr(kind)}\">\n");
        Header(sb, site, path, root);
        sb.Append(body);
        Footer(sb, site, path);
        sb.Append($"<script src=\"{root}{Theme.NavScript}\" defer=\"defer\"></script>\n");
        sb.Append($"<script src=\"{root}{Theme.VersionScript}\" defer=\"defer\"></script>\n");
        sb.Append("</body>\n</html>\n");
        return sb.ToString();
    }

    private static void Header(StringBuilder sb, SiteContext site, string path, string root)
    {
        sb.Append("<header class=\"topbar\">\n");
        sb.Append("<div class=\"brand\">");
        sb.Append($"<a class=\"wordmark\" href=\"{(root.Length == 0 ? "./" : root)}\">Lyric</a>");
        sb.Append($"<span class=\"version\" id=\"version\">{Html.Text(site.Version)}</span>");
        sb.Append("</div>\n");
        sb.Append("<nav class=\"areas\" aria-label=\"Areas\">");
        foreach (var area in site.Areas)
            sb.Append(Anchor(site, path, area, IsWithin(path, area.Target) ? "current" : null));
        sb.Append("</nav>\n");
        sb.Append("<form class=\"search\" role=\"search\">");
        sb.Append("<input type=\"search\" name=\"q\" placeholder=\"Search\" aria-label=\"Search\" disabled=\"disabled\" />");
        sb.Append("</form>\n");
        sb.Append("<button class=\"theme-toggle\" type=\"button\" aria-label=\"Switch between light and dark\" hidden=\"hidden\">◐</button>\n");
        sb.Append($"<a class=\"github\" href=\"{Repository}\">GitHub</a>\n");
        sb.Append("</header>\n");
    }

    private static void Footer(StringBuilder sb, SiteContext site, string path)
    {
        sb.Append("<footer class=\"footer\">");
        sb.Append(Anchor(site, path, new Link("Releases", "releases/")));
        sb.Append($"<a href=\"{LegacyTarget}\">{Html.Text(LegacyLabel)}</a>");
        sb.Append($"<a href=\"{Repository}\">GitHub</a>");
        sb.Append("</footer>\n");
    }

    /// <summary>A link from the page at <paramref name="path"/> to a target under the version
    /// root, or the label alone where the version has no such page.</summary>
    public static string Anchor(SiteContext site, string path, Link link, string? cls = null)
    {
        var classAttr = cls is null ? "" : $" class=\"{Html.Attr(cls)}\"";
        if (!site.Has(link.Target))
            return $"<span class=\"pending{(cls is null ? "" : " " + Html.Attr(cls))}\">{Html.Text(link.Label)}</span>";
        return $"<a{classAttr} href=\"{Html.Attr(Href(path, link.Target))}\">{Html.Text(link.Label)}</a>";
    }

    /// <summary>The relative href from a page to a target under the version root. An anchor alone
    /// stays on the page.</summary>
    public static string Href(string path, string target)
    {
        if (target.StartsWith('#')) return target;
        var href = SiteContext.RootOf(path) + target;
        return href.Length == 0 ? "./" : href;
    }

    private static bool IsWithin(string path, string target) =>
        target.EndsWith('/') && path.StartsWith(target, StringComparison.Ordinal);
}
