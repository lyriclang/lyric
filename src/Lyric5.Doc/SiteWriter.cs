using System.Text;

namespace Lyric5.Doc;

/// <summary>A written page: its path under the version root and its HTML.</summary>
public sealed record Page(string Path, string Html);

/// <summary>Writes one version of the site: its pages and the theme's files into a directory.</summary>
public static class SiteWriter
{
    /// <summary>Writes the pages and the theme into <paramref name="versionRoot"/>, which is
    /// emptied first: a page that left the site does not stay behind.</summary>
    public static void Write(string versionRoot, IEnumerable<Page> pages)
    {
        if (Directory.Exists(versionRoot)) Directory.Delete(versionRoot, recursive: true);
        Directory.CreateDirectory(versionRoot);

        foreach (var (path, bytes) in Theme.Files())
            WriteFile(versionRoot, path, bytes);
        foreach (var page in pages)
            WriteFile(versionRoot, page.Path, new UTF8Encoding(false).GetBytes(page.Html));
    }

    private static void WriteFile(string root, string path, byte[] bytes)
    {
        var full = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, bytes);
    }
}
