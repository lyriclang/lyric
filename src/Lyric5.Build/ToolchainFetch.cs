using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using Lyric5.Toolchain;

namespace Lyric5.Build;

/// <summary>Where a channel's index and its archives are fetched from: its name for a message, and
/// the address of each.</summary>
public sealed record ToolchainSource(string Name, Func<string, string> IndexUrl, Func<string, ChannelRelease, ChannelArchive, string> ArchiveUrl)
{
    /// <summary>A mirror's layout: <c>&lt;base&gt;/&lt;channel&gt;.json</c> with its
    /// <c>.minisig</c>, the archives under <c>&lt;base&gt;/&lt;channel&gt;/</c>.</summary>
    public static ToolchainSource OfMirror(string address)
    {
        var root = address.TrimEnd('/');
        return new ToolchainSource(root, channel => $"{root}/{ChannelIndex.FileName(channel)}", (channel, _, archive) => $"{root}/{channel}/{archive.File}");
    }

    /// <summary>The project's releases on GitHub: <c>dev</c> carries the development build and its
    /// index, <c>channels</c> the stable index, <c>v&lt;version&gt;</c> each stable release.</summary>
    public static readonly ToolchainSource GitHub = new("GitHub",
        channel => $"{ToolchainFetch.GitHubReleases}/{(channel == ChannelIndex.Dev ? "dev" : "channels")}/{ChannelIndex.FileName(channel)}",
        (channel, release, archive) => $"{ToolchainFetch.GitHubReleases}/{(channel == ChannelIndex.Dev ? "dev" : "v" + release.Version)}/{archive.File}");
}

/// <summary>
/// Fetches a channel's index and a toolchain's archive and installs it: the index only with a
/// signature the key holds, the archive only at the size and the SHA-256 the index gives, unpacked
/// beside the toolchains, checked against its <c>toolchain.json</c> and only then moved into place —
/// a run cut short leaves nothing a later one counts as installed. The mirror is asked first and
/// GitHub after it, where the mirror cannot be reached; a signature or a hash that does not hold is
/// never routed around. <c>LYRIC_MIRROR</c> names another mirror in place of both, and
/// <c>LYRIC_MIRROR_KEY</c> the key its signatures are checked with.
/// </summary>
public sealed class ToolchainFetch
{
    public const string Mirror = "https://dl.lyric.oil1i.de";
    public const string GitHubReleases = "https://github.com/lyriclang/lyric/releases/download";

    /// <summary>The key the project signs its releases with, as the line of base64 of its
    /// <c>.pub</c>; none before the first signed release.</summary>
    public static readonly string? PublicKey = null;

    private readonly IReadOnlyList<ToolchainSource> _sources;
    private readonly MinisignKey? _key;
    private readonly HttpClient _http;
    private readonly TextWriter? _progress;
    private readonly Dictionary<string, (ChannelIndex Index, ToolchainSource Source)> _indexes = new(StringComparer.Ordinal);

    public ToolchainFetch(IReadOnlyList<ToolchainSource> sources, MinisignKey? key, string userAgent, TextWriter? progress)
    {
        _sources = sources;
        _key = key;
        _progress = progress;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
    }

    /// <summary>The sources and the key as the environment sets them; the progress of a download
    /// is drawn on <paramref name="progress"/> where it is given.</summary>
    /// <exception cref="ToolchainException"><c>LYR-CLI0023</c>: <c>LYRIC_MIRROR_KEY</c> is no key, or is set without a mirror.</exception>
    public static ToolchainFetch ForUser(string version, TextWriter? progress)
    {
        var mirror = Environment.GetEnvironmentVariable("LYRIC_MIRROR");
        var named = Environment.GetEnvironmentVariable("LYRIC_MIRROR_KEY");
        MinisignKey? key = null;
        if (!string.IsNullOrWhiteSpace(named))
        {
            if (string.IsNullOrWhiteSpace(mirror))
                throw new ToolchainException("LYR-CLI0023", "LYRIC_MIRROR_KEY is set and LYRIC_MIRROR is not", "the key is the one a mirror of your own signs with; name the mirror too");
            try { key = MinisignKey.Parse(named); }
            catch (SignatureException e) { throw new ToolchainException("LYR-CLI0023", $"LYRIC_MIRROR_KEY: {e.Message}"); }
        }
        else if (PublicKey is not null) key = MinisignKey.Parse(PublicKey);
        IReadOnlyList<ToolchainSource> sources = string.IsNullOrWhiteSpace(mirror)
            ? [ToolchainSource.OfMirror(Mirror), ToolchainSource.GitHub]
            : [ToolchainSource.OfMirror(mirror)];
        return new ToolchainFetch(sources, key, $"lyric/{version}", progress);
    }

