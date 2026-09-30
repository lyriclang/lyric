using System.Text;
using Lyric.Core;
using Lyric.Ir;

namespace Lyric5.Compiler;

/// <summary>
/// The IR of one module as one C file (design/v5/spec/01 L8): C11 against <c>lyr/lyr.h</c>, one
/// C function per IR function in the platform ABI (S1), locals and temps as C locals (S2), blocks
/// as labels and terminators as <c>goto</c> (C5), a <c>#line</c> wherever the source line changes
/// (C6), names mangled <c>lyr_&lt;module&gt;_&lt;name&gt;</c> (C4). Integer arithmetic goes through
/// the runtime's checked forms, the wrap operators and shifts through <c>lyr/numeric.h</c> (03 T2);
/// floats are C's <c>double</c> and <c>float</c> under IEEE semantics, compiled without
/// contraction (01 L10); a conversion wraps, saturates or checks as T1d says. Strings are the runtime's
/// <c>LyrStr *</c>, literals static objects (V9). A class is a heap object (V3): a C struct that
/// begins with the header, reached through a pointer, allocated by <c>lyr_alloc</c> with a static
/// descriptor the emitter writes beside it (V4) — size, reference bitmap, qualified name — and
/// every reference stored into an object goes through the write barrier (L1). An optional (V5)
/// of a reference is that pointer, null when absent — the niche, zero bytes; of anything else a
/// C struct of the value and a flag, held by value like a struct. An enum (V6) is a tag and a
/// union of its variants' payloads, inline and by value; an optional of an enum takes a tag no
/// variant has. A struct is a C struct by value (V2): locals,
/// parameters, fields and results hold it; the IR's struct-typed temps are aliases into that
/// storage — <c>load</c> and a struct-typed <c>loadfield</c> alias, <c>newobj</c> and
/// <c>structcopy</c> make fresh storage, <c>store</c>, <c>storefield</c>, arguments and
/// <c>return</c> copy the value — so in C they are pointers, each with its own storage beside it.
///
/// <para>Only what the <see cref="SubsetGate"/> lets through arrives here, so an instruction or a
/// type this emitter has no case for is a bug, not a diagnostic.</para>
/// </summary>
public sealed class CEmitter
{
    /// <summary>What the gate lets through but this emitter cannot translate yet, within M2: the
    /// driver reports it like a gate refusal (<see cref="SubsetGate.NotYet"/>).</summary>
    public sealed class NotYetException(string what, string milestone)
        : Exception($"not yet in Lyric 5: {what} ({milestone})")
    {
        public string What { get; } = what;
        public string Milestone { get; } = milestone;
    }

    /// <summary>Part of every build cache key: a change in emission is a change in the C, and the
    /// cache must not hand out the old C for it. Bump it with the emission.</summary>
    public const string Version = "m3-s5";

    private readonly IrModule _module;
    private readonly SourceManager _sources;
    private readonly StringBuilder _out = new();
    private readonly StringBuilder _constants = new();
    private readonly Dictionary<string, string> _literals = new(StringComparer.Ordinal);
    private StringBuilder _fn = null!;
    private IrFunction _function = null!;
    private FileId? _file;

    /// <summary>Which entry of the type table is a variant of which enum, and which number it
    /// has there: the tag.</summary>
    private readonly Dictionary<int, (int Enum, int Tag)> _variants = new();

    private CEmitter(IrModule module, SourceManager sources)
    {
        _module = module;
        _sources = sources;
        for (var i = 0; i < module.Types.Count; i++)
            for (var tag = 0; tag < module.Types[i].Variants.Length; tag++)
                _variants[module.Types[i].Variants[tag].Value] = (i, tag);
    }

    /// <summary>The C text of the module. <paramref name="sources"/> answers the <c>#line</c> positions.</summary>
    public static string Emit(IrModule module, SourceManager sources) => new CEmitter(module, sources).Module();

    // --- names and types -------------------------------------------------------------------------

    /// <summary>
    /// <c>lyr_&lt;module&gt;_&lt;name&gt;</c>: dots become underscores; what C cannot spell (generic
    /// instances carry their arguments in the name) becomes an underscore with a short hash, so
    /// two names that differ only there stay apart.
    /// </summary>
    public static string FunctionName(string irName)
    {
        var plain = new StringBuilder("lyr_");
        var changed = false;
        foreach (var c in irName)
        {
            if (c == '.') plain.Append('_');
            else if (char.IsAsciiLetterOrDigit(c) || c == '_') plain.Append(c);
            else { plain.Append('_'); changed = true; }
        }
        if (!changed) return plain.ToString();
        var hash = (uint)irName.Aggregate(2166136261u, (h, c) => (h ^ c) * 16777619u);
        return $"{plain}_{hash:x8}";
    }

