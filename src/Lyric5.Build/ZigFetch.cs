using System.Formats.Tar;
using System.IO.Compression;
using System.Text.Json;
using Lyric5.Toolchain;

namespace Lyric5.Build;

/// <summary>
/// <c>lyric toolchain install zig</c>: the zig this toolchain pins, fetched the way zig asks a tool
/// to — the archive's name, size and SHA-256 from its index, the archive from the community
/// mirrors in a shuffled order and from ziglang.org last, each with <c>?source=lyric</c> —, checked
/// with zig's minisign key and with the file its signature's trusted comment names, unpacked beside
/// the toolchains and moved to <c>zig/&lt;version&gt;/</c> only then. A mirror whose archive does
/// not hold is passed over for the next, as zig has it: the mirrors are anyone's, the signature is
/// zig's. <c>LYRIC_ZIG_MIRROR</c> names a server in place of ziglang.org's download page, with its
/// index, its list of mirrors and the archives, and <c>LYRIC_ZIG_MIRROR_KEY</c> the key it signs with.
/// </summary>
public sealed class ZigFetch
{
    public const string Download = "https://ziglang.org/download";

    /// <summary>The Zig Software Foundation's key, as its download page gives it.</summary>
    public const string PublicKey = "RWSGOq2NVecA2UPNdBUZykf1CCb147pkmdtYxgb3Ti+JO/wCYvhbAb/U";

    private readonly string _base;
    private readonly MinisignKey _key;
    private readonly HttpClient _http;
    private readonly TextWriter? _progress;
    private readonly Random _shuffle;

    public ZigFetch(string address, MinisignKey key, string userAgent, TextWriter? progress, Random? shuffle = null)
    {
        _base = address.TrimEnd('/');
        _key = key;
        _progress = progress;
        _shuffle = shuffle ?? Random.Shared;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
    }

    /// <summary>The download page and the key as the environment sets them.</summary>
    /// <exception cref="ToolchainException"><c>LYR-CLI0023</c>: <c>LYRIC_ZIG_MIRROR_KEY</c> is no key, or is set without a mirror.</exception>
    public static ZigFetch ForUser(string version, TextWriter? progress)
    {
        var mirror = Environment.GetEnvironmentVariable("LYRIC_ZIG_MIRROR");
        var named = Environment.GetEnvironmentVariable("LYRIC_ZIG_MIRROR_KEY");
        if (!string.IsNullOrWhiteSpace(named) && string.IsNullOrWhiteSpace(mirror))
            throw new ToolchainException("LYR-CLI0023", "LYRIC_ZIG_MIRROR_KEY is set and LYRIC_ZIG_MIRROR is not", "the key is the one a mirror of your own signs with; name the mirror too");
        MinisignKey key;
        try { key = MinisignKey.Parse(string.IsNullOrWhiteSpace(named) ? PublicKey : named); }
        catch (SignatureException e) { throw new ToolchainException("LYR-CLI0023", $"LYRIC_ZIG_MIRROR_KEY: {e.Message}"); }
        return new ZigFetch(string.IsNullOrWhiteSpace(mirror) ? Download : mirror, key, $"lyric/{version}", progress);
    }