    /// <summary>The index of <paramref name="channel"/>, from the first source that serves it,
    /// checked against the key.</summary>
    /// <exception cref="ToolchainException"><c>LYR-CLI0023</c> where no source serves it,
    /// <c>LYR-CLI0024</c> where its signature does not hold.</exception>
    public ChannelIndex Index(string channel)
    {
        if (_indexes.TryGetValue(channel, out var known)) return known.Index;
        if (_key is null)
            throw new ToolchainException("LYR-CLI0023", "this toolchain carries no key to check a channel's signature with",
                "a build of your own installs from a mirror of your own: LYRIC_MIRROR names it, LYRIC_MIRROR_KEY its key");
        var failures = new List<string>();
        foreach (var source in _sources)
        {
            var url = source.IndexUrl(channel);
            if (Get(url, out var reason) is not { } json || Get(url + ".minisig", out reason) is not { } signature)
            {
                failures.Add(reason);
                continue;
            }
            ChannelIndex index;
            try { index = ChannelIndex.Read(json, System.Text.Encoding.UTF8.GetString(signature), _key, channel); }
            catch (Exception e) when (e is SignatureException or InvalidDataException)
            {
                throw new ToolchainException("LYR-CLI0024", $"the index of the channel '{channel}' from {source.Name} is refused: {e.Message}",
                    "nothing was installed; a mirror that serves this is not to be trusted");
            }
            _indexes[channel] = (index, source);
            return index;
        }
        throw new ToolchainException("LYR-CLI0023", $"the index of the channel '{channel}' cannot be fetched: {string.Join("; ", failures)}");
    }

    /// <summary>The release <paramref name="asked"/> names — <c>dev</c> or <c>stable</c> for the
    /// newest of the channel, else a version, with or without its build part — and its channel.</summary>
    /// <exception cref="ToolchainException"><c>LYR-CLI0023</c>: the channel has no such version.</exception>
    public (ChannelRelease Release, string Channel) Release(string asked)
    {
        if (asked is ChannelIndex.Dev or ChannelIndex.Stable)
            return (Index(asked).Newest ?? throw new ToolchainException("LYR-CLI0023", $"the channel '{asked}' holds no version"), asked);
        if (SemVer.Parse(asked) is null)
            throw new ToolchainException("LYR-CLI0023", $"'{asked}' is no version", "a version such as 5.0.0, or a channel: dev, stable");
        var channel = ToolchainHome.ChannelOf(asked);
        var index = Index(channel);
        var release = index.Find(asked) ?? index.Releases.FirstOrDefault(r => ToolchainHome.WithoutBuild(r.Version) == asked);
        return release is not null
            ? (release, channel)
            : throw new ToolchainException("LYR-CLI0023", $"the channel '{channel}' has no version {asked}",
                "lyric5 toolchain list --available says which it has");
    }

