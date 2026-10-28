using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Lyric5.Toolchain;

/// <summary>
/// Reads the <c>.xz</c> format — the form zig's archives for Linux and macOS come in, and one .NET
/// has no reader for: streams of blocks, each block's filter chain LZMA2 alone (what <c>xz</c>
/// writes without a filter option, and what zig's archives use), its check CRC32, CRC64, SHA-256 or
/// none, the index after the blocks checked against them. A filter it does not know, a check that
/// does not hold, or a stream cut short is refused (<see cref="InvalidDataException"/>); the archive
/// it reads has passed its signature and its hash before, so a refusal here is a bug or a damaged
/// file, never routed around.
/// </summary>
public static class Xz
{
    private static readonly byte[] HeaderMagic = [0xFD, (byte)'7', (byte)'z', (byte)'X', (byte)'Z', 0x00];

    /// <summary>A dictionary larger than this is refused rather than allocated; <c>xz -9</c> takes 64 MiB.</summary>
    private const long MaxDictionary = 1L << 30;

    /// <summary>Decompresses every stream in <paramref name="input"/> into <paramref name="output"/>.</summary>
    /// <exception cref="InvalidDataException">The input is no <c>.xz</c> this reader takes.</exception>
    public static void Decompress(Stream input, Stream output)
    {
        var reader = new Input(input);
        var streams = 0;
        while (true)
        {
            // stream padding: zero bytes in fours between and after the streams
            var padding = 0;
            int first;
            while ((first = reader.TryByte()) == 0) padding++;
            if (first < 0)
            {
                if (streams == 0) throw new InvalidDataException("no xz stream: the input is empty");
                if (padding % 4 != 0) throw new InvalidDataException("the stream padding is not a multiple of four bytes");
                return;
            }
            if (streams > 0 && padding % 4 != 0) throw new InvalidDataException("the stream padding is not a multiple of four bytes");
            DecompressStream(reader, (byte)first, output);
            streams++;
        }
    }

    private static void DecompressStream(Input input, byte first, Stream output)
    {
        Span<byte> header = stackalloc byte[12];
        header[0] = first;
        input.Read(header[1..]);
        if (!header[..6].SequenceEqual(HeaderMagic)) throw new InvalidDataException("no xz stream: the magic bytes are missing");
        if (header[6] != 0 || header[7] > 0x0F) throw new InvalidDataException("the stream flags are not known");
        if (Crc32.Of(header.Slice(6, 2)) != BinaryPrimitives.ReadUInt32LittleEndian(header[8..]))
            throw new InvalidDataException("the stream header's CRC32 does not hold");
        var check = header[7];
        var checkSize = CheckSize(check);

        var records = new List<(long Unpadded, long Uncompressed)>();
        while (true)
        {
            var start = input.Position;
            var size = input.Byte();
            if (size == 0) break; // the index indicator
            records.Add(DecompressBlock(input, start, (size + 1) * 4, check, checkSize, output));
        }

        // the index: its records against the blocks just read
        var indexStart = input.Position - 1;
        var crc = new Crc32();
        crc.Append(0);
        long Vli()
        {
            var value = input.Vli(out var bytes);
            crc.Append(bytes);
            return value;
        }
        if (Vli() != records.Count) throw new InvalidDataException("the index counts another number of blocks");
        foreach (var (unpadded, uncompressed) in records)
            if (Vli() != unpadded || Vli() != uncompressed) throw new InvalidDataException("the index gives a block other sizes");
        while ((input.Position - indexStart) % 4 != 0)
        {
            if (input.Byte() != 0) throw new InvalidDataException("the index padding is not zero");
            crc.Append(0);
        }
        Span<byte> stored = stackalloc byte[4];
        input.Read(stored);
        if (crc.Value != BinaryPrimitives.ReadUInt32LittleEndian(stored)) throw new InvalidDataException("the index's CRC32 does not hold");
        var indexSize = input.Position - indexStart;

        Span<byte> footer = stackalloc byte[12];
        input.Read(footer);
        if (footer[10] != (byte)'Y' || footer[11] != (byte)'Z') throw new InvalidDataException("the stream footer's magic bytes are missing");
        if (Crc32.Of(footer.Slice(4, 6)) != BinaryPrimitives.ReadUInt32LittleEndian(footer))
            throw new InvalidDataException("the stream footer's CRC32 does not hold");
        if (footer[8] != header[6] || footer[9] != header[7]) throw new InvalidDataException("the stream footer's flags are not the header's");
        if ((BinaryPrimitives.ReadUInt32LittleEndian(footer[4..]) + 1L) * 4 != indexSize)
            throw new InvalidDataException("the stream footer gives the index another size");
    }

