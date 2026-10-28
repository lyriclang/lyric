using System.Text;
using Lyric5.Build;
using Lyric5.Toolchain;

namespace Lyric5.Tests;

/// <summary>
/// The signature check a download passes: BLAKE2b-512 and Ed25519 against the RFCs' vectors,
/// minisign signatures and channel indexes made by OpenSSL (<c>minisign/make.py</c>), and what
/// each refuses — a changed byte, another key, the legacy form, a changed trusted comment, a key
/// of small order, an index of another channel.
/// </summary>
public class MinisignTests
{
    private static readonly string Fixtures = Path.Combine(RuntimeLayout.FindRoot(AppContext.BaseDirectory), "tests", "Lyric5.Tests", "minisign");

    private static byte[] Bytes(string name) => File.ReadAllBytes(Path.Combine(Fixtures, name));
    private static string Text(string name) => File.ReadAllText(Path.Combine(Fixtures, name));
    private static MinisignKey Key(string name = "key.pub") => MinisignKey.Parse(Text(name));

    private static byte[] Pattern(int length) => Enumerable.Range(0, length).Select(i => (byte)(i % 251)).ToArray();

    [Fact]
    public void Blake2b_gives_the_RFC_7693_hash_and_OpenSSL_s_at_a_block_s_edges_however_it_is_fed()
    {
        Assert.Equal(
            "ba80a53f981c4d0d6a2797b69f12f6e94c212f14685ac4b74b12bb6fdbffa2d17d87c5392aab792dc252d5de4533cc9518d38aa8dbf1925ab92386edd4009923",
            Convert.ToHexStringLower(Blake2b.Hash("abc"u8)));
        var lines = Text("blake2b.txt").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(12, lines.Length);
        foreach (var line in lines)
        {
            var (length, expected) = (int.Parse(line.Split(' ')[0]), line.Split(' ')[1]);
            var data = Pattern(length);
            Assert.Equal(expected, Convert.ToHexStringLower(Blake2b.Hash(data)));
            foreach (var piece in new[] { 1, 7, 128, 4096 })
            {
                var hash = new Blake2b();
                for (var at = 0; at < data.Length; at += piece) hash.Append(data.AsSpan(at, Math.Min(piece, data.Length - at)));
                Assert.Equal(expected, Convert.ToHexStringLower(hash.Finish()));
            }
            Assert.Equal(expected, Convert.ToHexStringLower(Blake2b.Hash(new MemoryStream(data))));
        }
    }

    [Theory]
    [InlineData(
        "d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a", "",
        "e5564300c360ac729086e2cc806e828a84877f1eb8e5d974d873e065224901555fb8821590a33bacc61e39701cf9b46bd25bf5f0595bbe24655141438e7a100b")]
    [InlineData(
        "3d4017c3e843895a92b70aa74d1b7ebc9c982ccf2ec4968cc0cd55f12af4660c", "72",
        "92a009a9f0d4cab8720e820b5f642540a2b27b5416503f8fb3762223ebdb69da085ac1e43e15996e458f3613d0f11d8c387b2eaeb4302aeeb00d291612bb0c00")]
    [InlineData(
        "fc51cd8e6218a1a38da47ed00230f0580816ed13ba3303ac5deb911548908025", "af82",
        "6291d657deec24024827e69c3abe01a30ce548a284743a445e3680d7db5ac3ac18ff9b538d16f290ae67f760984dc6594a7c15e9716ed28dc027beceea1ec40a")]
    public void Ed25519_holds_for_RFC_8032_s_vectors_and_for_nothing_changed(string key, string message, string signature)
    {
        var (k, m, s) = (Convert.FromHexString(key), Convert.FromHexString(message), Convert.FromHexString(signature));
        Assert.True(Ed25519.Verify(k, m, s));

        Assert.False(Ed25519.Verify(k, [.. m, 0], s));
        for (var at = 0; at < 64; at += 21)
        {
            var changed = s.ToArray();
            changed[at] ^= 0x01;
            Assert.False(Ed25519.Verify(k, m, changed));
        }
        var otherKey = k.ToArray();
        otherKey[0] ^= 0x01;
        Assert.False(Ed25519.Verify(otherKey, m, s));
    }

