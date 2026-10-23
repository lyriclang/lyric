namespace Lyric5.Doc;

/// <summary>
/// The files every version of the site carries at its root: the stylesheet, the four scripts and
/// the fonts. They are embedded in this assembly; each is named by its path under the version root.
/// </summary>
public static class Theme
{
    public const string Stylesheet = "site.css";
    public const string ThemeScript = "theme.js";
    public const string NavScript = "nav.js";
    public const string VersionScript = "version.js";
    public const string SearchScript = "search.js";

    /// <summary>Every theme file as its path under the version root and its bytes. The fonts are
    /// those the assembly holds under <c>fonts/</c>; with none, the stylesheet falls back to the
    /// system's.</summary>
    public static IEnumerable<(string Path, byte[] Bytes)> Files()
    {
        var assembly = typeof(Theme).Assembly;
        foreach (var name in assembly.GetManifestResourceNames().Order(StringComparer.Ordinal))
        {
            var path = name.Replace('\\', '/');
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            yield return (path, copy.ToArray());
        }
    }

    /// <summary>One theme file's text, for a test or a page that inlines it.</summary>
    public static string Text(string path)
    {
        foreach (var (name, bytes) in Files())
            if (name == path) return System.Text.Encoding.UTF8.GetString(bytes);
        throw new FileNotFoundException($"no theme file '{path}'");
    }
}
