using System.Text;

namespace Lyric5.Toolchain;

/// <summary>A minisign public key: the line of base64 under its comment, the algorithm
/// <c>Ed</c>, an 8-byte key id and the Ed25519 key.</summary>
public sealed record MinisignKey(byte[] Id, byte[] Key)
{
    /// <summary>The key id as minisign prints it: the bytes from the last, in upper-case hex.</summary>
    public string IdText => Convert.ToHexString(Id.Reverse().ToArray());

    /// <summary>A key from a <c>.pub</c> file's text, or from its line of base64 alone.</summary>
    /// <exception cref="SignatureException">It is not a minisign public key.</exception>
    public static MinisignKey Parse(string text)
    {
        var lines = Minisign.Lines(text);
        var line = lines.Count > 0 && lines[0].StartsWith("untrusted comment:", StringComparison.Ordinal) ? lines.ElementAtOrDefault(1) : lines.FirstOrDefault();
        var bytes = Minisign.Base64(line, "public key");
        if (bytes.Length != 2 + 8 + Ed25519.KeySize || bytes[0] != 'E' || bytes[1] != 'd')
            throw new SignatureException("not a minisign public key");
        return new MinisignKey(bytes[2..10], bytes[10..]);
    }
}

/// <summary>A signature was refused: its file is malformed, another key made it, or it does not
/// match what it signs.</summary>
public sealed class SignatureException(string message) : Exception(message);

/// <summary>
/// Checks a minisign signature (<c>.minisig</c>): an Ed25519 signature over the BLAKE2b-512 hash
/// of the file (the algorithm <c>ED</c>), and a second one over that signature and the trusted
/// comment, so the comment cannot change either. A signature over the file itself (<c>Ed</c>,
/// minisign's legacy form) is refused — every signer this toolchain checks, its own release
/// pipeline and zig's, signs the hash, and accepting both would let a signature be read as the
/// other kind.
/// </summary>
public static class Minisign
{
    /// <summary>Checks <paramref name="signature"/>, the text of a <c>.minisig</c>, over
    /// <paramref name="data"/>; the trusted comment it carries where it holds.</summary>
    /// <exception cref="SignatureException">The signature does not hold.</exception>
    public static string Verify(MinisignKey key, Stream data, string signature) => VerifyHash(key, Blake2b.Hash(data), signature);

    /// <inheritdoc cref="Verify(MinisignKey, Stream, string)"/>
    public static string Verify(MinisignKey key, ReadOnlySpan<byte> data, string signature) => VerifyHash(key, Blake2b.Hash(data), signature);

    private static string VerifyHash(MinisignKey key, byte[] hash, string text)
    {
        var lines = Lines(text);
        if (lines.Count < 4 || !lines[0].StartsWith("untrusted comment:", StringComparison.Ordinal))
            throw new SignatureException("not a minisign signature: it has no untrusted comment");
        const string Trusted = "trusted comment: ";
        if (!lines[2].StartsWith(Trusted, StringComparison.Ordinal))
            throw new SignatureException("not a minisign signature: it has no trusted comment");
        var signature = Base64(lines[1], "signature");
        if (signature.Length != 2 + 8 + Ed25519.SignatureSize)
            throw new SignatureException("not a minisign signature: the signature line has the wrong length");
        var algorithm = Encoding.ASCII.GetString(signature, 0, 2);
        if (algorithm == "Ed")
            throw new SignatureException("a signature over the file itself (minisign's legacy form); only one over its BLAKE2b hash is accepted");
        if (algorithm != "ED") throw new SignatureException($"not a minisign signature: unknown algorithm '{algorithm}'");

        var id = signature[2..10];
        if (!id.AsSpan().SequenceEqual(key.Id))
            throw new SignatureException($"signed with the key {Convert.ToHexString(id.Reverse().ToArray())}, not with {key.IdText}");
        var bytes = signature[10..];
        if (!Ed25519.Verify(key.Key, hash, bytes)) throw new SignatureException("the signature does not match the file");

        var comment = lines[2][Trusted.Length..];
        var global = Base64(lines[3], "global signature");
        var signed = bytes.Concat(Encoding.UTF8.GetBytes(comment)).ToArray();
        if (global.Length != Ed25519.SignatureSize || !Ed25519.Verify(key.Key, signed, global))
            throw new SignatureException("the trusted comment does not match its signature");
        return comment;
    }

    internal static List<string> Lines(string text) =>
        text.Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.Length > 0).ToList();

    internal static byte[] Base64(string? line, string what)
    {
        try
        {
            return Convert.FromBase64String(line ?? throw new FormatException());
        }
        catch (FormatException)
        {
            throw new SignatureException($"not a minisign {what}: its line is not base64");
        }
    }
}