    [Fact]
    public void Ed25519_refuses_an_S_beyond_the_group_order_and_a_key_or_an_R_of_small_order()
    {
        var (k, s) = (Convert.FromHexString("3d4017c3e843895a92b70aa74d1b7ebc9c982ccf2ec4968cc0cd55f12af4660c"),
            Convert.FromHexString("92a009a9f0d4cab8720e820b5f642540a2b27b5416503f8fb3762223ebdb69da085ac1e43e15996e458f3613d0f11d8c387b2eaeb4302aeeb00d291612bb0c00"));
        // S + L: the same point, an encoding libsodium refuses
        var order = System.Numerics.BigInteger.Pow(2, 252) + System.Numerics.BigInteger.Parse("27742317777372353535851937790883648493");
        var big = new System.Numerics.BigInteger(s.AsSpan(32), isUnsigned: true) + order;
        var malleable = s[..32].Concat(big.ToByteArray(isUnsigned: true).Concat(new byte[32]).Take(32)).ToArray();
        Assert.False(Ed25519.Verify(k, [0x72], malleable));

        // the identity as the key, R = B, S = 1: [1]B = B + [h]·identity for every message, a
        // signature that would hold for anything if a key of small order were let through
        var identity = new byte[32];
        identity[0] = 1;
        var basePoint = Convert.FromHexString("5866666666666666666666666666666666666666666666666666666666666666");
        var one = new byte[32];
        one[0] = 1;
        Assert.False(Ed25519.Verify(identity, "anything"u8, [.. basePoint, .. one]));

        // the identity as R with RFC 8032's second key, whose secret is known: S = h·a gives
        // [S]B = [h]A = R + [h]A, an R of small order libsodium refuses
        var secret = System.Security.Cryptography.SHA512.HashData(Convert.FromHexString("4ccd089b28ff96da9db6c346ec114e0f5b8a319f35aba624da8cf6ed4fb8a6fb"));
        secret[0] &= 248;
        secret[31] &= 127;
        secret[31] |= 64;
        var a = new System.Numerics.BigInteger(secret.AsSpan(0, 32), isUnsigned: true);
        byte[] message = [0x72];
        var h = new System.Numerics.BigInteger(System.Security.Cryptography.SHA512.HashData([.. identity, .. k, .. message]), isUnsigned: true) % order;
        var smallR = identity.Concat(((h * a) % order).ToByteArray(isUnsigned: true).Concat(new byte[32]).Take(32)).ToArray();
        Assert.False(Ed25519.Verify(k, message, smallR));
    }

    [Fact]
    public void A_signature_made_the_way_minisign_makes_it_holds_and_gives_its_trusted_comment()
    {
        var key = Key();
        Assert.Equal(Text("key.pub").Split('\n')[0]["untrusted comment: minisign public key ".Length..], key.IdText);
        const string comment = "timestamp:1793145600\tfile:archive.bin\thashed";
        Assert.Equal(comment, Minisign.Verify(key, Bytes("archive.bin"), Text("archive.bin.minisig")));
        Assert.Equal(comment, Minisign.Verify(key, new MemoryStream(Bytes("archive.bin")), Text("archive.bin.minisig").Replace("\n", "\r\n")));
        Assert.Equal(key, MinisignKey.Parse(Text("key.pub").Split('\n')[1]), new KeyEquality());
    }

    [Fact]
    public void A_signature_is_refused_for_a_changed_byte_another_key_the_legacy_form_and_a_changed_comment()
    {
        var key = Key();
        var data = Bytes("archive.bin");
        var signature = Text("archive.bin.minisig");

        var changed = data.ToArray();
        changed[500] ^= 0x01;
        Assert.Equal("the signature does not match the file", Refused(() => Minisign.Verify(key, changed, signature)));
        Assert.Equal("the signature does not match the file", Refused(() => Minisign.Verify(key, data.AsSpan(0, 999), signature)));

        var other = Key("other.pub");
        Assert.Equal($"signed with the key {key.IdText}, not with {other.IdText}", Refused(() => Minisign.Verify(other, data, signature)));
        // the right id with another key's bytes: the id is no secret, the key decides
        var forged = new MinisignKey(key.Id, other.Key);
        Assert.Equal("the signature does not match the file", Refused(() => Minisign.Verify(forged, data, signature)));

        Assert.StartsWith("a signature over the file itself", Refused(() => Minisign.Verify(key, data, Text("archive.bin.legacy.minisig"))));

        var recommented = signature.Replace("file:archive.bin", "file:archive.bin.old");
        Assert.Equal("the trusted comment does not match its signature", Refused(() => Minisign.Verify(key, data, recommented)));
    }

