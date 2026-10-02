using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Lyric5.Toolchain;

/// <summary>
/// What a linker writes into an image besides the program (design/v5/spec/11 W2 P6). lld stamps a
/// PE image with the time of the link, and pairs it with its PDB by a GUID that hashes the PDB —
/// which names the objects, the working directory and the linker's command line by their paths.
/// zig's Mach-O linker writes a debug map into the image, every object by its path and time, and
/// hashes it into the image's UUID. A PE image takes its stamps and its GUID from its own bytes
/// instead (<see cref="NormalizePe"/>); a Mach-O image is linked without the map, and the dSYM of a
/// second link that has one takes the image's UUID (<see cref="CBuild.LinkExecutable"/>).
/// </summary>
public static class Reproducible
{
    private const uint PeSignature = 0x0000_4550;  // "PE\0\0"
    private const uint DebugCodeView = 2;          // IMAGE_DEBUG_TYPE_CODEVIEW
    private const uint Rsds = 0x5344_5352;         // "RSDS", a PDB 7.0 record
    private const uint MachO64 = 0xFEED_FACF;
    private const uint SegmentCommand = 0x19;      // LC_SEGMENT_64
    private const uint UuidCommand = 0x1B;         // LC_UUID

    /// <summary>The first 32 bytes of an MSF 7.00 file, the container a PDB is.</summary>
    private static ReadOnlySpan<byte> MsfMagic => "Microsoft C/C++ MSF 7.00\r\n\u001aDS\0\0\0"u8;

