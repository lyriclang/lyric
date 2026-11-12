using System.Formats.Tar;
using System.Net.Sockets;
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
/// signature one of the channel's keys made and no older than the one this machine has seen, the
/// archive only at the size and the SHA-256 the index gives, unpacked beside the toolchains, checked
/// against its <c>toolchain.json</c> and only then moved into place — a run cut short leaves nothing
/// a later one counts as installed. The mirror is asked first and GitHub after it. What one source
/// serves wrongly is said and the next one asked: only a signature and a hash make anything
/// acceptable, so another source can add no risk, and a broken mirror stops nobody. Where no source
/// serves what holds, nothing is installed. <c>LYRIC_MIRROR</c> names another mirror in place of
/// both, and <c>LYRIC_MIRROR_KEY</c> the key its signatures are checked with.
/// </summary>
public sealed class ToolchainFetch
{
    public const string Mirror = "https://dl.lyriclang.de";
    public const string GitHubReleases = "https://github.com/lyriclang/lyric/releases/download";

    /// <summary>The key the development builds are signed with, as the line of base64 of its <c>.pub</c>.</summary>
    public const string DevKey = "RWQM0eXpRYf8iCHBHUgYJPQOdE1oXy+fpGIov6Kz1PlP7Wp2llW+CCOv";

    /// <summary>The key the stable releases are signed with.</summary>
    public const string StableKey = "RWRzuPHBZrHnLIGQOgkuBJqmlH9C0P5rHYKFHlPd/8S1dreWrbTox657";

    /// <summary>The key held offline for the day a channel's own key is lost; both channels accept it.</summary>
    public const string ReserveKey = "RWShnZuifvfSSrKoQURRq3baivxxQTHuAXLJxOgsLU0Sz/R1JJ1P8RTH";

    /// <summary>How long a download may go without a byte before its source counts as gone.</summary>
    public static readonly TimeSpan DefaultStall = TimeSpan.FromSeconds(60);

    private readonly IReadOnlyList<ToolchainSource> _sources;
    private readonly Func<string, IReadOnlyList<MinisignKey>> _keys;
    private readonly HttpClient _http;
    private readonly TextWriter? _progress;
    private readonly Action<string>? _warn;
    private readonly string? _seen;
    private readonly TimeSpan _stall;
    private readonly Dictionary<string, (ChannelIndex Index, ToolchainSource Source)> _indexes = new(StringComparer.Ordinal);

    /// <param name="warn">Told of each source that served something refused, where another follows it.</param>
    /// <param name="seen">The directory that remembers the newest index seen; nothing is remembered without one.</param>
    /// <param name="stall">How long a download may go without a byte; <see cref="DefaultStall"/> if not given.</param>
    public ToolchainFetch(IReadOnlyList<ToolchainSource> sources, Func<string, IReadOnlyList<MinisignKey>> keys, string userAgent, TextWriter? progress, Action<string>? warn = null, string? seen = null, TimeSpan? stall = null)
    {
        _sources = sources;
        _keys = keys;
        _progress = progress;
        _warn = warn;
        _seen = seen;
        _stall = stall ?? DefaultStall;
        _http = Client(userAgent, _stall, TimeSpan.FromSeconds(60));
    }

