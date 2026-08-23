using System.Buffers.Binary;

namespace Lyric.Core;

/// <summary>
/// The two edits a Mach-O needs before a payload may be appended to it.
///
/// <para><b>Why it needs any.</b> A PE and an ELF do not describe where they end, so bytes after
/// them are simply ignored and a packed program runs. A Mach-O does describe it: every segment
/// names its extent in a load command, and macOS refuses to run a main executable whose file
/// carries anything the load commands do not account for — "main executable failed strict
/// validation". A signature is not optional either; an unsigned or stale-signed executable is
/// killed on launch.</para>
///
/// <para><b>What this does.</b> It drops the stub's existing signature (which our payload would
/// invalidate anyway), removes the load command that pointed at it, and grows <c>__LINKEDIT</c> —
/// the segment that always ends the file — so that its extent reaches the new end. The result is a
/// structurally valid, UNSIGNED Mach-O, which <c>codesign</c> then signs; it appends the new
/// signature and repairs the header itself.</para>
///
/// <para><b>Why it lives in Core.</b> Beside <see cref="PackFooter"/>: both are the pack FORMAT
/// rather than the packer, and a format is testable without starting a process. The stub never
/// calls it — it reads a finished file — but the knowledge belongs where the rest of the layout
/// is written down.</para>
///
/// <para><b>What it deliberately does not do.</b> It writes no signature. Ad-hoc signing is a
/// CodeDirectory of page hashes inside a SuperBlob, and macOS ships the program that produces one
/// correctly — packing for macOS happens on macOS, because the packer only ever finds the stub for
/// its own platform.</para>
/// </summary>
public static class MachO
{
    private const uint MagicLittle64 = 0xFEEDFACF;   // MH_MAGIC_64, this file's byte order
    private const uint MagicBig64 = 0xCFFAEDFE;      // the same, byte-swapped
    private const uint MagicFat = 0xCAFEBABE;        // a universal binary, and its swapped twin
    private const uint MagicFatSwapped = 0xBEBAFECA;

    private const uint LcSegment64 = 0x19;
    private const uint LcCodeSignature = 0x1D;

    private const int HeaderSize = 32;               // mach_header_64
    private const int NcmdsOffset = 16;
    private const int SizeOfCmdsOffset = 20;

    // segment_command_64, from the start of the command:
    //   0 cmd, 4 cmdsize, 8 segname[16], 24 vmaddr, 32 vmsize, 40 fileoff, 48 filesize, …
    // Written out because a field counted wrong here is a header that says the file ends
    // somewhere it does not — which is the whole point of this class.
    private const int SegmentNameOffset = 8;
    private const int SegmentVmSize = 32;
    private const int SegmentFileOff = 40;
    private const int SegmentFileSize = 48;

    /// <summary>Is this file a Mach-O this packer can edit? A universal binary is one too, and is
    /// refused by name rather than silently mangled.</summary>
    public static bool Looks(Stream stream, out string? refusal)
    {
        refusal = null;
        if (stream.Length < HeaderSize) return false;

        stream.Seek(0, SeekOrigin.Begin);
        Span<byte> magic = stackalloc byte[4];
        stream.ReadExactly(magic);
        var value = BinaryPrimitives.ReadUInt32LittleEndian(magic);

        if (value is MagicFat or MagicFatSwapped)
        {
            refusal = "the stub is a universal (fat) binary; this packer edits a single-architecture Mach-O";
            return false;
        }

        if (value == MagicBig64)
        {
            refusal = "the stub is a byte-swapped Mach-O; this packer edits little-endian files";
            return false;
        }

        return value == MagicLittle64;
    }

    /// <summary>Where the existing code signature begins, or <c>null</c> when there is none. The
    /// payload is written there: the signature is about to be replaced, so the bytes it occupies
    /// are the natural place for it, and the file stays as tight as it was.</summary>
    public static long? SignatureOffset(Stream stream)
    {
        foreach (var (cmd, offset, _) in Commands(stream))
        {
            if (cmd != LcCodeSignature) continue;
            return (long)ReadU32(stream, offset + 8);   // linkedit_data_command.dataoff
        }
        return null;
    }

    /// <summary>
    /// Removes the code-signature load command and grows <c>__LINKEDIT</c> to the end of the file.
    ///
    /// <para>The command has to GO rather than be repointed: <c>codesign</c> reads it to find the
    /// signature it is replacing and truncates the file there, which is where the payload now
    /// lies. With no such command it appends a fresh signature at the end and writes the command
    /// back into the header — into the space this removal just freed.</para>
    /// </summary>
    public static void RemoveSignatureCommand(Stream stream)
    {
        var signature = Commands(stream).FirstOrDefault(c => c.Cmd == LcCodeSignature);
        if (signature.Size != 0) RemoveCommand(stream, signature.Offset, signature.Size);
    }

