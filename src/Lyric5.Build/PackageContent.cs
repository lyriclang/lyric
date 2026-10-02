using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Lyric5.Build;

/// <summary>
/// What a package is made of (design/v5/spec/11 W2 P8): by default its manifest, <c>src/</c>,
/// <c>native/</c>, <c>build.lyr</c> and the files <c>README*</c> and <c>LICENSE*</c> at its root —
/// not <c>tests/</c>, not <c>out/</c>. <c>include</c> names the files instead of that default,
/// <c>exclude</c> takes files out; the manifest always belongs. A package read from git is this
/// content and nothing else.
/// </summary>
public static class PackageContent
{
    private static readonly string[] Default = ["src/", "native/", "build.lyr", "README*", "LICENSE*"];

    private static readonly ConcurrentDictionary<string, Regex> Compiled = new(StringComparer.Ordinal);

    /// <summary>Whether <paramref name="path"/> — relative to the package's root, '/' between
    /// directories — belongs to the package.</summary>
    public static bool Holds(Manifest manifest, string path) =>
        path == "lyric.toml"
        || (Matches(manifest.Include ?? Default, path) && !Matches(manifest.Exclude, path));

    /// <summary>Whether one of <paramref name="patterns"/> holds <paramref name="path"/>.</summary>
    public static bool Matches(IEnumerable<string> patterns, string path) => patterns.Any(p => Matches(p, path));

    /// <summary>Whether <paramref name="pattern"/> holds <paramref name="path"/>: it matches the path,
    /// or a directory the path lies in — a pattern naming a directory holds what is below it.</summary>
    public static bool Matches(string pattern, string path)
    {
        var regex = Compiled.GetOrAdd(pattern, Glob);
        for (var end = path.Length; end > 0; end = path.LastIndexOf('/', end - 1))
        {
            if (regex.IsMatch(path.AsSpan(0, end))) return true;
        }
        return false;
    }

    /// <summary>The hash of the files under <paramref name="root"/> (07 P5), as Go hashes a module
    /// ("h1"): SHA-256 over one line per file — the file's SHA-256 in hex, two spaces, its path
    /// with '/' between directories — sorted by path, written <c>h1:</c> and base64.</summary>
    public static string Hash(string root)
    {
        var lines = new StringBuilder();
        foreach (var (path, full) in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                     .Select(full => (Path.GetRelativePath(root, full).Replace('\\', '/'), full))
                     .OrderBy(file => file.Item1, StringComparer.Ordinal))
            lines.Append(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(full)))).Append("  ").Append(path).Append('\n');
        return "h1:" + Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(lines.ToString())));
    }

    /// <summary>A pattern a manifest may write: relative to the package's root, '/' between
    /// directories, none of them empty, '.' or '..'.</summary>
    public static bool IsPattern(string pattern) =>
        pattern.Length > 0 && !pattern.StartsWith('/') && !pattern.Contains('\\')
        && pattern.TrimEnd('/').Split('/').All(segment => segment.Length > 0 && segment is not ("." or ".."));

    /// <summary>A pattern as a regular expression over a whole path: <c>*</c> any characters within
    /// a directory, <c>?</c> one, <c>**</c> any number of directories.</summary>
    private static Regex Glob(string pattern)
    {
        var text = new StringBuilder("^");
        var segments = pattern.TrimEnd('/').Split('/');
        for (var i = 0; i < segments.Length; i++)
        {
            var last = i == segments.Length - 1;
            if (segments[i] == "**")
            {
                text.Append(last ? ".*" : "(?:[^/]+/)*");
                continue;
            }
            foreach (var c in segments[i])
                text.Append(c switch { '*' => "[^/]*", '?' => "[^/]", _ => Regex.Escape(c.ToString()) });
            if (!last) text.Append('/');
        }
        return new Regex(text.Append('$').ToString(), RegexOptions.CultureInvariant);
    }
}
