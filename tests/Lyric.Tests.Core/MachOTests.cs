using System.Buffers.Binary;
using Lyric.Core;

namespace Lyric.Tests.Core;

/// <summary>
/// The two header edits a Mach-O needs before a payload may follow it (#54).
///
/// <para>Whether the RESULT runs is a question only macOS answers, and the pack-and-run gate in
/// both workflows asks it there. What is decidable here is the arithmetic: which command is
/// removed, what the counts become afterwards, and that <c>__LINKEDIT</c> ends where the file
/// does — the three things that make the file structurally valid, without which no signature can
/// be written over it at all.</para>
///
/// <para>The fixture is synthesized rather than checked in: a real stub is 70 MB, and every field
/// that matters here is one this test writes itself, which is also what makes a wrong expectation
/// visible.</para>
/// </summary>
public class MachOTests
{
    private const uint MagicLittle64 = 0xFEEDFACF;
    private const uint LcSegment64 = 0x19;
    private const uint LcCodeSignature = 0x1D;

    private const int HeaderSize = 32;
    private const int SegmentCommandSize = 72;   // segment_command_64 without sections
    private const int SignatureCommandSize = 16; // linkedit_data_command

    /// <summary>
    /// A minimal signed Mach-O: header, one <c>__TEXT</c> segment, one <c>__LINKEDIT</c>, and a
    /// code signature at the end of the file.
    /// </summary>
    private static MemoryStream Fixture(int payloadInLinkEdit = 64, int signatureSize = 48)
    {
        var linkEditFileOff = HeaderSize + SegmentCommandSize * 2 + SignatureCommandSize;
        var signatureOffset = linkEditFileOff + payloadInLinkEdit;
        var total = signatureOffset + signatureSize;

        var bytes = new byte[total];
        var span = bytes.AsSpan();

        BinaryPrimitives.WriteUInt32LittleEndian(span, MagicLittle64);
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], 0x0100000C);   // cputype arm64
        BinaryPrimitives.WriteUInt32LittleEndian(span[12..], 2);           // MH_EXECUTE
        BinaryPrimitives.WriteUInt32LittleEndian(span[16..], 3);           // ncmds
        BinaryPrimitives.WriteUInt32LittleEndian(span[20..],
            (uint)(SegmentCommandSize * 2 + SignatureCommandSize));        // sizeofcmds

        var text = HeaderSize;
        WriteSegment(span, text, "__TEXT", vmSize: 0x4000, fileOff: 0, fileSize: (ulong)linkEditFileOff);

        var linkEdit = text + SegmentCommandSize;
        WriteSegment(span, linkEdit, "__LINKEDIT", vmSize: 0x4000,
            fileOff: (ulong)linkEditFileOff, fileSize: (ulong)(payloadInLinkEdit + signatureSize));

        var signature = linkEdit + SegmentCommandSize;
        BinaryPrimitives.WriteUInt32LittleEndian(span[signature..], LcCodeSignature);
        BinaryPrimitives.WriteUInt32LittleEndian(span[(signature + 4)..], SignatureCommandSize);
        BinaryPrimitives.WriteUInt32LittleEndian(span[(signature + 8)..], (uint)signatureOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(span[(signature + 12)..], (uint)signatureSize);

        var stream = new MemoryStream();
        stream.Write(bytes);
        stream.Position = 0;
        return stream;
    }

    private static void WriteSegment(Span<byte> span, int offset, string name, ulong vmSize,
        ulong fileOff, ulong fileSize)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(span[offset..], LcSegment64);
        BinaryPrimitives.WriteUInt32LittleEndian(span[(offset + 4)..], SegmentCommandSize);
        System.Text.Encoding.ASCII.GetBytes(name).CopyTo(span[(offset + 8)..]);
        BinaryPrimitives.WriteUInt64LittleEndian(span[(offset + 24)..], 0x1000);      // vmaddr
        BinaryPrimitives.WriteUInt64LittleEndian(span[(offset + 32)..], vmSize);
        BinaryPrimitives.WriteUInt64LittleEndian(span[(offset + 40)..], fileOff);
        BinaryPrimitives.WriteUInt64LittleEndian(span[(offset + 48)..], fileSize);
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

    [Fact]
    public void A_mach_o_is_recognized_and_a_fat_binary_is_refused_by_name()
    {
        using var thin = Fixture();
        Assert.True(MachO.Looks(thin, out var noRefusal));
        Assert.Null(noRefusal);

        using var fat = new MemoryStream();
        fat.Write(new byte[64]);
        fat.Position = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(fat.GetBuffer(), 0xCAFEBABE);

        Assert.False(MachO.Looks(fat, out var refusal));
        Assert.Contains("universal", refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void The_signature_offset_is_where_the_payload_goes()
    {
        using var file = Fixture(payloadInLinkEdit: 64, signatureSize: 48);

        // The stub's own signature is about to be replaced, so its bytes are the natural place
        // for the payload — and the file stays as tight as it was.
        Assert.Equal(HeaderSize + SegmentCommandSize * 2 + SignatureCommandSize + 64,
            MachO.SignatureOffset(file));
    }

    [Fact]
    public void Removing_the_signature_command_corrects_the_counts()
    {
        using var file = Fixture();

        MachO.RemoveSignatureCommand(file);

        Assert.Equal(2u, ReadU32(file, 16));                                     // ncmds
        Assert.Equal((uint)(SegmentCommandSize * 2), ReadU32(file, 20));         // sizeofcmds
        Assert.Null(MachO.SignatureOffset(file));

        // The bytes the command left behind are zeroed rather than left as a stale copy of it.
        var freed = HeaderSize + SegmentCommandSize * 2;
        for (var i = 0; i < SignatureCommandSize; i++)
            Assert.Equal(0, file.GetBuffer()[freed + i]);
    }

    [Fact]
    public void The_link_edit_segment_is_grown_to_the_end_of_the_file()
    {
        using var file = Fixture(payloadInLinkEdit: 64, signatureSize: 48);
        var linkEditCommand = HeaderSize + SegmentCommandSize;
        var linkEditFileOff = (long)ReadU64(file, linkEditCommand + 40);

        // Pack: drop the signature, append a payload, then let the segment cover it.
        file.SetLength(MachO.SignatureOffset(file)!.Value);
        MachO.RemoveSignatureCommand(file);
        file.Seek(0, SeekOrigin.End);
        file.Write(new byte[500]);

        MachO.GrowLinkEditToEnd(file);

        Assert.Equal((ulong)(file.Length - linkEditFileOff), ReadU64(file, linkEditCommand + 48));
        Assert.True(ReadU64(file, linkEditCommand + 32) >= (ulong)(file.Length - linkEditFileOff));
    }

    [Fact]
    public void A_file_that_is_not_a_mach_o_is_simply_not_one()
    {
        using var elf = new MemoryStream(new byte[] { 0x7F, (byte)'E', (byte)'L', (byte)'F', 0, 0, 0, 0 });
        Assert.False(MachO.Looks(elf, out var refusal));
        Assert.Null(refusal);
    }
}