    /// <summary>Grows <c>__LINKEDIT</c> so its extent reaches the end of the file. Called AFTER the
    /// payload is there — it is the payload that has to end up inside the segment.</summary>
    public static void GrowLinkEditToEnd(Stream stream)
    {
        var linkEdit = Commands(stream)
            .Where(c => c.Cmd == LcSegment64)
            .FirstOrDefault(c => SegmentName(stream, c.Offset) == "__LINKEDIT");

        if (linkEdit.Size == 0)
            throw new InvalidOperationException("the stub has no __LINKEDIT segment to grow");

        var fileOff = (long)ReadU64(stream, linkEdit.Offset + SegmentFileOff);
        var wanted = stream.Length - fileOff;

        WriteU64(stream, linkEdit.Offset + SegmentFileSize, (ulong)wanted);
        WriteU64(stream, linkEdit.Offset + SegmentVmSize, (ulong)RoundUpToPage(wanted));
    }

    /// <summary>A segment's vmsize covers whole pages. 16 KiB is the page size of every Mach-O
    /// arm64 macOS runs, and rounding up on x64 (4 KiB pages) is harmless — a segment may be
    /// larger in memory than in the file.</summary>
    private static long RoundUpToPage(long size)
    {
        const long page = 16 * 1024;
        return (size + page - 1) / page * page;
    }

    private static void RemoveCommand(Stream stream, long offset, uint size)
    {
        var ncmds = ReadU32(stream, NcmdsOffset);
        var sizeOfCmds = ReadU32(stream, SizeOfCmdsOffset);
        var commandsEnd = HeaderSize + sizeOfCmds;

        // Everything behind the command slides forward over it, and the bytes it left free at the
        // end are zeroed: a stale copy of a load command there would be read as one by anything
        // that trusts sizeofcmds less than it should.
        var tail = new byte[commandsEnd - (offset + size)];
        stream.Seek(offset + size, SeekOrigin.Begin);
        stream.ReadExactly(tail);

        stream.Seek(offset, SeekOrigin.Begin);
        stream.Write(tail);
        stream.Write(new byte[size]);

        WriteU32(stream, NcmdsOffset, ncmds - 1);
        WriteU32(stream, SizeOfCmdsOffset, sizeOfCmds - size);
    }

    private static IEnumerable<(uint Cmd, long Offset, uint Size)> Commands(Stream stream)
    {
        var ncmds = ReadU32(stream, NcmdsOffset);
        var offset = (long)HeaderSize;

        for (var i = 0; i < ncmds; i++)
        {
            var cmd = ReadU32(stream, offset);
            var size = ReadU32(stream, offset + 4);
            if (size < 8 || offset + size > stream.Length)
                throw new InvalidOperationException($"the stub's load command {i} is malformed");

            yield return (cmd, offset, size);
            offset += size;
        }
    }

    private static string SegmentName(Stream stream, long commandOffset)
    {
        Span<byte> name = stackalloc byte[16];
        stream.Seek(commandOffset + SegmentNameOffset, SeekOrigin.Begin);
        stream.ReadExactly(name);

        var end = name.IndexOf((byte)0);
        return System.Text.Encoding.ASCII.GetString(name[..(end < 0 ? name.Length : end)]);
    }

    private static uint ReadU32(Stream stream, long offset)
    {
        Span<byte> buffer = stackalloc byte[4];
        stream.Seek(offset, SeekOrigin.Begin);
        stream.ReadExactly(buffer);
        return BinaryPrimitives.ReadUInt32LittleEndian(buffer);
    }

    private static ulong ReadU64(Stream stream, long offset)
    {
        Span<byte> buffer = stackalloc byte[8];
        stream.Seek(offset, SeekOrigin.Begin);
        stream.ReadExactly(buffer);
        return BinaryPrimitives.ReadUInt64LittleEndian(buffer);
    }

    private static void WriteU32(Stream stream, long offset, uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        stream.Seek(offset, SeekOrigin.Begin);
        stream.Write(buffer);
    }

    private static void WriteU64(Stream stream, long offset, ulong value)
    {
        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(buffer, value);
        stream.Seek(offset, SeekOrigin.Begin);
        stream.Write(buffer);
    }
}
