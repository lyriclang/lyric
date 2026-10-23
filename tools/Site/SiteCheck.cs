using System.Text.RegularExpressions;
using Lyric5.Doc;

namespace Lyric5.Site;

/// <summary>What a built site is checked for before it is published.</summary>
/// <param name="Broken">Every relative link that reaches no written file, or no anchor there.</param>
/// <param name="External">Every stylesheet, script, font or import that would be fetched from another host.</param>
/// <param name="Checked">How many relative links were looked at.</param>
public sealed record SiteCheckResult(IReadOnlyList<string> Broken, IReadOnlyList<string> External, int Checked)
{
    public bool Passed => Broken.Count == 0 && External.Count == 0;
}

/// <summary>
/// The checks of a built site, read from its files as a browser would meet them: each relative
/// <c>href</c> and <c>src</c> of each page reaches a written file — a directory by its
/// <c>index.html</c> — and its anchor an <c>id</c> there; no page or stylesheet asks another host
/// for anything. Absolute paths (<c>/legacy/</c>) and links to other hosts are not followed.
/// </summary>
public static partial class SiteCheck
{
    [GeneratedRegex("(?:href|src)=\"(?<target>[^\"]*)\"")]
    private static partial Regex Target();

    [GeneratedRegex("\\sid=\"(?<id>[^\"]*)\"")]
    private static partial Regex Id();

    [GeneratedRegex("<link[^>]*\\shref=\"(?:https?:)?//|<script[^>]*\\ssrc=\"(?:https?:)?//", RegexOptions.IgnoreCase)]
    private static partial Regex ExternalInHtml();

    [GeneratedRegex("url\\(\\s*[\"']?(?:https?:)?//|@import", RegexOptions.IgnoreCase)]
    private static partial Regex ExternalInCss();

    /// <summary>Checks every file under <paramref name="output"/>.</summary>
    public static SiteCheckResult Run(string output)
    {
        var (broken, checkedLinks) = Links(output);
        return new SiteCheckResult(broken, External(output), checkedLinks);
    }

    /// <summary>Every relative link that leads nowhere, and how many links were looked at.</summary>
    public static (List<string> Broken, int Checked) Links(string output)
    {
        var broken = new List<string>();
        var checkedLinks = 0;
        var ids = new Dictionary<string, HashSet<string>>();
        foreach (var file in Directory.EnumerateFiles(output, "*.html", SearchOption.AllDirectories))
        {
            var html = File.ReadAllText(file);
            foreach (Match m in Target().Matches(html))
            {
                var target = m.Groups["target"].Value.Replace("&amp;", "&");
                if (target.Contains("://", StringComparison.Ordinal) || target.StartsWith('/')
                    || target.StartsWith("mailto:", StringComparison.Ordinal))
                    continue;
                checkedLinks++;
                var hash = target.IndexOf('#');
                var path = hash < 0 ? target : target[..hash];
                var anchor = hash < 0 ? "" : target[(hash + 1)..];
                var resolved = path.Length == 0
                    ? file
                    : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, SiteContext.FileOf(path)));
                var where = $"{Path.GetRelativePath(output, file)}: {target}";
                if (!File.Exists(resolved)) broken.Add(where);
                else if (anchor.Length > 0)
                {
                    if (!ids.TryGetValue(resolved, out var known))
                        ids[resolved] = known = Id().Matches(File.ReadAllText(resolved)).Select(id => id.Groups["id"].Value).ToHashSet();
                    if (!known.Contains(anchor)) broken.Add(where + " (no such anchor)");
                }
            }
        }
        return (broken, checkedLinks);
    }

    /// <summary>Every place a page or a stylesheet would fetch something from another host.</summary>
    public static List<string> External(string output)
    {
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(output, "*.*", SearchOption.AllDirectories))
        {
            var pattern = Path.GetExtension(file) switch
            {
                ".html" => ExternalInHtml(),
                ".css" => ExternalInCss(),
                _ => null,
            };
            if (pattern is null) continue;
            foreach (Match m in pattern.Matches(File.ReadAllText(file)))
                offenders.Add($"{Path.GetRelativePath(output, file)}: {m.Value}");
        }
        return offenders;
    }
}
