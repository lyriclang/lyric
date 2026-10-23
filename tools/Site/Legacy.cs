using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lyric5.Doc;

namespace Lyric5.Site;

/// <summary>One step of the legacy move, with the files and bytes it touches; a page it writes
/// carries its text.</summary>
public sealed record LegacyStep(string Kind, string From, string? To, int Files, long Bytes, string? Text = null)
{
    public override string ToString() => Kind switch
    {
        "move" => $"move   {From}/ -> {To}/ ({Files} files, {Bytes} bytes)",
        "move-file" => $"move   {From} -> {To} ({Bytes} bytes)",
        "remove" => $"remove {From}/ ({Files} files, {Bytes} bytes)",
        _ => $"write  {From} ({Bytes} bytes)",
    };
}

/// <summary>
/// The move of the 1.x–4.x documentation under <c>legacy/</c> on a work tree of the published site:
/// every <c>vX.Y.Z/</c> directory and the old version list go there unchanged — their links are
/// relative, and the old switch finds its list as <c>../versions.json</c> from a version's root,
/// which is <c>legacy/versions.json</c> after the move —; <c>legacy/index.html</c> lists them;
/// <c>nightly/</c>, a cut that was never released, goes, and with it its entry in the old list; a
/// root <c>404.html</c> sends an old
/// address <c>/vX.Y.Z/…</c> to <c>/legacy/vX.Y.Z/…</c>, which also carries the old pages' few
/// root-absolute links. Nothing else in the tree is touched, and a second run finds nothing to do.
/// </summary>
public static partial class Legacy
{
    public const string Directory = "legacy";
    public const string NotFoundPage = "404.html";
    public const string Nightly = "nightly";

    /// <summary>A path of the old site, its version and the rest: <c>/v4.5.0/guide/</c>.</summary>
    public const string OldPathPattern = @"^/(v\d+\.\d+\.\d+)(/.*)?$";

    /// <summary>An old version's directory name.</summary>
    [GeneratedRegex(@"^v\d+\.\d+\.\d+$")]
    public static partial Regex VersionDirectory();

    [GeneratedRegex(OldPathPattern)]
    private static partial Regex OldPath();

    [GeneratedRegex(@"^/nightly(/|$)")]
    private static partial Regex NightlyPath();

    /// <summary>Where the not-found page sends <paramref name="path"/>: an old version's page to
    /// the same page under <c>/legacy/</c>, a page of the removed nightly cut to the legacy
    /// overview; <c>null</c> for any other path, which is not found.</summary>
    public static string? RedirectOf(string path)
    {
        if (OldPath().Match(path) is { Success: true } m)
            return "/" + Directory + "/" + m.Groups[1].Value + (m.Groups[2].Success ? m.Groups[2].Value : "/");
        return NightlyPath().IsMatch(path) ? "/" + Directory + "/" : null;
    }

