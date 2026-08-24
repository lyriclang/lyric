using Lyric.Bytecode;

namespace Lyric.Tests.Bytecode;

/// <summary>
/// The reader's "must reject" catalogue, driven by hand-built modules.
///
/// <para>The round-trip tests hang on the lowering's net, so they can only ever show the reader
/// ACCEPTING what the writer produces. What a validator is for — refusing what no correct writer
/// produces — needs bytes no writer writes, which is what the builder below is for.</para>
/// </summary>
public class ReaderValidationTests
{
    // ------------------------------------------------------------------ byte building

    private sealed class Bytes
    {
        private readonly List<byte> _data = [];

        public byte[] Data => _data.ToArray();

        public Bytes U8(byte value) { _data.Add(value); return this; }

        public Bytes Leb(ulong value)
        {
            do
            {
                var group = (byte)(value & 0x7F);
                value >>= 7;
                _data.Add(value == 0 ? group : (byte)(group | 0x80));
            } while (value != 0);
            return this;
        }

        public Bytes Str(string value)
        {
            var utf8 = System.Text.Encoding.UTF8.GetBytes(value);
            Leb((ulong)utf8.Length);
            _data.AddRange(utf8);
            return this;
        }

        public Bytes Raw(params byte[] bytes) { _data.AddRange(bytes); return this; }
    }

    private static byte[] Module(params (byte Id, byte[] Payload)[] sections)
    {
        var file = new Bytes()
            .Raw(0x4C, 0x59, 0x52, 0x42)            // 'LYRB'
            .Raw((byte)Format.VersionMajor, 0, 6, 0);     // version 3.6, little-endian u16 pair
        foreach (var (id, payload) in sections)
        {
            file.U8(id).Leb((ulong)payload.Length).Raw(payload);
        }
        return file.Data;
    }

    /// <summary>One function: name index 0, no parameters, one block at offset 0.</summary>
    private static byte[] FunctionSection(byte returnTag, byte[] code, int maxStack = 4,
        params byte[] slotTags)
    {
        var section = new Bytes()
            .Leb(1)                     // function count
            .Leb(0)                     // nameIndex
            .Leb(0)                     // paramCount
            .U8(returnTag)
            .Leb((ulong)slotTags.Length);
        foreach (var tag in slotTags) section.U8(tag);
        section.Leb((ulong)maxStack)
            .Leb(1).Leb(0)              // one block at offset 0
            .Leb((ulong)code.Length).Raw(code);
        return section.Data;
    }

    private static byte[] Strings(params string[] values)
    {
        var section = new Bytes().Leb((ulong)values.Length);
        foreach (var value in values) section.Str(value);
        return section.Data;
    }

    private const byte I64 = 0x04;
    private const byte Void = 0x0E;

    private static MalformedBytecodeException Rejects(byte[] module) =>
        Assert.Throws<MalformedBytecodeException>(() => BytecodeReader.ReadOrThrow(module));

    // ------------------------------------------------------------------ the catalogue

    [Fact]
    public void A_block_without_a_terminator_is_rejected()
    {
        // 'const i64 1' and then nothing: at runtime the interpreter would run past the end of
        // the code. The walk used to end quietly with the instructions.
        var code = new Bytes().Raw(0x01, I64).Leb(1).Data;
        var ex = Rejects(Module(
            (2, Strings("f")),
            (5, FunctionSection(Void, code))));
        Assert.Contains("without a terminator", ex.Message);
    }

    [Fact]
    public void A_throw_ending_block_satisfies_the_terminator_rule()
    {
        // throw IS a terminator (§5). Type index 0 means "carried by the value".
        var code = new Bytes().Raw(0x01, I64).Leb(1).Raw(0x73).Leb(0).Data;
        var module = BytecodeReader.ReadOrThrow(Module(
            (2, Strings("f")),
            (5, FunctionSection(Void, code))));
        Assert.Single(module.Functions);
    }

    [Fact]
    public void Retval_in_a_void_function_is_rejected()
    {
        var code = new Bytes().Raw(0x01, I64).Leb(1).Raw(0x42).Data;   // const i64 1; retval
        var ex = Rejects(Module(
            (2, Strings("f")),
            (5, FunctionSection(Void, code))));
        Assert.Contains("retval", ex.Message);
    }