    private static string LocalName(IrLocal local) =>
        $"l{local.Id.Value}_" + new string(local.Name.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_').ToArray());

    private static string Temp(TempId temp) => $"t{temp.Value}";

    public string CType(IrType type) => type switch
    {
        IrStructType s => StructName(s.Type),
        IrEnumType e => StructName(e.Type),
        IrRefType r => StructName(r.Type) + " *",
        IrArrayType => "LyrArr *",
        IrSliceType s => SliceName(s),
        IrInlineArrayType ia => InlineName(ia),
        IrOptionalType o when IsNiche(o) || IsTagNiche(o) => CType(o.Inner),
        IrOptionalType o => OptionalName(o),
        IrScalarType { Kind: IrScalar.I8 } => "int8_t",
        IrScalarType { Kind: IrScalar.I16 } => "int16_t",
        IrScalarType { Kind: IrScalar.I32 } => "int32_t",
        IrScalarType { Kind: IrScalar.I64 } => "int64_t",
        IrScalarType { Kind: IrScalar.U8 } => "uint8_t",
        IrScalarType { Kind: IrScalar.U16 } => "uint16_t",
        IrScalarType { Kind: IrScalar.U32 } => "uint32_t",
        IrScalarType { Kind: IrScalar.U64 } => "uint64_t",
        IrScalarType { Kind: IrScalar.F32 } => "float",
        IrScalarType { Kind: IrScalar.F64 } => "double",
        IrScalarType { Kind: IrScalar.Char } => "uint32_t",
        IrScalarType { Kind: IrScalar.Bool } => "uint8_t",
        IrScalarType { Kind: IrScalar.String } => "LyrStr *",
        IrScalarType { Kind: IrScalar.Void } => "void",
        _ => throw new InvalidOperationException($"the C emitter has no type for {type}; the gate let it through"),
    };

    /// <summary>A declaration, with the pointer star against the name as C is written.</summary>
    private string Declare(IrType type, string name)
    {
        var c = CType(type);
        return c.EndsWith('*') ? c + name : c + " " + name;
    }

    private static bool IsSigned(IrType type) =>
        type is IrScalarType { Kind: IrScalar.I8 or IrScalar.I16 or IrScalar.I32 or IrScalar.I64 };

    private static bool IsFloat(IrType type) => type is IrScalarType { Kind: IrScalar.F32 or IrScalar.F64 };

    private static bool IsInteger(IrType type) => type is IrScalarType
    {
        Kind: IrScalar.I8 or IrScalar.I16 or IrScalar.I32 or IrScalar.I64
        or IrScalar.U8 or IrScalar.U16 or IrScalar.U32 or IrScalar.U64
    };

    /// <summary>The unsigned twin of an integer type and its width, for the operations that
    /// compute in unsigned arithmetic (<c>lyr/numeric.h</c>).</summary>
    private static (string Unsigned, int Bits) Shape(IrType type) => type switch
    {
        IrScalarType { Kind: IrScalar.I8 or IrScalar.U8 } => ("uint8_t", 8),
        IrScalarType { Kind: IrScalar.I16 or IrScalar.U16 } => ("uint16_t", 16),
        IrScalarType { Kind: IrScalar.I32 or IrScalar.U32 } => ("uint32_t", 32),
        IrScalarType { Kind: IrScalar.I64 or IrScalar.U64 } => ("uint64_t", 64),
        _ => throw new InvalidOperationException($"{type} has no unsigned twin; the gate let it through"),
    };

    private static bool IsVoid(IrType type) => type is IrScalarType { Kind: IrScalar.Void };

    private static bool IsString(IrType type) => type is IrScalarType { Kind: IrScalar.String };

    /// <summary>
    /// An optional in the niche (01 V5): around a reference — a class value or a string — the
    /// pointer itself says whether there is a value, and the optional costs nothing. Around an
    /// optional there is no niche left: <c>??Node</c> tells "absent" from "present and null",
    /// and needs its flag.
    /// </summary>
    private static bool IsNiche(IrType type) =>
        type is IrOptionalType { Inner: IrRefType or IrArrayType or IrScalarType { Kind: IrScalar.String } };

    /// <summary>
    /// A type C holds by value as an aggregate: a struct, and an optional outside the niche.
    /// Locals, fields, parameters and results hold the value; the IR's temps of such a type are
    /// aliases into that storage, pointers in C, each with storage beside it for the values it
    /// makes fresh. One scheme for both, so a narrowed <c>?Point</c> is written in place like
    /// the struct it holds.
    /// </summary>
    private static bool IsAggregate(IrType type) =>
        type is IrStructType or IrEnumType or IrInlineArrayType || (type is IrOptionalType && !IsNiche(type));

    /// <summary>
    /// An optional of an enum (01 V5): the enum itself, with a tag no variant has standing for
    /// "no value" (<c>LYR_ENUM_NONE</c>). No flag, no growth. Around that optional there is no
    /// niche left, as for a reference.
    /// </summary>
    private static bool IsTagNiche(IrType type) => type is IrOptionalType { Inner: IrEnumType };

    /// <summary><c>lyr_opt_&lt;inner&gt;</c>: one C struct per optional type outside the niche,
    /// named after what it holds.</summary>
    private static string OptionalName(IrOptionalType type) => "lyr_opt_" + Mangle(type.Inner);

    /// <summary><c>lyr_slice_&lt;element&gt;</c>: a view of <c>T[]</c> (03 T13 A2), a pointer into
    /// the elements and a length — two words, a value C passes and copies as one.</summary>
    private static string SliceName(IrSliceType type) => "lyr_slice_" + Mangle(type.Element);

    /// <summary><c>lyr_inl&lt;N&gt;_&lt;element&gt;</c>: an inline array (03 T13 A4), a struct around a
    /// C array so that C copies it as a value.</summary>
    private static string InlineName(IrInlineArrayType type) => $"lyr_inl{type.Length}_" + Mangle(type.Element);

    private static string Mangle(IrType type) => type switch
    {
        IrScalarType { Kind: IrScalar.String } => "str",
        IrScalarType s => s.Kind.ToString().ToLowerInvariant(),
        IrStructType s => $"ty{s.Type.Value}",
        IrEnumType e => $"en{e.Type.Value}",
        IrRefType r => $"ref{r.Type.Value}",
        IrArrayType a => "arr_" + Mangle(a.Element),
        IrSliceType s => "slice_" + Mangle(s.Element),
        IrInlineArrayType ia => $"inl{ia.Length}_" + Mangle(ia.Element),
        IrOptionalType o => "opt_" + Mangle(o.Inner),
        _ => throw new InvalidOperationException($"the C emitter has no name for an optional of {type}; the gate let it through"),
    };

    /// <summary>The type as Lyric writes it, for a descriptor's name.</summary>
    private string Display(IrType type) => type switch
    {
        IrScalarType { Kind: IrScalar.String } => "string",
        IrScalarType { Kind: IrScalar.I64 } => "int",
        IrScalarType { Kind: IrScalar.U64 } => "uint",
        IrScalarType { Kind: IrScalar.F64 } => "float",
        IrScalarType { Kind: IrScalar.F32 } => "float32",
        IrScalarType s => s.Kind.ToString().ToLowerInvariant(),
        IrStructType s => Qualified(_module.Types[s.Type.Value]),
        IrEnumType e => Qualified(_module.Types[e.Type.Value]),
        IrRefType r => Qualified(_module.Types[r.Type.Value]),
        IrArrayType a => Display(a.Element) + "[]",
        IrSliceType s => $"Slice<{Display(s.Element)}>",
        IrInlineArrayType ia => $"{Display(ia.Element)}[{ia.Length}]",
        IrOptionalType o => "?" + Display(o.Inner),
        _ => type.ToString() ?? "?",
    };

    private static string Qualified(IrTypeDef def) => def.Module.Length > 0 ? $"{def.Module}.{def.Name}" : def.Name;

    /// <summary><c>lyr_ty&lt;index&gt;_&lt;Name&gt;</c>: the index makes it unique (the IR's type names
    /// are not module-qualified), the name keeps it readable in a debugger.</summary>
    private string StructName(TypeId id) =>
        $"lyr_ty{id.Value}_" + Identifier(_module.Types[id.Value].Name);

    private static string Identifier(string name) =>
        new string(name.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_').ToArray());

    private static string FieldName(string name) => "f_" + Identifier(name);

    private string Field(TypeId type, FieldId field) => FieldName(_module.Types[type.Value].FieldNames[field.Value]);

    private IrType TypeOf(TempId temp) => _function.Temps[temp.Value].Type;

    /// <summary>A zero of the type, for the declarations: every C local starts defined.</summary>
    private static string Zero(IrType type) => type switch
    {
        IrScalarType { Kind: IrScalar.String } => "NULL",
        IrRefType or IrArrayType => "NULL",
        _ when IsNiche(type) => "NULL",
        _ when IsAggregate(type) => "{0}",
        IrSliceType => "{0}",
        _ => "0",
    };

    // --- layout ----------------------------------------------------------------------------------

    /// <summary>
    /// The size and alignment C gives a type on every Tier 1 target (all LP64 or LLP64 with
    /// natural alignment: a scalar aligns to its size, a pointer is eight bytes). The emitter
    /// needs the numbers for one thing, the reference bitmap of a descriptor (V4), and it does
    /// not trust them: every class carries a <c>_Static_assert</c> on its size and on each
    /// field offset, so a target that lays a type out differently fails to compile instead of
    /// handing the collector a wrong map.
    /// </summary>
    private (int Size, int Align) LayoutOf(IrType type) => type switch
    {
        IrScalarType { Kind: IrScalar.I8 or IrScalar.U8 or IrScalar.Bool } => (1, 1),
        IrScalarType { Kind: IrScalar.I16 or IrScalar.U16 } => (2, 2),
        IrScalarType { Kind: IrScalar.I32 or IrScalar.U32 or IrScalar.F32 or IrScalar.Char } => (4, 4),
        IrScalarType { Kind: IrScalar.I64 or IrScalar.U64 or IrScalar.F64 or IrScalar.String } => (8, 8),
        IrRefType or IrArrayType => (8, 8),
        IrSliceType => (16, 8),
        IrInlineArrayType ia => (LayoutOf(ia.Element).Size * ia.Length, LayoutOf(ia.Element).Align),
        IrOptionalType o when IsNiche(o) => (8, 8),
        IrOptionalType o when IsTagNiche(o) => LayoutOf(o.Inner),
        IrOptionalType o => OptionalLayout(o),
        IrStructType s => Fields(_module.Types[s.Type.Value], 0).Total,
        IrEnumType e => EnumLayout(_module.Types[e.Type.Value]).Total,
        _ => throw new InvalidOperationException($"the C emitter has no layout for {type}; the gate let it through"),
    };

    /// <summary>The fields of a type laid out from <paramref name="start"/> (8 for a class, past
    /// its header): the offset of each, and the size and alignment of the whole.</summary>
    private (int[] Offsets, (int Size, int Align) Total) Fields(IrTypeDef def, int start) =>
        Fields(def.FieldTypes, start);

    private (int[] Offsets, (int Size, int Align) Total) Fields(IReadOnlyList<IrType> types, int start)
    {
        var offsets = new int[types.Count];
        if (types.Count == 0 && start == 0) return (offsets, (1, 1)); // the unused byte of an empty struct
        var (at, align) = (start, start > 0 ? 8 : 1);
        for (var i = 0; i < types.Count; i++)
        {
            var (size, fieldAlign) = LayoutOf(types[i]);
            at = (at + fieldAlign - 1) / fieldAlign * fieldAlign;
            offsets[i] = at;
            at += size;
            align = Math.Max(align, fieldAlign);
        }
        return (offsets, ((at + align - 1) / align * align, align));
    }

    /// <summary>The payload of a variant: its fields without slot 0, which the IR keeps for the
    /// tag and C keeps in the enum.</summary>
    private static IrType[] Payload(IrTypeDef variant) => variant.FieldTypes.Skip(1).ToArray();

    /// <summary>
    /// An enum (01 V6): the tag, four bytes, then the union of the variants' payloads, aligned
    /// as the widest of them asks. An enum whose variants carry nothing is the tag alone.
    /// </summary>
    private ((int Size, int Align) Total, int UnionOffset) EnumLayout(IrTypeDef def)
    {
        var (size, align) = (0, 1);
        foreach (var variant in def.Variants)
        {
            var payload = Payload(_module.Types[variant.Value]);
            if (payload.Length == 0) continue;
            var (s, a) = Fields(payload, 0).Total;
            (size, align) = (Math.Max(size, s), Math.Max(align, a));
        }
        if (size == 0) return ((4, 4), 4);
        var outer = Math.Max(4, align);
        var offset = (4 + align - 1) / align * align;
        return (((offset + size + outer - 1) / outer * outer, outer), offset);
    }

    /// <summary>An optional outside the niche: the value, then the flag, one byte; padded to the
    /// value's alignment.</summary>
    private (int Size, int Align) OptionalLayout(IrOptionalType type)
    {
        var (size, align) = LayoutOf(type.Inner);
        align = Math.Max(align, 1);
        return ((size + 1 + align - 1) / align * align, align);
    }

    /// <summary>A reference: one pointer-sized word the collector follows. An optional in the
    /// niche is one, null when absent.</summary>
    private static bool IsReference(IrType type) =>
        type is IrRefType or IrArrayType or IrScalarType { Kind: IrScalar.String } || IsNiche(type);

    /// <summary>Whether a value of the type holds a reference anywhere: itself, or in a struct
    /// or an optional it holds by value.</summary>
    private bool HoldsReferences(IrType type) => type switch
    {
        IrStructType s => _module.Types[s.Type.Value].FieldTypes.Any(HoldsReferences),
        IrEnumType e => _module.Types[e.Type.Value].Variants
            .Any(v => Payload(_module.Types[v.Value]).Any(HoldsReferences)),
        IrOptionalType o when !IsNiche(o) => HoldsReferences(o.Inner),
        IrSliceType => true, // its pointer, into the array's elements
        IrInlineArrayType ia => HoldsReferences(ia.Element),
        _ => IsReference(type),
    };

    /// <summary>
    /// The pointer-sized words of an object that are references, as word indices: a reference
    /// field's own word, and through a struct or an optional held by value the words of what it
    /// holds. An absent optional is all zero, so its words are null and harmless.
    ///
    /// <para>AN ENUM HAS NO SUCH WORDS TO NAME. A word of its union is a reference under one
    /// tag and a number under another, and a bitmap has one bit for it. Where an enum that holds
    /// references lies in an object, <paramref name="ambiguous"/> is set and the descriptor says
    /// so (<c>LYR_DESC_CONSERVATIVE</c>): the bitmap is not the whole truth, and a collector
    /// scans the object as it scans a stack. Nothing moves (01 L1), so that is sound.</para>
    /// </summary>
    private void ReferenceWords(IrTypeDef def, int start, List<int> words, ref bool ambiguous)
    {
        var offsets = Fields(def, start).Offsets;
        for (var i = 0; i < def.FieldTypes.Length; i++)
            ReferenceWords(def.FieldTypes[i], offsets[i], words, ref ambiguous);
    }

    private void ReferenceWords(IrType type, int offset, List<int> words, ref bool ambiguous)
    {
        if (IsReference(type)) words.Add(offset / 8);
        else if (type is IrSliceType) words.Add(offset / 8); // the pointer, first word; an interior one
        else if (type is IrInlineArrayType inline)
            for (var i = 0; i < inline.Length; i++)
                ReferenceWords(inline.Element, offset + i * LayoutOf(inline.Element).Size, words, ref ambiguous);
        else if (type is IrStructType inner) ReferenceWords(_module.Types[inner.Type.Value], offset, words, ref ambiguous);
        else if (type is IrEnumType) ambiguous |= HoldsReferences(type);
        else if (type is IrOptionalType optional) ReferenceWords(optional.Inner, offset, words, ref ambiguous);
    }

    private string DescriptorName(TypeId id) => $"lyr_desc_ty{id.Value}_" + Identifier(_module.Types[id.Value].Name);

    /// <summary>The descriptor of <c>T[]</c>, one per element type (V10).</summary>
    private static string ArrayDescriptor(IrType element) => "lyr_desc_arr_" + Mangle(element);

    /// <summary>The elements of an array or a view temp, typed: <c>LYR_ARR_DATA(t, T)[i]</c>, or
    /// the view's pointer.</summary>
    private string Elements(TempId array, IrType element) => TypeOf(array) switch
    {
        IrSliceType => $"{Temp(array)}.ptr",
        IrInlineArrayType => $"{Temp(array)}->v", // the temp aliases the value's storage
        _ => $"LYR_ARR_DATA({Temp(array)}, {CType(element)})",
    };

    /// <summary>The length of an array, a view or an inline array temp.</summary>
    private string Length(TempId array) => TypeOf(array) switch
    {
        IrSliceType => $"{Temp(array)}.len",
        IrInlineArrayType ia => $"INT64_C({ia.Length})",
        _ => $"{Temp(array)}->len",
    };

    /// <summary>The storage beside a struct-typed temp, for the values it makes fresh.</summary>
    private static string Storage(TempId temp) => $"t{temp.Value}_s";

    // --- module ----------------------------------------------------------------------------------

    private string Module()
    {
        _out.AppendLine("/* Generated by lyric5 from the IR of this module. Do not edit: the source is the .lyr. */");
        _out.AppendLine("#include \"lyr/lyr.h\"");
        _out.AppendLine("#include <stdint.h>");
        _out.AppendLine("#include <math.h>");
        _out.AppendLine();

        Structs();

        var prototypes = new StringBuilder();
        prototypes.AppendLine("/* prototypes */");
        foreach (var function in _module.Functions) prototypes.Append(Signature(function)).AppendLine(";");

        var body = new StringBuilder();
        foreach (var function in _module.Functions) body.Append(Function(function));
        if (_module.EntryFunction is { } entry) body.Append(Entry(_module.Functions[entry.Value]));

        if (_constants.Length > 0) _out.AppendLine("/* string literals */").Append(_constants).AppendLine();
        _out.Append(prototypes).AppendLine();
        _out.Append(body);
        return _out.ToString();
    }

    /// <summary>
    /// The types: every struct and class named first, so a field may point at any of them, then
    /// each defined after the structs it holds by value (a field by value needs a complete
    /// type). A class begins with the object header and is followed by its descriptor.
    /// </summary>
    private void Structs()
    {
        var indices = Enumerable.Range(0, _module.Types.Count)
            .Where(i => _module.Types[i].IsStruct || _module.Types[i].IsClass || _module.Types[i].IsEnum).ToList();
        // Every type a function names, for the optionals among them: an optional outside the
        // niche is a C struct of its own and is defined once, wherever it is first needed.
        var named = _module.Functions
            .SelectMany(f => f.Locals.Select(l => l.Type).Concat(f.Temps.Select(t => t.Type)).Append(f.ReturnType))
            .Concat(_module.Types.SelectMany(t => t.FieldTypes))
            .ToList();
        var used = named.Where(t => (t is IrOptionalType && !IsNiche(t)) || t is IrSliceType or IrInlineArrayType).ToList();
        // Every element type an array is made of, the array's own element type included when it
        // is itself an array: each gets a descriptor, after the structs it may hold.
        var elements = new List<IrType>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Element(IrType type)
        {
            if (type is IrArrayType a && seen.Add(Mangle(a.Element))) { elements.Add(a.Element); Element(a.Element); }
            else if (type is IrOptionalType o) Element(o.Inner);
            else if (type is IrSliceType s) Element(s.Element);
            else if (type is IrInlineArrayType ia) Element(ia.Element);
        }
        foreach (var type in named) Element(type);
        if (indices.Count == 0 && used.Count == 0 && elements.Count == 0) return;

        _out.AppendLine("/* types: a struct is a value, a class an object behind its header, an enum a tag and a union */");
        foreach (var i in indices)
        {
            var name = StructName(new TypeId(i));
            _out.AppendLine($"typedef struct {name} {name};");
            foreach (var variant in _module.Types[i].Variants)
                _out.AppendLine($"typedef struct {StructName(variant)} {StructName(variant)};");
        }

        var done = new HashSet<int>();
        var optionals = new HashSet<string>(StringComparer.Ordinal);

        // What holding a type BY VALUE needs defined first: the struct itself, or the optional's
        // own struct after what it holds.
        void Require(IrType type)
        {
            if (type is IrStructType held) Define(held.Type.Value);
            else if (type is IrEnumType chosen) Define(chosen.Type.Value);
            else if (IsTagNiche(type)) Require(((IrOptionalType)type).Inner);
            else if (type is IrOptionalType optional && !IsNiche(optional) && optionals.Add(OptionalName(optional)))
            {
                Require(optional.Inner);
                _out.AppendLine($"typedef struct {{ {Declare(optional.Inner, "value")}; uint8_t has; }} {OptionalName(optional)};");
            }
            else if (type is IrInlineArrayType inline && optionals.Add(InlineName(inline)))
            {
                Require(inline.Element);
                var (size, _) = LayoutOf(inline);
                _out.AppendLine($"typedef struct {{ {Declare(inline.Element, $"v[{inline.Length}]")}; }} {InlineName(inline)};");
                _out.AppendLine($"_Static_assert(sizeof({InlineName(inline)}) == {size}, \"layout of {InlineName(inline)}\");");
            }
            else if (type is IrSliceType slice && optionals.Add(SliceName(slice)))
            {
                Require(slice.Element);
                _out.AppendLine($"typedef struct {{ {CType(slice.Element)} *ptr; int64_t len; }} {SliceName(slice)};");
                _out.AppendLine($"_Static_assert(sizeof({SliceName(slice)}) == 16, \"layout of {SliceName(slice)}\");");
            }
        }

        void Define(int index)
        {
            var def = _module.Types[index];
            if (!(def.IsStruct || def.IsClass || def.IsEnum) || !done.Add(index)) return;
            if (def.IsEnum) { DefineEnum(index, def, Require); return; }
            foreach (var field in def.FieldTypes) Require(field);
            var name = StructName(new TypeId(index));
            _out.AppendLine($"struct {name} {{");
            if (def.IsClass) _out.AppendLine("    LyrObj header;");
            // A struct without fields holds one unused byte: C has no empty struct.
            if (def.IsStruct && def.FieldTypes.Length == 0) _out.AppendLine("    uint8_t lyr_unit;");
            for (var i = 0; i < def.FieldTypes.Length; i++)
                _out.AppendLine($"    {Declare(def.FieldTypes[i], FieldName(def.FieldNames[i]))};");
            _out.AppendLine("};");
            if (def.IsClass) Descriptor(new TypeId(index), def);
        }
        foreach (var i in indices) Define(i);
        foreach (var type in used) Require(type);
        foreach (var element in elements) { Require(element); ArrayDescriptorOf(element); }
        _out.AppendLine();
    }

    /// <summary>
    /// The descriptor of an array of <paramref name="element"/> (V10): the fixed part is the
    /// header and the length, then <c>len</c> elements of the element's size, each laid out as
    /// the element type is — the reference map describes ONE element. An element that is an
    /// enum holding references makes the whole array conservative, as it does an object.
    /// </summary>
    private void ArrayDescriptorOf(IrType element)
    {
        var name = ArrayDescriptor(element);
        var (size, _) = LayoutOf(element);
        var c = CType(element);
        _out.AppendLine($"_Static_assert(sizeof({c}) == {size}, \"layout of {Display(element)}[]\");");
        var words = new List<int>();
        var ambiguous = false;
        ReferenceWords(element, 0, words, ref ambiguous);
        var text = Display(element).Replace("\\", "\\\\").Replace("\"", "\\\"");
        var flags = "LYR_DESC_ARRAY" + (words.Count > 0 || ambiguous ? " | LYR_DESC_HAS_REFS" : "") + (ambiguous ? " | LYR_DESC_CONSERVATIVE" : "");
        if (words.Count == 0)
        {
            _out.AppendLine($"static const LyrDesc {name} = {{ (uint32_t)offsetof(LyrArr, data), {flags}, sizeof({c}), 0, NULL, \"{text}[]\", NULL }};");
            return;
        }
        var map = new ulong[(size / 8 + 63) / 64];
        foreach (var word in words) map[word / 64] |= 1UL << (word % 64);
        var bits = string.Join(", ", map.Select(m => $"UINT64_C(0x{m:x})"));
        _out.AppendLine($"static const uint64_t lyr_refmap_arr_{Mangle(element)}[] = {{ {bits} }};");
        _out.AppendLine($"static const LyrDesc {name} = {{ (uint32_t)offsetof(LyrArr, data), {flags}, sizeof({c}), {map.Length}, lyr_refmap_arr_{Mangle(element)}, \"{text}[]\", NULL }};");
    }

    /// <summary>
    /// An enum (V6): one struct per variant for its payload, named fields as the IR names them,
    /// then the enum itself — the tag and the union of the payloads that hold something. A
    /// variant without a payload is a tag value and nothing else; its struct exists so the IR's
    /// reference to it has a type, and holds one unused byte because C has no empty struct.
    /// </summary>
    private void DefineEnum(int index, IrTypeDef def, Action<IrType> require)
    {
        foreach (var variant in def.Variants)
            foreach (var field in Payload(_module.Types[variant.Value])) require(field);

        var members = new List<string>();
        for (var tag = 0; tag < def.Variants.Length; tag++)
        {
            var variant = _module.Types[def.Variants[tag].Value];
            var name = StructName(def.Variants[tag]);
            _out.AppendLine($"struct {name} {{");
            if (variant.FieldTypes.Length <= 1) _out.AppendLine("    uint8_t lyr_unit;");
            for (var i = 1; i < variant.FieldTypes.Length; i++)
                _out.AppendLine($"    {Declare(variant.FieldTypes[i], FieldName(variant.FieldNames[i]))};");
            _out.AppendLine("};");
            if (variant.FieldTypes.Length > 1) members.Add($"{name} v{tag};");
        }

        var (total, _) = EnumLayout(def);
        var self = StructName(new TypeId(index));
        _out.AppendLine(members.Count == 0
            ? $"struct {self} {{ uint32_t tag; }};"
            : $"struct {self} {{ uint32_t tag; union {{ {string.Join(" ", members)} }} as; }};");
        _out.AppendLine($"_Static_assert(sizeof({self}) == {total.Size}, \"layout of {self}\");");
    }

    /// <summary>
    /// The descriptor of a class (V4): its size, which of its words are references — the header
    /// is word 0 and never one — and its qualified name. Static and constant; its address is
    /// the type's identity. The asserts hold the emitter's layout against the compiler's.
    /// </summary>
    private void Descriptor(TypeId id, IrTypeDef def)
    {
        var name = StructName(id);
        var (offsets, total) = Fields(def, 8);
        _out.AppendLine($"_Static_assert(sizeof({name}) == {total.Size}, \"layout of {name}\");");
        for (var i = 0; i < offsets.Length; i++)
            _out.AppendLine($"_Static_assert(offsetof({name}, {FieldName(def.FieldNames[i])}) == {offsets[i]}, \"layout of {name}\");");

        var words = new List<int>();
        var ambiguous = false;
        ReferenceWords(def, 8, words, ref ambiguous);
        var qualified = def.Module.Length > 0 ? $"{def.Module}.{def.Name}" : def.Name;
        var text = qualified.Replace("\\", "\\\\").Replace("\"", "\\\"");
        var flags = ambiguous ? "LYR_DESC_HAS_REFS | LYR_DESC_CONSERVATIVE" : "LYR_DESC_HAS_REFS";
        if (words.Count == 0)
        {
            _out.AppendLine($"static const LyrDesc {DescriptorName(id)} = {{ sizeof({name}), {(ambiguous ? flags : "0")}, 0, 0, NULL, \"{text}\", NULL }};");
            return;
        }

        var map = new ulong[(total.Size / 8 + 63) / 64];
        foreach (var word in words) map[word / 64] |= 1UL << (word % 64);
        var bits = string.Join(", ", map.Select(m => $"UINT64_C(0x{m:x})"));
        _out.AppendLine($"static const uint64_t lyr_refmap_ty{id.Value}[] = {{ {bits} }};");
        _out.AppendLine($"static const LyrDesc {DescriptorName(id)} = {{ sizeof({name}), {flags}, 0, {map.Length}, lyr_refmap_ty{id.Value}, \"{text}\", NULL }};");
    }

    /// <summary>A parameter: a value, except the receiver of a struct method, which is the
    /// caller's place (02 M5).</summary>
    private string Parameter(IrFunction function, IrLocal local) =>
        function.ReceiverByRef && local.Id.Value == 0
            ? $"{CType(local.Type)} *{LocalName(local)}"
            : Declare(local.Type, LocalName(local));

    private bool IsReceiverPlace(LocalId local) => _function.ReceiverByRef && local.Value == 0;

    private string Signature(IrFunction function)
    {
        var parameters = function.ParamCount == 0
            ? "void"
            : string.Join(", ", function.Locals.Take(function.ParamCount).Select(p => Parameter(function, p)));
        return $"static {CType(function.ReturnType)} {FunctionName(function.Name)}({parameters})";
    }

    /// <summary>
    /// The program's <c>main</c>: the runtime's <c>lyr_run_main</c> around the entry function,
    /// whose value is the exit code masked to 0..255 by the runtime (11 C5); a <c>void</c> entry
    /// exits with 0.
    /// </summary>
    private string Entry(IrFunction entry)
    {
        var name = FunctionName(entry.Name);
        var text = new StringBuilder();
        text.AppendLine();
        text.AppendLine("/* the program */");
        if (IsVoid(entry.ReturnType))
        {
            text.AppendLine($"static int64_t lyr_entry(void) {{ {name}(); return 0; }}");
            name = "lyr_entry";
        }
        text.AppendLine($"int main(int argc, char **argv) {{ return lyr_run_main(argc, argv, {name}); }}");
        return text.ToString();
    }

    // --- functions -------------------------------------------------------------------------------

    private string Function(IrFunction function)
    {
        _function = function;
        _file = null;
        _fn = new StringBuilder();
        // The definition itself under #line as well, at the function's first statement: a debugger
        // places the function in the .lyr, and DbgHelp keeps the inline sites of a function whose
        // definition and body are in one file — with the definition left in the C, the inlined
        // frames of a Windows backtrace went missing (measured, M2 S2).
        Line(FirstSpan(function));
        _fn.Append(Signature(function)).AppendLine(" {");

        foreach (var local in function.Locals.Skip(function.ParamCount))
            _fn.AppendLine($"    {Declare(local.Type, LocalName(local))} = {Zero(local.Type)};");
        foreach (var temp in function.Temps)
        {
            if (IsVoid(temp.Type)) continue;
            if (IsAggregate(temp.Type))
            {
                _fn.AppendLine($"    {CType(temp.Type)} {Storage(temp.Id)} = {{0}};");
                _fn.AppendLine($"    {CType(temp.Type)} *{Temp(temp.Id)} = &{Storage(temp.Id)};");
            }
            else _fn.AppendLine($"    {Declare(temp.Type, Temp(temp.Id))} = {Zero(temp.Type)};");
        }

        if (function.Blocks.Count > 0 && function.Blocks[0].Id != function.Entry)
            _fn.AppendLine($"    goto bb{function.Entry.Value};");

        foreach (var block in function.Blocks)
        {
            _fn.AppendLine($"bb{block.Id.Value}:;");
            foreach (var inst in block.Insts)
            {
                Line(inst.Span);
                _fn.Append("    ").AppendLine(Instruction(inst));
            }
            if (block.Terminator is { } terminator)
            {
                Line(terminator.Span);
                _fn.Append("    ").AppendLine(Terminator(terminator));
            }
        }
        _fn.AppendLine("}");
        _fn.AppendLine();
        return _fn.ToString();
    }

    private static Span FirstSpan(IrFunction function)
    {
        foreach (var block in function.Blocks)
        {
            if (block.Insts.Count > 0) return block.Insts[0].Span;
            if (block.Terminator is { } t && t.Span != default) return t.Span;
        }
        return default;
    }

    /// <summary>
    /// A <c>#line</c> before every statement that has a source position: the debugger, the
    /// profiler and the backtrace then show the <c>.lyr</c> line (L12), never the C. Before EVERY
    /// statement, because the directive numbers only the line that follows it and C counts on from
    /// there — two statements from one <c>.lyr</c> line would otherwise land on two. The file name
    /// goes only where it changes.
    /// </summary>
    private void Line(Span span)
    {
        if (span == default) return;
        var position = _sources.LocateStart(span);
        if (span.File != _file)
        {
            _file = span.File;
            _fn.AppendLine($"#line {position.Line} \"{_sources.GetPath(span.File).Replace("\\", "\\\\").Replace("\"", "\\\"")}\"");
        }
        else _fn.AppendLine($"#line {position.Line}");
    }