    /// <summary>The root's not-found page: the redirect of <see cref="RedirectOf"/> as a script
    /// with the same patterns, and links for a reader without scripts. It asks no other host.</summary>
    public static string NotFoundHtml()
    {
        // As a JavaScript string; the pattern holds nothing a script element would end at.
        var oldPath = JsonSerializer.Serialize(OldPathPattern,
            new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        return "<!doctype html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\" />\n"
            + "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />\n"
            + "<title>Not found — Lyric</title>\n"
            + "<script>\n(function () {\n"
            + "  var path = location.pathname, to = null;\n"
            + $"  var old = new RegExp({oldPath}).exec(path);\n"
            + "  if (old) to = \"/legacy/\" + old[1] + (old[2] || \"/\");\n"
            + "  else if (/^\\/nightly(\\/|$)/.test(path)) to = \"/legacy/\";\n"
            + "  if (to) location.replace(to + location.search + location.hash);\n"
            + "})();\n</script>\n"
            + "<style>body { font: 15px/1.5 system-ui, sans-serif; max-width: 40em; margin: 3em auto; padding: 0 16px; }</style>\n"
            + "</head>\n<body>\n<h1>Not found</h1>\n"
            + "<p>There is no page at this address. The documentation of Lyric 1.0.0–4.5.0 is under "
            + "<a href=\"/legacy/\">/legacy/</a>; the current site starts at <a href=\"/\">/</a>.</p>\n"
            + "</body>\n</html>\n";
    }

    /// <summary>The legacy overview: the old versions, newest first, each its directory.</summary>
    public static string OverviewHtml(IEnumerable<string> versions)
    {
        var sb = new StringBuilder();
        sb.Append("<!doctype html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\" />\n")
          .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />\n")
          .Append($"<title>{Html.Text(Layout.LegacyLabel)}</title>\n")
          .Append("<style>body { font: 15px/1.5 system-ui, sans-serif; max-width: 40em; margin: 3em auto; padding: 0 16px; } ")
          .Append("ul { columns: 4 8em; padding: 0; list-style: none; }</style>\n")
          .Append("</head>\n<body>\n")
          .Append($"<h1>{Html.Text(Layout.LegacyLabel)}</h1>\n")
          .Append("<p>The documentation of the 1.x–4.x line of Lyric, as each release published it. "
              + "The language that line built lives on as Lyric-Script; Lyric 5 is at <a href=\"../\">the current site</a>.</p>\n")
          .Append("<ul>\n");
        foreach (var v in versions.OrderByDescending(VersionKey))
            sb.Append($"<li><a href=\"{Html.Attr(v)}/\">{Html.Text(v)}</a></li>\n");
        return sb.Append("</ul>\n</body>\n</html>\n").ToString();
    }

    private static (int, int, int) VersionKey(string v)
    {
        var parts = v[1..].Split('.');
        return (int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]));
    }

    /// <summary>What the move does in <paramref name="pages"/>, a work tree of the published site,
    /// step by step; empty when it is done.</summary>
    /// <exception cref="InvalidOperationException">The directory is no such work tree — it holds
    /// the source repository, a branch other than <c>gh-pages</c> is checked out, or it has no
    /// sign of the site —, or a version is both at the root and under <c>legacy/</c>.</exception>
    public static IReadOnlyList<LegacyStep> Plan(string pages)
    {
        Refuse(pages);
        var steps = new List<LegacyStep>();
        var legacy = Path.Combine(pages, Directory);

        var versions = System.IO.Directory.EnumerateDirectories(pages)
            .Select(Path.GetFileName).Where(n => VersionDirectory().IsMatch(n!)).Select(n => n!)
            .Order(StringComparer.Ordinal).ToList();
        foreach (var v in versions)
        {
            if (System.IO.Directory.Exists(Path.Combine(legacy, v)))
                throw new InvalidOperationException($"{v} is both at the root and under {Directory}/ in {pages}");
            var (files, bytes) = Size(Path.Combine(pages, v));
            steps.Add(new LegacyStep("move", v, $"{Directory}/{v}", files, bytes));
        }

        var list = Path.Combine(pages, Versions.ListFile);
        if (File.Exists(list) && IsOldList(list))
        {
            if (File.Exists(Path.Combine(legacy, Versions.ListFile)))
                throw new InvalidOperationException($"the old version list is both at the root and under {Directory}/ in {pages}");
            steps.Add(new LegacyStep("move-file", Versions.ListFile, $"{Directory}/{Versions.ListFile}", 1, new FileInfo(list).Length));
        }

        var nightly = Path.Combine(pages, Nightly);
        if (System.IO.Directory.Exists(nightly))
        {
            var (files, bytes) = Size(nightly);
            steps.Add(new LegacyStep("remove", Nightly, null, files, bytes));
        }

        // The old switch would still offer the removed nightly; its list loses that entry.
        var moved = Path.Combine(legacy, Versions.ListFile);
        var oldList = steps.Any(s => s.Kind == "move-file") ? list : File.Exists(moved) ? moved : null;
        if (oldList is not null && WithoutNightly(File.ReadAllText(oldList)) is { } kept)
            AddWrite(steps, pages, $"{Directory}/{Versions.ListFile}", kept);

        var after = versions.Concat(System.IO.Directory.Exists(legacy)
            ? System.IO.Directory.EnumerateDirectories(legacy).Select(Path.GetFileName).Where(n => VersionDirectory().IsMatch(n!)).Select(n => n!)
            : []).Distinct().ToList();
        if (after.Count > 0) AddWrite(steps, pages, $"{Directory}/index.html", OverviewHtml(after));
        AddWrite(steps, pages, NotFoundPage, NotFoundHtml());
        return steps;
    }

    /// <summary>Does what <see cref="Plan"/> says; the steps it took.</summary>
    public static IReadOnlyList<LegacyStep> Apply(string pages)
    {
        var steps = Plan(pages);
        foreach (var step in steps)
        {
            var from = Path.Combine(pages, step.From);
            switch (step.Kind)
            {
                case "move-file":
                    System.IO.Directory.CreateDirectory(Path.Combine(pages, Directory));
                    File.Move(from, Path.Combine(pages, step.To!));
                    break;
                case "move":
                    System.IO.Directory.CreateDirectory(Path.Combine(pages, Directory));
                    System.IO.Directory.Move(from, Path.Combine(pages, step.To!));
                    break;
                case "remove":
                    System.IO.Directory.Delete(from, recursive: true);
                    break;
                default:
                    System.IO.Directory.CreateDirectory(Path.GetDirectoryName(from)!);
                    File.WriteAllText(from, step.Text, new UTF8Encoding(false));
                    break;
            }
        }
        return steps;
    }

    /// <summary>The old version list without its <c>nightly</c> entry, written as the old site
    /// wrote it (two spaces); <c>null</c> when it has none.</summary>
    public static string? WithoutNightly(string json)
    {
        var entries = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsArray();
        var nightly = entries.Where(e => e?["version"]?.GetValue<string>() == Nightly).ToList();
        if (nightly.Count == 0) return null;
        foreach (var e in nightly) entries.Remove(e);
        var text = entries.ToJsonString(new JsonSerializerOptions { WriteIndented = true, IndentSize = 2 });
        return json.EndsWith('\n') ? text + "\n" : text;
    }

    private static void AddWrite(List<LegacyStep> steps, string pages, string path, string html)
    {
        var file = Path.Combine(pages, path);
        if (File.Exists(file) && File.ReadAllText(file) == html) return;
        steps.Add(new LegacyStep("write", path, null, 1, Encoding.UTF8.GetByteCount(html), html));
    }

    private static bool IsOldList(string file)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(file));
        return json.RootElement.ValueKind == JsonValueKind.Array
            && json.RootElement.EnumerateArray().All(e => e.ValueKind == JsonValueKind.Object && e.TryGetProperty("version", out _));
    }

    private static (int Files, long Bytes) Size(string dir)
    {
        var files = new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories).ToList();
        return (files.Count, files.Sum(f => f.Length));
    }

    private static void Refuse(string pages)
    {
        if (!System.IO.Directory.Exists(pages))
            throw new InvalidOperationException($"no directory {pages}");
        if (File.Exists(Path.Combine(pages, "Lyric.slnx")) || System.IO.Directory.Exists(Path.Combine(pages, "stdlib5")))
            throw new InvalidOperationException($"{pages} is the source repository, not a work tree of the published site");
        if (CheckedOutBranch(pages) is { } branch && branch != "gh-pages")
            throw new InvalidOperationException($"{pages} has the branch '{branch}' checked out; the published site is gh-pages");
        var looksLikeSite = File.Exists(Path.Combine(pages, "CNAME"))
            || System.IO.Directory.Exists(Path.Combine(pages, Directory))
            || System.IO.Directory.EnumerateDirectories(pages).Any(d => VersionDirectory().IsMatch(Path.GetFileName(d)));
        if (!looksLikeSite)
            throw new InvalidOperationException($"{pages} has neither a CNAME, nor {Directory}/, nor a version directory: not the published site");
    }

    /// <summary>The branch a git work tree at <paramref name="dir"/> has checked out; <c>null</c>
    /// for no work tree or a detached head.</summary>
    private static string? CheckedOutBranch(string dir)
    {
        var git = Path.Combine(dir, ".git");
        string? gitDir = null;
        if (System.IO.Directory.Exists(git)) gitDir = git;
        else if (File.Exists(git) && File.ReadAllText(git).Trim() is var link && link.StartsWith("gitdir:", StringComparison.Ordinal))
        {
            gitDir = link["gitdir:".Length..].Trim();
            if (!Path.IsPathRooted(gitDir)) gitDir = Path.Combine(dir, gitDir);
        }
        if (gitDir is null || !File.Exists(Path.Combine(gitDir, "HEAD"))) return null;
        var head = File.ReadAllText(Path.Combine(gitDir, "HEAD")).Trim();
        const string prefix = "ref: refs/heads/";
        return head.StartsWith(prefix, StringComparison.Ordinal) ? head[prefix.Length..] : null;
    }
}
