using System.Buffers.Binary;
using System.Numerics;

namespace Lyric5.Toolchain;

/// <summary>
/// BLAKE2b-512 without a key (RFC 7693): the hash minisign signs in place of a file. .NET has
/// none, and a signature check must not depend on a library each platform may lack. Fed in
/// pieces with <see cref="Append"/>, read once with <see cref="Finish"/>.
/// </summary>
public sealed class Blake2b
{
    public const int Size = 64;
    private const int Block = 128;

    private static readonly ulong[] Iv =
    [
        0x6a09e667f3bcc908, 0xbb67ae8584caa73b, 0x3c6ef372fe94f82b, 0xa54ff53a5f1d36f1,
        0x510e527fade682d1, 0x9b05688c2b3e6c1f, 0x1f83d9abfb41bd6b, 0x5be0cd19137e2179,
    ];

    private static readonly byte[][] Sigma =
    [
        [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15],
        [14, 10, 4, 8, 9, 15, 13, 6, 1, 12, 0, 2, 11, 7, 5, 3],
        [11, 8, 12, 0, 5, 2, 15, 13, 10, 14, 3, 6, 7, 1, 9, 4],
        [7, 9, 3, 1, 13, 12, 11, 14, 2, 6, 5, 10, 4, 0, 15, 8],
        [9, 0, 5, 7, 2, 4, 10, 15, 14, 1, 11, 12, 6, 8, 3, 13],
        [2, 12, 6, 10, 0, 11, 8, 3, 4, 13, 7, 5, 15, 14, 1, 9],
        [12, 5, 1, 15, 14, 13, 4, 10, 0, 7, 6, 3, 9, 2, 8, 11],
        [13, 11, 7, 14, 12, 1, 3, 9, 5, 0, 15, 4, 8, 6, 2, 10],
        [6, 15, 14, 9, 11, 3, 0, 8, 12, 2, 13, 7, 1, 4, 10, 5],
        [10, 2, 8, 4, 7, 6, 1, 5, 15, 11, 9, 14, 3, 12, 13, 0],
        [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15],
        [14, 10, 4, 8, 9, 15, 13, 6, 1, 12, 0, 2, 11, 7, 5, 3],
    ];

    private readonly ulong[] _h = new ulong[8];
    private readonly byte[] _buffer = new byte[Block];
    private int _filled;
    private UInt128 _count;
    private bool _finished;

    public Blake2b()
    {
        Iv.CopyTo(_h, 0);
        // the parameter block: digest length 64, no key, fan-out and depth 1
        _h[0] ^= 0x01010000UL | Size;
    }

    public static byte[] Hash(ReadOnlySpan<byte> data)
    {
        var hash = new Blake2b();
        hash.Append(data);
        return hash.Finish();
    }

    public static byte[] Hash(Stream data)
    {
        var hash = new Blake2b();
        var chunk = new byte[1 << 16];
        int read;
        while ((read = data.Read(chunk)) > 0) hash.Append(chunk.AsSpan(0, read));
        return hash.Finish();
    }

    public void Append(ReadOnlySpan<byte> data)
    {
        if (_finished) throw new InvalidOperationException("the hash is finished");
        while (data.Length > 0)
        {
            // a full buffer is compressed only once more input follows: the last block is marked
            if (_filled == Block)
            {
                _count += Block;
                Compress(_buffer, last: false);
                _filled = 0;
            }
            var take = Math.Min(Block - _filled, data.Length);
            data[..take].CopyTo(_buffer.AsSpan(_filled));
            _filled += take;
            data = data[take..];
        }
    }

    public byte[] Finish()
    {
        if (_finished) throw new InvalidOperationException("the hash is finished");
        _finished = true;
        _count += (UInt128)_filled;
        _buffer.AsSpan(_filled).Clear();
        Compress(_buffer, last: true);
        var digest = new byte[Size];
        for (var i = 0; i < 8; i++) BinaryPrimitives.WriteUInt64LittleEndian(digest.AsSpan(i * 8), _h[i]);
        return digest;
    }

    private void Compress(ReadOnlySpan<byte> block, bool last)
    {
        Span<ulong> m = stackalloc ulong[16];
        for (var i = 0; i < 16; i++) m[i] = BinaryPrimitives.ReadUInt64LittleEndian(block[(i * 8)..]);
        Span<ulong> v = stackalloc ulong[16];
        _h.CopyTo(v);
        Iv.CopyTo(v[8..]);
        v[12] ^= (ulong)_count;
        v[13] ^= (ulong)(_count >> 64);
        if (last) v[14] = ~v[14];
        foreach (var s in Sigma)
        {
            Mix(v, 0, 4, 8, 12, m[s[0]], m[s[1]]);
            Mix(v, 1, 5, 9, 13, m[s[2]], m[s[3]]);
            Mix(v, 2, 6, 10, 14, m[s[4]], m[s[5]]);
            Mix(v, 3, 7, 11, 15, m[s[6]], m[s[7]]);
            Mix(v, 0, 5, 10, 15, m[s[8]], m[s[9]]);
            Mix(v, 1, 6, 11, 12, m[s[10]], m[s[11]]);
            Mix(v, 2, 7, 8, 13, m[s[12]], m[s[13]]);
            Mix(v, 3, 4, 9, 14, m[s[14]], m[s[15]]);
        }
        for (var i = 0; i < 8; i++) _h[i] ^= v[i] ^ v[i + 8];
    }

    private static void Mix(Span<ulong> v, int a, int b, int c, int d, ulong x, ulong y)
    {
        v[a] += v[b] + x;
        v[d] = BitOperations.RotateRight(v[d] ^ v[a], 32);
        v[c] += v[d];
        v[b] = BitOperations.RotateRight(v[b] ^ v[c], 24);
        v[a] += v[b] + y;
        v[d] = BitOperations.RotateRight(v[d] ^ v[a], 16);
        v[c] += v[d];
        v[b] = BitOperations.RotateRight(v[b] ^ v[c], 63);
    }
}
