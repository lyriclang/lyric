using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using Lyric5.Toolchain;

namespace Lyric5.Build;

/// <summary>An archive a channel offers for one target: its file name, size and SHA-256 in hex.</summary>
public sealed record ChannelArchive(string File, long Size, string Sha256);

/// <summary>A version a channel offers, the day it was made, and its archive per target triple.</summary>
public sealed record ChannelRelease(string Version, SemVer Number, string Date, IReadOnlyDictionary<string, ChannelArchive> Archives);

/// <summary>
/// A channel's index — <c>dev.json</c>, <c>stable.json</c> — the versions it offers, each with its
/// archive per target, and a number that only grows from one index of the channel to the next. It
/// is signed (<c>dev.json.minisig</c>) like the archives, so a mirror can neither offer a version
/// the release pipeline did not make nor hide a newer one behind an older index of another channel:
/// <see cref="Read(byte[], string, IReadOnlyList{MinisignKey}, string)"/> checks the signature and
/// that the index names the channel asked for. An older index of the same channel is told by its
/// number, and only by a machine that has seen a newer one.
/// </summary>
public sealed record ChannelIndex(string Channel, long Sequence, IReadOnlyList<ChannelRelease> Releases)
{
    public const string Dev = "dev";
    public const string Stable = "stable";

    public static string FileName(string channel) => $"{channel}.json";

    /// <summary>The newest version — the first of the file's where two compare equal, as two dev
    /// builds of a day differ only in their build data; <c>null</c> for an empty channel.</summary>
    public ChannelRelease? Newest => Releases.MaxBy(r => r.Number);

    public ChannelRelease? Find(string version) => Releases.FirstOrDefault(r => r.Version == version);

    /// <summary>The id of the key that signed this index; <c>null</c> for one read unchecked.</summary>
    public string? SignedBy { get; init; }

    /// <summary>The index of <paramref name="channel"/> from its bytes and the text of its
    /// signature, checked against <paramref name="key"/>.</summary>
    /// <exception cref="SignatureException">The signature does not hold.</exception>
    /// <exception cref="InvalidDataException">It is not an index, or one of another channel.</exception>
    public static ChannelIndex Read(byte[] json, string signature, MinisignKey key, string channel) => Read(json, signature, [key], channel);

    /// <summary>The index of <paramref name="channel"/>, signed by one of <paramref name="keys"/>.</summary>
    /// <inheritdoc cref="Read(byte[], string, MinisignKey, string)"/>
    public static ChannelIndex Read(byte[] json, string signature, IReadOnlyList<MinisignKey> keys, string channel)
    {
        var signer = Minisign.Signer(keys, signature);
        Minisign.Verify(signer, json, signature);
        var index = Parse(json);
        if (index.Channel != channel)
            throw new InvalidDataException($"the index of the channel '{index.Channel}' stands where '{channel}' was asked for");
        return index with { SignedBy = signer.IdText };
    }

    /// <summary>The target of each archive name the release pipeline gives, by the name's ending.</summary>
    private static readonly (string Rid, string Triple)[] Targets =
    [
        ("win-x64", "x86_64-windows-gnu"),
        ("linux-x64", "x86_64-linux-gnu"),
        ("linux-arm64", "aarch64-linux-gnu"),
        ("osx-arm64", "aarch64-macos"),
        ("osx-x64", "x86_64-macos"),
    ];