    /// <summary>One block, its header's first byte read: decoded into <paramref name="output"/>,
    /// its check compared; its unpadded and its uncompressed size, for the index.</summary>
    private static (long, long) DecompressBlock(Input input, long start, int headerSize, byte check, int checkSize, Stream output)
    {
        var header = new byte[headerSize];
        header[0] = (byte)(headerSize / 4 - 1);
        input.Read(header.AsSpan(1));
        if (Crc32.Of(header.AsSpan(0, headerSize - 4)) != BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(headerSize - 4)))
            throw new InvalidDataException("a block header's CRC32 does not hold");
        var flags = header[1];
        if ((flags & 0x3C) != 0) throw new InvalidDataException("a block header sets a reserved flag");
        var at = 2;
        long? compressedSize = (flags & 0x40) != 0 ? HeaderVli(header, ref at) : null;
        long? uncompressedSize = (flags & 0x80) != 0 ? HeaderVli(header, ref at) : null;
        if ((flags & 0x03) != 0) throw new InvalidDataException("a block filters through more than LZMA2, which this reader does not take");
        var filter = HeaderVli(header, ref at);
        if (filter != 0x21) throw new InvalidDataException($"a block's filter is 0x{filter:x}, and this reader takes LZMA2 (0x21) alone");
        if (HeaderVli(header, ref at) != 1 || at >= headerSize - 4) throw new InvalidDataException("LZMA2's properties are one byte");
        var dictionary = Lzma2.DictionarySize(header[at++]);
        while (at < headerSize - 4)
            if (header[at++] != 0) throw new InvalidDataException("a block header's padding is not zero");

