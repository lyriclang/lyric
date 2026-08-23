using System.Buffers.Binary;

namespace Lyric.Core;

/// <summary>Where a packed program lies inside its executable: <see cref="Offset"/> bytes from the
/// start, <see cref="Length"/> bytes long.</summary>
public readonly record struct PackPayload(long Offset, long Length);

/// <summary>What reading the tail of an executable found.</summary>
public enum PackFooterState
{
    /// <summary>No footer: the file is a bare stub, or not a packed program at all.</summary>
    Absent,

    /// <summary>The magic is there but the rest does not hold together — a truncated copy, or a
    /// version this reader does not know.</summary>
    Damaged,

    /// <summary>A payload is present and its bounds lie inside the file.</summary>
    Present,
}

/// <summary>
/// The trailer of a packed executable, specified in <c>docs/Pack.md</c>.
///
/// <para>A packed program is the stub executable with the <c>.lyrbc</c> module appended and this
/// footer after it. The footer is the LAST thing the packer writes, so the stub finds the payload
/// without knowing anything about its own size: read the last <see cref="Size"/> bytes, check the
/// magic, and the length names the payload directly before the footer.</para>
///
/// <para>On macOS something does follow it. A Mach-O has to be signed to run, and a signature is
/// written at the end of the file — after our footer, which is then no longer last. So the search
/// falls back to scanning backwards for the magic. The scan is bounded, because what may follow is
/// a signature rather than anything unbounded, and a candidate has to hold together completely
/// before it is believed.</para>
///
/// <para>It lives in <c>Lyric.Core</c> because writer (<c>lyrpack</c>) and reader
/// (<c>lyrstub</c>) must agree byte for byte, and Core is the one project every binary
/// shares.</para>
/// </summary>
public static class PackFooter
{
    /// <summary>Fixed layout, little-endian: <c>u32 version</c>, <c>u32 reserved</c> (written as
    /// zero, ignored on read), <c>u64 payload length</c>, 8 bytes magic.</summary>
    public const int Size = 24;

    /// <summary>Bumped only when the footer LAYOUT changes. The payload's own format has its own
    /// version inside the <c>.lyrbc</c> header and is none of the footer's business.</summary>
    public const uint Version = 1;

    private static ReadOnlySpan<byte> Magic => "LYRPACK1"u8;

    /// <summary>Appends the footer for a payload of <paramref name="payloadLength"/> bytes to
    /// wherever the stream currently stands.</summary>
    public static void Write(Stream stream, long payloadLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(payloadLength);

        Span<byte> footer = stackalloc byte[Size];
        BinaryPrimitives.WriteUInt32LittleEndian(footer, Version);
        BinaryPrimitives.WriteUInt32LittleEndian(footer[4..], 0);
        BinaryPrimitives.WriteUInt64LittleEndian(footer[8..], (ulong)payloadLength);
        Magic.CopyTo(footer[16..]);
        stream.Write(footer);
    }

    /// <summary>
    /// Reads the footer from the end of <paramref name="stream"/>.
    ///
    /// <para>Absent and damaged are distinct answers: a bare stub is a working executable that
    /// carries no program yet, a damaged pack is a broken copy of one that did. The messages for
    /// the two must not be the same.</para>
    /// </summary>
    /// <summary>How far back the scan looks when the footer is not the last thing in the file.
    ///
    /// <para>What can stand behind it is a code signature and nothing else. One covers the file in
    /// page hashes — 32 bytes per 4 KiB — so even a 100 MB stub signs in well under a megabyte.
    /// The bound is what keeps a file that merely CONTAINS the magic somewhere from being read
    /// end to end.</para></summary>
    private const int TrailerSearchLimit = 4 * 1024 * 1024;

    public static PackFooterState TryRead(Stream stream, out PackPayload payload)
    {
        payload = default;
        if (stream.Length < Size) return PackFooterState.Absent;

        // The footer at the very end: every platform but macOS, and macOS before it is signed.
        var atEnd = At(stream, stream.Length - Size, out payload);
        if (atEnd != PackFooterState.Absent) return atEnd;

        return Behind(stream, out payload);
    }

    /// <summary>Reads a footer at one offset and checks it completely.</summary>
    private static PackFooterState At(Stream stream, long offset, out PackPayload payload)
    {
        payload = default;
        if (offset < 0) return PackFooterState.Absent;

        Span<byte> footer = stackalloc byte[Size];
        stream.Seek(offset, SeekOrigin.Begin);
        stream.ReadExactly(footer);

        if (!footer[16..].SequenceEqual(Magic)) return PackFooterState.Absent;

        var version = BinaryPrimitives.ReadUInt32LittleEndian(footer);
        var length = BinaryPrimitives.ReadUInt64LittleEndian(footer[8..]);

        // An unknown version is damage, not absence: the magic says a payload is there, and
        // running the stub as if it were empty would hide it.
        if (version != Version) return PackFooterState.Damaged;
        if (length == 0 || length > (ulong)offset) return PackFooterState.Damaged;

        payload = new PackPayload(offset - (long)length, (long)length);
        return PackFooterState.Present;
    }

    /// <summary>
    /// The footer with something behind it: a signed Mach-O, where the signature was written after
    /// everything the packer put in.
    ///
    /// <para>Scans backwards for the magic and believes the first candidate that holds together —
    /// the right version, and a payload that fits entirely in front of it. A signature is hashes
    /// and structure; for one to be mistaken for a footer it would have to carry these eight bytes
    /// AND a valid version AND a plausible length, and it is still checked before it is used.</para>
    /// </summary>
    private static PackFooterState Behind(Stream stream, out PackPayload payload)
    {
        payload = default;

        var window = (int)Math.Min(TrailerSearchLimit, stream.Length);
        var start = stream.Length - window;
        var buffer = new byte[window];
        stream.Seek(start, SeekOrigin.Begin);
        stream.ReadExactly(buffer);

        // From the back: the LAST footer in the file is the one this packer wrote.
        for (var i = window - Magic.Length; i >= 0; i--)
        {
            if (!buffer.AsSpan(i, Magic.Length).SequenceEqual(Magic)) continue;

            var state = At(stream, start + i - 16, out payload);
            if (state == PackFooterState.Present) return state;
        }

        return PackFooterState.Absent;
    }
}
