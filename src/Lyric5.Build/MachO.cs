using System.Buffers.Binary;
using System.Text;

namespace Lyric5.Build;

/// <summary>
/// What the build reads of a Mach-O shared library: the name it gives itself
/// (<c>LC_ID_DYLIB</c>), which a binary that links it records and the loader looks for — a library
/// beside the binary is found only by a name that begins with <c>@rpath/</c>. Read from any host,
/// as a build for macOS runs from any host: a 64-bit image, or the first of a universal one.
/// </summary>
public static class MachO
{
    private const uint Magic64 = 0xfeedfacf;
    private const uint FatMagic = 0xcafebabe;
    private const uint IdDylib = 0xd;

    /// <summary>The install name of the library <paramref name="file"/>; <c>null</c> where it is no
    /// 64-bit Mach-O image or names none — the link then says what is wrong with it.</summary>
    public static string? InstallName(string file)
    {
        byte[] bytes;
        try { bytes = File.ReadAllBytes(file); }
        catch (IOException) { return null; }
        var image = bytes.AsSpan();
        // a universal image: its slices' offsets, big-endian; the first slice speaks for it
        if (image.Length >= 28 && BinaryPrimitives.ReadUInt32BigEndian(image) == FatMagic)
        {
            var offset = BinaryPrimitives.ReadUInt32BigEndian(image[16..]);
            if (offset >= image.Length) return null;
            image = image[(int)offset..];
        }
        if (image.Length < 32 || BinaryPrimitives.ReadUInt32LittleEndian(image) != Magic64) return null;
        var commands = BinaryPrimitives.ReadUInt32LittleEndian(image[16..]);
        var at = 32;
        for (var i = 0; i < commands && at + 8 <= image.Length; i++)
        {
            var command = BinaryPrimitives.ReadUInt32LittleEndian(image[at..]);
            var size = (int)BinaryPrimitives.ReadUInt32LittleEndian(image[(at + 4)..]);
            if (size < 8 || at + size > image.Length) return null;
            if (command == IdDylib)
            {
                // dylib_command: cmd, cmdsize, then the name's offset from the command's start
                var name = (int)BinaryPrimitives.ReadUInt32LittleEndian(image[(at + 8)..]);
                if (name >= size) return null;
                var text = image.Slice(at + name, size - name);
                var end = text.IndexOf((byte)0);
                return Encoding.UTF8.GetString(end < 0 ? text : text[..end]);
            }
            at += size;
        }
        return null;
    }
}

/// <summary>
/// What the build reads of an ELF shared library: whether it names itself (<c>DT_SONAME</c>). A
/// link records a library without one by the path it was given — the build's own, in every binary
/// (measured with zig 0.16) —, so such a library is not found beside the binary, and the binary is
/// not the same from another machine. A 64-bit little-endian image, as Tier 1 and 2 have them.
/// </summary>
public static class Elf
{
    private const uint PtDynamic = 2;
    private const long DtNull = 0, DtSoname = 14;

    /// <summary>Whether <paramref name="file"/> names itself; <c>null</c> where it is no 64-bit
    /// little-endian ELF image with a dynamic segment — the link then says what is wrong with it.</summary>
    public static bool? HasSoname(string file)
    {
        byte[] bytes;
        try { bytes = File.ReadAllBytes(file); }
        catch (IOException) { return null; }
        var image = bytes.AsSpan();
        // \x7fELF, ELFCLASS64, ELFDATA2LSB
        if (image.Length < 64 || image[0] != 0x7f || !image[1..4].SequenceEqual("ELF"u8) || image[4] != 2 || image[5] != 1)
            return null;
        var phoff = (long)BinaryPrimitives.ReadUInt64LittleEndian(image[0x20..]);
        var phentsize = BinaryPrimitives.ReadUInt16LittleEndian(image[0x36..]);
        var phnum = BinaryPrimitives.ReadUInt16LittleEndian(image[0x38..]);
        for (var i = 0; i < phnum; i++)
        {
            var header = phoff + (long)i * phentsize;
            if (header + 56 > image.Length) return null;
            var entry = image[(int)header..];
            if (BinaryPrimitives.ReadUInt32LittleEndian(entry) != PtDynamic) continue;
            var offset = (long)BinaryPrimitives.ReadUInt64LittleEndian(entry[8..]);
            var size = (long)BinaryPrimitives.ReadUInt64LittleEndian(entry[32..]);
            if (offset + size > image.Length) return null;
            // Elf64_Dyn: d_tag, d_val — eight bytes each
            for (var at = offset; at + 16 <= offset + size; at += 16)
            {
                var tag = BinaryPrimitives.ReadInt64LittleEndian(image[(int)at..]);
                if (tag == DtSoname) return true;
                if (tag == DtNull) break;
            }
            return false;
        }
        return null;
    }
}