    /// <summary>A release from the archives in <paramref name="directory"/>: every <c>lyric-*</c>
    /// file there, by the target its name ends in.</summary>
    /// <exception cref="InvalidDataException">An archive names no known target, or two the same.</exception>
    public static ChannelRelease ReleaseFrom(string directory, string version, string date)
    {
        var number = SemVer.Parse(version) ?? throw new InvalidDataException($"'{version}' is not a version");
        var archives = new Dictionary<string, ChannelArchive>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(directory, "lyric-*").Order(StringComparer.Ordinal))
        {
            var file = Path.GetFileName(path);
            if (file.EndsWith(".minisig", StringComparison.Ordinal)) continue;
            var stem = file.EndsWith(".tar.gz", StringComparison.Ordinal) ? file[..^7] : file.EndsWith(".zip", StringComparison.Ordinal) ? file[..^4] : null;
            var target = stem is null ? default : Targets.FirstOrDefault(t => stem.EndsWith("-" + t.Rid, StringComparison.Ordinal));
            if (target.Triple is null) throw new InvalidDataException($"{file} names no target of a release");
            using var stream = File.OpenRead(path);
            if (!archives.TryAdd(target.Triple, new ChannelArchive(file, stream.Length, Convert.ToHexStringLower(SHA256.HashData(stream)))))
                throw new InvalidDataException($"two archives for {target.Triple}: {file} and {archives[target.Triple].File}");
        }
        if (archives.Count == 0) throw new InvalidDataException($"no archive in {directory}");
        return new ChannelRelease(version, number, date, archives);
    }

    /// <summary>The next index of <paramref name="channel"/>: <paramref name="release"/> first — two
    /// builds of a day compare equal, and <see cref="Newest"/> takes the first —, then what
    /// <paramref name="previous"/> offered, the same version not twice, <paramref name="keep"/> at most.</summary>
    /// <exception cref="InvalidDataException"><paramref name="sequence"/> does not grow.</exception>
    public static ChannelIndex Next(ChannelIndex? previous, string channel, ChannelRelease release, long sequence, int keep)
    {
        if (previous is not null && previous.Channel != channel)
            throw new InvalidDataException($"the index of the channel '{previous.Channel}' stands where '{channel}' was asked for");
        if (sequence <= (previous?.Sequence ?? 0))
            throw new InvalidDataException($"the number {sequence} does not grow from the previous index's {previous?.Sequence ?? 0}");
        return new ChannelIndex(channel, sequence, [release, .. (previous?.Releases ?? []).Where(r => r.Version != release.Version).Take(Math.Max(keep - 1, 0))]);
    }

    /// <summary>The file's bytes, in the form <see cref="Parse"/> reads.</summary>
    public byte[] ToJson()
    {
        using var bytes = new MemoryStream();
        using (var json = new Utf8JsonWriter(bytes, new JsonWriterOptions { Indented = true, NewLine = "\n", Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            json.WriteStartObject();
            json.WriteString("channel", Channel);
            json.WriteNumber("sequence", Sequence);
            json.WriteStartArray("versions");
            foreach (var release in Releases)
            {
                json.WriteStartObject();
                json.WriteString("version", release.Version);
                json.WriteString("date", release.Date);
                json.WriteStartObject("archives");
                foreach (var (target, archive) in release.Archives.OrderBy(a => a.Key, StringComparer.Ordinal))
                {
                    json.WriteStartObject(target);
                    json.WriteString("file", archive.File);
                    json.WriteNumber("size", archive.Size);
                    json.WriteString("sha256", archive.Sha256);
                    json.WriteEndObject();
                }
                json.WriteEndObject();
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteEndObject();
        }
        bytes.WriteByte((byte)'\n');
        return bytes.ToArray();
    }

    /// <summary>An index from its bytes, unchecked.</summary>
    /// <exception cref="InvalidDataException">It is not an index.</exception>
    public static ChannelIndex Parse(byte[] json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var top = document.RootElement;
            var releases = new List<ChannelRelease>();
            foreach (var release in top.GetProperty("versions").EnumerateArray())
            {
                var version = release.GetProperty("version").GetString()!;
                var number = SemVer.Parse(version) ?? throw new InvalidDataException($"'{version}' is not a version");
                if (releases.Any(r => r.Version == version)) throw new InvalidDataException($"the version {version} stands twice");
                var archives = new Dictionary<string, ChannelArchive>(StringComparer.Ordinal);
                foreach (var archive in release.GetProperty("archives").EnumerateObject())
                {
                    var target = Target.Parse(archive.Name).Triple;
                    var file = archive.Value.GetProperty("file").GetString()!;
                    // the name is joined to the mirror's address and to a directory on disk
                    if (file.Length == 0 || file.IndexOfAny(['/', '\\']) >= 0 || file.StartsWith('.'))
                        throw new InvalidDataException($"'{file}' is not an archive's file name");
                    var size = archive.Value.GetProperty("size").GetInt64();
                    var sha256 = archive.Value.GetProperty("sha256").GetString()!;
                    if (size <= 0) throw new InvalidDataException($"{file}: the size {size}");
                    if (sha256.Length != 64 || !sha256.All(char.IsAsciiHexDigitLower))
                        throw new InvalidDataException($"{file}: '{sha256}' is not a SHA-256 in lower-case hex");
                    archives.Add(target, new ChannelArchive(file, size, sha256));
                }
                releases.Add(new ChannelRelease(version, number, release.GetProperty("date").GetString()!, archives));
            }
            var sequence = top.GetProperty("sequence").GetInt64();
            if (sequence <= 0) throw new InvalidDataException($"the number {sequence}");
            return new ChannelIndex(top.GetProperty("channel").GetString()!, sequence, releases);
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException or FormatException)
        {
            throw new InvalidDataException($"not a channel index: {e.Message}");
        }
    }
}