    [Fact]
    public void Ret_in_a_valued_function_is_rejected()
    {
        var code = new Bytes().Raw(0x41).Data;                         // ret
        var ex = Rejects(Module(
            (2, Strings("f")),
            (5, FunctionSection(I64, code))));
        Assert.Contains("ret without a value", ex.Message);
    }

    [Fact]
    public void A_throw_type_outside_the_table_is_rejected()
    {
        // throw carries type index + 1; with an empty type table anything above 0 points nowhere.
        var code = new Bytes().Raw(0x01, I64).Leb(1).Raw(0x73).Leb(5).Data;
        var ex = Rejects(Module(
            (2, Strings("f")),
            (5, FunctionSection(Void, code))));
        Assert.Contains("throw", ex.Message);
    }

    [Fact]
    public void An_entry_point_with_a_non_string_parameter_is_rejected()
    {
        // fn main(a: i64): i64 as the Start function. §8.5: nothing, or a single string[]. The
        // runner would hand a string[] to an i64 slot.
        var section = new Bytes()
            .Leb(1)             // function count
            .Leb(0)             // nameIndex
            .Leb(1)             // paramCount
            .U8(I64)            // returnType
            .Leb(1).U8(I64)     // one slot: i64
            .Leb(1)             // maxStack
            .Leb(1).Leb(0)      // one block at offset 0
            .Leb(3).Raw(0x02, 0x00, 0x42)   // ldloc 0; retval
            .Data;
        var ex = Rejects(Module(
            (2, Strings("main")),
            (5, section),
            (7, new Bytes().Leb(0).Data)));
        Assert.Contains("string[]", ex.Message);
    }

    [Fact]
    public void An_enum_attribute_value_naming_a_payload_variant_is_rejected()
    {
        // Types: [0] layout 'V' with a tag slot AND a payload field, [1] enum 'E' over it,
        // [2] struct 'A' with one field of enum type. The attribute row writes variant 0 of 'E',
        // which carries a payload — §Attributes allows only payload-free variants.
        var types = new Bytes()
            .Leb(3)
            .Leb(0).U8(0).Leb(2).U8(I64).U8(I64)    // 'V': layout, tag + payload
            .Leb(1).U8(1).Leb(1).Leb(0)             // 'E': enum, one variant -> type 0
            .Leb(2).U8(3).Leb(1).U8(0x43).Leb(1)    // 'A': struct, one field of enum 'E'
            .Data;
        var attributes = new Bytes()
            .Leb(1)             // row count
            .U8(2).Leb(0)       // module target
            .Leb(2)             // attribute type: 'A'
            .Leb(1)             // one value
            .U8(0x43).Leb(0)    // enum value: variant 0
            .Data;
        var ex = Rejects(Module(
            (2, Strings("V", "E", "A")),
            (3, types),
            (11, attributes)));
        Assert.Contains("payload", ex.Message);
    }

    [Fact]
    public void An_enum_attribute_value_naming_a_payload_free_variant_loads()
    {
        // The same module with a payload-free variant: slot 0 is the tag and nothing follows.
        var types = new Bytes()
            .Leb(3)
            .Leb(0).U8(0).Leb(1).U8(I64)            // 'V': layout, tag only
            .Leb(1).U8(1).Leb(1).Leb(0)             // 'E': enum, one variant -> type 0
            .Leb(2).U8(3).Leb(1).U8(0x43).Leb(1)    // 'A': struct, one field of enum 'E'
            .Data;
        var attributes = new Bytes()
            .Leb(1)
            .U8(2).Leb(0)
            .Leb(2)
            .Leb(1)
            .U8(0x43).Leb(0)
            .Data;
        var module = BytecodeReader.ReadOrThrow(Module(
            (2, Strings("V", "E", "A")),
            (3, types),
            (11, attributes)));
        Assert.Single(module.Attributes);
        Assert.Equal("V", module.Attributes[0].Values[0].Text);
    }
}
