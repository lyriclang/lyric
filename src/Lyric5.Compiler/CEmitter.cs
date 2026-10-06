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
    public const string Version = "r1e";

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

    /// <summary>What each entry of the type table is called in C (<see cref="TypeToken"/>), made
    /// when first asked for.</summary>
    private readonly string?[] _typeTokens;

    /// <summary>What each global is called in C (<see cref="GlobalName"/>).</summary>
    private readonly string[] _globalNames;

    /// <summary>Per entry of the type table, how many entries before it have its module and its
    /// name: the environments of two lambdas in one function are both named after the function.</summary>
    private readonly int[] _twins;

    private readonly string? _stdlibRoot;

    /// <summary>The generic instance this emitter writes the unit of, or <c>null</c> for the
    /// module's own unit (01 C3); <see cref="_scope"/> are the functions that belong to it.</summary>
    private readonly string? _instance;
    private readonly bool _main;
    private readonly List<int> _scope;

    /// <summary>Whether <c>+ - *</c> and negation panic on overflow (03 T2) — or wrap, where a
    /// profile says so (11 W2 P3).</summary>
    private readonly bool _overflowChecks;

    private CEmitter(IrModule module, SourceManager sources, string? stdlibRoot, string? instance, bool overflowChecks)
    {
        _module = module;
        _sources = sources;
        _overflowChecks = overflowChecks;
        _stdlibRoot = stdlibRoot is null ? null : Path.GetFullPath(stdlibRoot).TrimEnd('/', '\\');
        _instance = instance;
        _main = instance is null;
        _scope = Enumerable.Range(0, module.Functions.Count)
            .Where(i => InstanceOf(module.Functions[i].Name) == instance).ToList();
        for (var i = 0; i < module.Types.Count; i++)
            for (var tag = 0; tag < module.Types[i].Variants.Length; tag++)
                _variants[module.Types[i].Variants[tag].Value] = (i, tag);
        _typeTokens = new string?[module.Types.Count];
        _twins = new int[module.Types.Count];
        var named = new Dictionary<(string, string), int>();
        for (var i = 0; i < module.Types.Count; i++)
        {
            var key = (module.Types[i].Module, module.Types[i].Name);
            _twins[i] = named.GetValueOrDefault(key);
            named[key] = _twins[i] + 1;
        }
        _globalNames = module.Globals
            .Select(g => "lyr_g_" + Token(g.Module.Length > 0 ? $"{g.Module}.{g.Name}" : g.Name)).ToArray();
        if (_main) RequireDistinctNames();
    }

    /// <summary>
    /// Two entries under one C name would be one struct for two layouts, and the C compiler would
    /// say so about a line nobody wrote — or, for two globals, say nothing. The names are made so
    /// that it cannot happen (<see cref="TypeToken"/>); this is the place that would notice if it
    /// did. Once per program, in the module's own unit.
    /// </summary>
    private void RequireDistinctNames()
    {
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < _module.Types.Count; i++)
            if (!seen.TryAdd(TypeToken(i), i))
                throw new InvalidOperationException(
                    $"the types '{Qualified(_module.Types[seen[TypeToken(i)]])}' and '{Qualified(_module.Types[i])}' "
                    + $"would both be '{TypeToken(i)}' in C");
        seen.Clear();
        for (var i = 0; i < _globalNames.Length; i++)
            if (!seen.TryAdd(_globalNames[i], i))
                throw new InvalidOperationException(
                    $"the globals '{_module.Globals[seen[_globalNames[i]]].Name}' and '{_module.Globals[i].Name}' "
                    + $"would both be '{_globalNames[i]}' in C");
    }

    /// <summary>One translation unit of the emission: the module's own (<see cref="Instance"/>
    /// is <c>null</c>) or the cache unit of one generic instance (01 C3), named by it.</summary>
    public sealed record Unit(string? Instance, string Text);

    /// <summary>
    /// The C of the module, as units: the module's own — its types, descriptors, globals and
    /// every function that is no generic instance — and one per generic instance, holding the
    /// instance's functions and nothing the program would change under them, so the build caches
    /// its object by content and compiles it once (01 C3). <paramref name="sources"/> answers the
    /// <c>#line</c> positions.
    /// </summary>
    /// <param name="stdlibRoot">The directory of the standard library, when known: a source
    /// under it is named in <c>#line</c> relative to the directory's parent (<c>stdlib5/std/core.lyr</c>),
    /// so the emitted C is the same on every machine — a golden compares it byte for byte, and
    /// the build cache keys on it.</param>
    /// <param name="overflowChecks">Whether integer overflow panics (03 T2), as every profile has
    /// it unless it says otherwise (11 W2 P3): without, <c>+ - *</c> and negation wrap.</param>
    public static IReadOnlyList<Unit> Emit(IrModule module, SourceManager sources, string? stdlibRoot = null, bool overflowChecks = true)
    {
        var units = new List<Unit> { new(null, new CEmitter(module, sources, stdlibRoot, null, overflowChecks).Text()) };
        var instances = module.Functions.Select(f => InstanceOf(f.Name)).OfType<string>()
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
        foreach (var instance in instances)
            units.Add(new Unit(instance, new CEmitter(module, sources, stdlibRoot, instance, overflowChecks).Text()));
        return units;
    }

    /// <summary>All units as one text, for reading (<c>--emit c</c>) and for the goldens. The
    /// units are separate translation units and this text is not one: a struct two of them need
    /// is defined in both.</summary>
    public static string Join(IReadOnlyList<Unit> units) =>
        string.Concat(units.Select(u => u.Instance is null ? u.Text : $"\n/* ==== unit: {u.Instance} ==== */\n{u.Text}"));

    /// <summary>
    /// The cache unit a function belongs to (01 C3): the generic instance whose body it is or
    /// lies in — <c>std.core.arrayOf&lt;int&gt;</c>; <c>std.core.Range&lt;int&gt;</c> for every
    /// method of that instance; the same for a lambda lifted out of one — or <c>null</c> for the
    /// module itself. Read off the IR name: an instance carries its arguments in angle brackets
    /// right behind an identifier, where a synthesized name (<c>&lt;lambda0&gt;</c>,
    /// <c>&lt;globals&gt;</c>) opens them behind a dot or at the start.
    /// </summary>
    public static string? InstanceOf(string irName)
    {
        for (var i = 1; i < irName.Length; i++)
        {
            if (irName[i] != '<' || !(char.IsAsciiLetterOrDigit(irName[i - 1]) || irName[i - 1] == '_')) continue;
            var depth = 0;
            for (var j = i; j < irName.Length; j++)
            {
                if (irName[j] == '<') depth++;
                else if (irName[j] == '>' && irName[j - 1] != '-' && --depth == 0) return irName[..(j + 1)];
            }
            return null;
        }
        return null;
    }

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

    /// <summary><c>lyr_g_&lt;module&gt;_&lt;name&gt;</c>: a global by the module that declares it
    /// and its name there — not by its place in the table, which moves with every global another
    /// module gains.</summary>
    private string GlobalName(int index) => _globalNames[index];

    /// <summary>A name as C spells it, without the <c>lyr_</c> in front: dots are underscores, and
    /// where a character had to go a short hash of the whole name follows
    /// (<see cref="FunctionName"/>).</summary>
    private static string Token(string name) => FunctionName(name)[4..];

    /// <summary>A token with its length in front, for a name made of several: the reader of
    /// <c>lyr_fn_ref12_app_main_Box_to_i64</c> knows where the type's name ends, so two different
    /// types cannot spell the same C name.</summary>
    private static string Sized(string token) => $"{token.Length}_{token}";

    /// <summary>
    /// What an entry of the type table is called in C, without a prefix: its module and its name
    /// there — <c>std_core_ParseError</c>; a generic instance with a hash of its arguments behind
    /// it. Not its number in the table: the table holds every type of the standard library, and a
    /// number made each new one there rename every type of every program.
    /// </summary>
    /// <remarks>
    /// What no module declares is named by what it is. A variant is its enum's, a tuple and the
    /// cell of a captured variable are what they hold (the lowering interns both by that), an
    /// environment is the function's its lambda stands in — the second lambda's there with a 2
    /// behind it. Those tokens begin with a digit, which no module's name does, so they stand
    /// apart from every declared type's.
    /// </remarks>
    private string TypeToken(int type) => _typeTokens[type] ??= NameType(type);

    private string NameType(int type)
    {
        var def = _module.Types[type];
        if (_variants.TryGetValue(type, out var of))
        {
            var owner = _module.Types[of.Enum].Name;
            var own = def.Name.StartsWith(owner + ".", StringComparison.Ordinal) ? def.Name[(owner.Length + 1)..] : def.Name;
            return Sized(TypeToken(of.Enum)) + "_" + Identifier(own);
        }
        if (def.Name == "<tuple>") return "0tup" + string.Concat(def.FieldTypes.Select(f => "_" + Mangle(f)));
        if (def.Name == "<cell>") return "0cell_" + Mangle(def.FieldTypes[0]);
        var token = def.Module.Length == 0 ? "0" + Token(def.Name) : Token($"{def.Module}.{def.Name}");
        // Entries of one name are told apart by their order among themselves — which only
        // another entry of that very name can move.
        return _twins[type] == 0 ? token : $"{token}_{_twins[type] + 1}";
    }

    public string CType(IrType type) => type switch
    {
        IrStructType s => StructName(s.Type),
        IrEnumType e => StructName(e.Type),
        IrRefType r => StructName(r.Type) + " *",
        IrArrayType => "LyrArr *",
        IrSliceType s => SliceName(s),
        IrInlineArrayType ia => InlineName(ia),
        IrFunctionType f => FnName(f),
        IrCoroutineType => "LyrCoro *",
        IrInterfaceType => "LyrIface",
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
        // A place (03 T12) is the address of the value where it lies.
        IrPlaceType p => CType(p.Value) + " *",
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
    /// An optional in the niche (01 V5): around a reference — a class value, a string, a
    /// coroutine — the pointer itself says whether there is a value, and the optional costs
    /// nothing. Around an optional there is no niche left: <c>??Node</c> tells "absent" from
    /// "present and null", and needs its flag.
    /// </summary>
    private static bool IsNiche(IrType type) =>
        type is IrOptionalType { Inner: IrRefType or IrArrayType or IrCoroutineType or IrScalarType { Kind: IrScalar.String } };

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
    private string OptionalName(IrOptionalType type) => "lyr_opt_" + Mangle(type.Inner);

    /// <summary><c>lyr_slice_&lt;element&gt;</c>: a view of <c>T[]</c> (03 T13 A2), a pointer into
    /// the elements and a length — two words, a value C passes and copies as one.</summary>
    private string SliceName(IrSliceType type) => "lyr_slice_" + Mangle(type.Element);

    /// <summary><c>lyr_inl&lt;N&gt;_&lt;element&gt;</c>: an inline array (03 T13 A4), a struct around a
    /// C array so that C copies it as a value.</summary>
    private string InlineName(IrInlineArrayType type) => $"lyr_inl{type.Length}_" + Mangle(type.Element);

    /// <summary><c>lyr_fn_&lt;signature&gt;</c>: a function value (01 V8), a pointer to the code and
    /// the environment it runs in — two words, a value. The code takes the environment first,
    /// as <c>void *</c>; a function without one gets a thunk that drops it.</summary>
    private string FnName(IrFunctionType type) => "lyr_" + Mangle(type);

    /// <summary>The C type of the code pointer of a function value: the environment first, the
    /// caller's error slot last (03 T17) — whether the type throws or not. One code pointer per
    /// signature makes a value with a smaller set fit a type with a larger one as it is (05 E2 K6):
    /// nothing is wrapped, and code that cannot throw never writes the slot.</summary>
    private string CodePointer(IrFunctionType type, string name) =>
        $"{CType(type.Return)} (*{name})(void *{string.Concat(type.Parameters.Select(p => ", " + CType(p)))}, LyrErr **)";

    /// <summary>The name a function's code takes as a function value's target: itself when it
    /// takes an environment, its thunk otherwise (a free function, a lambda without captures).</summary>
    private string CodeOf(IrFunction target) =>
        TakesEnvironment(target) ? FunctionName(target.Name) : "lyr_thunk_" + FunctionName(target.Name)[4..];

    /// <summary>Whether the function's parameter 0 is a closure's environment (the lowering
    /// names it so): then the C signature takes it as <c>void *</c> and the body casts.</summary>
    private static bool TakesEnvironment(IrFunction function) =>
        function.ParamCount > 0 && function.Locals[0].Name == "<env>";

    private string Mangle(IrType type) => type switch
    {
        IrScalarType { Kind: IrScalar.String } => "str",
        IrScalarType s => s.Kind.ToString().ToLowerInvariant(),
        IrStructType s => "ty" + Sized(TypeToken(s.Type.Value)),
        IrEnumType e => "en" + Sized(TypeToken(e.Type.Value)),
        IrRefType r => "ref" + Sized(TypeToken(r.Type.Value)),
        IrArrayType a => "arr_" + Mangle(a.Element),
        IrSliceType s => "slice_" + Mangle(s.Element),
        IrInlineArrayType ia => $"inl{ia.Length}_" + Mangle(ia.Element),
        IrFunctionType f => "fn" + string.Concat(f.Parameters.Select(p => "_" + Mangle(p))) + "_to_" + Mangle(f.Return),
        IrCoroutineType c => "coro_" + Mangle(c.Yield) + "_to_" + Mangle(c.Result),
        IrInterfaceType i => "iface" + Sized(TypeToken(i.Type.Value)),
        IrOptionalType o => "opt_" + Mangle(o.Inner),
        IrPlaceType p => "place_" + Mangle(p.Value),
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
        IrFunctionType f => $"fn({string.Join(", ", f.Parameters.Select(Display))}) -> {Display(f.Return)}",
        IrCoroutineType c => IsVoid(c.Result) ? $"Coroutine<{Display(c.Yield)}>" : $"Coroutine<{Display(c.Yield)}, {Display(c.Result)}>",
        IrInterfaceType i => Qualified(_module.Types[i.Type.Value]),
        IrOptionalType o => "?" + Display(o.Inner),
        IrPlaceType p => "&" + Display(p.Value),
        _ => type.ToString() ?? "?",
    };

    private static string Qualified(IrTypeDef def) => def.Module.Length > 0 ? $"{def.Module}.{def.Name}" : def.Name;

    /// <summary><c>lyr_ty_&lt;module&gt;_&lt;Name&gt;</c> (<see cref="TypeToken"/>): what a
    /// debugger shows, and the same in every program that uses the type.</summary>
    private string StructName(TypeId id) => "lyr_ty_" + TypeToken(id.Value);

    private static string Identifier(string name) =>
        new string(name.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_').ToArray());

    private static string FieldName(string name) => "f_" + Identifier(name);

    private string Field(TypeId type, FieldId field) => FieldName(_module.Types[type.Value].FieldNames[field.Value]);

    private IrType TypeOf(TempId temp) => _function.Temps[temp.Value].Type;

    /// <summary>A zero of the type, for the declarations: every C local starts defined.</summary>
    private static string Zero(IrType type) => type switch
    {
        IrScalarType { Kind: IrScalar.String } => "NULL",
        IrRefType or IrArrayType or IrCoroutineType or IrPlaceType => "NULL",
        _ when IsNiche(type) => "NULL",
        _ when IsAggregate(type) => "{0}",
        IrSliceType or IrFunctionType or IrInterfaceType => "{0}",
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
        IrRefType or IrArrayType or IrCoroutineType => (8, 8),
        IrSliceType or IrFunctionType or IrInterfaceType => (16, 8),
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

    /// <summary>A reference: one pointer-sized word the collector follows — a coroutine's among
    /// them, whose object keeps its stack and its environment. An optional in the niche is one,
    /// null when absent.</summary>
    private static bool IsReference(IrType type) =>
        type is IrRefType or IrArrayType or IrCoroutineType or IrScalarType { Kind: IrScalar.String } || IsNiche(type);

    /// <summary>Whether a value of the type holds a reference anywhere: itself, or in a struct
    /// or an optional it holds by value.</summary>
    private bool HoldsReferences(IrType type) => type switch
    {
        IrStructType s => _module.Types[s.Type.Value].FieldTypes.Any(HoldsReferences),
        IrEnumType e => _module.Types[e.Type.Value].Variants
            .Any(v => Payload(_module.Types[v.Value]).Any(HoldsReferences)),
        IrOptionalType o when !IsNiche(o) => HoldsReferences(o.Inner),
        IrSliceType => true, // its pointer, into the array's elements
        IrFunctionType => true, // its environment
        IrInterfaceType => true, // its data word
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
        else if (type is IrFunctionType) words.Add(offset / 8 + 1); // the environment, second word
        else if (type is IrInterfaceType) words.Add(offset / 8); // the data word; the table is static
        else if (type is IrInlineArrayType inline)
            for (var i = 0; i < inline.Length; i++)
                ReferenceWords(inline.Element, offset + i * LayoutOf(inline.Element).Size, words, ref ambiguous);
        else if (type is IrStructType inner) ReferenceWords(_module.Types[inner.Type.Value], offset, words, ref ambiguous);
        else if (type is IrEnumType) ambiguous |= HoldsReferences(type);
        else if (type is IrOptionalType optional) ReferenceWords(optional.Inner, offset, words, ref ambiguous);
    }

    private string DescriptorName(TypeId id) => "lyr_desc_ty_" + TypeToken(id.Value);

    /// <summary>The descriptor of <c>T[]</c>, one per element type (V10).</summary>
    private string ArrayDescriptor(IrType element) => "lyr_desc_arr_" + Mangle(element);

    /// <summary>The elements of an array or a view temp, typed: <c>LYR_ARR_DATA(t, T)[i]</c>, or
    /// the view's pointer.</summary>
    private string Elements(TempId array, IrType element) => TypeOf(array) switch
    {
        IrSliceType => $"{Temp(array)}.ptr",
        IrInlineArrayType => $"{Temp(array)}->v", // the temp aliases the value's storage
        IrScalarType { Kind: IrScalar.String } => $"(uint8_t *){Temp(array)}->bytes", // a string's bytes (10 S1)
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

    // --- the unit --------------------------------------------------------------------------------

    private IEnumerable<IrFunction> Scope() => _scope.Select(i => _module.Functions[i]);

    private IEnumerable<IrOp> ScopeOps() => Scope().SelectMany(f => f.Blocks).SelectMany(b => b.Insts);

    private string Text()
    {
        _out.AppendLine("/* Generated by lyric5 from the IR of this module. Do not edit: the source is the .lyr. */");
        if (_instance is { } instance)
            _out.AppendLine($"/* The unit of the generic instance '{instance}' (01 C3): its functions, the types they reach, and nothing else. */");
        _out.AppendLine("#include \"lyr/lyr.h\"");
        _out.AppendLine("#include <stdint.h>");
        _out.AppendLine("#include <math.h>");
        _out.AppendLine();

        // The C functions the program declares (extern "C", 11 W4): a prototype each, in the
        // platform's C ABI; the linker finds the symbol in the native part (07 B5) or a library.
        var foreign = _module.Imports.Where(i => Intrinsics.IsForeign(i.Name)).ToList();
        foreach (var import in foreign)
        {
            var parameters = import.ParamTypes.Length == 0 ? "void" : string.Join(", ", import.ParamTypes.Select(CType));
            _out.AppendLine($"extern {CType(import.ReturnType)} {import.Name[2..]}({parameters});");
        }
        if (foreign.Count > 0) _out.AppendLine();

        Structs();

        // The module's globals (07 V5): variables of the module's unit, filled by the initializer
        // before the entry runs; an instance's unit declares the ones it touches. Static data is
        // a root the collector scans (01 L1).
        if (_main && _module.Globals.Count > 0)
        {
            _out.AppendLine("/* module-level bindings */");
            for (var i = 0; i < _module.Globals.Count; i++)
                _out.AppendLine($"{Declare(_module.Globals[i].Type, GlobalName(i))} = {Zero(_module.Globals[i].Type)};");
            _out.AppendLine();
        }
        else if (!_main)
        {
            var touched = ScopeOps().Select(op => op switch
            {
                LoadGlobal l => l.Global.Value,
                StoreGlobal s => s.Global.Value,
                AddrGlobal a => a.Global.Value,
                _ => -1,
            }).Where(i => i >= 0).Distinct().Order().ToList();
            if (touched.Count > 0)
            {
                _out.AppendLine("/* module-level bindings, defined by the module's unit */");
                foreach (var i in touched) _out.AppendLine($"extern {Declare(_module.Globals[i].Type, GlobalName(i))};");
                _out.AppendLine();
            }
        }

        // The module's unit declares every function; an instance's unit its own and the ones
        // they name, wherever those are defined — every function has external linkage, so a
        // unit may call across.
        var declared = _main ? Enumerable.Range(0, _module.Functions.Count)
            : _scope.Concat(ScopeOps().Select(op => op switch
            {
                Call c => c.Target.Value,
                MakeClosure m => m.Target.Value,
                MakeCoroutine mc => mc.Body.Value,
                _ => -1,
            }).Where(i => i >= 0)).Distinct().Order();
        var prototypes = new StringBuilder();
        prototypes.AppendLine("/* prototypes */");
        foreach (var index in declared) prototypes.Append(Signature(_module.Functions[index])).AppendLine(";");

        // A function used as a value without an environment gets a thunk that takes and drops
        // one, so every function value is called the same way (01 V8) — and takes the error slot,
        // forwarded where the function throws (03 T17). Static, in every unit that makes the value:
        // two thunks of one function are two spellings of the same call.
        var thunked = ScopeOps()
            .OfType<MakeClosure>().Where(m => m.Environment is null).Select(m => m.Target.Value).Distinct().Order().ToList();
        if (thunked.Count > 0)
        {
            prototypes.AppendLine();
            prototypes.AppendLine("/* thunks: a function as a value, without an environment */");
            foreach (var index in thunked)
            {
                var target = _module.Functions[index];
                var parameters = target.Locals.Take(target.ParamCount).ToList();
                var signature = string.Concat(parameters.Select(p => ", " + Declare(p.Type, LocalName(p))));
                var arguments = parameters.Select(LocalName).Concat(target.Throws ? ["lyr_err"] : []);
                var call = $"{FunctionName(target.Name)}({string.Join(", ", arguments)})";
                prototypes.AppendLine($"static {CType(target.ReturnType)} {CodeOf(target)}(void *lyr_env{signature}, LyrErr **lyr_err) {{ (void)lyr_env; "
                    + (target.Throws ? "" : "(void)lyr_err; ")
                    + (IsVoid(target.ReturnType) ? $"{call}; }}" : $"return {call}; }}"));
            }
        }

        // A coroutine (06 N2) starts from an environment — the arguments it was called with and the
        // place its body leaves the result, on the heap, where the coroutine keeps it alive — and
        // runs a C body around the IR one: the call, then the result or the error left on the
        // coroutine for the pull that ran into the end (05 E10). Static, in every unit that makes
        // one, as the thunks are.
        var started = ScopeOps().OfType<MakeCoroutine>().DistinctBy(m => m.Body.Value).OrderBy(m => m.Body.Value).ToList();
        if (started.Count > 0)
        {
            prototypes.AppendLine();
            prototypes.AppendLine("/* coroutines: the environment a body starts from, and the body as the runtime runs it */");
            foreach (var type in started.Select(m => m.Type).DistinctBy(t => Mangle(t)))
                prototypes.AppendLine($"static const LyrDesc {CoroutineDesc(type)} = {{ 0, 0, 0, 0, NULL, \"{Display(type).Replace("\\", "\\\\").Replace("\"", "\\\"")}\", NULL }};");
            foreach (var made in started) prototypes.Append(CoroutineBody(made.Body.Value, made.Type));
        }

        // The interface tables (01 V7): one per (type, interface) row, defined in the module's
        // unit with the thunks they point at, declared in an instance's unit that lifts a value.
        var tables = new StringBuilder();
        if (_main && _module.Impls.Count > 0)
        {
            tables.AppendLine("/* interface tables: the descriptor, then the implementation of every slot */");
            foreach (var row in _module.Impls) tables.Append(Table(row));
            // The conformance lists (03 T11): per concrete type its rows — the interface's
            // identity and the table — NULL-terminated, where the type's descriptor points.
            foreach (var group in _module.Impls.GroupBy(r => r.Type.Value).OrderBy(g => g.Key))
                tables.AppendLine($"const LyrItable {ItableName(group.Key)}[] = {{ "
                    + string.Join(", ", group.Select(r => $"{{ {IfaceId(r.Interface.Value)}, &{VtName(r.Type.Value, r.Interface.Value)} }}"))
                    + " , { NULL, NULL } };");
        }
        else if (!_main)
        {
            var lifted = ScopeOps().OfType<MakeInterface>().Select(m => (Concrete: m.Concrete.Value, Interface: m.Interface.Value)).Distinct().Order().ToList();
            if (lifted.Count > 0)
            {
                tables.AppendLine("/* interface tables, defined by the module's unit */");
                foreach (var (concrete, iface) in lifted) tables.AppendLine($"extern const {VtType(iface)} {VtName(concrete, iface)};");
            }
        }

        // The members of Error the runtime reports through (05 E6 O4, E8): defined by the module's
        // unit wherever an error can end the program, declared by an instance's unit that panics.
        var adapters = !ErrorsEndTheProgram() ? ""
            : _main ? ReportAdapters(define: true)
            : Scope().Any(f => f.Blocks.Any(b => b.Terminator is PanicError)) ? ReportAdapters(define: false) : "";

        var body = new StringBuilder();
        foreach (var function in Scope()) body.Append(Function(function));
        if (_main && _module.EntryFunction is { } entry) body.Append(Entry(_module.Functions[entry.Value]));

        if (_constants.Length > 0) _out.AppendLine("/* string literals */").Append(_constants).AppendLine();
        _out.Append(prototypes).AppendLine();
        if (tables.Length > 0) _out.Append(tables).AppendLine();
        if (adapters.Length > 0) _out.Append(adapters).AppendLine();
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
        // The module's unit defines every type the PROGRAM reaches and its descriptor, whichever
        // unit uses them; an instance's unit defines the types its functions reach — the same
        // text, since a C struct may be defined in every translation unit that needs it — and
        // declares the descriptors, whose addresses are the identities and are defined once, in
        // the module's unit. A type nothing reaches is in neither: the type table holds every type
        // the lowering came across, and a program that prints one line carried the string
        // builder's struct and the hashers' descriptors.
        var reachable = _main ? ReachedByProgram() : Reachable();
        var indices = Enumerable.Range(0, _module.Types.Count)
            .Where(i => (_module.Types[i].IsStruct || _module.Types[i].IsClass || _module.Types[i].IsEnum || _module.Types[i].IsInterface)
                        && reachable.Contains(i)).ToList();
        // Every type a function names, for the optionals among them: an optional outside the
        // niche is a C struct of its own and is defined once, wherever it is first needed.
        var named = (_main ? _module.Functions : Scope())
            .SelectMany(f => f.Locals.Select(l => l.Type).Concat(f.Temps.Select(t => t.Type)).Append(f.ReturnType))
            .Concat(indices.SelectMany(i => _module.Types[i].FieldTypes))
            .ToList();
        var used = named.Select(t => t is IrPlaceType p ? p.Value : t)
            .Where(t => (t is IrOptionalType && !IsNiche(t)) || t is IrSliceType or IrInlineArrayType or IrFunctionType).ToList();
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
            else if (type is IrFunctionType f) { foreach (var p in f.Parameters) Element(p); Element(f.Return); }
            else if (type is IrCoroutineType c) { Element(c.Yield); Element(c.Result); }
            else if (type is IrPlaceType p) Element(p.Value);
        }
        foreach (var type in named) Element(type);
        if (indices.Count == 0 && used.Count == 0 && elements.Count == 0) return;

        _out.AppendLine("/* types: a struct is a value, a class an object behind its header, an enum a tag and a union */");
        foreach (var i in indices)
        {
            // An interface is its table's type (01 V7): the value itself is 'LyrIface' for every
            // interface, and only the table knows the slots.
            if (_module.Types[i].IsInterface)
            {
                _out.AppendLine($"typedef struct {VtType(i)} {VtType(i)};");
                _out.AppendLine(_main
                    ? $"const char {IfaceId(i)}[] = \"{Qualified(_module.Types[i]).Replace("\\", "\\\\").Replace("\"", "\\\"")}\";"
                    : $"extern const char {IfaceId(i)}[];");
                continue;
            }
            var name = StructName(new TypeId(i));
            _out.AppendLine($"typedef struct {name} {name};");
            foreach (var variant in _module.Types[i].Variants)
                _out.AppendLine($"typedef struct {StructName(variant)} {StructName(variant)};");
        }

        var done = new HashSet<int>();
        var optionals = new HashSet<string>(StringComparer.Ordinal);

        // What holding a type BY VALUE needs defined first: the struct itself, or the optional's
        // own struct after what it holds. Marked written only once what it holds is: the path in
        // can lead back to it — a field '?Error' requires Error's table, whose 'cause()' slot
        // returns '?Error' — and the inner request must then write it, before the table needs it.
        void Require(IrType type)
        {
            if (type is IrPlaceType place) Require(place.Value);
            else if (type is IrStructType held) Define(held.Type.Value);
            else if (type is IrInterfaceType dyn) Define(dyn.Type.Value);
            else if (type is IrEnumType chosen) Define(chosen.Type.Value);
            else if (IsTagNiche(type)) Require(((IrOptionalType)type).Inner);
            else if (type is IrOptionalType optional && !IsNiche(optional) && !optionals.Contains(OptionalName(optional)))
            {
                Require(optional.Inner);
                if (optionals.Add(OptionalName(optional)))
                    _out.AppendLine($"typedef struct {{ {Declare(optional.Inner, "value")}; uint8_t has; }} {OptionalName(optional)};");
            }
            else if (type is IrFunctionType fn && !optionals.Contains(FnName(fn)))
            {
                foreach (var p in fn.Parameters) Require(p);
                Require(fn.Return);
                if (optionals.Add(FnName(fn)))
                {
                    _out.AppendLine($"typedef struct {{ {CodePointer(fn, "fn")}; void *env; }} {FnName(fn)};");
                    _out.AppendLine($"_Static_assert(sizeof({FnName(fn)}) == 16, \"layout of {FnName(fn)}\");");
                }
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
            if (def.IsInterface) { if (done.Add(index)) DefineInterface(index, Require); return; }
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
        // The boxes (V7, D12): a value type copied to the heap at its transition to an interface
        // value, behind a header, with a descriptor of its own that maps the value's references.
        foreach (var boxed in Boxed())
        {
            Require(_module.Types[boxed].IsEnum ? new IrEnumType(new TypeId(boxed)) : new IrStructType(new TypeId(boxed)));
            DefineBox(boxed);
        }
        _out.AppendLine();
    }

    // --- interfaces (01 V7) ----------------------------------------------------------------------

    private string VtType(int iface) => "lyr_vt_ty_" + TypeToken(iface);

    /// <summary>The table of one conformance: the interface's name, then the type's, each with
    /// its length (<see cref="Sized"/>).</summary>
    private string VtName(int concrete, int iface) => $"lyr_vt_{Sized(TypeToken(iface))}_{Sized(TypeToken(concrete))}";

    private string ItableName(int concrete) => "lyr_itab_ty_" + TypeToken(concrete);

    /// <summary>An interface's identity at runtime: the address of its name, defined once in the
    /// module's unit (03 T11).</summary>
    private string IfaceId(int iface) => "lyr_ifid_ty_" + TypeToken(iface);

    /// <summary>The conformance list of a concrete type, or <c>NULL</c> where no row names it.</summary>
    private string ItablesOf(int concrete) =>
        _module.Impls.Any(r => r.Type.Value == concrete) ? ItableName(concrete) : "NULL";

    /// <summary>The descriptor an interface value's table begins with (01 V7).</summary>
    private string DescOfIface(TempId value) => $"(*(const LyrDesc *const *){Temp(value)}.vt)";

    /// <summary>The descriptor a concrete type's values carry behind an interface: the class's own,
    /// the box's for a struct or an enum.</summary>
    private string DescOfConcrete(int type) => _module.Types[type].IsClass ? DescriptorName(new TypeId(type)) : BoxDesc(type);

    private string BoxName(int type) => "lyr_box_ty_" + TypeToken(type);

    private string BoxDesc(int type) => "lyr_desc_box_ty_" + TypeToken(type);

    private readonly Dictionary<int, (IrType[] Params, IrType Return)[]> _slots = new();

    /// <summary>
    /// The signature of every slot of an interface, the receiver left out: read off the first
    /// table row — every row's functions agree, conformance saw to that — else off the calls
    /// through the interface; a slot nothing names takes nothing and returns nothing. The IR's
    /// interface entry carries the slot names only; its signatures are the rows'.
    /// </summary>
    private (IrType[] Params, IrType Return)[] SlotSignatures(int iface)
    {
        if (_slots.TryGetValue(iface, out var known)) return known;
        var count = _module.Types[iface].MethodSlots.Length;
        var sigs = new (IrType[], IrType)[count];
        var found = new bool[count];
        foreach (var row in _module.Impls)
        {
            if (row.Interface.Value != iface) continue;
            for (var k = 0; k < count; k++)
            {
                var f = _module.Functions[row.Methods[k].Value];
                sigs[k] = (f.Locals.Take(f.ParamCount).Skip(1).Select(l => l.Type).ToArray(), f.ReturnType);
                found[k] = true;
            }
            break;
        }
        if (found.Any(seen => !seen))
            foreach (var function in _module.Functions)
                foreach (var op in function.Blocks.SelectMany(b => b.Insts))
                    if (op is CallVirt c && c.Interface.Value == iface && !found[c.Slot])
                    {
                        sigs[c.Slot] = (c.Args.Skip(1).Select(a => function.Temps[a.Value].Type).ToArray(), c.ReturnType);
                        found[c.Slot] = true;
                    }
        for (var k = 0; k < count; k++)
            if (!found[k]) sigs[k] = ([], new IrScalarType(IrScalar.Void));
        return _slots[iface] = sigs;
    }

    private string SlotPointer((IrType[] Params, IrType Return) sig, string name, bool throws) =>
        $"{(IsVoid(sig.Return) ? "void" : CType(sig.Return))} (*{name})(LyrIface{string.Concat(sig.Params.Select(p => ", " + CType(p)))}"
        + $"{(throws ? ", LyrErr **" : "")})";

    /// <summary>The table's type: the concrete type's descriptor first, then one function
    /// pointer per slot, each taking the interface value — the same for a default, whose
    /// receiver IS the interface value, and for a thunk around a concrete method.</summary>
    private void DefineInterface(int index, Action<IrType> require)
    {
        var sigs = SlotSignatures(index);
        foreach (var (parameters, result) in sigs)
        {
            foreach (var p in parameters) require(p);
            if (!IsVoid(result)) require(result);
        }
        _out.AppendLine($"struct {VtType(index)} {{");
        _out.AppendLine("    const LyrDesc *desc;");
        for (var k = 0; k < sigs.Length; k++) _out.AppendLine($"    {SlotPointer(sigs[k], $"s{k}", SlotThrows(index, k))};");
        _out.AppendLine("};");
    }

    /// <summary>The value types boxed in this unit: lifted by one of its functions, and — in the
    /// module's unit, where the tables are defined — every one a table row names.</summary>
    private IEnumerable<int> Boxed()
    {
        bool IsValue(int type) => _module.Types[type].IsStruct || _module.Types[type].IsEnum;
        var lifted = ScopeOps().OfType<MakeInterface>().Select(m => m.Concrete.Value).Where(IsValue);
        var tested = ScopeOps().Select(op => op switch { TypeTest t => t.Target.Value, Downcast d => d.Target.Value, _ => -1 })
            .Where(t => t >= 0 && IsValue(t));
        var rows = _main ? _module.Impls.Select(r => r.Type.Value).Where(IsValue) : [];
        return lifted.Concat(tested).Concat(rows).Distinct().Order();
    }

    private void DefineBox(int type)
    {
        var def = _module.Types[type];
        var inner = def.IsEnum ? (IrType)new IrEnumType(new TypeId(type)) : new IrStructType(new TypeId(type));
        var (size, align) = LayoutOf(inner);
        if (align > 8) throw new InvalidOperationException($"a box of {Display(inner)} would need {align}-byte alignment");
        var total = 8 + (size + 7) / 8 * 8;
        var name = BoxName(type);
        _out.AppendLine($"typedef struct {{ LyrObj header; {Declare(inner, "value")}; }} {name};");
        _out.AppendLine($"_Static_assert(sizeof({name}) == {total}, \"layout of {name}\");");
        _out.AppendLine($"_Static_assert(offsetof({name}, value) == 8, \"layout of {name}\");");
        if (!_main) { _out.AppendLine($"extern const LyrDesc {BoxDesc(type)};"); return; }

        var words = new List<int>();
        var ambiguous = false;
        ReferenceWords(inner, 8, words, ref ambiguous);
        var text = $"box<{Qualified(def)}>".Replace("\\", "\\\\").Replace("\"", "\\\"");
        var flags = ambiguous ? "LYR_DESC_HAS_REFS | LYR_DESC_CONSERVATIVE" : "LYR_DESC_HAS_REFS";
        var itables = ItablesOf(type);
        if (itables != "NULL") _out.AppendLine($"extern const LyrItable {itables}[];");
        if (words.Count == 0)
        {
            _out.AppendLine($"const LyrDesc {BoxDesc(type)} = {{ sizeof({name}), {(ambiguous ? flags : "0")}, 0, 0, NULL, \"{text}\", {itables} }};");
            return;
        }
        var map = new ulong[(total / 8 + 63) / 64];
        foreach (var word in words) map[word / 64] |= 1UL << (word % 64);
        var bits = string.Join(", ", map.Select(m => $"UINT64_C(0x{m:x})"));
        _out.AppendLine($"static const uint64_t lyr_refmap_box{type}[] = {{ {bits} }};");
        _out.AppendLine($"const LyrDesc {BoxDesc(type)} = {{ sizeof({name}), {flags}, 0, {map.Length}, lyr_refmap_box{type}, \"{text}\", {itables} }};");
    }

    /// <summary>
    /// One table row (V7): the descriptor, then per slot the implementation — a default as it is,
    /// since its receiver is the interface value; a concrete method behind a thunk that takes the
    /// value out of the data word (the object, or the box's payload — as the place, for a
    /// struct's or an enum's method, which writes through it).
    /// </summary>
    private string Table(IrImpl row)
    {
        var text = new StringBuilder();
        var iface = row.Interface.Value;
        var concrete = row.Type.Value;
        var sigs = SlotSignatures(iface);
        var isClass = _module.Types[concrete].IsClass;
        var entries = new List<string>();
        for (var k = 0; k < sigs.Length; k++)
        {
            var f = _module.Functions[row.Methods[k].Value];
            var throws = SlotThrows(iface, k);
            // An implementation throws at most what its slot declares (05 K6): one that throws
            // nothing ignores the slot's error parameter; the other way round the sema refused.
            if (f.Throws && !throws)
                throw new InvalidOperationException($"'{f.Name}' throws, and slot {k} of {Display(new IrInterfaceType(row.Interface))} does not");
            if (f.ParamCount > 0 && f.Locals[0].Type is IrInterfaceType)
            {
                // A default: its receiver IS the interface value, its error slot the slot's own.
                if (f.Throws != throws)
                    throw new InvalidOperationException($"the default '{f.Name}' and its slot disagree about throwing");
                entries.Add(FunctionName(f.Name));
                continue;
            }
            var (parameters, result) = sigs[k];
            var thunk = $"{VtName(concrete, iface)}_s{k}";
            var receiver = isClass ? $"({CType(new IrRefType(row.Type))})self.data"
                : f.ReceiverByRef ? $"&(({BoxName(concrete)} *)self.data)->value"
                : $"(({BoxName(concrete)} *)self.data)->value";
            var call = $"{FunctionName(f.Name)}({receiver}{string.Concat(parameters.Select((_, j) => $", a{j}"))}{(f.Throws ? ", lyr_err" : "")})";
            text.AppendLine($"static {(IsVoid(result) ? "void" : CType(result))} {thunk}(LyrIface self{string.Concat(parameters.Select((p, j) => ", " + Declare(p, $"a{j}")))}{(throws ? ", LyrErr **lyr_err" : "")}) "
                + $"{{ {(IsVoid(result) ? call + ";" : "return " + call + ";")} }}");
            entries.Add(thunk);
        }
        var desc = isClass ? DescriptorName(row.Type) : BoxDesc(concrete);
        text.AppendLine($"const {VtType(iface)} {VtName(concrete, iface)} = {{ &{desc}, {string.Join(", ", entries)} }};");
        return text.ToString();
    }

    // --- coroutines (06 N2) ------------------------------------------------------------------------

    /// <summary>The descriptor of a coroutine type: the object is the runtime's, traced by its own
    /// kind, so the descriptor names the type and describes nothing.</summary>
    private string CoroutineDesc(IrCoroutineType type) => "lyr_desc_" + Mangle(type);

    /// <summary>The environment and the runner of a coroutine's body, named after the body — not
    /// after its number among the program's functions.</summary>
    private string CoroutineEnv(int body) => "lyr_coenv_" + FunctionName(_module.Functions[body].Name)[4..];

    private string CoroutineRunner(int body) => "lyr_corun_" + FunctionName(_module.Functions[body].Name)[4..];

    /// <summary>Whether a body needs an environment: it takes arguments, or leaves a result.</summary>
    private static bool HasEnvironment(IrFunction body, IrCoroutineType type) => body.ParamCount > 0 || !IsVoid(type.Result);

    /// <summary>
    /// What a coroutine's body needs beside its IR function: the environment — a heap object of
    /// the arguments, <c>a0</c>…, and the result, laid out and described like a class — and the
    /// runner the runtime calls on the coroutine's stack. The runner calls the body with the
    /// arguments (the receiver of a value's method as the place in the environment, 02 M5), then
    /// leaves on the coroutine what the pull that ran into the end reads: the result's address,
    /// or the error and no result (05 E10).
    /// </summary>
    private string CoroutineBody(int index, IrCoroutineType type)
    {
        var body = _module.Functions[index];
        var text = new StringBuilder();
        var parameters = body.Locals.Take(body.ParamCount).Select(l => l.Type).ToList();
        var returns = !IsVoid(type.Result);
        var env = CoroutineEnv(index);
        if (HasEnvironment(body, type))
        {
            var fields = returns ? parameters.Append(type.Result).ToList() : parameters;
            var names = parameters.Select((_, i) => $"a{i}").Concat(returns ? ["result"] : []).ToList();
            text.AppendLine($"typedef struct {{ LyrObj header;{string.Concat(fields.Select((f, i) => $" {Declare(f, names[i])};"))} }} {env};");
            var (offsets, total) = Fields(fields, 8);
            text.AppendLine($"_Static_assert(sizeof({env}) == {total.Size}, \"layout of {env}\");");
            for (var i = 0; i < offsets.Length; i++)
                text.AppendLine($"_Static_assert(offsetof({env}, {names[i]}) == {offsets[i]}, \"layout of {env}\");");
            var words = new List<int>();
            var ambiguous = false;
            for (var i = 0; i < fields.Count; i++) ReferenceWords(fields[i], offsets[i], words, ref ambiguous);
            var name = body.Name.Replace("\\", "\\\\").Replace("\"", "\\\"");
            var flags = ambiguous ? "LYR_DESC_HAS_REFS | LYR_DESC_CONSERVATIVE" : words.Count > 0 ? "LYR_DESC_HAS_REFS" : "0";
            if (words.Count == 0)
                text.AppendLine($"static const LyrDesc {env}_desc = {{ sizeof({env}), {flags}, 0, 0, NULL, \"{name}\", NULL }};");
            else
            {
                var map = new ulong[(total.Size / 8 + 63) / 64];
                foreach (var word in words) map[word / 64] |= 1UL << (word % 64);
                var bits = string.Join(", ", map.Select(m => $"UINT64_C(0x{m:x})"));
                text.AppendLine($"static const uint64_t {env}_refmap[] = {{ {bits} }};");
                text.AppendLine($"static const LyrDesc {env}_desc = {{ sizeof({env}), {flags}, 0, {map.Length}, {env}_refmap, \"{name}\", NULL }};");
            }
        }

        // The error slot as any call passes it (SlotArgument): the body's own where it throws, none
        // to write for a generator lambda's body that cannot — it takes one either way (03 T17).
        var arguments = parameters.Select((_, i) => body.ReceiverByRef && i == 0 ? $"&lyr_env->a{i}" : $"lyr_env->a{i}")
            .Concat(body.Throws ? ["&lyr_e"] : TakesEnvironment(body) ? ["NULL"] : []);
        var call = $"{FunctionName(body.Name)}({string.Join(", ", arguments)})";
        var run = new StringBuilder($"static void {CoroutineRunner(index)}(void *lyr_arg) {{ ");
        run.Append(HasEnvironment(body, type) ? $"{env} *lyr_env = lyr_arg; " : "(void)lyr_arg; ");
        if (body.Throws) run.Append("LyrErr *lyr_e = NULL; ");
        run.Append(returns ? $"{Declare(type.Result, "lyr_r")} = {call}; " : $"{call}; ");
        run.Append("LyrCoro *lyr_co = lyr_coro_current(); ");
        if (body.Throws)
            run.Append("if (LYR_UNLIKELY(lyr_e != NULL)) { lyr_coro_set_error(lyr_co, lyr_e); lyr_coro_set_transfer(lyr_co, NULL); return; } ");
        run.Append(returns
            ? $"{StoreInto("lyr_env", "lyr_env->result", type.Result, "lyr_r")} lyr_coro_set_transfer(lyr_co, &lyr_env->result); }}"
            : "lyr_coro_set_transfer(lyr_co, NULL); }");
        text.AppendLine(run.ToString());
        return text.ToString();
    }

    /// <summary>A store into a slot of a fresh object, through the barrier as a field write goes.</summary>
    private string StoreInto(string place, string slot, IrType type, string value) =>
        IsReference(type) ? $"LYR_WRITE_BARRIER({place}, &{slot}, {value});"
        : HoldsReferences(type) ? $"LYR_WRITE_BARRIER_VALUE({place}, &{slot}, {value});"
        : $"{slot} = {value};";

    /// <summary>A coroutine over its environment (06 A1): the arguments stored, then the runtime's
    /// object — not started; nothing of the body runs before the first pull.</summary>
    private string StartCoroutine(MakeCoroutine m)
    {
        var body = _module.Functions[m.Body.Value];
        var create = $"{Temp(m.Dest)} = lyr_coro_new(&{CoroutineDesc(m.Type)}, {CoroutineRunner(m.Body.Value)}, ";
        if (!HasEnvironment(body, m.Type)) return create + "NULL, 0);" + Cleanup(m);
        var env = CoroutineEnv(m.Body.Value);
        var stores = string.Concat(m.Args.Select((a, i) => " " + StoreInto("lyr_ce", $"lyr_ce->a{i}", body.Locals[i].Type, Value(a))));
        return $"{{ {env} *lyr_ce = lyr_alloc(&{env}_desc);{stores} {create}lyr_ce, 0); }}" + Cleanup(m);
    }

    /// <summary>A body with cleanup marks its coroutine, so the debug profile can report it dropped
    /// without <c>close()</c> (06 A5).</summary>
    private string Cleanup(MakeCoroutine m) =>
        (_module.Functions[m.Body.Value].Cleanup ? $" lyr_coro_set_cleanup({Temp(m.Dest)});" : "")
        + (YieldsDynamically ? $" lyr_coro_set_yield_key({Temp(m.Dest)}, \"{YieldKey(m.Type.Yield)}\");" : "");

    /// <summary>Whether a yield outside a coroutine's own body stands anywhere in the program
    /// (06 §10a): then every coroutine records its yield type, for such a yield to be held to.</summary>
    private bool YieldsDynamically => _yieldsDynamically ??=
        _module.Functions.Any(f => f.Blocks.Any(b => b.Insts.Any(op => op is YieldSuspend { Dynamic: true })));

    private bool? _yieldsDynamically;

    /// <summary>A yield type as Lyric writes it, in a C string: what a dynamic yield and the running
    /// coroutine compare, and what the panic names when they differ.</summary>
    private string YieldKey(IrType type) => Display(type).Replace("\\", "\\\\").Replace("\"", "\\\"");

    /// <summary>
    /// <c>co.close()</c> (06 A5): the runtime unwinds a suspended coroutine; the error its body
    /// ended with is taken, and the <c>Cancelled</c> the close itself threw — told by its
    /// descriptor — is dropped. Anything else goes on to the error branch where the coroutine's type
    /// throws; where it does not, nothing else can come.
    /// </summary>
    private string Close(CoroutineClose c)
    {
        var co = Temp(c.Coroutine);
        var cancelled = $"&{DescriptorName(c.Cancelled)}";
        // The Cancelled the body ended with is dropped — but not what a defer threw on the way
        // out, which was suppressed into it: the first of those goes on (the review's M6-10).
        return c.Throws
            ? $"lyr_coro_close({co}); lyr_e = lyr_coro_take_error({co}); "
              + $"if (lyr_e != NULL && *(const LyrDesc *const *)lyr_e->value.vt == {cancelled}) lyr_e = lyr_err_unsuppress(lyr_e);"
            : $"lyr_coro_close({co}); (void)lyr_coro_take_error({co});";
    }

    /// <summary>
    /// <c>co.next()</c> (06 N2 A2): a coroutine that is not done runs to its next yield or past its
    /// end — one that runs, or belongs to another thread, the runtime refuses (RT0014). Done, it
    /// answers null — false where it yields nothing — and a coroutine whose type throws hands over
    /// the error its body ended with, once, to the error branch that follows (05 E10). Else the
    /// value is copied out of the suspended frame, where the yield left its address.
    /// </summary>
    private string Pull(ResumePull r)
    {
        var co = Temp(r.Coroutine);
        var run = $"if (lyr_coro_status({co}) != LYR_CORO_DONE) lyr_coro_resume({co}); ";
        var error = r.Throws ? $"lyr_e = lyr_coro_take_error({co});" : "";
        if (IsVoid(r.YieldType))
            return run + $"{Temp(r.Dest)} = (uint8_t)(lyr_coro_status({co}) != LYR_CORO_DONE);"
                   + (r.Throws ? $" if (!{Temp(r.Dest)}) {error}" : "");
        return run + $"if (lyr_coro_status({co}) == LYR_CORO_DONE) {{ {(r.Throws ? error + " " : "")}{None(r.Dest)} }} "
               + $"else {{ {Some(r.Dest, $"*({CType(r.YieldType)} *)lyr_coro_transfer({co})")} }}";
    }

    /// <summary>An optional temp made absent: the niche's null, the tag no variant has, or the
    /// flag clear.</summary>
    private string None(TempId dest) =>
        IsTagNiche(TypeOf(dest)) ? $"{Storage(dest)} = ({CType(TypeOf(dest))}){{ .tag = LYR_ENUM_NONE }}; {Temp(dest)} = &{Storage(dest)};"
        : IsNiche(TypeOf(dest)) ? $"{Temp(dest)} = NULL;"
        : $"{Storage(dest)} = ({CType(TypeOf(dest))}){{0}}; {Temp(dest)} = &{Storage(dest)};";

    /// <summary>An optional temp made present with <paramref name="value"/>, a C expression of
    /// the inner type.</summary>
    private string Some(TempId dest, string value) =>
        IsTagNiche(TypeOf(dest)) ? $"{Storage(dest)} = {value}; {Temp(dest)} = &{Storage(dest)};"
        : IsNiche(TypeOf(dest)) ? $"{Temp(dest)} = {value};"
        : $"{Storage(dest)} = ({CType(TypeOf(dest))}){{ .value = {value}, .has = 1 }}; {Temp(dest)} = &{Storage(dest)};";

    /// <summary>The composite types an instance unit's functions reach: named by a local, a temp
    /// or a return, or held by one of those through any field, at any depth. A variant brings
    /// its enum, an enum its variants.</summary>
    private HashSet<int> Reachable() => Reached(Scope(), ScopeOps(), program: false);

    /// <summary>
    /// The types the program reaches: what any of its functions names — the pruning has left
    /// only the functions its roots reach (01 L11) —, what its globals hold, every row of the
    /// interface tables, and the table the report of an escaping error goes through.
    /// </summary>
    private HashSet<int> ReachedByProgram() =>
        Reached(_module.Functions, _module.Functions.SelectMany(f => f.Blocks).SelectMany(b => b.Insts), program: true);

    private HashSet<int> Reached(IEnumerable<IrFunction> functions, IEnumerable<IrOp> ops, bool program)
    {
        var reached = new HashSet<int>();
        void Visit(IrType type)
        {
            switch (type)
            {
                case IrStructType s: Add(s.Type.Value); break;
                case IrEnumType e: Add(e.Type.Value); break;
                case IrRefType r: Add(r.Type.Value); break;
                case IrOptionalType o: Visit(o.Inner); break;
                case IrArrayType a: Visit(a.Element); break;
                case IrSliceType sl: Visit(sl.Element); break;
                case IrInlineArrayType ia: Visit(ia.Element); break;
                case IrFunctionType f: foreach (var p in f.Parameters) Visit(p); Visit(f.Return); break;
                case IrCoroutineType c: Visit(c.Yield); Visit(c.Result); break;
                case IrInterfaceType i: Add(i.Type.Value); break;
                case IrPlaceType p: Visit(p.Value); break;
            }
        }
        void Add(int index)
        {
            if (!reached.Add(index)) return;
            var def = _module.Types[index];
            if (def.IsInterface)
                foreach (var (parameters, result) in SlotSignatures(index)) { foreach (var p in parameters) Visit(p); Visit(result); }
            foreach (var field in def.FieldTypes) Visit(field);
            foreach (var variant in def.Variants) Add(variant.Value);
            if (_variants.TryGetValue(index, out var of)) Add(of.Enum);
        }
        foreach (var function in functions)
        {
            foreach (var local in function.Locals) Visit(local.Type);
            foreach (var temp in function.Temps) Visit(temp.Type);
            Visit(function.ReturnType);
        }
        // A test or a downcast names its target by id alone: a descriptor to declare — as does a
        // close, the Cancelled it drops.
        foreach (var op in ops)
            if (op is TypeTest tt) Add(tt.Target.Value);
            else if (op is Downcast dc) Add(dc.Target.Value);
            else if (op is CoroutineClose cc) Add(cc.Cancelled.Value);

        if (program)
        {
            foreach (var global in _module.Globals) Visit(global.Type);
            foreach (var row in _module.Impls) { Add(row.Type.Value); Add(row.Interface.Value); }
            foreach (var attribute in _module.Attributes)
                if (attribute.TargetKind == IrAttributeTarget.Type) Add(attribute.Target);
            if (ErrorSlots().Index >= 0) Add(ErrorSlots().Index);
        }

        return reached;
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
        if (!_main) { _out.AppendLine($"extern const LyrDesc {name};"); return; }
        var words = new List<int>();
        var ambiguous = false;
        ReferenceWords(element, 0, words, ref ambiguous);
        var text = Display(element).Replace("\\", "\\\\").Replace("\"", "\\\"");
        var flags = "LYR_DESC_ARRAY" + (words.Count > 0 || ambiguous ? " | LYR_DESC_HAS_REFS" : "") + (ambiguous ? " | LYR_DESC_CONSERVATIVE" : "");
        if (words.Count == 0)
        {
            _out.AppendLine($"const LyrDesc {name} = {{ (uint32_t)offsetof(LyrArr, data), {flags}, sizeof({c}), 0, NULL, \"{text}[]\", NULL }};");
            return;
        }
        var map = new ulong[(size / 8 + 63) / 64];
        foreach (var word in words) map[word / 64] |= 1UL << (word % 64);
        var bits = string.Join(", ", map.Select(m => $"UINT64_C(0x{m:x})"));
        _out.AppendLine($"static const uint64_t lyr_refmap_arr_{Mangle(element)}[] = {{ {bits} }};");
        _out.AppendLine($"const LyrDesc {name} = {{ (uint32_t)offsetof(LyrArr, data), {flags}, sizeof({c}), {map.Length}, lyr_refmap_arr_{Mangle(element)}, \"{text}[]\", NULL }};");
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
    /// is word 0 and never one — and its qualified name. Constant, defined once in the module's
    /// unit and declared in every other; its address is the type's identity. The asserts hold
    /// the emitter's layout against the compiler's.
    /// </summary>
    private void Descriptor(TypeId id, IrTypeDef def)
    {
        var name = StructName(id);
        var (offsets, total) = Fields(def, 8);
        _out.AppendLine($"_Static_assert(sizeof({name}) == {total.Size}, \"layout of {name}\");");
        for (var i = 0; i < offsets.Length; i++)
            _out.AppendLine($"_Static_assert(offsetof({name}, {FieldName(def.FieldNames[i])}) == {offsets[i]}, \"layout of {name}\");");

        if (!_main) { _out.AppendLine($"extern const LyrDesc {DescriptorName(id)};"); return; }
        var words = new List<int>();
        var ambiguous = false;
        ReferenceWords(def, 8, words, ref ambiguous);
        var qualified = def.Module.Length > 0 ? $"{def.Module}.{def.Name}" : def.Name;
        var text = qualified.Replace("\\", "\\\\").Replace("\"", "\\\"");
        var flags = ambiguous ? "LYR_DESC_HAS_REFS | LYR_DESC_CONSERVATIVE" : "LYR_DESC_HAS_REFS";
        var itables = ItablesOf(id.Value);
        if (itables != "NULL") _out.AppendLine($"extern const LyrItable {itables}[];");
        if (words.Count == 0)
        {
            _out.AppendLine($"const LyrDesc {DescriptorName(id)} = {{ sizeof({name}), {(ambiguous ? flags : "0")}, 0, 0, NULL, \"{text}\", {itables} }};");
            return;
        }

        var map = new ulong[(total.Size / 8 + 63) / 64];
        foreach (var word in words) map[word / 64] |= 1UL << (word % 64);
        var bits = string.Join(", ", map.Select(m => $"UINT64_C(0x{m:x})"));
        _out.AppendLine($"static const uint64_t lyr_refmap_ty_{TypeToken(id.Value)}[] = {{ {bits} }};");
        _out.AppendLine($"const LyrDesc {DescriptorName(id)} = {{ sizeof({name}), {flags}, 0, {map.Length}, lyr_refmap_ty_{TypeToken(id.Value)}, \"{text}\", {itables} }};");
    }

    /// <summary>A parameter: a value, except the receiver of a struct method, which is the
    /// caller's place (02 M5).</summary>
    private string Parameter(IrFunction function, IrLocal local) =>
        function.ReceiverByRef && local.Id.Value == 0
            ? $"{CType(local.Type)} *{LocalName(local)}"
            : TakesEnvironment(function) && local.Id.Value == 0
                ? "void *lyr_env"
                : Declare(local.Type, LocalName(local));

    private bool IsReceiverPlace(LocalId local) => _function.ReceiverByRef && local.Value == 0;

    private string Signature(IrFunction function)
    {
        var parameters = function.Locals.Take(function.ParamCount).Select(p => Parameter(function, p)).ToList();
        // The caller's error slot (01 L5 E1), the last parameter: a throwing function's own keep
        // their places, and a set slot is the status. A closure's code takes it either way: every
        // function value is called with one (03 T17).
        if (function.Throws || TakesEnvironment(function)) parameters.Add("LyrErr **lyr_err");
        // External linkage: an instance's unit calls the module's functions and the module the
        // instance's, and the names are unique by construction (01 C3, C4).
        //
        // The program's main stays a frame of its own: 'lyr_entry' calls it, and a trace ends at
        // lyr_entry's frame — main folded into it took the program's frames with it (Windows,
        // whose symbolizer gives the physical frame before the inlined ones).
        var keep = _module.EntryFunction is { } entry && ReferenceEquals(_module.Functions[entry.Value], function)
            ? "LYR_NOINLINE " : "";
        return $"{keep}{CType(function.ReturnType)} {FunctionName(function.Name)}({(parameters.Count == 0 ? "void" : string.Join(", ", parameters))})";
    }

    /// <summary>Whether a function handles errors at all: the in-flight error local exists only
    /// where something can set or read it.</summary>
    private bool UsesErrors(IrFunction function) =>
        function.Blocks.Any(b => b.Terminator is Throw or ErrorBranch or Propagate or PanicError
            || b.Insts.Any(op => op is CurrentError or ClearError or StashError or RestoreError or SuppressError
                || op is Call c && _module.Functions[c.Target.Value].Throws
                || op is CallIndirect { Throws: true }
                || op is ResumePull { Throws: true }
                || op is CoroutineClose { Throws: true }
                || op is CallVirt v && SlotThrows(v.Interface.Value, v.Slot)));

    /// <summary>Whether slot <paramref name="slot"/> of an interface takes the error slot (05 E2).</summary>
    private bool SlotThrows(int iface, int slot) =>
        _module.Types[iface].SlotThrows is { } flags && slot < flags.Length && flags[slot];

    /// <summary>The error slot as the last argument of a call of a throwing function.</summary>
    private static string ErrorArgument(bool throws, int arguments) =>
        !throws ? "" : arguments == 0 ? "&lyr_e" : ", &lyr_e";

    /// <summary>The slot a direct call hands its callee: the caller's for a function that throws,
    /// none to write for a closure's code that cannot (it takes one either way), else nothing.</summary>
    private static string SlotArgument(IrFunction callee, int arguments) =>
        callee.Throws ? ErrorArgument(true, arguments)
        : TakesEnvironment(callee) ? (arguments == 0 ? "NULL" : ", NULL")
        : "";

    /// <summary>A zero of the type as an expression, for the return that leaves with an error: the
    /// caller reads its error slot and nothing else.</summary>
    private string ZeroValue(IrType type) => Zero(type) == "{0}" ? $"({CType(type)}){{0}}" : Zero(type);

    /// <summary>
    /// The program's <c>main</c>: the runtime's <c>lyr_run_main</c> around the entry function,
    /// whose value is the exit code masked to 0..255 by the runtime (11 C5); a <c>void</c> entry
    /// exits with 0. Where main is a task (06 T6), <c>lyr_run_main_task</c>, which runs the same
    /// function as main's context under std.task's loop.
    /// </summary>
    private string Entry(IrFunction entry)
    {
        var name = FunctionName(entry.Name);
        var text = new StringBuilder();
        text.AppendLine();
        text.AppendLine("/* the program */");
        // The globals are filled first (07 V5 G2), in declaration order, then the entry runs.
        var init = _module.GlobalInit is { } id ? FunctionName(_module.Functions[id.Value].Name) + "(); " : "";
        if (entry.Throws)
        {
            // An error that escapes main (05 E6 O4): its message and its causes on the error
            // writer, and the exit code 1 — through Error's own table, which the runtime cannot name.
            var (message, cause) = ReportNames();
            var call = $"{name}(&lyr_e)";
            text.AppendLine($"static int64_t lyr_entry(void) {{ {init}LyrErr *lyr_e = NULL; "
                + (IsVoid(entry.ReturnType) ? $"{call}; int64_t lyr_r = 0; " : $"int64_t lyr_r = {call}; ")
                + $"if (LYR_UNLIKELY(lyr_e != NULL)) return lyr_err_report(lyr_e, {message}, {cause}); return lyr_r; }}");
            text.AppendLine($"int main(int argc, char **argv) {{ return {RunMain("lyr_entry")}; }}");
            return text.ToString();
        }
        if (IsVoid(entry.ReturnType) || init.Length > 0)
        {
            text.AppendLine(IsVoid(entry.ReturnType)
                ? $"static int64_t lyr_entry(void) {{ {init}{name}(); return 0; }}"
                : $"static int64_t lyr_entry(void) {{ {init}return {name}(); }}");
            name = "lyr_entry";
        }
        text.AppendLine($"int main(int argc, char **argv) {{ return {RunMain(name)}; }}");
        return text.ToString();
    }

    /// <summary>The runtime's start around the program's main function, as a task under std.task's
    /// loop where the program waits (06 T6).</summary>
    private string RunMain(string program) =>
        _module.TaskMain is { } loop
            ? $"lyr_run_main_task(argc, argv, {program}, {FunctionName(_module.Functions[loop.Value].Name)})"
            : $"lyr_run_main(argc, argv, {program})";

    /// <summary>Whether an error can end the program: <c>main</c> throws (05 E6 O4), or a
    /// <c>try!</c> panics with an error's message (E4, E8). Asked of the whole module, so every
    /// unit agrees on whether the adapters exist.</summary>
    private bool ErrorsEndTheProgram() =>
        _module.EntryFunction is { } entry && _module.Functions[entry.Value].Throws
        || _module.Functions.Any(f => f.Blocks.Any(b => b.Terminator is PanicError));

    /// <summary><c>std.core</c>'s <c>Error</c> with the slots the report calls, or -1 where no type
    /// of the program conforms — nothing can be thrown then, and the report gets no members.</summary>
    private (int Index, int Message, int Cause) ErrorSlots()
    {
        var index = Enumerable.Range(0, _module.Types.Count).FirstOrDefault(i =>
            _module.Types[i].IsInterface && _module.Types[i].Name == "Error" && _module.Types[i].Module == "std.core", -1);
        if (index < 0 || !_module.Impls.Any(r => r.Interface.Value == index)) return (-1, -1, -1);
        var slots = _module.Types[index].MethodSlots;
        var message = Array.IndexOf(slots, "message");
        var cause = Array.IndexOf(slots, "cause");
        return message < 0 || cause < 0 ? (-1, -1, -1) : (index, message, cause);
    }

    /// <summary>The adapters the runtime calls, or <c>NULL</c> for each where there are none.</summary>
    private (string Message, string Cause) ReportNames() =>
        ErrorSlots().Index < 0 ? ("NULL", "NULL") : ("lyr_error_message", "lyr_error_cause");

    /// <summary>
    /// The two members of <c>Error</c> the runtime calls (05 E6 O1, O4, E8): <c>message()</c> and
    /// <c>cause()</c>, each through the value's own table. External, so an instance's unit that
    /// panics with an error reaches the module's definition.
    /// </summary>
    private string ReportAdapters(bool define)
    {
        var (index, message, cause) = ErrorSlots();
        if (index < 0) return "";
        var text = new StringBuilder();
        text.AppendLine("/* the members of Error the runtime reports through */");
        if (!define)
        {
            text.AppendLine("const LyrStr *lyr_error_message(LyrIface e);");
            text.AppendLine("int lyr_error_cause(LyrIface e, LyrIface *next);");
            return text.ToString();
        }
        var causeType = SlotSignatures(index)[cause].Return;
        text.AppendLine($"const LyrStr *lyr_error_message(LyrIface e) {{ return ((const {VtType(index)} *)e.vt)->s{message}(e); }}");
        text.AppendLine($"int lyr_error_cause(LyrIface e, LyrIface *next) {{ {CType(causeType)} c = ((const {VtType(index)} *)e.vt)->s{cause}(e); "
            + "*next = c.value; return c.has; }");
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

        // The environment arrives as 'void *' — one code pointer type per function type, whatever
        // the environment's own type — and is cast to it once, here.
        if (TakesEnvironment(function))
        {
            _fn.AppendLine($"    {Declare(function.Locals[0].Type, LocalName(function.Locals[0]))} = ({CType(function.Locals[0].Type)})lyr_env;");
            // A closure's code that cannot throw has the slot every function value takes, unused.
            if (!function.Throws) _fn.AppendLine("    (void)lyr_err;");
        }
        foreach (var local in function.Locals.Skip(function.ParamCount))
            _fn.AppendLine($"    {Declare(local.Type, LocalName(local))} = {Zero(local.Type)};");
        // The in-flight error (01 L5 E1): what a throw sets, what a call that failed wrote, what a
        // catch reads and clears.
        if (UsesErrors(function)) _fn.AppendLine("    LyrErr *lyr_e = NULL;");
        // The errors set aside while a defer body runs with one in flight (05 E7).
        foreach (var stash in function.Blocks.SelectMany(b => b.Insts).OfType<StashError>().Select(s => s.Stash).Distinct().Order())
            _fn.AppendLine($"    LyrErr *lyr_s{stash} = NULL;");
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
            _fn.AppendLine($"#line {position.Line} \"{SourcePath(span.File).Replace("\\", "\\\\").Replace("\"", "\\\"")}\"");
        }
        else _fn.AppendLine($"#line {position.Line}");
    }

    /// <summary>The path a <c>#line</c> names: a standard-library source relative to the
    /// library's parent directory, with forward slashes; anything else as the source manager
    /// has it.</summary>
    private string SourcePath(FileId file)
    {
        var path = _sources.GetPath(file);
        if (_stdlibRoot is null) return path;
        var full = Path.IsPathRooted(path) ? Path.GetFullPath(path) : path;
        var parent = Path.GetDirectoryName(_stdlibRoot) ?? "";
        if (!full.StartsWith(_stdlibRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !full.StartsWith(_stdlibRoot + '/', StringComparison.Ordinal)) return path;
        return Path.GetRelativePath(parent, full).Replace('\\', '/');
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
        // A global is storage like a local: an aggregate is aliased in place, anything else read.
        LoadGlobal l => IsAggregate(l.Type)
            ? $"{Temp(l.Dest)} = &{GlobalName(l.Global.Value)};"
            : $"{Temp(l.Dest)} = {GlobalName(l.Global.Value)};",
        StoreGlobal g => $"{GlobalName(g.Global.Value)} = {Value(g.Value)};",
        // Places (03 T12). The receiver of a struct method already is one.
        AddrLocal a => IsReceiverPlace(a.Local)
            ? $"{Temp(a.Dest)} = {LocalName(_function.Locals[a.Local.Value])};"
            : $"{Temp(a.Dest)} = &{LocalName(_function.Locals[a.Local.Value])};",
        AddrField a => $"{Temp(a.Dest)} = &{Temp(a.Object)}->{Field(a.Type, a.Field)};",
        AddrElem a => $"LYR_CHECK_INDEX({Temp(a.Index)}, {Length(a.Array)}); "
            + $"{Temp(a.Dest)} = &{Elements(a.Array, a.Element)}[{Temp(a.Index)}];",
        AddrGlobal a => $"{Temp(a.Dest)} = &{GlobalName(a.Global.Value)};",
        // A struct temp aliases the value where it lies, as it aliases a local.
        LoadPlace l => IsAggregate(l.Type)
            ? $"{Temp(l.Dest)} = {Temp(l.Place)};"
            : $"{Temp(l.Dest)} = *{Temp(l.Place)};",
        StorePlace s => StoreThrough(s),
        Call call => Assign(call.Dest, $"{FunctionName(_module.Functions[call.Target.Value].Name)}({Arguments(_module.Functions[call.Target.Value], call.Args)}"
            + $"{SlotArgument(_module.Functions[call.Target.Value], call.Args.Length)})"),
        CallImport call => Assign(call.Dest, Intrinsics.Call(_module.Imports[call.Target.Value].Name, call.Args.Select(Value).ToArray())),
        NewObject { Result: IrRefType r } n => $"{Temp(n.Dest)} = ({CType(r)})lyr_alloc(&{DescriptorName(r.Type)});",
        NewObject n => $"{Storage(n.Dest)} = ({CType(n.Result)}){{0}}; {Temp(n.Dest)} = &{Storage(n.Dest)};",
        StructCopy c => $"{Storage(c.Dest)} = *{Temp(c.Value)}; {Temp(c.Dest)} = &{Storage(c.Dest)};",
        CopyValue c => $"{Storage(c.Dest)} = *{Temp(c.Value)}; {Temp(c.Dest)} = &{Storage(c.Dest)};",
        // An inline array (A4): its storage filled element by element, or from one value.
        // An interface value (01 V7): an object as it is; a value copied into a fresh box. The
        // call goes through the table the value carries, by slot.
        MakeInterface m when _module.Types[m.Concrete.Value].IsClass =>
            $"{Temp(m.Dest)} = (LyrIface){{ {Temp(m.Value)}, &{VtName(m.Concrete.Value, m.Interface.Value)} }};",
        MakeInterface m =>
            $"{{ {BoxName(m.Concrete.Value)} *lyr_box = lyr_alloc(&{BoxDesc(m.Concrete.Value)}); lyr_box->value = {Value(m.Value)}; "
            + $"{Temp(m.Dest)} = (LyrIface){{ lyr_box, &{VtName(m.Concrete.Value, m.Interface.Value)} }}; }}",
        CallVirt c => Assign(c.Dest, $"((const {VtType(c.Interface.Value)} *){Temp(c.Args[0])}.vt)->s{c.Slot}({string.Join(", ", c.Args.Select(Value))}"
            + $"{ErrorArgument(SlotThrows(c.Interface.Value, c.Slot), c.Args.Length)})"),
        // The in-flight error's value, and the clause that takes it (05 E4, E9).
        CurrentError e => $"{Temp(e.Dest)} = lyr_e->value;",
        ClearError => "lyr_e = NULL;",
        // A defer body with an error in flight (05 E7): set aside, back, or the body's own appended
        // to it as suppressed — the first wins.
        StashError s => $"lyr_s{s.Stash} = lyr_e; lyr_e = NULL;",
        RestoreError r => $"lyr_e = lyr_s{r.Stash};",
        SuppressError s => $"lyr_err_suppress(lyr_s{s.Stash}, lyr_e); lyr_e = lyr_s{s.Stash};",
        // 'x is T' (03 T11): the descriptor the value's table begins with, or the conformance
        // list behind that descriptor for an interface.
        TypeTest t when _module.Types[t.Target.Value].IsInterface =>
            $"{Temp(t.Dest)} = (lyr_iface_find({DescOfIface(t.Value)}, {IfaceId(t.Target.Value)}) != NULL);",
        TypeTest t => $"{Temp(t.Dest)} = ({DescOfIface(t.Value)} == &{DescOfConcrete(t.Target.Value)});",
        Downcast d when _module.Types[d.Target.Value].IsInterface =>
            $"{Temp(d.Dest)} = (LyrIface){{ {Temp(d.Value)}.data, lyr_iface_find({DescOfIface(d.Value)}, {IfaceId(d.Target.Value)}) }};",
        Downcast d when _module.Types[d.Target.Value].IsClass =>
            $"{Temp(d.Dest)} = ({CType(d.Result)}){Temp(d.Value)}.data;",
        Downcast d => Assign(d.Dest, $"(({BoxName(d.Target.Value)} *){Temp(d.Value)}.data)->value"),
        // A function value (01 V8): the code and its environment, or a thunk and no environment.
        MakeClosure m => $"{Temp(m.Dest)} = ({CType(m.Type)}){{ {CodeOf(_module.Functions[m.Target.Value])}, {(m.Environment is { } e ? Temp(e) : "NULL")} }};",
        // The slot goes with every call of a function value (03 T17): the caller's own where the
        // type throws, none to write where it cannot.
        CallIndirect c => Assign(c.Dest, $"{Temp(c.Callee)}.fn({Temp(c.Callee)}.env{string.Concat(c.Args.Select(a => ", " + Value(a)))}, "
            + $"{(c.Throws ? "&lyr_e" : "NULL")})"),
        NewInline n => n.Repeat
            ? $"for (int64_t lyr_i = 0; lyr_i < {n.Length}; lyr_i++) {Storage(n.Dest)}.v[lyr_i] = {Value(n.Elements[0])}; {Temp(n.Dest)} = &{Storage(n.Dest)};"
            : $"{Storage(n.Dest)} = ({CType(TypeOf(n.Dest))}){{ .v = {{ {string.Join(", ", n.Elements.Select(Value))} }} }}; {Temp(n.Dest)} = &{Storage(n.Dest)};",
        LoadField f => IsAggregate(f.FieldType)
            ? $"{Temp(f.Dest)} = &{Temp(f.Object)}->{Field(f.Type, f.Field)};"
            : $"{Temp(f.Dest)} = {Temp(f.Object)}->{Field(f.Type, f.Field)};",
        StoreField f => Store(f),
        OptNone n => None(n.Dest),
        OptSome s => Some(s.Dest, Value(s.Value)),
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
        MakeSlice s => (s.Chars
                ? $"LYR_CHECK_CHAR_RANGE({Elements(s.Array, s.Element)}, {Temp(s.Low)}, {Temp(s.High)}, {Length(s.Array)}); "
                : $"LYR_CHECK_RANGE({Temp(s.Low)}, {Temp(s.High)}, {Length(s.Array)}); ")
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
        // A coroutine (06 N2): made over its environment, pulled, asked whether it is done and
        // what its body returned. A yield hands the resumer the value's address in the frame —
        // a temp's own, or the storage an aggregate temp points at.
        MakeCoroutine m => StartCoroutine(m),
        ResumePull r => Pull(r),
        YieldSuspend { Dynamic: true, Value: { } y } d =>
            $"lyr_coro_yield_dynamic({(IsAggregate(TypeOf(y)) ? Temp(y) : "&" + Temp(y))}, \"{YieldKey(d.YieldType)}\");",
        YieldSuspend { Dynamic: true } d => $"lyr_coro_yield_dynamic(NULL, \"{YieldKey(d.YieldType)}\");",
        YieldSuspend { Value: { } y } => $"lyr_coro_yield_value({(IsAggregate(TypeOf(y)) ? Temp(y) : "&" + Temp(y))});",
        YieldSuspend => "lyr_coro_yield_value(NULL);",
        CoroutineDone d => $"{Temp(d.Dest)} = (uint8_t)(lyr_coro_status({Temp(d.Coroutine)}) == LYR_CORO_DONE);",
        CoroutineClosing c => $"{Temp(c.Dest)} = (uint8_t)lyr_coro_closing();",
        CoroutineClose c => Close(c),
        CoroutineResult r => $"if (lyr_coro_status({Temp(r.Coroutine)}) == LYR_CORO_DONE && lyr_coro_transfer({Temp(r.Coroutine)}) != NULL) {{ "
            + $"{Some(r.Dest, $"*({CType(r.ResultType)} *)lyr_coro_transfer({Temp(r.Coroutine)})")} }} else {{ {None(r.Dest)} }}",
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

    /// <summary>A write through a place (03 T12), through the barrier when the value is or holds a
    /// reference: the place is the slot, wherever it lies — in an object, in a struct on the
    /// stack, in a global — and the barrier takes it as it is (the note on <see cref="Store"/>).</summary>
    private string StoreThrough(StorePlace s)
    {
        var place = Temp(s.Place);
        var type = ((IrPlaceType)TypeOf(s.Place)).Value;
        if (IsReference(type)) return $"LYR_WRITE_BARRIER({place}, {place}, {Value(s.Value)});";
        if (HoldsReferences(type)) return $"LYR_WRITE_BARRIER_VALUE({place}, {place}, {Value(s.Value)});";
        return $"*{place} = {Value(s.Value)};";
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
            IrBinKind.Add when !_overflowChecks => $"LYR_WRAP_ADD({CType(type)}, {Shape(type).Unsigned}, {lhs}, {rhs})",
            IrBinKind.Sub when !_overflowChecks => $"LYR_WRAP_SUB({CType(type)}, {Shape(type).Unsigned}, {lhs}, {rhs})",
            IrBinKind.Mul when !_overflowChecks => $"LYR_WRAP_MUL({CType(type)}, {Shape(type).Unsigned}, {lhs}, {rhs})",
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
            // A view of bytes by its bytes — a StringView matched against a literal (10 S1).
            IrBinKind.Eq when TypeOf(b.Lhs) is IrSliceType { Element: IrScalarType { Kind: IrScalar.U8 } }
                => $"(uint8_t)LYR_VIEW_EQ({lhs}, {rhs})",
            IrBinKind.Ne when TypeOf(b.Lhs) is IrSliceType { Element: IrScalarType { Kind: IrScalar.U8 } }
                => $"(uint8_t)!LYR_VIEW_EQ({lhs}, {rhs})",
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
            IrUnKind.Neg when !_overflowChecks => $"LYR_WRAP_SUB({type}, {Shape(u.Type).Unsigned}, ({type})0, {operand})",
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
        Unreachable => "LYR_UNREACHABLE();",
        // The error path (01 L5 E1-E3): a throw allocates the record and goes to its landing, a
        // failed call goes there too, the bottom hands the error to the caller's slot.
        Throw t => $"lyr_e = lyr_err_new({Value(t.Value)}); goto bb{t.Landing.Value};",
        ErrorBranch e => $"if (LYR_UNLIKELY(lyr_e != NULL)) goto bb{e.OnError.Value}; goto bb{e.Continue.Value};",
        Propagate => IsVoid(_function.ReturnType)
            ? "*lyr_err = lyr_e; return;"
            : $"*lyr_err = lyr_e; return {ZeroValue(_function.ReturnType)};",
        // 'try!' (05 E4, E8): the error's message through Error's own table, then the panic.
        PanicError => $"lyr_panic_error(lyr_e, {ReportNames().Message});",
        _ => throw new InvalidOperationException($"the C emitter has no case for {terminator.GetType().Name}; the gate let it through"),
    };
}