    private string Instruction(IrOp op) => op switch
    {
        Const c => $"{Temp(c.Dest)} = {Constant(c)};",
        BinOp b => $"{Temp(b.Dest)} = {Binary(b)};",
        UnOp u => $"{Temp(u.Dest)} = {Unary(u)};",
        Lyric.Ir.Convert v => $"{Temp(v.Dest)} = {Conversion(v)};",
        // The receiver of a struct method is already the place; every other struct local is
        // storage, and its temp aliases it.
        LoadLocal l when IsReceiverPlace(l.Local) => $"{Temp(l.Dest)} = {LocalName(_function.Locals[l.Local.Value])};",
        LoadLocal l => IsAggregate(l.Type)
            ? $"{Temp(l.Dest)} = &{LocalName(_function.Locals[l.Local.Value])};"
            : $"{Temp(l.Dest)} = {LocalName(_function.Locals[l.Local.Value])};",
        StoreLocal s when IsReceiverPlace(s.Local) => $"*{LocalName(_function.Locals[s.Local.Value])} = {Value(s.Value)};",
        StoreLocal s => $"{LocalName(_function.Locals[s.Local.Value])} = {Value(s.Value)};",
        Call call => Assign(call.Dest, $"{FunctionName(_module.Functions[call.Target.Value].Name)}({Arguments(_module.Functions[call.Target.Value], call.Args)})"),
        CallImport call => Assign(call.Dest, Intrinsics.Call(_module.Imports[call.Target.Value].Name, call.Args.Select(Value).ToArray())),
        NewObject { Result: IrRefType r } n => $"{Temp(n.Dest)} = ({CType(r)})lyr_alloc(&{DescriptorName(r.Type)});",
        NewObject n => $"{Storage(n.Dest)} = ({CType(n.Result)}){{0}}; {Temp(n.Dest)} = &{Storage(n.Dest)};",
        StructCopy c => $"{Storage(c.Dest)} = *{Temp(c.Value)}; {Temp(c.Dest)} = &{Storage(c.Dest)};",
        CopyValue c => $"{Storage(c.Dest)} = *{Temp(c.Value)}; {Temp(c.Dest)} = &{Storage(c.Dest)};",
        // An inline array (A4): its storage filled element by element, or from one value.
        NewInline n => n.Repeat
            ? $"for (int64_t lyr_i = 0; lyr_i < {n.Length}; lyr_i++) {Storage(n.Dest)}.v[lyr_i] = {Value(n.Elements[0])}; {Temp(n.Dest)} = &{Storage(n.Dest)};"
            : $"{Storage(n.Dest)} = ({CType(TypeOf(n.Dest))}){{ .v = {{ {string.Join(", ", n.Elements.Select(Value))} }} }}; {Temp(n.Dest)} = &{Storage(n.Dest)};",
        LoadField f => IsAggregate(f.FieldType)
            ? $"{Temp(f.Dest)} = &{Temp(f.Object)}->{Field(f.Type, f.Field)};"
            : $"{Temp(f.Dest)} = {Temp(f.Object)}->{Field(f.Type, f.Field)};",
        StoreField f => Store(f),
        OptNone n when IsTagNiche(TypeOf(n.Dest)) =>
            $"{Storage(n.Dest)} = ({CType(TypeOf(n.Dest))}){{ .tag = LYR_ENUM_NONE }}; {Temp(n.Dest)} = &{Storage(n.Dest)};",
        OptNone n => IsNiche(TypeOf(n.Dest))
            ? $"{Temp(n.Dest)} = NULL;"
            : $"{Storage(n.Dest)} = ({CType(TypeOf(n.Dest))}){{0}}; {Temp(n.Dest)} = &{Storage(n.Dest)};",
        OptSome s when IsTagNiche(TypeOf(s.Dest)) =>
            $"{Storage(s.Dest)} = {Value(s.Value)}; {Temp(s.Dest)} = &{Storage(s.Dest)};",
        OptSome s => IsNiche(TypeOf(s.Dest))
            ? $"{Temp(s.Dest)} = {Temp(s.Value)};"
            : $"{Storage(s.Dest)} = ({CType(TypeOf(s.Dest))}){{ .value = {Value(s.Value)}, .has = 1 }}; {Temp(s.Dest)} = &{Storage(s.Dest)};",
        OptIsSome i when IsTagNiche(TypeOf(i.Option)) =>
            $"{Temp(i.Dest)} = (uint8_t)({Temp(i.Option)}->tag != LYR_ENUM_NONE);",
        OptIsSome i => IsNiche(TypeOf(i.Option))
            ? $"{Temp(i.Dest)} = (uint8_t)({Temp(i.Option)} != NULL);"
            : $"{Temp(i.Dest)} = {Temp(i.Option)}->has;",
        OptGet g => Unwrap(g),
        NewVariant v => Variant(v),
        EnumTag t => $"{Temp(t.Dest)} = (int64_t){Temp(t.Value)}->tag;",
        NewArray n => $"{Temp(n.Dest)} = lyr_alloc_array(&{ArrayDescriptor(n.Element)}, {n.Elements.Length});"
            + string.Concat(n.Elements.Select((e, i) => $" {Elements(n.Dest, n.Element)}[{i}] = {Value(e)};")),
        ArrayLen a => $"{Temp(a.Dest)} = {Length(a.Array)};",
        // A view (03 T13 A2): its bounds checked against the source, then a pointer into the
        // source's elements and a length — of an array, or of a view of one.
        MakeSlice s => $"LYR_CHECK_RANGE({Temp(s.Low)}, {Temp(s.High)}, {Length(s.Array)}); "
            + $"{Temp(s.Dest)} = ({CType(TypeOf(s.Dest))}){{ {Elements(s.Array, s.Element)} + {Temp(s.Low)}, {Temp(s.High)} - {Temp(s.Low)} }};",
        // An element is a place in the array (02 M3): a struct element is aliased where it lies,
        // a scalar or a reference read out. The check is the one of 03 T14 N5, in every profile.
        LoadElem e => $"LYR_CHECK_INDEX({Temp(e.Index)}, {Length(e.Array)}); {Temp(e.Dest)} = "
            + (IsAggregate(e.Element) ? "&" : "") + $"{Elements(e.Array, e.Element)}[{Temp(e.Index)}];",
        StoreElem s => StoreElement(s),
        ArrayConcat c => $"{Temp(c.Dest)} = lyr_arr_concat(&{ArrayDescriptor(c.Element)}, {Temp(c.Left)}, {Temp(c.Right)});",
        ArrayRepeat r => $"{Temp(r.Dest)} = lyr_arr_repeat(&{ArrayDescriptor(r.Element)}, {Temp(r.Array)}, {Temp(r.Count)});",
        // The variant of a value whose tag was tested: its payload, in place. A variant without
        // one has nothing to point at, and nothing reads through the pointer.
        EnumAs a => _module.Types[a.Variant.Value].FieldTypes.Length > 1
            ? $"{Temp(a.Dest)} = &{Temp(a.Value)}->as.v{_variants[a.Variant.Value].Tag};"
            : $"{Temp(a.Dest)} = ({StructName(a.Variant)} *)(void *){Temp(a.Value)};",
        _ => throw new InvalidOperationException($"the C emitter has no case for {op.GetType().Name}; the gate let it through"),
    };