    /// <summary>Installs <paramref name="release"/> of <paramref name="channel"/> for this machine
    /// into <paramref name="home"/>; <c>false</c> where it is there already.</summary>
    /// <exception cref="ToolchainException"><c>LYR-CLI0023</c> where the archive cannot be had,
    /// <c>LYR-CLI0024</c> where it is not what the index says.</exception>
    public bool Install(ToolchainHome home, ChannelRelease release, string channel)
    {
        var version = release.Version;
        var triple = Target.Host.Triple;
        if (home.IsInstalled(version)) return false;
        if (!release.Archives.TryGetValue(triple, out var archive))
            throw new ToolchainException("LYR-CLI0023", $"lyric {version} has no archive for {triple}",
                $"it has: {string.Join(", ", release.Archives.Keys.Order(StringComparer.Ordinal))}");
        var source = _indexes.TryGetValue(channel, out var known) ? known.Source : _sources[0];
        var partial = home.Partial();
        try
        {
            var file = Path.Combine(partial, archive.File);
            Download(source.ArchiveUrl(channel, release, archive), file, archive);
            var tree = Path.Combine(partial, "toolchain");
            Unpack(file, tree);
            Check(tree, version, triple);
            try { Directory.Move(tree, home.DirectoryOf(version)); }
            catch (IOException) when (home.IsInstalled(version)) { return false; }
            return true;
        }
        finally
        {
            try { Directory.Delete(partial, recursive: true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    private byte[]? Get(string url, out string reason) => Get(_http, url, out reason);

    /// <summary>The body at <paramref name="url"/>; <c>null</c> with the reason where the server
    /// cannot be reached or does not serve it.</summary>
    internal static byte[]? Get(HttpClient http, string url, out string reason)
    {
        try
        {
            using var response = http.Send(new HttpRequestMessage(HttpMethod.Get, url));
            if (!response.IsSuccessStatusCode)
            {
                reason = $"{url}: {(int)response.StatusCode} {response.ReasonPhrase}";
                return null;
            }
            using var body = response.Content.ReadAsStream();
            using var bytes = new MemoryStream();
            body.CopyTo(bytes);
            reason = "";
            return bytes.ToArray();
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException)
        {
            reason = $"{url}: {e.Message}";
            return null;
        }
    }

    private void Download(string url, string file, ChannelArchive archive) => Download(_http, _progress, url, file, archive);

    /// <summary>The archive at <paramref name="url"/> into <paramref name="file"/>, read no further
    /// than the index's size and hashed on the way; its progress drawn on <paramref name="progress"/>.</summary>
    /// <exception cref="ToolchainException"><c>LYR-CLI0023</c> where it cannot be had,
    /// <c>LYR-CLI0024</c> where it is not the size or the hash the index gives.</exception>
    internal static void Download(HttpClient http, TextWriter? progress, string url, string file, ChannelArchive archive)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long read = 0;
        try
        {
            using var response = http.Send(new HttpRequestMessage(HttpMethod.Get, url), HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
                throw new ToolchainException("LYR-CLI0023", $"{url}: {(int)response.StatusCode} {response.ReasonPhrase}");
            using var body = response.Content.ReadAsStream();
            using var output = File.Create(file);
            var buffer = new byte[81920];
            var drawn = DateTime.MinValue;
            while (body.Read(buffer) is var n and > 0)
            {
                read += n;
                if (read > archive.Size)
                    throw new ToolchainException("LYR-CLI0024", $"{archive.File} is larger than the {archive.Size} bytes its index gives", "nothing was installed");
                hash.AppendData(buffer, 0, n);
                output.Write(buffer, 0, n);
                if (progress is not null && DateTime.UtcNow - drawn > TimeSpan.FromMilliseconds(100))
                {
                    progress.Write($"\r  fetching {archive.File}  {read / 1048576.0:0.0} of {archive.Size / 1048576.0:0.0} MB");
                    drawn = DateTime.UtcNow;
                }
            }
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException)
        {
            throw new ToolchainException("LYR-CLI0023", $"{url}: {e.Message}");
        }
        finally
        {
            if (progress is not null && read > 0) progress.WriteLine($"\r  fetched {archive.File}  {read / 1048576.0:0.0} MB{new string(' ', 16)}");
        }
        if (read != archive.Size)
            throw new ToolchainException("LYR-CLI0024", $"{archive.File} ended after {read} of the {archive.Size} bytes its index gives", "nothing was installed");
        var sha256 = Convert.ToHexStringLower(hash.GetHashAndReset());
        if (sha256 != archive.Sha256)
            throw new ToolchainException("LYR-CLI0024", $"{archive.File} has the SHA-256 {sha256}, and its index gives {archive.Sha256}", "nothing was installed");
    }

    /// <summary>A <c>.tar.gz</c> or a <c>.zip</c> unpacked into <paramref name="tree"/>.</summary>
    private static void Unpack(string file, string tree)
    {
        Directory.CreateDirectory(tree);
        try
        {
            if (file.EndsWith(".zip", StringComparison.Ordinal)) ZipFile.ExtractToDirectory(file, tree);
            else if (file.EndsWith(".tar.gz", StringComparison.Ordinal))
            {
                using var gzip = new GZipStream(File.OpenRead(file), CompressionMode.Decompress);
                TarFile.ExtractToDirectory(gzip, tree, overwriteFiles: false);
            }
            else throw new InvalidDataException("neither a .tar.gz nor a .zip");
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            throw new ToolchainException("LYR-CLI0024", $"{Path.GetFileName(file)} cannot be unpacked: {e.Message}", "nothing was installed");
        }
    }

    /// <summary>The unpacked toolchain against its <c>toolchain.json</c>: the version and the target
    /// the index promised, and every file as listed.</summary>
    private static void Check(string tree, string version, string triple)
    {
        try
        {
            var (named, target, _) = ToolchainManifest.Read(tree);
            if (named != version) throw new InvalidDataException($"it is lyric {named}, not {version}");
            if (target != triple) throw new InvalidDataException($"it is built for {target}, not {triple}");
            if (ToolchainManifest.Verify(tree) is [var first, ..] problems)
                throw new InvalidDataException(problems.Count == 1 ? first : $"{first} (and {problems.Count - 1} more)");
        }
        catch (InvalidDataException e)
        {
            throw new ToolchainException("LYR-CLI0024", $"the archive of lyric {version} does not hold what its {ToolchainManifest.FileName} lists: {e.Message}", "nothing was installed");
        }
    }
}