    /// <summary>zig's name for a target in its index: <c>x86_64-linux</c>, <c>aarch64-macos</c>.</summary>
    public static string HostKey(Target target) => $"{target.Arch}-{target.Os switch
    {
        TargetOs.Windows => "windows",
        TargetOs.MacOs => "macos",
        _ => "linux",
    }}";

    /// <summary>Installs zig <paramref name="version"/> for this machine into <paramref name="home"/>;
    /// <c>false</c> where it is there already.</summary>
    /// <exception cref="ToolchainException"><c>LYR-CLI0023</c> where it cannot be had,
    /// <c>LYR-CLI0024</c> where no source serves an archive that holds.</exception>
    public bool Install(ToolchainHome home, string version)
    {
        if (home.IsZigInstalled(version)) return false;
        var archive = Archive(version, HostKey(Target.Host));
        var partial = home.Partial();
        try
        {
            var file = Path.Combine(partial, archive.File);
            Fetch(version, archive, file);
            var tree = Path.Combine(partial, "zig");
            Unpack(file, tree);
            var top = Top(tree, archive.File);
            Directory.CreateDirectory(Path.GetDirectoryName(home.ZigDirectoryOf(version))!);
            try { Directory.Move(top, home.ZigDirectoryOf(version)); }
            catch (IOException) when (home.IsZigInstalled(version)) { return false; }
            return true;
        }
        finally
        {
            try { Directory.Delete(partial, recursive: true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>The archive of <paramref name="version"/> for <paramref name="key"/> as zig's index
    /// gives it: its file's name, size and SHA-256.</summary>
    private ChannelArchive Archive(string version, string key)
    {
        var url = $"{_base}/index.json";
        if (ToolchainFetch.Get(_http, url, out var reason) is not { } json)
            throw new ToolchainException("LYR-CLI0023", $"zig's index cannot be fetched: {reason}");
        try
        {
            using var index = JsonDocument.Parse(json);
            if (!index.RootElement.TryGetProperty(version, out var release))
                throw new ToolchainException("LYR-CLI0023", $"zig's index has no version {version}");
            if (!release.TryGetProperty(key, out var entry))
                throw new ToolchainException("LYR-CLI0023", $"zig {version} has no archive for {key}");
            var tarball = entry.GetProperty("tarball").GetString() ?? throw new InvalidDataException("'tarball' is no text");
            var name = tarball[(tarball.LastIndexOf('/') + 1)..];
            if (name.Length == 0 || name.Contains('\\') || name.StartsWith('.') || !(name.EndsWith(".tar.xz", StringComparison.Ordinal) || name.EndsWith(".zip", StringComparison.Ordinal)))
                throw new InvalidDataException($"'{tarball}' names no .tar.xz or .zip");
            var size = long.Parse(entry.GetProperty("size").GetString() ?? "", System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture);
            var sha256 = entry.GetProperty("shasum").GetString() ?? "";
            if (sha256.Length != 64 || !sha256.All(Uri.IsHexDigit)) throw new InvalidDataException($"'{sha256}' is no SHA-256");
            return new ChannelArchive(name, size, sha256.ToLowerInvariant());
        }
        catch (Exception e) when (e is JsonException or InvalidDataException or FormatException or OverflowException or KeyNotFoundException or InvalidOperationException)
        {
            throw new ToolchainException("LYR-CLI0023", $"zig's index at {url} cannot be read: {e.Message}");
        }
    }

    /// <summary>The archive into <paramref name="file"/> from the first source whose bytes hold:
    /// the mirrors in a shuffled order, then the download page itself.</summary>
    private void Fetch(string version, ChannelArchive archive, string file)
    {
        var sources = Mirrors().Select(mirror => $"{mirror}/{archive.File}?source=lyric").ToList();
        sources.Add($"{_base}/{version}/{archive.File}");
        var unreachable = new List<string>();
        var refused = new List<string>();
        foreach (var url in sources)
        {
            var signatureUrl = url.Contains('?') ? url.Replace("?", ".minisig?", StringComparison.Ordinal) : url + ".minisig";
            try
            {
                ToolchainFetch.Download(_http, _progress, url, file, archive);
                if (ToolchainFetch.Get(_http, signatureUrl, out var reason) is not { } signature)
                    throw new ToolchainException("LYR-CLI0023", reason);
                Check(file, archive.File, System.Text.Encoding.UTF8.GetString(signature));
                return;
            }
            catch (ToolchainException failed)
            {
                (failed.Code == "LYR-CLI0024" ? refused : unreachable).Add($"{Host(url)}: {failed.Message}");
                File.Delete(file);
            }
        }
        if (refused.Count > 0)
            throw new ToolchainException("LYR-CLI0024", $"no source serves zig {version} as zig signed it: {string.Join("; ", refused.Concat(unreachable))}", "nothing was installed");
        throw new ToolchainException("LYR-CLI0023", $"zig {version} cannot be fetched: {string.Join("; ", unreachable)}");
    }

    /// <summary>The signature over <paramref name="file"/> with zig's key, and the file its trusted
    /// comment names against <paramref name="name"/> — a mirror could otherwise serve an older
    /// archive zig signed under the name of a newer one.</summary>
    private void Check(string file, string name, string signature)
    {
        string comment;
        try
        {
            using var data = File.OpenRead(file);
            comment = Minisign.Verify(_key, data, signature);
        }
        catch (SignatureException e)
        {
            throw new ToolchainException("LYR-CLI0024", $"{name}: {e.Message}");
        }
        var named = comment.Split('\t').FirstOrDefault(field => field.StartsWith("file:", StringComparison.Ordinal))?["file:".Length..];
        if (named != name)
            throw new ToolchainException("LYR-CLI0024", named is null
                ? $"{name}: its signature names no file"
                : $"{name}: its signature is the one of {named}");
    }

    /// <summary>The community mirrors from the list beside the index, shuffled; none where the list
    /// cannot be had — the download page is then the one source.</summary>
    private List<string> Mirrors()
    {
        if (ToolchainFetch.Get(_http, $"{_base}/community-mirrors.txt", out _) is not { } list) return [];
        var mirrors = System.Text.Encoding.UTF8.GetString(list)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => Uri.TryCreate(line, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" && uri.Query.Length == 0)
            .Select(line => line.TrimEnd('/'))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        for (var i = mirrors.Count - 1; i > 0; i--)
        {
            var j = _shuffle.Next(i + 1);
            (mirrors[i], mirrors[j]) = (mirrors[j], mirrors[i]);
        }
        return mirrors;
    }

    private static string Host(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.GetLeftPart(UriPartial.Authority) : url;

    /// <summary>A <c>.tar.xz</c> or a <c>.zip</c> unpacked into <paramref name="tree"/>.</summary>
    private static void Unpack(string file, string tree)
    {
        Directory.CreateDirectory(tree);
        try
        {
            if (file.EndsWith(".zip", StringComparison.Ordinal)) ZipFile.ExtractToDirectory(file, tree);
            else
            {
                var tar = file[..^".xz".Length];
                using (var input = File.OpenRead(file))
                using (var output = File.Create(tar))
                    Xz.Decompress(input, output);
                File.Delete(file);
                TarFile.ExtractToDirectory(tar, tree, overwriteFiles: false);
                File.Delete(tar);
            }
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            throw new ToolchainException("LYR-CLI0024", $"{Path.GetFileName(file)} cannot be unpacked: {e.Message}", "nothing was installed");
        }
    }

    /// <summary>The one directory the archive holds, with zig's binary and its <c>lib</c> in it.</summary>
    private static string Top(string tree, string name)
    {
        var entries = Directory.GetFileSystemEntries(tree);
        var binary = "zig" + (OperatingSystem.IsWindows() ? ".exe" : "");
        if (entries is not [var top] || !Directory.Exists(top) || !File.Exists(Path.Combine(top, binary)) || !Directory.Exists(Path.Combine(top, "lib")))
            throw new ToolchainException("LYR-CLI0024", $"{name} does not hold one directory with {binary} and lib", "nothing was installed");
        return top;
    }
}