    /// <summary>A temp as a value: a struct temp points at its value.</summary>
    private string Value(TempId temp) => IsAggregate(TypeOf(temp)) ? $"*{Temp(temp)}" : Temp(temp);

    /// <summary>
    /// The value inside an optional. After a test — a narrowing, a <c>??</c>, a <c>?.</c> — it
    /// is a read and nothing more. After <c>x!</c> it is the one unwrap that can be wrong, and
    /// panics with <c>LYR-RT0004</c> at the program's line (03 T4 O4). A struct or an optional
    /// inside is aliased in place, not copied out: a narrowed <c>?Point</c> is written where it
    /// lies.
    /// </summary>
    private string Unwrap(OptGet g)
    {
        var option = Temp(g.Option);
        if (IsNiche(TypeOf(g.Option)))
            return g.Checked ? $"{Temp(g.Dest)} = LYR_UNWRAP({option});" : $"{Temp(g.Dest)} = {option};";
        if (IsTagNiche(TypeOf(g.Option)))
            return (g.Checked ? $"if (LYR_UNLIKELY({option}->tag == LYR_ENUM_NONE)) lyr_panic_null(); " : "")
                   + $"{Temp(g.Dest)} = {option};";

        var check = g.Checked ? $"if (LYR_UNLIKELY(!{option}->has)) lyr_panic_null(); " : "";
        return IsAggregate(g.Inner)
            ? $"{check}{Temp(g.Dest)} = &{option}->value;"
            : $"{check}{Temp(g.Dest)} = {option}->value;";
    }

