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
/// every reference stored into an object goes through the write barrier (L1). A struct is a C struct by value (V2): locals,
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
    public const string Version = "m3-s2";

    private readonly IrModule _module;
    private readonly SourceManager _sources;
    private readonly StringBuilder _out = new();
    private readonly StringBuilder _constants = new();
    private readonly Dictionary<string, string> _literals = new(StringComparer.Ordinal);
    private StringBuilder _fn = null!;
    private IrFunction _function = null!;
    private FileId? _file;

    private CEmitter(IrModule module, SourceManager sources)
    {
        _module = module;
        _sources = sources;
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
        IrRefType r => StructName(r.Type) + " *",
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

    private static bool IsStruct(IrType type) => type is IrStructType;

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
        IrRefType => "NULL",
        IrStructType => "{0}",
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
        IrRefType => (8, 8),
        IrStructType s => Fields(_module.Types[s.Type.Value], 0).Total,
        _ => throw new InvalidOperationException($"the C emitter has no layout for {type}; the gate let it through"),
    };

    /// <summary>The fields of a type laid out from <paramref name="start"/> (8 for a class, past
    /// its header): the offset of each, and the size and alignment of the whole.</summary>
    private (int[] Offsets, (int Size, int Align) Total) Fields(IrTypeDef def, int start)
    {
        var offsets = new int[def.FieldTypes.Length];
        var (at, align) = (start, start > 0 ? 8 : 1);
        for (var i = 0; i < def.FieldTypes.Length; i++)
        {
            var (size, fieldAlign) = LayoutOf(def.FieldTypes[i]);
            at = (at + fieldAlign - 1) / fieldAlign * fieldAlign;
            offsets[i] = at;
            at += size;
            align = Math.Max(align, fieldAlign);
        }
        return (offsets, ((at + align - 1) / align * align, align));
    }

    private static bool IsReference(IrType type) =>
        type is IrRefType or IrScalarType { Kind: IrScalar.String };

    /// <summary>Whether a value of the type holds a reference anywhere: itself, or in a struct
    /// it holds by value.</summary>
    private bool HoldsReferences(IrType type) => type switch
    {
        IrStructType s => _module.Types[s.Type.Value].FieldTypes.Any(HoldsReferences),
        _ => IsReference(type),
    };

    /// <summary>The pointer-sized words of an object that are references, as word indices: a
    /// reference field's own word, and through a struct held by value the words of its
    /// reference fields.</summary>
    private void ReferenceWords(IrTypeDef def, int start, List<int> words)
    {
        var offsets = Fields(def, start).Offsets;
        for (var i = 0; i < def.FieldTypes.Length; i++)
        {
            if (IsReference(def.FieldTypes[i])) words.Add(offsets[i] / 8);
            else if (def.FieldTypes[i] is IrStructType inner)
                ReferenceWords(_module.Types[inner.Type.Value], offsets[i], words);
        }
    }

    private string DescriptorName(TypeId id) => $"lyr_desc_ty{id.Value}_" + Identifier(_module.Types[id.Value].Name);

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
            .Where(i => _module.Types[i].IsStruct || _module.Types[i].IsClass).ToList();
        if (indices.Count == 0) return;

        _out.AppendLine("/* types: a struct is a value, a class an object behind its header */");
        foreach (var i in indices)
        {
            var name = StructName(new TypeId(i));
            _out.AppendLine($"typedef struct {name} {name};");
        }

        var done = new HashSet<int>();
        void Define(int index)
        {
            var def = _module.Types[index];
            if (!(def.IsStruct || def.IsClass) || !done.Add(index)) return;
            foreach (var field in def.FieldTypes)
                if (field is IrStructType inner) Define(inner.Type.Value);
            var name = StructName(new TypeId(index));
            _out.AppendLine($"struct {name} {{");
            if (def.IsClass) _out.AppendLine("    LyrObj header;");
            for (var i = 0; i < def.FieldTypes.Length; i++)
                _out.AppendLine($"    {Declare(def.FieldTypes[i], FieldName(def.FieldNames[i]))};");
            _out.AppendLine("};");
            if (def.IsClass) Descriptor(new TypeId(index), def);
        }
        foreach (var i in indices) Define(i);
        _out.AppendLine();
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
        ReferenceWords(def, 8, words);
        var qualified = def.Module.Length > 0 ? $"{def.Module}.{def.Name}" : def.Name;
        var text = qualified.Replace("\\", "\\\\").Replace("\"", "\\\"");
        if (words.Count == 0)
        {
            _out.AppendLine($"static const LyrDesc {DescriptorName(id)} = {{ sizeof({name}), 0, 0, 0, NULL, \"{text}\", NULL }};");
            return;
        }

        var map = new ulong[(total.Size / 8 + 63) / 64];
        foreach (var word in words) map[word / 64] |= 1UL << (word % 64);
        var bits = string.Join(", ", map.Select(m => $"UINT64_C(0x{m:x})"));
        _out.AppendLine($"static const uint64_t lyr_refmap_ty{id.Value}[] = {{ {bits} }};");
        _out.AppendLine($"static const LyrDesc {DescriptorName(id)} = {{ sizeof({name}), LYR_DESC_HAS_REFS, 0, {map.Length}, lyr_refmap_ty{id.Value}, \"{text}\", NULL }};");
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
            if (IsStruct(temp.Type))
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
        LoadLocal l => IsStruct(l.Type)
            ? $"{Temp(l.Dest)} = &{LocalName(_function.Locals[l.Local.Value])};"
            : $"{Temp(l.Dest)} = {LocalName(_function.Locals[l.Local.Value])};",
        StoreLocal s when IsReceiverPlace(s.Local) => $"*{LocalName(_function.Locals[s.Local.Value])} = {Value(s.Value)};",
        StoreLocal s => $"{LocalName(_function.Locals[s.Local.Value])} = {Value(s.Value)};",
        Call call => Assign(call.Dest, $"{FunctionName(_module.Functions[call.Target.Value].Name)}({Arguments(_module.Functions[call.Target.Value], call.Args)})"),
        CallImport call => Assign(call.Dest, Intrinsics.Call(_module.Imports[call.Target.Value].Name, call.Args.Select(Value).ToArray())),
        NewObject { Result: IrRefType r } n => $"{Temp(n.Dest)} = ({CType(r)})lyr_alloc(&{DescriptorName(r.Type)});",
        NewObject n => $"{Storage(n.Dest)} = ({CType(n.Result)}){{0}}; {Temp(n.Dest)} = &{Storage(n.Dest)};",
        StructCopy c => $"{Storage(c.Dest)} = *{Temp(c.Value)}; {Temp(c.Dest)} = &{Storage(c.Dest)};",
        LoadField f => IsStruct(f.FieldType)
            ? $"{Temp(f.Dest)} = &{Temp(f.Object)}->{Field(f.Type, f.Field)};"
            : $"{Temp(f.Dest)} = {Temp(f.Object)}->{Field(f.Type, f.Field)};",
        StoreField f => Store(f),
        _ => throw new InvalidOperationException($"the C emitter has no case for {op.GetType().Name}; the gate let it through"),
    };

    /// <summary>A temp as a value: a struct temp points at its value.</summary>
    private string Value(TempId temp) => IsStruct(TypeOf(temp)) ? $"*{Temp(temp)}" : Temp(temp);

    private string Assign(TempId? dest, string call) => dest switch
    {
        null => $"{call};",
        { } d when IsStruct(TypeOf(d)) => $"{Storage(d)} = {call}; {Temp(d)} = &{Storage(d)};",
        { } d => $"{Temp(d)} = {call};",
    };

    /// <summary>The arguments of a call: values, except the receiver of a struct method, which
    /// goes as the place its temp aliases (02 M5) — so a <c>mut fn</c> writes the caller's
    /// value and not a copy of it.</summary>
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