    /// <summary>A client whose connections give up after 10 s, and whose reads give up when nothing
    /// came for <paramref name="stall"/>: a mirror that drops packets, or one that stops sending, is
    /// told apart from a slow one in time for the source after it. The limit sits on the socket,
    /// where a synchronous read honours it on every system. The connection is made on the calling
    /// thread too: a fetch is synchronous, and a callback that awaited would block it until a pool
    /// thread came free to finish the connect — in a busy process, past the client's timeout.</summary>
    public static HttpClient Client(string userAgent, TimeSpan stall, TimeSpan timeout)
    {
        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(10),
            ConnectCallback = (context, cancel) =>
            {
                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true, ReceiveTimeout = (int)stall.TotalMilliseconds };
                try
                {
                    // the connect timeout cancels the token; closing the socket ends the blocked connect
                    using (cancel.Register(socket.Dispose))
                        socket.Connect(context.DnsEndPoint);
                    // A connect that came just as the timeout ran out: the registration may have closed
                    // the socket under it. Leaving the using waits for that, so the token now says.
                    cancel.ThrowIfCancellationRequested();
                    return ValueTask.FromResult<Stream>(new NetworkStream(socket, ownsSocket: true));
                }
                catch (Exception e) when (cancel.IsCancellationRequested)
                {
                    socket.Dispose();
                    throw new OperationCanceledException($"no connection to {context.DnsEndPoint.Host} came", e, cancel);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            },
        };
        var client = new HttpClient(handler) { Timeout = timeout };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        return client;
    }

    /// <summary>The keys an index of <paramref name="channel"/> may be signed with: the channel's
    /// own and the reserve, so that a key of one channel can sign nothing for the other.</summary>
    public static IReadOnlyList<MinisignKey> KeysOf(string channel) =>
        [MinisignKey.Parse(channel == ChannelIndex.Dev ? DevKey : StableKey), MinisignKey.Parse(ReserveKey)];

    /// <summary>The sources and the keys as the environment sets them; the progress of a download
    /// is drawn on <paramref name="progress"/> where it is given, and <paramref name="warn"/> told
    /// of a source that served something refused.</summary>
    /// <exception cref="ToolchainException"><c>LYR-CLI0023</c>: <c>LYRIC_MIRROR_KEY</c> is no key, or is set without a mirror.</exception>
    public static ToolchainFetch ForUser(string version, TextWriter? progress, Action<string>? warn = null)
    {
        var mirror = Environment.GetEnvironmentVariable("LYRIC_MIRROR");
        var named = Environment.GetEnvironmentVariable("LYRIC_MIRROR_KEY");
        Func<string, IReadOnlyList<MinisignKey>> keys = KeysOf;
        if (!string.IsNullOrWhiteSpace(named))
        {
            if (string.IsNullOrWhiteSpace(mirror))
                throw new ToolchainException("LYR-CLI0023", "LYRIC_MIRROR_KEY is set and LYRIC_MIRROR is not", "the key is the one a mirror of your own signs with; name the mirror too");
            MinisignKey key;
            try { key = MinisignKey.Parse(named); }
            catch (SignatureException e) { throw new ToolchainException("LYR-CLI0023", $"LYRIC_MIRROR_KEY: {e.Message}"); }
            keys = _ => [key];
        }
        IReadOnlyList<ToolchainSource> sources = string.IsNullOrWhiteSpace(mirror)
            ? [ToolchainSource.OfMirror(Mirror), ToolchainSource.GitHub]
            : [ToolchainSource.OfMirror(mirror)];
        return new ToolchainFetch(sources, keys, $"lyric/{version}", progress, warn, Path.Combine(LyricHome.Root, "seen"));
    }

    /// <summary>The index of <paramref name="channel"/>, from the first source that serves one that
    /// holds. A signature that does not fit is fetched once more with its index: the two are two
    /// requests, and a publish between them parts a good pair.</summary>
    /// <exception cref="ToolchainException"><c>LYR-CLI0024</c> where a source served one refused and
    /// none served one that holds, <c>LYR-CLI0023</c> where no source served any.</exception>
    public ChannelIndex Index(string channel)
    {
        if (_indexes.TryGetValue(channel, out var known)) return known.Index;
        var keys = _keys(channel);
        var failures = new List<string>();
        var refusals = new List<string>();
        foreach (var source in _sources)
        {
            var url = source.IndexUrl(channel);
            string? refusal = null;
            for (var attempt = 0; attempt < 2; attempt++)
            {
                if (Get(url, out var reason) is not { } json || Get(url + ".minisig", out reason) is not { } signature)
                {
                    failures.Add(reason);
                    refusal = null;
                    break;
                }
                ChannelIndex index;
                try { index = ChannelIndex.Read(json, System.Text.Encoding.UTF8.GetString(signature), keys, channel); }
                catch (Exception e) when (e is SignatureException or InvalidDataException)
                {
                    refusal = $"the index of the channel '{channel}' from {source.Name} is refused: {e.Message}";
                    continue;
                }
                if (Seen(channel, index) is var seen && index.Sequence < seen)
                {
                    refusal = $"the index of the channel '{channel}' from {source.Name} is refused: it is number {index.Sequence}, and this machine has seen number {seen}";
                    break;
                }
                Remember(channel, index);
                _indexes[channel] = (index, source);
                return index;
            }
            if (refusal is null) continue;
            refusals.Add(refusal);
            if (!ReferenceEquals(source, _sources[^1])) _warn?.Invoke(refusal);
        }
        if (refusals.Count > 0)
            throw new ToolchainException("LYR-CLI0024", string.Join("; ", refusals), "nothing was installed; a mirror that serves this is not to be trusted");
        throw new ToolchainException("LYR-CLI0023", $"the index of the channel '{channel}' cannot be fetched: {string.Join("; ", failures)}");
    }

    /// <summary>The highest number of an index of <paramref name="channel"/> by the same signer this
    /// machine has seen, 0 for none. Numbers of another signer, a mirror of one's own, do not compare.</summary>
    private long Seen(string channel, ChannelIndex index)
    {
        if (_seen is null) return 0;
        try { return long.TryParse(File.ReadAllText(SeenFile(channel, index)).Trim(), out var number) ? number : 0; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return 0; }
    }

    /// <summary>Keeps the index's number; a home that cannot be written costs the memory, not the install.</summary>
    private void Remember(string channel, ChannelIndex index)
    {
        if (_seen is null || index.Sequence == Seen(channel, index)) return;
        var file = SeenFile(channel, index);
        var partial = $"{file}.{Environment.ProcessId}";
        try
        {
            Directory.CreateDirectory(_seen);
            File.WriteAllText(partial, index.Sequence + "\n");
            File.Move(partial, file, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private string SeenFile(string channel, ChannelIndex index) => Path.Combine(_seen!, $"{channel}-{index.SignedBy}");

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
        var first = _indexes.TryGetValue(channel, out var known) ? known.Source : _sources[0];
        var partial = home.Partial();
        try
        {
            var file = Path.Combine(partial, archive.File);
            FromSomeSource([first, .. _sources.Where(s => !ReferenceEquals(s, first))], channel, release, archive, file);
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

    /// <summary>The archive into <paramref name="file"/> from the first source that has it as the
    /// index says; the index is signed, so any source will do.</summary>
    private void FromSomeSource(IReadOnlyList<ToolchainSource> sources, string channel, ChannelRelease release, ChannelArchive archive, string file)
    {
        var failures = new List<ToolchainException>();
        foreach (var source in sources)
        {
            try
            {
                Download(_http, _progress, source.ArchiveUrl(channel, release, archive), file, archive, _stall);
                return;
            }
            catch (ToolchainException failure)
            {
                failures.Add(failure);
                if (!ReferenceEquals(source, sources[^1])) _warn?.Invoke(failure.Message);
            }
        }
        throw failures.FirstOrDefault(f => f.Code == "LYR-CLI0024") ?? failures[0];
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

    /// <summary>The archive at <paramref name="url"/> into <paramref name="file"/>, read no further
    /// than the index's size and hashed on the way; its progress drawn on <paramref name="progress"/>.
    /// A source that sends no byte for the limit the client's sockets carry (see <see cref="Client"/>)
    /// counts as gone; <paramref name="stall"/> names that limit in the message.</summary>
    /// <exception cref="ToolchainException"><c>LYR-CLI0023</c> where it cannot be had,
    /// <c>LYR-CLI0024</c> where it is not the size or the hash the index gives.</exception>
    internal static void Download(HttpClient http, TextWriter? progress, string url, string file, ChannelArchive archive, TimeSpan? stall = null)
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
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or IOException)
        {
            var stalled = e is IOException { InnerException: SocketException { SocketErrorCode: SocketError.TimedOut } };
            throw new ToolchainException("LYR-CLI0023", stalled ? $"{url}: nothing came for {(stall ?? DefaultStall).TotalSeconds:0.#} s" : $"{url}: {e.Message}");
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