    private string Assign(TempId? dest, string call) => dest switch
    {
        null => $"{call};",
        { } d when IsAggregate(TypeOf(d)) => $"{Storage(d)} = {call}; {Temp(d)} = &{Storage(d)};",
        { } d => $"{Temp(d)} = {call};",
    };

    /// <summary>The arguments of a call: values, except the receiver of a struct method, which
    /// goes as the place its temp aliases (02 M5) — so a <c>mut fn</c> writes the caller's
    /// value and not a copy of it.</summary>
    /// <summary>A value of a variant: the tag, and the payload under its name in the union. The
    /// rest of the union stays zero, so a word no variant wrote is never a stray pointer.</summary>
    private string Variant(NewVariant v)
    {
        var variant = _module.Types[v.Variant.Value];
        var tag = _variants[v.Variant.Value].Tag;
        var fields = string.Join(", ", v.Fields.Select((f, i) => $".{FieldName(variant.FieldNames[i + 1])} = {Value(f)}"));
        var payload = v.Fields.Length == 0 ? "" : $", .as.v{tag} = {{ {fields} }}";
        return $"{Storage(v.Dest)} = ({CType(TypeOf(v.Dest))}){{ .tag = {tag}{payload} }}; {Temp(v.Dest)} = &{Storage(v.Dest)};";
    }

