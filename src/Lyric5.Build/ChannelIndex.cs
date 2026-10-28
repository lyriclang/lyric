using System.Text.Json;
using Lyric5.Toolchain;

namespace Lyric5.Build;

/// <summary>An archive a channel offers for one target: its file name, size and SHA-256 in hex.</summary>
public sealed record ChannelArchive(string File, long Size, string Sha256);

/// <summary>A version a channel offers, the day it was made, and its archive per target triple.</summary>
public sealed record ChannelRelease(string Version, SemVer Number, string Date, IReadOnlyDictionary<string, ChannelArchive> Archives);

/// <summary>
/// A channel's index — <c>dev.json</c>, <c>stable.json</c> — the versions it offers, each with its
/// archive per target. It is signed (<c>dev.json.minisig</c>) like the archives, so a mirror can
/// neither offer a version the release pipeline did not make nor hide a newer one behind an older
/// index of another channel: <see cref="Read"/> checks the signature and that the index names the
/// channel asked for.
/// </summary>
public sealed record ChannelIndex(string Channel, IReadOnlyList<ChannelRelease> Releases)
{
    public const string Dev = "dev";
    public const string Stable = "stable";

    public static string FileName(string channel) => $"{channel}.json";

    /// <summary>The newest version — the first of the file's where two compare equal, as two dev
    /// builds of a day differ only in their build data; <c>null</c> for an empty channel.</summary>
    public ChannelRelease? Newest => Releases.MaxBy(r => r.Number);

    public ChannelRelease? Find(string version) => Releases.FirstOrDefault(r => r.Version == version);

    /// <summary>The index of <paramref name="channel"/> from its bytes and the text of its
    /// signature, checked against <paramref name="key"/>.</summary>
    /// <exception cref="SignatureException">The signature does not hold.</exception>
    /// <exception cref="InvalidDataException">It is not an index, or one of another channel.</exception>
    public static ChannelIndex Read(byte[] json, string signature, MinisignKey key, string channel)
    {
        Minisign.Verify(key, json, signature);
        var index = Parse(json);
        if (index.Channel != channel)
            throw new InvalidDataException($"the index of the channel '{index.Channel}' stands where '{channel}' was asked for");
        return index;
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
            return new ChannelIndex(top.GetProperty("channel").GetString()!, releases);
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException)
        {
            throw new InvalidDataException($"not a channel index: {e.Message}");
        }
    }
}
