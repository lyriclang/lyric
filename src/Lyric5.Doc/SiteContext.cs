namespace Lyric5.Doc;

/// <summary>A labelled target under the version root: <c>book/</c>, <c>releases/</c>, or a
/// page's anchor <c>#v5-0-0</c>.</summary>
public sealed record Link(string Label, string Target);

/// <summary>
/// What every page of one version shares: the version's name, the areas of the header and the
/// pages the version holds. A link to a page the version does not hold is written as its label
/// alone, so a built site has no link that leads nowhere — an area appears as a link once it has
/// a page.
/// </summary>
public sealed class SiteContext
{
    private readonly HashSet<string> _pages;

    /// <param name="version">The version's name as the switcher shows it: <c>dev</c>, <c>5.0</c>.</param>
    /// <param name="pages">The paths of the version's pages under its root, <c>index.html</c>,
    /// <c>releases/index.html</c>.</param>
    /// <param name="areas">The header's areas, in order.</param>
    public SiteContext(string version, IEnumerable<string> pages, IReadOnlyList<Link> areas)
    {
        Version = version;
        _pages = new HashSet<string>(pages, StringComparer.Ordinal);
        Areas = areas;
    }

    public string Version { get; }
    public IReadOnlyList<Link> Areas { get; }

    /// <summary>The header's areas of the site: Book, std, Spec, Diagnostics, Download.</summary>
    public static IReadOnlyList<Link> DefaultAreas { get; } =
    [
        new("Book", "book/"),
        new("std", "std/"),
        new("Spec", "spec/"),
        new("Diagnostics", "diagnostics/"),
        new("Download", "download/"),
    ];

    /// <summary>Whether the version holds the page a target names. An anchor alone is on the
    /// page itself; a target ending in <c>/</c> names that directory's <c>index.html</c>.</summary>
    public bool Has(string target)
    {
        var hash = target.IndexOf('#');
        var path = hash < 0 ? target : target[..hash];
        if (path.Length == 0 && hash >= 0) return true;
        return _pages.Contains(FileOf(path));
    }

    /// <summary>The file a target names: <c>book/</c> is <c>book/index.html</c>, the root is
    /// <c>index.html</c>.</summary>
    public static string FileOf(string path) =>
        path.Length == 0 || path.EndsWith('/') ? path + "index.html" : path;

    /// <summary>The way up from a page to the version root: <c>""</c> at the root,
    /// <c>"../"</c> one directory down.</summary>
    public static string RootOf(string pagePath) =>
        string.Concat(Enumerable.Repeat("../", pagePath.Count(c => c == '/')));
}
