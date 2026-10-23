using System.Text;
using Lyric.Core;
using Lyric5.Doc;

namespace Lyric5.Site;

/// <summary>
/// The diagnostic catalog as pages: an index of every code by family, and one reference page per
/// code — what it means, why, a wrong and a right program, its lint where it is a warning. A
/// code's page has the path <see cref="DiagnosticCatalog.PagePath"/> gives, the one
/// <c>lyric explain</c> names.
/// </summary>
public static class CatalogPages
{
    public const string Directory = "diagnostics";
    public const string IndexPath = "diagnostics/index.html";

    /// <summary>The families of codes, by the letters after <c>LYR-</c>, in the index's order.</summary>
    public static readonly (string Prefix, string Name)[] Families =
    [
        ("LEX", "Lexing"),
        ("PAR", "Parsing"),
        ("RES", "Names"),
        ("SEM", "Checking"),
        ("CT", "Compile-time evaluation"),
        ("MAC", "Macros"),
        ("IR", "Lowering"),
        ("CG", "C generation"),
        ("PKG", "Packages"),
        ("BLD", "Building"),
        ("CLI", "The command line"),
        ("RT", "Run time"),
        ("ICE", "The toolchain itself"),
    ];

    /// <summary>The letters of a code's family: <c>SEM</c> in <c>LYR-SEM0001</c>.</summary>
    public static string FamilyOf(string code) => new(code["LYR-".Length..].TakeWhile(char.IsAsciiLetterUpper).ToArray());

    /// <summary>The paths of the pages <see cref="Build"/> writes.</summary>
    public static IEnumerable<string> Paths(IEnumerable<CatalogEntry> entries) =>
        entries.Select(e => SiteContext.FileOf(DiagnosticCatalog.PagePath(e.Code))).Prepend(IndexPath);

    /// <summary>The index and every code's page.</summary>
    public static IEnumerable<Page> Build(SiteContext site, IReadOnlyList<CatalogEntry> entries)
    {
        var unknown = entries.Select(e => FamilyOf(e.Code)).Distinct().Except(Families.Select(f => f.Prefix)).ToList();
        if (unknown.Count > 0)
            throw new InvalidDataException($"codes of no family on the site: {string.Join(", ", unknown)}");

        yield return new Page(IndexPath, Pages.Article(site, IndexPath, Index(entries)));
        foreach (var entry in entries)
        {
            var path = SiteContext.FileOf(DiagnosticCatalog.PagePath(entry.Code));
            yield return new Page(path, Pages.Reference(site, path, CodePage(site, path, entry, entries)));
        }
    }

    /// <summary>Every code, family by family: its link, its title, its severity.</summary>
    public static ArticleContent Index(IReadOnlyList<CatalogEntry> entries)
    {
        var sb = new StringBuilder();
        sb.Append("<p>Every code the toolchain reports. <code>lyric explain</code> prints a code's page "
                  + "in the terminal.</p>\n");
        foreach (var (prefix, name) in Families)
        {
            var codes = entries.Where(e => FamilyOf(e.Code) == prefix).ToList();
            if (codes.Count == 0) continue;
            sb.Append($"<h2 id=\"{prefix.ToLowerInvariant()}\">{Html.Text(name)} <span class=\"family\">{prefix}</span></h2>\n");
            sb.Append("<table class=\"codes\">\n<tbody>\n");
            foreach (var entry in codes)
            {
                var href = Layout.Href(IndexPath, DiagnosticCatalog.PagePath(entry.Code));
                sb.Append($"<tr><td><a href=\"{Html.Attr(href)}\"><code>{entry.Code}</code></a></td>"
                          + $"<td>{Html.Text(entry.Title)}</td><td>{Html.Text(Severity(entry))}</td></tr>\n");
            }
            sb.Append("</tbody>\n</table>\n");
        }
        var navigation = Families.Where(f => entries.Any(e => FamilyOf(e.Code) == f.Prefix))
            .Select(f => new Link(f.Name, "#" + f.Prefix.ToLowerInvariant()))
            .ToList();
        return new ArticleContent("Diagnostics", sb.ToString(), navigation, "Diagnostics");
    }

    /// <summary>A code's page: the title, severity, the spec's chapter and the lint; the summary
    /// and the explanation; the programs that show it.</summary>
    public static ReferenceContent CodePage(SiteContext site, string path, CatalogEntry entry, IReadOnlyList<CatalogEntry> entries)
    {
        var sb = new StringBuilder();
        var headings = new List<Heading>();
        sb.Append($"<p class=\"code-title\">{Html.Text(entry.Title)}</p>\n");
        sb.Append("<p class=\"meta\">");
        sb.Append(Html.Text(Severity(entry)));
        sb.Append(" · ");
        sb.Append(Layout.Anchor(site, path, new Link($"spec {entry.Area}", $"{SpecPages.Directory}/{entry.Area}/")));
        if (entry.Lint is { } lint)
            sb.Append($" · lint <code>{Html.Text(lint)}</code>, group <code>{Html.Text(entry.Group!)}</code>, "
                      + $"{entry.Level.ToString().ToLowerInvariant()} unless said");
        sb.Append("</p>\n");

        if (entry.Retired is { } retired)
            sb.Append("<div class=\"retired\"><p><strong>Retired.</strong></p>\n").Append(Markdown.Render(retired).Html).Append("</div>\n");
        sb.Append(Markdown.Render(entry.Summary).Html);

        void Section(string id, string title, string html)
        {
            headings.Add(new Heading(2, title, id));
            sb.Append($"<h2 id=\"{id}\">{Html.Text(title)}</h2>\n").Append(html);
        }
        if (entry.Explanation is { } explanation) Section("explanation", "Explanation", Markdown.Render(explanation).Html);
        if (entry.Wrong is { } wrong) Section("wrong", "Wrong", Lyric(wrong));
        if (entry.Right is { } right) Section("right", "Right", Lyric(right));
        if (entry.Program is { } program) Section("program", "Program", Lyric(program));
        if (entry.Example is { } example) Section("example", "Example", $"<pre><code>{Html.Text(example)}</code></pre>\n");

        var family = FamilyOf(entry.Code);
        IReadOnlyList<Link> navigation =
        [
            new Link("All codes", Directory + "/"),
            .. entries.Where(e => FamilyOf(e.Code) == family).Select(e => new Link(e.Code, DiagnosticCatalog.PagePath(e.Code))),
        ];
        return new ReferenceContent(entry.Code, sb.ToString(), navigation, family, headings);
    }

    private static string Lyric(string source) =>
        $"<pre><code class=\"language-lyric\">{Highlighter.Highlight(source)}</code></pre>\n";

    private static string Severity(CatalogEntry entry) => entry.Retired is null ? entry.Severity : entry.Severity + ", retired";
}
