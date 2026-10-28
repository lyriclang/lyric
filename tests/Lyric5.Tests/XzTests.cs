using System.Security.Cryptography;
using System.Text.Json;
using Lyric5.Toolchain;

namespace Lyric5.Tests;

/// <summary>
/// The <c>.xz</c> reader zig's archives pass through: files made by liblzma and the xz command
/// (<c>xz/make.py</c>) read to the bytes they were made from — each check, LZMA chunks and stored
/// ones, other literal properties, blocks with their sizes, two streams —, and what it refuses: a
/// changed byte anywhere, a file cut short, a filter it does not take.
/// </summary>
public class XzTests
{
    private static readonly string Fixtures = Path.Combine(RuntimeLayout.FindRoot(AppContext.BaseDirectory), "tests", "Lyric5.Tests", "xz");

    private static byte[] Bytes(string name) => File.ReadAllBytes(Path.Combine(Fixtures, name));

    private static byte[] Decompress(byte[] data)
    {
        using var output = new MemoryStream();
        Xz.Decompress(new MemoryStream(data), output);
        return output.ToArray();
    }

    [Fact]
    public void Every_fixture_reads_to_the_bytes_it_was_made_from()
    {
        using var expected = JsonDocument.Parse(File.ReadAllText(Path.Combine(Fixtures, "expected.json")));
        var read = 0;
        foreach (var entry in expected.RootElement.EnumerateObject())
        {
            if (entry.Name.Contains("x86")) continue;
            var plain = Decompress(Bytes(entry.Name));
            Assert.True(entry.Value.GetProperty("size").GetInt32() == plain.Length, $"{entry.Name}: {plain.Length} bytes");
            Assert.True(entry.Value.GetProperty("sha256").GetString() == Convert.ToHexStringLower(SHA256.HashData(plain)), entry.Name);
            read++;
        }
        Assert.Equal(10, read);
    }

    [Fact]
    public void Read_through_a_stream_that_hands_out_one_byte_at_a_time_it_gives_the_same()
    {
        var data = Bytes("mixed-properties.xz");
        using var output = new MemoryStream();
        Xz.Decompress(new Trickle(data), output);
        Assert.Equal(Decompress(data), output.ToArray());
    }

    [Fact]
    public void A_changed_byte_anywhere_is_refused()
    {
        // the stream header, a block header, the compressed data early and late, the check, the
        // index and the footer; for each check the reader verifies
        foreach (var name in new[] { "text-crc64.xz", "noise-crc32.xz", "mixed-sha256.xz", "text-blocks.xz" })
        {
            var data = Bytes(name);
            foreach (var at in new[] { 7, 13, 40, data.Length / 3, data.Length / 2, data.Length - 60, data.Length - 30, data.Length - 14, data.Length - 3 })
            {
                var changed = (byte[])data.Clone();
                changed[at] ^= 0x10;
                Assert.True(Refuses(changed), $"{name}: the byte at {at} changed is read");
            }
        }
    }

    [Fact]
    public void A_file_cut_short_or_with_bytes_after_it_is_refused()
    {
        var data = Bytes("text-crc64.xz");
        foreach (var length in new[] { 0, 6, 12, 100, data.Length / 2, data.Length - 12, data.Length - 1 })
            Assert.True(Refuses(data[..length]), $"cut at {length} is read");
        Assert.True(Refuses([.. data, 1, 2, 3, 4]), "a tail is read");
        Assert.True(Refuses([.. data, 0, 0]), "padding of two bytes is read");
        Assert.Equal(Decompress(data), Decompress([.. data, 0, 0, 0, 0]));
    }

    [Fact]
    public void A_filter_before_LZMA2_is_refused_by_its_name()
    {
        var refused = Assert.Throws<InvalidDataException>(() => Decompress(Bytes("small-x86.xz")));
        Assert.Contains("filter", refused.Message);
    }

    private static bool Refuses(byte[] data)
    {
        try
        {
            Decompress(data);
            return false;
        }
        catch (InvalidDataException)
        {
            return true;
        }
    }

    /// <summary>A stream that hands out one byte a read.</summary>
    private sealed class Trickle(byte[] data) : MemoryStream(data)
    {
        public override int Read(Span<byte> buffer) => base.Read(buffer[..Math.Min(1, buffer.Length)]);
        public override int Read(byte[] buffer, int offset, int count) => base.Read(buffer, offset, Math.Min(1, count));
    }
}
