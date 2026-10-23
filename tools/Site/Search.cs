using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Lyric5.Site;

/// <summary>The Pagefind version the site is built with and the sha256 of each release archive,
/// as <c>tooling/pagefind-version</c> lists them.</summary>
public sealed record PagefindPin(string Version, IReadOnlyDictionary<string, string> Sums)
{
    public const string File = "tooling/pagefind-version";

    /// <summary>Reads the pin: <c>name = value</c> lines, <c>#</c> comments, blank lines.</summary>
    public static PagefindPin Read(string text)
    {
        string? version = null;
        var sums = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var eq = line.IndexOf(" = ", StringComparison.Ordinal);
            if (eq < 0) throw new FormatException($"{File}: cannot read '{line}'");
            var (name, value) = (line[..eq], line[(eq + 3)..]);
            if (name == "version") version = value;
            else sums[name] = value;
        }
        return new PagefindPin(version ?? throw new FormatException($"{File}: no version"), sums);
    }

    public static PagefindPin Of(string root) => Read(System.IO.File.ReadAllText(Path.Combine(root, File)));
}

/// <summary>The site's search: Pagefind run over a written version, its index under
/// <c>pagefind/</c> beside the pages.</summary>
public static class Search
{
    /// <summary>The directory of a version the index is written to, and what the page's script
    /// loads from it.</summary>
    public const string IndexDirectory = "pagefind";

    /// <summary>The Pagefind binary: the one given, else <c>LYRIC_PAGEFIND</c>, else the pinned
    /// version under the user's cache (<c>~/.cache/lyric/pagefind/&lt;version&gt;/</c>);
    /// <c>null</c> when none of them is a file.</summary>
    public static string? Locate(string root, string? given)
    {
        if (given is not null) return System.IO.File.Exists(given) ? given : null;
        if (Environment.GetEnvironmentVariable("LYRIC_PAGEFIND") is { Length: > 0 } fromEnv)
            return System.IO.File.Exists(fromEnv) ? fromEnv : null;
        var cache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME") is { Length: > 0 } xdg
            ? xdg
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache");
        var name = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "pagefind.exe" : "pagefind";
        var cached = Path.Combine(cache, "lyric", "pagefind", PagefindPin.Of(root).Version, name);
        return System.IO.File.Exists(cached) ? cached : null;
    }

    /// <summary>Indexes the version written at <paramref name="versionRoot"/>; what Pagefind
    /// prints goes to the caller's error stream only when it fails.</summary>
    public static void Index(string binary, string versionRoot)
    {
        var start = new ProcessStartInfo(binary)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in new[] { "--site", versionRoot, "--output-subdir", IndexDirectory, "--quiet" })
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"cannot start {binary}");
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"pagefind failed with exit code {process.ExitCode}:\n{output.Result}{errors.Result}");
    }
}