    private string Arguments(IrFunction callee, TempId[] args) =>
        string.Join(", ", args.Select((arg, i) => callee.ReceiverByRef && i == 0 ? Temp(arg) : Value(arg)));

    /// <summary>
    /// A field write. A reference goes through the write barrier (01 L1) wherever the field
    /// lives: in an object, or in a struct that may itself lie inside one — the temp that names
    /// the place does not say which, so the barrier takes the place as it is, a heap object, an
    /// interior pointer or a stack address. A struct value that holds references is stored
    /// through the barrier's value form for the same reason. Both are plain stores in stage 1.
    /// </summary>
    private string Store(StoreField f)
    {
        var place = Temp(f.Object);
        var slot = $"{place}->{Field(f.Type, f.Field)}";
        var type = _module.Types[f.Type.Value].FieldTypes[f.Field.Value];
        if (IsReference(type)) return $"LYR_WRITE_BARRIER({place}, &{slot}, {Value(f.Value)});";
        if (HoldsReferences(type)) return $"LYR_WRITE_BARRIER_VALUE({place}, &{slot}, {Value(f.Value)});";
        return $"{slot} = {Value(f.Value)};";
    }

    /// <summary>An element write, through the barrier when the element is or holds a reference,
    /// as a field write is.</summary>
    private string StoreElement(StoreElem s)
    {
        var element = TypeOf(s.Array) switch
        {
            IrSliceType view => view.Element,
            IrInlineArrayType inline => inline.Element,
            _ => ((IrArrayType)TypeOf(s.Array)).Element,
        };
        var slot = $"{Elements(s.Array, element)}[{Temp(s.Index)}]";
        var check = $"LYR_CHECK_INDEX({Temp(s.Index)}, {Length(s.Array)}); ";
        // Through a view the barrier gets the interior pointer as the place: it takes a place as
        // it is (the note on Store), and the collector understands interior pointers (01 L1).
        var place = TypeOf(s.Array) is IrSliceType ? $"{Temp(s.Array)}.ptr" : Temp(s.Array);
        if (IsReference(element)) return $"{check}LYR_WRITE_BARRIER({place}, &{slot}, {Value(s.Value)});";
        if (HoldsReferences(element)) return $"{check}LYR_WRITE_BARRIER_VALUE({place}, &{slot}, {Value(s.Value)});";
        return $"{check}{slot} = {Value(s.Value)};";
    }

