using Lyric.Core;

namespace Lyric.Ir.Interp;

/// <summary>
/// What <c>embed</c> reads: a file named relative to the module
/// that calls, within its package's directory — the one thing code run at compile time reads from
/// outside the program. Every file read is noted, absolute, for the build, which depends on it as
/// on a source.
/// </summary>
public sealed class EmbedReader(Func<FileId, string> pathOf, IReadOnlyCollection<string> packageDirectories)
{
    private readonly List<string> _read = [];

    /// <summary>The files read, in the order first read, each once.</summary>
    public IReadOnlyList<string> Read => _read;

    /// <summary>The bytes of <paramref name="path"/>, from the module of <paramref name="at"/>.</summary>
    public byte[] Bytes(Span at, string path)
    {
        var module = Path.GetFullPath(pathOf(at.File));
        var here = Path.GetDirectoryName(module)!;
        // the package the module lies in — the deepest, a dependency nested in its user's tree
        // being its own —; a module of no package (a single file, std) is held to its directory
        var package = packageDirectories.Where(dir => Within(module, dir)).MaxBy(dir => dir.Length) ?? here;
        var full = Path.GetFullPath(Path.Combine(here, path));
        if (!Within(full, package))
            throw ComptimeFailure.Embed($"'embed' reads within the package, and '{path}' lies outside it ({package})");
        if (!File.Exists(full))
            throw ComptimeFailure.Embed($"'embed' found no file '{path}' ({full})");
        if (!_read.Contains(full)) _read.Add(full);
        return File.ReadAllBytes(full);
    }

    private static bool Within(string path, string directory)
    {
        var dir = Path.TrimEndingDirectorySeparator(directory) + Path.DirectorySeparatorChar;
        return path.StartsWith(dir, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
}