    [Fact]
    public void What_is_not_a_signature_or_a_key_is_refused_as_such()
    {
        var key = Key();
        var data = Bytes("archive.bin");
        var lines = Text("archive.bin.minisig").Split('\n');
        Assert.StartsWith("not a minisign signature", Refused(() => Minisign.Verify(key, data, "")));
        Assert.StartsWith("not a minisign signature", Refused(() => Minisign.Verify(key, data, string.Join('\n', lines[..3]))));
        Assert.StartsWith("not a minisign signature", Refused(() => Minisign.Verify(key, data, string.Join('\n', lines[1..]))));
        Assert.StartsWith("not a minisign signature", Refused(() => Minisign.Verify(key, data, string.Join('\n', [lines[0], "RUQ!!", lines[2], lines[3]]))));
        Assert.StartsWith("not a minisign signature", Refused(() => Minisign.Verify(key, data, string.Join('\n', [lines[0], lines[1][..40], lines[2], lines[3]]))));
        Assert.StartsWith("not a minisign signature", Refused(() => Minisign.Verify(key, data, string.Join('\n', [lines[0], lines[1], "comment: x", lines[3]]))));

        Assert.StartsWith("not a minisign public key", Refused(() => MinisignKey.Parse("")));
        Assert.StartsWith("not a minisign public key", Refused(() => MinisignKey.Parse("untrusted comment: x\nnot base64")));
        // a signature's line where a key belongs: the algorithm and the length differ
        Assert.StartsWith("not a minisign public key", Refused(() => MinisignKey.Parse(lines[1])));
    }

    [Fact]
    public void A_channel_index_is_read_when_its_signature_holds_and_names_the_channel_asked_for()
    {
        var index = ChannelIndex.Read(Bytes("dev.json"), Text("dev.json.minisig"), Key(), ChannelIndex.Dev);
        Assert.Equal(["5.0.0-dev.20261028+0b3eb5e", "5.0.0-dev.20261027+80182e3"], index.Releases.Select(r => r.Version));
        Assert.Equal("5.0.0-dev.20261028+0b3eb5e", index.Newest!.Version);
        var release = index.Find("5.0.0-dev.20261027+80182e3")!;
        Assert.Equal("2026-10-27", release.Date);
        var linux = release.Archives["x86_64-linux-gnu"];
        Assert.Equal(("lyric-5.0.0-dev.20261027-linux-x64.tar.gz", 1000L), (linux.File, linux.Size));
        Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Bytes("archive.bin"))), linux.Sha256);
        Assert.Null(index.Find("5.0.0"));

        Assert.Equal("5.0.0", ChannelIndex.Read(Bytes("stable.json"), Text("stable.json.minisig"), Key(), ChannelIndex.Stable).Newest!.Version);
    }

    [Fact]
    public void A_channel_index_is_refused_changed_signed_by_another_key_or_standing_for_another_channel()
    {
        var json = Bytes("dev.json");
        var changed = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(json).Replace("20261027+80182e3", "20261029+80182e3"));
        Assert.Throws<SignatureException>(() => ChannelIndex.Read(changed, Text("dev.json.minisig"), Key(), ChannelIndex.Dev));
        Assert.Throws<SignatureException>(() => ChannelIndex.Read(json, Text("dev.json.minisig"), Key("other.pub"), ChannelIndex.Dev));
        // a signed index of the other channel, served under this one's name
        var wrong = Assert.Throws<InvalidDataException>(() => ChannelIndex.Read(Bytes("stable.json"), Text("stable.json.minisig"), Key(), ChannelIndex.Dev));
        Assert.Equal("the index of the channel 'stable' stands where 'dev' was asked for", wrong.Message);
    }

    [Theory]
    [InlineData("\"file\": \"lyric-5.0.0-dev.20261028-linux-x64.tar.gz\"", "\"file\": \"../lyric.tar.gz\"", "is not an archive's file name")]
    [InlineData("\"file\": \"lyric-5.0.0-dev.20261028-linux-x64.tar.gz\"", "\"file\": \".lyric.tar.gz\"", "is not an archive's file name")]
    [InlineData("\"size\": 1000", "\"size\": 0", "the size 0")]
    [InlineData("4e4c294b", "4E4C294B", "is not a SHA-256 in lower-case hex")]
    [InlineData("\"x86_64-linux-gnu\"", "\"riscv64-linux-gnu\"", "unsupported architecture")]
    [InlineData("5.0.0-dev.20261027+80182e3", "5.0.0-dev.20261028+0b3eb5e", "stands twice")]
    [InlineData("5.0.0-dev.20261028+0b3eb5e", "5.0", "is not a version")]
    [InlineData("\"channel\"", "\"kanal\"", "not a channel index")]
    public void An_index_that_is_not_one_is_refused_before_anything_is_loaded(string from, string to, string message)
    {
        var text = Encoding.UTF8.GetString(Bytes("dev.json"));
        Assert.Contains(from, text);
        var refused = Assert.Throws<InvalidDataException>(() => ChannelIndex.Parse(Encoding.UTF8.GetBytes(text.Replace(from, to))));
        Assert.Contains(message, refused.Message);
    }

    private static string Refused(Action check) => Assert.Throws<SignatureException>(check).Message;

    private sealed class KeyEquality : IEqualityComparer<MinisignKey>
    {
        public bool Equals(MinisignKey? x, MinisignKey? y) => x!.Id.SequenceEqual(y!.Id) && x.Key.SequenceEqual(y.Key);
        public int GetHashCode(MinisignKey key) => 0;
    }
}