    private string Constant(Const c) => c.Value switch
    {
        BoolConst b => b.Value ? "1" : "0",
        IntConst i => Integer(i.Value, c.Type),
        FloatConst f => Float(f.Value, c.Type),
        CharConst ch => $"(uint32_t){ch.CodePoint}",
        StringConst t => $"(LyrStr *)&{Literal(t.Value)}",
        _ => throw new InvalidOperationException($"the C emitter has no constant for {c.Value.GetType().Name}; the gate let it through"),
    };

    /// <summary>
    /// A string literal as a static object the runtime reads without a copy (V9): one per distinct
    /// text, as UTF-8 in a C literal with octal escapes for everything that is not plain printable
    /// ASCII (octal, because a hex escape would swallow a following hex digit).
    /// </summary>
    private string Literal(string text)
    {
        if (_literals.TryGetValue(text, out var existing)) return existing;
        var bytes = Encoding.UTF8.GetBytes(text);
        var literal = new StringBuilder("\"");
        foreach (var b in bytes)
        {
            if (b >= 0x20 && b < 0x7F && b != '"' && b != '\\' && b != '?') literal.Append((char)b);
            else literal.Append('\\').Append(System.Convert.ToString(b, 8).PadLeft(3, '0'));
        }
        literal.Append('"');
        var name = $"lyr_lit{_literals.Count}";
        _literals[text] = name;
        _constants.AppendLine($"static const LyrStaticStr({bytes.Length + 1}) {name} = LYR_STR_INIT({literal});");
        return name;
    }

    /// <summary>A literal of the type's own width: the IR carries the bits, the type says how to
    /// read them. INT64_MIN has no literal of its own in C (its magnitude does not fit), so it is
    /// spelled through its macro.</summary>
    private string Integer(ulong bits, IrType type)
    {
        var c = CType(type);
        if (!IsSigned(type)) return $"({c})UINT64_C({bits})";
        var value = unchecked((long)bits);
        return value == long.MinValue ? "INT64_MIN" : $"({c})INT64_C({value})";
    }