        var hash = CheckHash.For(check);
        var sink = new CheckedOutput(output, hash);
        var dataStart = input.Position;
        new Lzma2(input, sink, dictionary).Run();
        var compressed = input.Position - dataStart;
        if (compressedSize is { } c && c != compressed) throw new InvalidDataException("a block is not the compressed size its header gives");
        if (uncompressedSize is { } u && u != sink.Count) throw new InvalidDataException("a block is not the uncompressed size its header gives");
        while ((input.Position - dataStart) % 4 != 0)
            if (input.Byte() != 0) throw new InvalidDataException("a block's padding is not zero");
        var stored = new byte[checkSize];
        input.Read(stored);
        if (hash is not null && !hash.Finish().AsSpan(0, checkSize).SequenceEqual(stored))
            throw new InvalidDataException("a block's check does not hold");
        return (dataStart - start + compressed + checkSize, sink.Count);
    }

    private static long HeaderVli(byte[] header, ref int at)
    {
        long value = 0;
        for (var i = 0; i < 9; i++)
        {
            if (at >= header.Length - 4) throw new InvalidDataException("a block header ends inside a number");
            var b = header[at++];
            value |= (long)(b & 0x7F) << (7 * i);
            if ((b & 0x80) == 0)
            {
                if (b == 0 && i > 0) throw new InvalidDataException("a number is written longer than it is");
                return value;
            }
        }
        throw new InvalidDataException("a number is longer than nine bytes");
    }

    private static int CheckSize(byte check) => check switch
    {
        0 => 0,
        <= 3 => 4,
        <= 6 => 8,
        <= 9 => 16,
        <= 12 => 32,
        _ => 64,
    };

    /// <summary>The check of a block's uncompressed data, by the stream's check ID; <c>null</c>
    /// for none. An ID xz has not defined has its size and is refused.</summary>
    private abstract class CheckHash
    {
        public abstract void Append(ReadOnlySpan<byte> data);
        public abstract byte[] Finish();

        public static CheckHash? For(byte check) => check switch
        {
            0x00 => null,
            0x01 => new Crc32Check(),
            0x04 => new Crc64Check(),
            0x0A => new Sha256Check(),
            _ => throw new InvalidDataException($"the stream's check 0x{check:x} is not one this reader knows"),
        };

        private sealed class Crc32Check : CheckHash
        {
            private readonly Crc32 _crc = new();
            public override void Append(ReadOnlySpan<byte> data) => _crc.Append(data);
            public override byte[] Finish()
            {
                var bytes = new byte[4];
                BinaryPrimitives.WriteUInt32LittleEndian(bytes, _crc.Value);
                return bytes;
            }
        }

        private sealed class Crc64Check : CheckHash
        {
            private readonly Crc64 _crc = new();
            public override void Append(ReadOnlySpan<byte> data) => _crc.Append(data);
            public override byte[] Finish()
            {
                var bytes = new byte[8];
                BinaryPrimitives.WriteUInt64LittleEndian(bytes, _crc.Value);
                return bytes;
            }
        }

        private sealed class Sha256Check : CheckHash
        {
            private readonly IncrementalHash _sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            public override void Append(ReadOnlySpan<byte> data) => _sha.AppendData(data);
            public override byte[] Finish() => _sha.GetHashAndReset();
        }
    }

    /// <summary>The output of a block: written on, counted, and its check fed.</summary>
    private sealed class CheckedOutput(Stream output, CheckHash? hash)
    {
        public long Count { get; private set; }

        public void Write(ReadOnlySpan<byte> data)
        {
            output.Write(data);
            hash?.Append(data);
            Count += data.Length;
        }
    }

    /// <summary>The compressed input, buffered, with its position for the paddings and sizes.</summary>
    private sealed class Input(Stream stream)
    {
        private readonly byte[] _buffer = new byte[1 << 16];
        private int _at;
        private int _end;
        private long _before;

        public long Position => _before + _at;

        public int TryByte()
        {
            if (_at == _end && !Fill()) return -1;
            return _buffer[_at++];
        }

        public byte Byte()
        {
            if (_at == _end && !Fill()) throw new InvalidDataException("the xz stream ends early");
            return _buffer[_at++];
        }

        public void Read(Span<byte> into)
        {
            for (var i = 0; i < into.Length; i++) into[i] = Byte();
        }

        public long Vli(out byte[] bytes)
        {
            var read = new List<byte>(2);
            long value = 0;
            for (var i = 0; i < 9; i++)
            {
                var b = Byte();
                read.Add(b);
                value |= (long)(b & 0x7F) << (7 * i);
                if ((b & 0x80) == 0)
                {
                    if (b == 0 && i > 0) throw new InvalidDataException("a number is written longer than it is");
                    bytes = [.. read];
                    return value;
                }
            }
            throw new InvalidDataException("a number is longer than nine bytes");
        }

        private bool Fill()
        {
            _before += _end;
            _at = 0;
            _end = stream.Read(_buffer);
            return _end > 0;
        }
    }

    /// <summary>
    /// LZMA2: chunks of LZMA data or of bytes stored as they are, each chunk saying whether it
    /// resets the dictionary, the coder's state or its properties. The LZMA coder within it is the
    /// one of Igor Pavlov's specification (<c>lzma-specification.txt</c> of the LZMA SDK), with a
    /// fresh range coder per chunk and no end marker.
    /// </summary>
    private sealed class Lzma2(Input input, CheckedOutput output, long dictionarySize)
    {
        private readonly Window _window = new(dictionarySize, output);
        private readonly Coder _coder = new();
        private bool _needDictionaryReset = true;
        private bool _needProperties = true;

        public static long DictionarySize(byte properties)
        {
            var bits = properties & 0x3F;
            if (bits > 40 || (properties & 0xC0) != 0) throw new InvalidDataException("LZMA2's dictionary size is not one it defines");
            var size = bits == 40 ? 0xFFFFFFFFL : (2L | (bits & 1)) << (bits / 2 + 11);
            if (size > MaxDictionary) throw new InvalidDataException($"LZMA2's dictionary of {size} bytes is larger than this reader takes");
            return size;
        }

        public void Run()
        {
            while (true)
            {
                var control = input.Byte();
                if (control == 0x00) return;
                if (control == 0x01 || control >= 0xE0)
                {
                    _needProperties = true;
                    _needDictionaryReset = false;
                    _window.Reset();
                }
                else if (_needDictionaryReset) throw new InvalidDataException("the first LZMA2 chunk does not reset the dictionary");

                if (control >= 0x80)
                {
                    var unpacked = ((control & 0x1F) << 16) + (input.Byte() << 8) + input.Byte() + 1;
                    var packed = (input.Byte() << 8) + input.Byte() + 1;
                    if (control >= 0xC0)
                    {
                        _needProperties = false;
                        _coder.SetProperties(input.Byte());
                        _coder.Reset();
                    }
                    else if (_needProperties) throw new InvalidDataException("an LZMA2 chunk comes before the properties it needs");
                    else if (control >= 0xA0) _coder.Reset();
                    var start = input.Position;
                    _coder.Decode(input, _window, unpacked);
                    if (input.Position - start != packed) throw new InvalidDataException("an LZMA2 chunk is not the compressed size it gives");
                }
                else
                {
                    if (control > 0x02) throw new InvalidDataException($"0x{control:x2} is no LZMA2 chunk");
                    var size = (input.Byte() << 8) + input.Byte() + 1;
                    for (var i = 0; i < size; i++) _window.Put(input.Byte());
                }
                _window.Flush();
            }
        }
    }

    /// <summary>The dictionary: the last bytes written, as far back as a match may reach, flushed
    /// to the output as it fills.</summary>
    private sealed class Window(long size, CheckedOutput output)
    {
        private readonly byte[] _buffer = new byte[size];
        private int _pos;
        private int _flushed;
        private bool _full;

        /// <summary>The bytes written since the dictionary was last reset.</summary>
        public long Total { get; private set; }

        public void Reset()
        {
            Flush();
            _pos = 0;
            _flushed = 0;
            _full = false;
            Total = 0;
        }

        public bool IsEmpty => Total == 0;

        /// <summary>Whether a match <paramref name="distance"/> back (0 for the last byte) reaches
        /// written bytes.</summary>
        public bool Reaches(uint distance) => distance < (_full ? _buffer.Length : _pos);

        public byte Get(uint distance)
        {
            var at = _pos - (long)distance - 1;
            if (at < 0) at += _buffer.Length;
            return _buffer[at];
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public void Put(byte b)
        {
            _buffer[_pos++] = b;
            Total++;
            if (_pos == _buffer.Length) Wrap();
        }

        private void Wrap()
        {
            output.Write(_buffer.AsSpan(_flushed, _pos - _flushed));
            _pos = 0;
            _flushed = 0;
            _full = true;
        }

        public void Copy(uint distance, int length)
        {
            var from = _pos - (int)distance - 1;
            // the common case: neither end wraps — copied in place, byte by byte where the ends overlap
            if (from >= 0 && _pos + length < _buffer.Length)
            {
                if (from + length <= _pos) _buffer.AsSpan(from, length).CopyTo(_buffer.AsSpan(_pos));
                else for (var i = 0; i < length; i++) _buffer[_pos + i] = _buffer[from + i];
                _pos += length;
                Total += length;
                return;
            }
            for (var i = 0; i < length; i++) Put(Get(distance));
        }

        public void Flush()
        {
            if (_pos > _flushed) output.Write(_buffer.AsSpan(_flushed, _pos - _flushed));
            _flushed = _pos;
        }
    }

    /// <summary>The range decoder, fresh for each LZMA2 chunk.</summary>
    private struct Range
    {
        private const uint Top = 1u << 24;
        private Input _input;
        private uint _range;
        private uint _code;

        public void Start(Input input)
        {
            _input = input;
            if (input.Byte() != 0) throw new InvalidDataException("an LZMA2 chunk's range coder does not start with zero");
            _range = 0xFFFFFFFF;
            _code = 0;
            for (var i = 0; i < 4; i++) _code = (_code << 8) | input.Byte();
            if (_code == _range) throw new InvalidDataException("an LZMA2 chunk's range coder starts damaged");
        }

        /// <summary>Whether the chunk's last bit has been read with nothing left over.</summary>
        public readonly bool Finished => _code == 0;

        private void Normalize()
        {
            if (_range >= Top) return;
            _range <<= 8;
            _code = (_code << 8) | _input.Byte();
        }

        public int Bit(ref ushort probability)
        {
            var bound = (_range >> 11) * probability;
            int bit;
            if (_code < bound)
            {
                probability += (ushort)((2048 - probability) >> 5);
                _range = bound;
                bit = 0;
            }
            else
            {
                probability -= (ushort)(probability >> 5);
                _code -= bound;
                _range -= bound;
                bit = 1;
            }
            Normalize();
            return bit;
        }

        public uint Direct(int count)
        {
            uint result = 0;
            for (var i = 0; i < count; i++)
            {
                _range >>= 1;
                _code -= _range;
                var t = 0u - (_code >> 31);
                _code += _range & t;
                if (_code == _range) throw new InvalidDataException("an LZMA2 chunk's range coder is damaged");
                Normalize();
                result = (result << 1) + (t + 1);
            }
            return result;
        }

        public int Tree(ushort[] probabilities, int offset, int bits)
        {
            var m = 1;
            for (var i = 0; i < bits; i++) m = (m << 1) + Bit(ref probabilities[offset + m]);
            return m - (1 << bits);
        }

        public int ReverseTree(ushort[] probabilities, int offset, int bits)
        {
            var m = 1;
            var symbol = 0;
            for (var i = 0; i < bits; i++)
            {
                var bit = Bit(ref probabilities[offset + m]);
                m = (m << 1) + bit;
                symbol |= bit << i;
            }
            return symbol;
        }
    }

    /// <summary>A length: 2 to 273, in three ranges by two choice bits.</summary>
    private sealed class Length
    {
        private readonly ushort[] _choice = new ushort[2];
        private readonly ushort[] _low = new ushort[16 << 3];
        private readonly ushort[] _mid = new ushort[16 << 3];
        private readonly ushort[] _high = new ushort[1 << 8];

        public void Reset()
        {
            Array.Fill(_choice, Coder.Half);
            Array.Fill(_low, Coder.Half);
            Array.Fill(_mid, Coder.Half);
            Array.Fill(_high, Coder.Half);
        }

        public int Decode(ref Range range, int posState)
        {
            if (range.Bit(ref _choice[0]) == 0) return range.Tree(_low, posState << 3, 3);
            if (range.Bit(ref _choice[1]) == 0) return 8 + range.Tree(_mid, posState << 3, 3);
            return 16 + range.Tree(_high, 0, 8);
        }
    }

    /// <summary>The LZMA coder's probabilities, state and last four distances, kept across chunks
    /// that do not reset them.</summary>
    private sealed class Coder
    {
        public const ushort Half = 1024;
        private const int States = 12;
        private const int EndPosModelIndex = 14;
        private const int FullDistances = 128;
        private const int AlignBits = 4;

        private int _lc, _lp, _pb;
        private ushort[] _literals = [];
        private readonly ushort[] _isMatch = new ushort[States << 4];
        private readonly ushort[] _isRep = new ushort[States];
        private readonly ushort[] _isRepG0 = new ushort[States];
        private readonly ushort[] _isRepG1 = new ushort[States];
        private readonly ushort[] _isRepG2 = new ushort[States];
        private readonly ushort[] _isRep0Long = new ushort[States << 4];
        private readonly ushort[] _posSlot = new ushort[4 << 6];
        private readonly ushort[] _posDecoders = new ushort[1 + FullDistances - EndPosModelIndex];
        private readonly ushort[] _align = new ushort[1 << AlignBits];
        private readonly Length _length = new();
        private readonly Length _repLength = new();
        private int _state;
        private uint _rep0, _rep1, _rep2, _rep3;
        private Range _range;

        public void SetProperties(byte properties)
        {
            if (properties >= 9 * 5 * 5) throw new InvalidDataException("LZMA's properties are not ones it defines");
            _lc = properties % 9;
            _lp = properties / 9 % 5;
            _pb = properties / 45;
            if (_lc + _lp > 4) throw new InvalidDataException("LZMA2 takes lc + lp up to 4");
            _literals = new ushort[0x300 << (_lc + _lp)];
        }

        public void Reset()
        {
            Array.Fill(_literals, Half);
            Array.Fill(_isMatch, Half);
            Array.Fill(_isRep, Half);
            Array.Fill(_isRepG0, Half);
            Array.Fill(_isRepG1, Half);
            Array.Fill(_isRepG2, Half);
            Array.Fill(_isRep0Long, Half);
            Array.Fill(_posSlot, Half);
            Array.Fill(_posDecoders, Half);
            Array.Fill(_align, Half);
            _length.Reset();
            _repLength.Reset();
            _state = 0;
            _rep0 = _rep1 = _rep2 = _rep3 = 0;
        }

        /// <summary>One chunk's <paramref name="unpacked"/> bytes into the window.</summary>
        public void Decode(Input input, Window window, int unpacked)
        {
            _range.Start(input);
            var pbMask = (1 << _pb) - 1;
            var lpMask = (1 << _lp) - 1;
            var left = unpacked;
            while (left > 0)
            {
                var posState = (int)(window.Total & pbMask);
                if (_range.Bit(ref _isMatch[(_state << 4) + posState]) == 0)
                {
                    Literal(window, lpMask);
                    _state = _state < 4 ? 0 : _state < 10 ? _state - 3 : _state - 6;
                    left--;
                    continue;
                }

                int length;
                if (_range.Bit(ref _isRep[_state]) != 0)
                {
                    if (window.IsEmpty) throw new InvalidDataException("an LZMA2 chunk repeats a match before any byte");
                    if (_range.Bit(ref _isRepG0[_state]) == 0)
                    {
                        if (_range.Bit(ref _isRep0Long[(_state << 4) + posState]) == 0)
                        {
                            if (!window.Reaches(_rep0)) throw new InvalidDataException("an LZMA2 chunk's match reaches before the dictionary");
                            _state = _state < 7 ? 9 : 11;
                            window.Put(window.Get(_rep0));
                            left--;
                            continue;
                        }
                    }
                    else
                    {
                        uint distance;
                        if (_range.Bit(ref _isRepG1[_state]) == 0) distance = _rep1;
                        else
                        {
                            if (_range.Bit(ref _isRepG2[_state]) == 0) distance = _rep2;
                            else
                            {
                                distance = _rep3;
                                _rep3 = _rep2;
                            }
                            _rep2 = _rep1;
                        }
                        _rep1 = _rep0;
                        _rep0 = distance;
                    }
                    length = _repLength.Decode(ref _range, posState);
                    _state = _state < 7 ? 8 : 11;
                }
                else
                {
                    _rep3 = _rep2;
                    _rep2 = _rep1;
                    _rep1 = _rep0;
                    length = _length.Decode(ref _range, posState);
                    _state = _state < 7 ? 7 : 10;
                    _rep0 = Distance(length);
                    if (_rep0 == 0xFFFFFFFF) throw new InvalidDataException("an LZMA2 chunk carries an end marker");
                }
                if (!window.Reaches(_rep0)) throw new InvalidDataException("an LZMA2 chunk's match reaches before the dictionary");
                length += 2;
                if (length > left) throw new InvalidDataException("an LZMA2 chunk's match runs past the chunk");
                window.Copy(_rep0, length);
                left -= length;
            }
            if (!_range.Finished) throw new InvalidDataException("an LZMA2 chunk's range coder does not end clean");
        }

        private void Literal(Window window, int lpMask)
        {
            var previous = window.IsEmpty ? 0 : window.Get(0);
            var offset = 0x300 * (int)((((int)window.Total & lpMask) << _lc) + (previous >> (8 - _lc)));
            var symbol = 1;
            if (_state >= 7)
            {
                int match = window.Get(_rep0);
                do
                {
                    var matchBit = (match >> 7) & 1;
                    match <<= 1;
                    var bit = _range.Bit(ref _literals[offset + ((1 + matchBit) << 8) + symbol]);
                    symbol = (symbol << 1) | bit;
                    if (matchBit != bit) break;
                }
                while (symbol < 0x100);
            }
            while (symbol < 0x100) symbol = (symbol << 1) | _range.Bit(ref _literals[offset + symbol]);
            window.Put((byte)(symbol - 0x100));
        }

        private uint Distance(int length)
        {
            var lengthState = Math.Min(length, 3);
            var slot = _range.Tree(_posSlot, lengthState << 6, 6);
            if (slot < 4) return (uint)slot;
            var direct = (slot >> 1) - 1;
            var distance = (uint)((2 | (slot & 1)) << direct);
            if (slot < EndPosModelIndex) return distance + (uint)_range.ReverseTree(_posDecoders, (int)distance - slot, direct);
            distance += _range.Direct(direct - AlignBits) << AlignBits;
            return distance + (uint)_range.ReverseTree(_align, 0, AlignBits);
        }
    }

    /// <summary>CRC-32 (IEEE 802.3, reflected), as xz checks its headers and, by choice, its data.</summary>
    private sealed class Crc32
    {
        private static readonly uint[] Table = MakeTable();
        private uint _crc = 0xFFFFFFFF;

        public uint Value => ~_crc;

        public void Append(byte b) => _crc = Table[(_crc ^ b) & 0xFF] ^ (_crc >> 8);

        public void Append(ReadOnlySpan<byte> data)
        {
            foreach (var b in data) Append(b);
        }

        public static uint Of(ReadOnlySpan<byte> data)
        {
            var crc = new Crc32();
            crc.Append(data);
            return crc.Value;
        }

        private static uint[] MakeTable()
        {
            var table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                var c = i;
                for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
                table[i] = c;
            }
            return table;
        }
    }

    /// <summary>CRC-64 (ECMA-182, reflected), xz's default check.</summary>
    private sealed class Crc64
    {
        private static readonly ulong[] Table = MakeTable();
        private ulong _crc = ulong.MaxValue;

        public ulong Value => ~_crc;

        public void Append(ReadOnlySpan<byte> data)
        {
            var crc = _crc;
            foreach (var b in data) crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
            _crc = crc;
        }

        private static ulong[] MakeTable()
        {
            var table = new ulong[256];
            for (ulong i = 0; i < 256; i++)
            {
                var c = i;
                for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xC96C5795D7870F42 ^ (c >> 1) : c >> 1;
                table[i] = c;
            }
            return table;
        }
    }
}