    /// <summary>
    /// A PE image's time stamps — the COFF header's and each debug directory entry's — and the GUID
    /// of its CodeView record, all from a hash of the image with those fields zeroed: the same
    /// program gives the same image. The PDB beside it gets the same GUID, by which DbgHelp pairs
    /// the two; the PDB keeps the paths it was written with, as a dSYM does.
    /// </summary>
    public static void NormalizePe(string image, string? pdb)
    {
        var bytes = File.ReadAllBytes(image);
        var (stamps, guid) = PeFields(bytes);
        foreach (var at in stamps) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at), 0);
        if (guid is { } cleared) bytes.AsSpan(cleared, 16).Clear();
        var hash = SHA256.HashData(bytes);
        var stamp = BinaryPrimitives.ReadUInt32LittleEndian(hash.AsSpan(16));
        foreach (var at in stamps) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at), stamp);
        if (guid is { } offset) hash.AsSpan(0, 16).CopyTo(bytes.AsSpan(offset));
        File.WriteAllBytes(image, bytes);
        if (guid is not null && pdb is not null && File.Exists(pdb)) SetPdbGuid(pdb, hash.AsSpan(0, 16), stamp);
    }

    /// <summary>Where a PE image keeps its time stamps, and the GUID of its CodeView record if it has one.</summary>
    private static (List<int> Stamps, int? Guid) PeFields(byte[] image)
    {
        if (U16(image, 0) != 0x5A4D) throw new InvalidDataException("not a PE image");  // "MZ"
        var coff = Int(image, 0x3C) + 4;
        if (U32(image, coff - 4) != PeSignature) throw new InvalidDataException("not a PE image");
        var sections = U16(image, coff + 2);
        var optional = coff + 20;
        var table = optional + U16(image, coff + 16);
        // The data directories end the optional header, after their count: at 112 in a PE32+ image.
        var directories = optional + (U16(image, optional) == 0x20B ? 112 : 96);
        var stamps = new List<int> { coff + 4 };
        int? guid = null;
        const int debugDirectory = 6;
        if (Int(image, directories - 4) > debugDirectory)
        {
            var rva = U32(image, directories + debugDirectory * 8);
            var size = Int(image, directories + debugDirectory * 8 + 4);
            var start = size == 0 ? 0 : FileOffset(image, table, sections, rva);
            for (var entry = start; entry + 28 <= start + size; entry += 28)
            {
                stamps.Add(entry + 4);
                if (U32(image, entry + 12) != DebugCodeView) continue;
                var data = Int(image, entry + 24);
                if (U32(image, data) == Rsds) guid = data + 4;
            }
        }
        return (stamps, guid);
    }

    /// <summary>Where in the file a relative virtual address lies: in the section that maps it.</summary>
    private static int FileOffset(byte[] image, int table, int count, uint rva)
    {
        for (var i = 0; i < count; i++)
        {
            var section = table + i * 40;
            var (virtualSize, address) = (U32(image, section + 8), U32(image, section + 12));
            var (rawSize, raw) = (U32(image, section + 16), U32(image, section + 20));
            if (rva >= address && rva - address < Math.Max(virtualSize, rawSize)) return checked((int)(raw + (rva - address)));
        }
        throw new InvalidDataException($"no section of the image maps RVA 0x{rva:x}");
    }

    /// <summary>
    /// The stamp and the GUID of a PDB's info stream, stream 1 of its container. An MSF file is
    /// blocks: the superblock names the block size and the block that lists the blocks of the
    /// stream directory, which holds the number of streams, each stream's size, then each stream's
    /// blocks in order — stream 1's first block right after stream 0's.
    /// </summary>
    private static void SetPdbGuid(string pdb, ReadOnlySpan<byte> guid, uint stamp)
    {
        using var file = new FileStream(pdb, FileMode.Open, FileAccess.ReadWrite);
        var super = Read(file, 0, 56);
        if (!super.AsSpan(0, MsfMagic.Length).SequenceEqual(MsfMagic)) throw new InvalidDataException($"{pdb} is not an MSF 7.00 file");
        var blockSize = Int(super, 32);
        var blocks = (Int(super, 44) + blockSize - 1) / blockSize;
        var map = Read(file, (long)Int(super, 52) * blockSize, blocks * 4);
        var directory = new byte[blocks * blockSize];
        for (var i = 0; i < blocks; i++) Read(file, (long)Int(map, i * 4) * blockSize, blockSize).CopyTo(directory, i * blockSize);
        var streams = Int(directory, 0);
        var firstSize = U32(directory, 4);
        var firstBlocks = firstSize == uint.MaxValue ? 0 : (int)((firstSize + (uint)blockSize - 1) / (uint)blockSize);
        var info = (long)Int(directory, 4 + 4 * streams + 4 * firstBlocks) * blockSize;
        Span<byte> field = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(field, stamp);
        file.Position = info + 4;   // after the version
        file.Write(field);
        file.Position = info + 12;  // after the age
        file.Write(guid);
    }

    private static byte[] Read(FileStream file, long at, int count)
    {
        var buffer = new byte[count];
        file.Position = at;
        file.ReadExactly(buffer);
        return buffer;
    }

    /// <summary>The UUID of a 64-bit Mach-O file (LC_UUID), or <c>null</c> without one.</summary>
    public static byte[]? MachOUuid(string file)
    {
        var header = MachOHeader(file);
        return UuidAt(header) is { } at ? header[at..(at + 16)] : null;
    }

    /// <summary>Gives a 64-bit Mach-O file — a dSYM's DWARF — the UUID of another.</summary>
    public static void SetMachOUuid(string file, ReadOnlySpan<byte> uuid)
    {
        var at = UuidAt(MachOHeader(file)) ?? throw new InvalidDataException($"{file} has no LC_UUID");
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Write);
        stream.Position = at;
        stream.Write(uuid[..16]);
    }

    /// <summary>
    /// Whether two Mach-O images hold the same segments at the same addresses with the same bytes,
    /// <c>__LINKEDIT</c> aside — the symbol table, where a debug map lies, and the code signature:
    /// a dSYM gathered from one then describes the other. The load commands are no part of it; they
    /// name the symbol table's size and the UUID.
    /// </summary>
    public static bool SameSegments(string first, string second)
    {
        var (a, b) = (File.ReadAllBytes(first), File.ReadAllBytes(second));
        var (left, right) = (Segments(a), Segments(b));
        return left.Count == right.Count && left.Zip(right).All(pair =>
            pair.First.Name == pair.Second.Name && pair.First.Address == pair.Second.Address && pair.First.Size == pair.Second.Size
            && a.AsSpan(pair.First.Bytes).SequenceEqual(b.AsSpan(pair.Second.Bytes)));
    }

    private static List<(string Name, ulong Address, ulong Size, Range Bytes)> Segments(byte[] image)
    {
        if (U32(image, 0) != MachO64) throw new InvalidDataException("not a 64-bit Mach-O image");
        var commandsEnd = 32 + Int(image, 20);
        var segments = new List<(string, ulong, ulong, Range)>();
        foreach (var (command, at) in Commands(image))
        {
            if (command != SegmentCommand) continue;
            var name = Encoding.ASCII.GetString(image, at + 8, 16).TrimEnd('\0');
            if (name == "__LINKEDIT") continue;
            var (offset, length) = (checked((int)U64(image, at + 40)), checked((int)U64(image, at + 48)));
            // __TEXT maps the header too: its bytes count from the end of the load commands.
            var from = offset == 0 && length > 0 ? commandsEnd : offset;
            if (offset + length > image.Length || from > offset + length) throw Truncated();
            segments.Add((name, U64(image, at + 24), U64(image, at + 32), from..(offset + length)));
        }
        return segments;
    }

    /// <summary>A 64-bit Mach-O file's header and load commands.</summary>
    private static byte[] MachOHeader(string file)
    {
        using var stream = File.OpenRead(file);
        var head = new byte[32];
        stream.ReadExactly(head);
        if (U32(head, 0) != MachO64) throw new InvalidDataException($"{file} is not a 64-bit Mach-O file");
        var header = new byte[32 + Int(head, 20)];
        head.CopyTo(header, 0);
        stream.ReadExactly(header.AsSpan(32));
        return header;
    }

    /// <summary>A Mach-O file's load commands: each one's kind, and where it starts.</summary>
    private static List<(uint Command, int At)> Commands(byte[] header)
    {
        var end = 32 + Int(header, 20);
        var commands = new List<(uint, int)>();
        for (int i = 0, count = Int(header, 16), at = 32; i < count; i++)
        {
            var size = Int(header, at + 4);
            if (size < 8 || at + size > end) throw Truncated();
            commands.Add((U32(header, at), at));
            at += size;
        }
        return commands;
    }

    private static int? UuidAt(byte[] header) =>
        Commands(header).Where(c => c.Command == UuidCommand).Select(c => (int?)(c.At + 8)).FirstOrDefault();

    private static ushort U16(ReadOnlySpan<byte> data, int at) =>
        at >= 0 && at + 2 <= data.Length ? BinaryPrimitives.ReadUInt16LittleEndian(data[at..]) : throw Truncated();

    private static uint U32(ReadOnlySpan<byte> data, int at) =>
        at >= 0 && at + 4 <= data.Length ? BinaryPrimitives.ReadUInt32LittleEndian(data[at..]) : throw Truncated();

    private static ulong U64(ReadOnlySpan<byte> data, int at) =>
        at >= 0 && at + 8 <= data.Length ? BinaryPrimitives.ReadUInt64LittleEndian(data[at..]) : throw Truncated();

    /// <summary>A field read as an offset or a count: one past int's range is no file this reads.</summary>
    private static int Int(ReadOnlySpan<byte> data, int at) => checked((int)U32(data, at));

    private static InvalidDataException Truncated() => new("the file ends inside a header it declares");
}
