using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lyric5.Doc;

namespace Lyric5.Site;

/// <summary>One entry of the version switch: the name it shows and its directory under the site
/// root, with a trailing slash.</summary>
public sealed record SiteVersionEntry(string Name, string Path);

/// <summary>
/// The site's list of versions, <c>versions.json</c> at its root, and the root page that leads to
/// the version a visitor should read. The list holds the stable versions, newest first, then
/// <c>dev</c>, then the legacy site of the 1.x–4.x line, always last. The root page leads to the
/// newest stable version, or to <c>dev</c> while there is none.
/// </summary>
public static partial class Versions
{
    public const string ListFile = "versions.json";
    public const string RootPage = "index.html";
    public const string Dev = "dev";

    /// <summary>The legacy site's entry: the old versions' directories under <c>legacy/</c>.</summary>
    public static readonly SiteVersionEntry Legacy = new(Layout.LegacyLabel, "legacy/");

    [GeneratedRegex(@"^\d+\.\d+$")]
    private static partial Regex StableName();

    /// <summary>Whether <paramref name="name"/> is a version the site is built as: <c>dev</c> or
    /// a stable <c>major.minor</c>.</summary>
    public static bool IsVersionName(string name) => name == Dev || StableName().IsMatch(name);

    /// <summary>The list with <paramref name="version"/> in it, in the list's order; an entry
    /// already there stays as it is, so adding a version twice changes nothing.</summary>
    public static IReadOnlyList<SiteVersionEntry> With(IEnumerable<SiteVersionEntry> list, string version)
    {
        if (!IsVersionName(version))
            throw new ArgumentException($"'{version}' is no version of the site: dev or major.minor", nameof(version));
        var entries = list.Where(e => e.Path != Legacy.Path).ToList();
        if (entries.All(e => e.Name != version)) entries.Add(new SiteVersionEntry(version, version + "/"));

        var stable = entries.Where(e => StableName().IsMatch(e.Name)).OrderByDescending(e => Parse(e.Name)).ToList();
        var others = entries.Where(e => !StableName().IsMatch(e.Name)).OrderBy(e => e.Name == Dev ? 0 : 1).ThenBy(e => e.Name, StringComparer.Ordinal);
        return [.. stable, .. others, Legacy];
    }

    private static (int, int) Parse(string name)
    {
        var dot = name.IndexOf('.');
        return (int.Parse(name[..dot]), int.Parse(name[(dot + 1)..]));
    }

    /// <summary>The list in <paramref name="siteRoot"/>, empty where there is none.</summary>
    /// <exception cref="InvalidOperationException">The file is in another form — the old site's
    /// <c>{ "version", "stable" }</c> list, which the legacy move takes to <c>legacy/</c> and which
    /// must not be written over before it.</exception>
    public static IReadOnlyList<SiteVersionEntry> Read(string siteRoot)
    {
        var file = Path.Combine(siteRoot, ListFile);
        if (!File.Exists(file)) return [];
        var refused = new InvalidOperationException(
            $"{file} is not the site's version list; if it is the old site's, move the legacy site first (site legacy)");
        using var json = JsonDocument.Parse(File.ReadAllText(file));
        if (json.RootElement.ValueKind != JsonValueKind.Array) throw refused;
        var entries = new List<SiteVersionEntry>();
        foreach (var e in json.RootElement.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.Object
                || !e.TryGetProperty("name", out var name) || !e.TryGetProperty("path", out var path)
                || name.ValueKind != JsonValueKind.String || path.ValueKind != JsonValueKind.String)
                throw refused;
            entries.Add(new SiteVersionEntry(name.GetString()!, path.GetString()!));
        }
        return entries;
    }

    /// <summary>The list as the switch reads it.</summary>
    public static string Json(IReadOnlyList<SiteVersionEntry> list)
    {
        var sb = new StringBuilder("[\n");
        for (var i = 0; i < list.Count; i++)
        {
            sb.Append("  { \"name\": ").Append(JsonSerializer.Serialize(list[i].Name, JsonOptions))
              .Append(", \"path\": ").Append(JsonSerializer.Serialize(list[i].Path, JsonOptions)).Append(" }");
            sb.Append(i + 1 < list.Count ? ",\n" : "\n");
        }
        return sb.Append("]\n").ToString();
    }

    private static readonly JsonSerializerOptions JsonOptions =
        new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>The version the root page leads to: the newest stable one, or <c>dev</c>.</summary>
    public static SiteVersionEntry Landing(IReadOnlyList<SiteVersionEntry> list) =>
        list.FirstOrDefault(e => StableName().IsMatch(e.Name))
        ?? list.FirstOrDefault(e => e.Name == Dev)
        ?? throw new InvalidOperationException("the version list has neither a stable version nor dev");

    /// <summary>The root page: a redirect to <paramref name="landing"/>, with a link for a browser
    /// that does not follow it.</summary>
    public static string RootHtml(SiteVersionEntry landing)
    {
        var target = Html.Attr(landing.Path);
        return "<!doctype html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\" />\n"
            + $"<meta http-equiv=\"refresh\" content=\"0; url={target}\" />\n"
            + $"<link rel=\"canonical\" href=\"{target}\" />\n<title>Lyric</title>\n</head>\n<body>\n"
            + $"<p><a href=\"{target}\">Lyric {Html.Text(landing.Name)}</a></p>\n</body>\n</html>\n";
    }

    /// <summary>Adds <paramref name="version"/> to the list in <paramref name="siteRoot"/> and
    /// writes the list and the root page; the list it wrote.</summary>
    public static IReadOnlyList<SiteVersionEntry> Update(string siteRoot, string version)
    {
        var list = With(Read(siteRoot), version);
        Directory.CreateDirectory(siteRoot);
        File.WriteAllText(Path.Combine(siteRoot, ListFile), Json(list), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(siteRoot, RootPage), RootHtml(Landing(list)), new UTF8Encoding(false));
        return list;
    }
}