    /// <summary>A float literal with every bit of its value: the shortest text that reads back
    /// as the same double (.NET's default), which C's compilers parse correctly rounded. A
    /// <c>float32</c> constant is that double narrowed, as the IR holds it. The values C has no
    /// literal for are spelled through the builtins.</summary>
    private string Float(double value, IrType type)
    {
        var c = CType(type);
        if (double.IsNaN(value)) return $"({c})__builtin_nan(\"\")";
        if (double.IsPositiveInfinity(value)) return $"({c})__builtin_inf()";
        if (double.IsNegativeInfinity(value)) return $"({c})-__builtin_inf()";
        var text = value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        if (!text.Contains('.') && !text.Contains('E')) text += ".0";
        return $"({c}){text}";
    }

    private string Binary(BinOp b)
    {
        var lhs = Temp(b.Lhs);
        var rhs = Temp(b.Rhs);
        var type = b.Type;
        // A float knows no overflow (IEEE has its infinities and NaN) and no division fault: the
        // plain C operator, and the library's remainder, which C does not spell as '%'.
        if (IsFloat(type) && b.Kind is IrBinKind.Add or IrBinKind.Sub or IrBinKind.Mul or IrBinKind.Div or IrBinKind.Rem)
            return b.Kind switch
            {
                IrBinKind.Add => $"({lhs} + {rhs})",
                IrBinKind.Sub => $"({lhs} - {rhs})",
                IrBinKind.Mul => $"({lhs} * {rhs})",
                IrBinKind.Div => $"({lhs} / {rhs})",
                _ => type is IrScalarType { Kind: IrScalar.F32 } ? $"fmodf({lhs}, {rhs})" : $"fmod({lhs}, {rhs})",
            };
        return b.Kind switch
        {
            IrBinKind.Add => $"LYR_CHECKED_ADD({lhs}, {rhs})",
            IrBinKind.Sub => $"LYR_CHECKED_SUB({lhs}, {rhs})",
            IrBinKind.Mul => $"LYR_CHECKED_MUL({lhs}, {rhs})",
            IrBinKind.Div => $"LYR_CHECKED_DIV({lhs}, {rhs})",
            IrBinKind.Rem => $"LYR_CHECKED_REM({lhs}, {rhs})",
            IrBinKind.AddWrap => $"LYR_WRAP_ADD({CType(type)}, {Shape(type).Unsigned}, {lhs}, {rhs})",
            IrBinKind.SubWrap => $"LYR_WRAP_SUB({CType(type)}, {Shape(type).Unsigned}, {lhs}, {rhs})",
            IrBinKind.MulWrap => $"LYR_WRAP_MUL({CType(type)}, {Shape(type).Unsigned}, {lhs}, {rhs})",
            IrBinKind.Shl => $"LYR_CHECKED_SHL({CType(type)}, {Shape(type).Unsigned}, {Shape(type).Bits}, {lhs}, {rhs})",
            IrBinKind.Shr => $"LYR_CHECKED_SHR({CType(type)}, {Shape(type).Unsigned}, {Shape(type).Bits}, {lhs}, {rhs})",
            IrBinKind.BitAnd => $"({CType(b.Type)})({lhs} & {rhs})",
            IrBinKind.BitOr => $"({CType(b.Type)})({lhs} | {rhs})",
            IrBinKind.BitXor => $"({CType(b.Type)})({lhs} ^ {rhs})",
            IrBinKind.Lt => $"(uint8_t)({lhs} < {rhs})",
            IrBinKind.Le => $"(uint8_t)({lhs} <= {rhs})",
            IrBinKind.Gt => $"(uint8_t)({lhs} > {rhs})",
            IrBinKind.Ge => $"(uint8_t)({lhs} >= {rhs})",
            // Two strings are equal when their bytes are (the runtime compares; a pointer
            // comparison would ask whether they are one object).
            IrBinKind.Eq when IsString(TypeOf(b.Lhs)) => $"(uint8_t)lyr_str_eq({lhs}, {rhs})",
            IrBinKind.Ne when IsString(TypeOf(b.Lhs)) => $"(uint8_t)!lyr_str_eq({lhs}, {rhs})",
            IrBinKind.Eq => $"(uint8_t)({lhs} == {rhs})",
            IrBinKind.Ne => $"(uint8_t)({lhs} != {rhs})",
            _ => throw new InvalidOperationException($"the C emitter has no case for '{b.Kind}'; the gate let it through"),
        };
    }

    private string Unary(UnOp u)
    {
        var operand = Temp(u.Operand);
        var type = CType(u.Type);
        return u.Kind switch
        {
            // -MIN does not fit: the same check as a subtraction from zero. A float negates its
            // sign bit, -0.0 included, which a subtraction from zero would not give.
            IrUnKind.Neg when IsFloat(u.Type) => $"(-{operand})",
            IrUnKind.Neg => $"LYR_CHECKED_SUB(({type})0, {operand})",
            IrUnKind.Not => $"(uint8_t)!{operand}",
            IrUnKind.BitNot => $"({type})~{operand}",
            _ => throw new InvalidOperationException($"the C emitter has no case for '{u.Kind}'"),
        };
    }

    /// <summary>
    /// <c>as</c> and the implicit widening, one instruction (03 T1c, T1d). Between integers, and
    /// from an integer to a float, the C conversion is the rule: a widening is exact, a narrowing
    /// reduces modulo the width (C defines that for the unsigned target; clang and gcc define it
    /// the same way for the signed one), an integer to a float rounds to nearest. A float to an
    /// integer is undefined in C outside the range, so it goes through the runtime's saturating
    /// conversion; a float to a float is the C conversion, IEEE rounding. A char is its scalar
    /// value as <c>uint32</c>, and the way back checks (RT0009).
    /// </summary>
    private string Conversion(Lyric.Ir.Convert v)
    {
        var operand = Temp(v.Operand);
        var to = CType(v.To);
        if (IsFloat(v.From) && IsInteger(v.To))
        {
            var name = v.To switch
            {
                IrScalarType { Kind: IrScalar.I8 } => "i8", IrScalarType { Kind: IrScalar.I16 } => "i16",
                IrScalarType { Kind: IrScalar.I32 } => "i32", IrScalarType { Kind: IrScalar.I64 } => "i64",
                IrScalarType { Kind: IrScalar.U8 } => "u8", IrScalarType { Kind: IrScalar.U16 } => "u16",
                IrScalarType { Kind: IrScalar.U32 } => "u32", _ => "u64",
            };
            return $"lyr_f64_to_{name}((double){operand})";
        }
        if (v.To is IrScalarType { Kind: IrScalar.Char }) return $"LYR_CHAR_FROM_U32({operand})";
        return $"({to}){operand}";
    }

    private string Terminator(IrTerminator terminator) => terminator switch
    {
        Return { Value: { } v } => $"return {Value(v)};",
        Return => "return;",
        Branch b => $"goto bb{b.Target.Value};",
        CondBranch c => $"if ({Temp(c.Cond)}) goto bb{c.IfTrue.Value}; else goto bb{c.IfFalse.Value};",
        // Reached only after a call that does not return; the verifier vouches for it.
        Unreachable => "__builtin_unreachable();",
        _ => throw new InvalidOperationException($"the C emitter has no case for {terminator.GetType().Name}; the gate let it through"),
    };
}
