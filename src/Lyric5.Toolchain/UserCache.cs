using System.Security.Cryptography;
using System.Text;

namespace Lyric5.Toolchain;

/// <summary>
/// The user's cache for what every project shares (design/v5/spec/11 T3, C9): the runtime
/// archives per toolchain version, and small memos such as a C compiler's version, so a warm
/// build spends no process start on questions whose answers do not change.
/// <c>~/.cache/lyric</c> (XDG on Linux, local application data on Windows), <c>LYRIC_CACHE</c>
/// overrides.
/// </summary>
public static class UserCache
{
    public static string Root
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable("LYRIC_CACHE");
            if (!string.IsNullOrWhiteSpace(configured)) return configured;
            if (OperatingSystem.IsWindows())
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "lyric", "cache");
            var xdg = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
            var cache = !string.IsNullOrWhiteSpace(xdg) ? xdg : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache");
            return Path.Combine(cache, "lyric");
        }
    }

    /// <summary>
    /// A memo under a key: the text kept, or computed once and kept. The key names what the
    /// answer depends on (a file's path, size and time, say); a different key is a different memo.
    /// </summary>
    public static string Memo(string kind, string key, Func<string> compute)
    {
        var dir = Path.Combine(Root, "memo", kind);
        var file = Path.Combine(dir, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..24]);
        try
        {
            if (File.Exists(file)) return File.ReadAllText(file);
        }
        catch (IOException) { }
        var value = compute();
        try
        {
            Directory.CreateDirectory(dir);
            var partial = $"{file}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
            File.WriteAllText(partial, value);
            try { File.Move(partial, file, overwrite: false); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { File.Delete(partial); }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return value;
    }

    /// <summary>A file's identity for a memo key: path, size and last write.</summary>
    public static string FileStamp(string path)
    {
        var info = new FileInfo(path);
        return $"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
    }
}
