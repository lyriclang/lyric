using System.Globalization;
using Lyric.Bytecode;
using Lyric.Core;

namespace Lyric.Vm;

/// <summary>
/// The native implementations behind the standard library's import declarations.
///
/// <para>Binding is symbolic, by name, at load time: the module names
/// <c>std.io.console.println</c> together with its signature and the registry supplies the
/// delegate. A missing name or a mismatched signature rejects the module rather than failing at
/// the call.</para>
///
/// <para>The host-facing <c>RegisterFunction</c> uses the same seam.</para>
///
/// <para><b>The argument array is a loan.</b> The runtime pools the <c>LyrValue[]</c> it passes
/// to an implementation and reuses it after the call returns. Read the arguments during the
/// call, freely and more than once — but an implementation that stores the ARRAY itself for
/// later reads someone else's arguments. Copy the values out instead; every implementation in
/// this file already does.</para>
/// </summary>
public sealed class NativeRegistry
{
    private readonly Dictionary<string, Native> _natives = new(StringComparer.Ordinal);

    private sealed record Native(
        TypeTag[] ParamTypes, TypeTag ReturnType, Func<LyrValue[], LyrValue> Implementation,
        TypeTag? ReturnElement = null,
        BytecodeType[]? FullParamTypes = null, BytecodeType? FullReturnType = null,
        TypeTag[]? StructResult = null, TypeTag?[]? ParamElements = null,
        TypeTag? ReturnInnerElement = null);

    public void Register(string name, TypeTag[] paramTypes, TypeTag returnType,
        Func<LyrValue[], LyrValue> implementation) =>
        _natives[name] = new Native(paramTypes, returnType, implementation);

    /// <summary>Whether this registry binds <paramref name="name"/>. The §11 contract check:
    /// the set a runtime must implement is exactly what the shipped stdlib declares bodiless,
    /// and a test holds this registry to it — registration runs through loops, so no static
    /// inspection can.</summary>
    public bool Binds(string name) => _natives.ContainsKey(name);

    /// <summary>A native returning a <c>T[]</c>.
    ///
    /// <para>An array has no layout: it is a homogeneous sequence whose element type is named in
    /// the import, so the host needs to know nothing about a module's field order. Objects stay
    /// outside.</para>
    ///
    /// <para><paramref name="element"/> is checked while binding; without it <c>string[]</c> and
    /// <c>char[]</c> would be indistinguishable.</para></summary>
    public void RegisterArrayReturning(string name, TypeTag[] paramTypes, TypeTag element,
        Func<LyrValue[], LyrValue> implementation) =>
        _natives[name] = new Native(paramTypes, TypeTag.Array, implementation, element);

    /// <summary>A native returning a <c>?T</c>, used where failure is an ordinary state of the
    /// world: a file that does not exist, an environment variable that is not set.
    ///
    /// <para>As with arrays the inner tag is checked while binding; <c>?string</c> and <c>?int</c>
    /// both carry <c>TypeTag.Optional</c>.</para></summary>
    public void RegisterOptionalReturning(string name, TypeTag[] paramTypes, TypeTag inner,
        Func<LyrValue[], LyrValue> implementation) =>
        _natives[name] = new Native(paramTypes, TypeTag.Optional, implementation, inner);

    /// <summary>
    /// A native returning a <c>?T[]</c> (v2.14) — the shape a read has when an empty result and a
    /// failure are different answers.
    ///
    /// <para>Nothing about the VALUE needs saying: an optional over a reference IS the reference,
    /// and "no value" is an empty one. What needs saying is the TYPE, twice: the tag says
    /// optional, its inner says array, and only the element below that distinguishes
    /// <c>?string[]</c> from <c>?uint8[]</c>. Without the third level the binder would accept a
    /// host handing back bytes where the module expects lines.</para>
    /// </summary>
    public void RegisterOptionalArrayReturning(string name, TypeTag[] paramTypes, TypeTag element,
        Func<LyrValue[], LyrValue> implementation) =>
        _natives[name] = new Native(paramTypes, TypeTag.Optional, implementation, TypeTag.Array,
            ReturnInnerElement: element);

    /// <summary>A native with array PARAMETERS (v1.14) — an array crossing the boundary INTO the
    /// host, the direction <c>readBytes</c> never needed. The implementation reads the argument
    /// as <c>(LyrValue[])args[i].AsObject</c>, under the loan contract of the class.
    ///
    /// <para><paramref name="paramElements"/> runs parallel to <paramref name="paramTypes"/>:
    /// the element tag for an array parameter, <c>null</c> for a scalar one. Checked while
    /// binding, as for an array return — without it <c>string[]</c> and <c>char[]</c> would be
    /// indistinguishable. <paramref name="returnElement"/> carries the element or inner tag when
    /// the return type itself is an array or an optional.</para></summary>
    public void RegisterWithArrayParams(string name, TypeTag[] paramTypes,
        TypeTag?[] paramElements, TypeTag returnType, Func<LyrValue[], LyrValue> implementation,
        TypeTag? returnElement = null) =>
        _natives[name] = new Native(paramTypes, returnType, implementation, returnElement,
            ParamElements: paramElements);

    /// <summary>
    /// A native whose signature contains host types.
    ///
    /// <para>It needs the full types rather than the tags: two host types both carry
    /// <see cref="TypeTag.Host"/>, and their name is all the module and the runtime know about
    /// them.</para>
    /// </summary>
    public void RegisterWithTypes(string name, BytecodeType[] paramTypes, BytecodeType returnType,
        Func<LyrValue[], LyrValue> implementation) =>
        _natives[name] = new Native(
            paramTypes.Select(p => p.Tag).ToArray(), returnType.Tag, implementation,
            ReturnElement: null, FullParamTypes: paramTypes, FullReturnType: returnType);

    /// <summary>
    /// A native declared in Lyric to RETURN a struct: <c>pub fn positionOf(e: int): Vec2;</c>.
    ///
    /// <para>On the wire the struct comes back through a trailing out-parameter — a buffer the
    /// runtime owns and passes as the LAST argument. The implementation receives the ordinary
    /// arguments plus that buffer's slots and fills ONE VALUE PER FIELD in field order. The same
    /// writing pattern as an array parameter, one entity wide; the script side sees an ordinary
    /// value.</para>
    ///
    /// <para><paramref name="resultFields"/> names the field tags the implementation writes.
    /// Binding checks them against the struct layout the module declares, so a host that
    /// disagrees with the SDK fails at load, not in frame 40 000.</para>
    ///
    /// <para>The loan contract of the class applies to BOTH arrays: read arguments and write the
    /// result during the call, keep neither. And a native that re-enters the VM does so before
    /// writing its result, or the inner call may read a half-written buffer.</para>
    /// </summary>
    public void RegisterStructReturning(string name, TypeTag[] paramTypes, TypeTag[] resultFields,
        Action<LyrValue[], LyrValue[]> implementation) =>
        _natives[name] = new Native(
            [.. paramTypes, TypeTag.Struct], TypeTag.Void,
            arguments =>
            {
                implementation(arguments, (LyrValue[])arguments[^1].AsObject);
                return default;
            },
            StructResult: resultFields);

    /// <summary>A bound import: the implementation plus what the call site needs. Arity and
    /// return kind live here so the interpreter looks nothing up in the hot path.</summary>
    public sealed record BoundNative(int Arity, bool ReturnsValue,
        Func<LyrValue[], LyrValue> Implementation);

    /// <summary>Binds every import of a module. Throws as soon as one is missing or differs from
    /// what the module declares.</summary>
    public BoundNative[] Bind(BytecodeModule module)
    {
        var bound = new BoundNative[module.Imports.Count];

        for (var i = 0; i < module.Imports.Count; i++)
        {
            var import = module.Imports[i];
            if (!_natives.TryGetValue(import.Name, out var native))
                throw new LyricRuntimeException(VmDiagnostics.ImportsNotBound,
                    $"no native implementation for '{import.Name}'");

            // A gated native may only be bound when the module DECLARED the capability it needs.
            // The load-time check (LoadedProgram.Load) refuses a module that declares more than
            // the grant; this refuses one that USES more than it declares. Together they make the
            // declared bitset a verified bound rather than a trusted one — which is what lets a
            // host loading foreign bytes rely on the declaration, as the capability contract
            // promises. The compiler always declares correctly, so no honest module trips this.
            var needed = CapabilityTable.RequiredForImport(import.Name);
            if (((Capability)module.Capabilities & needed) != needed)
                throw new LyricRuntimeException(VmDiagnostics.CapabilityDenied,
                    $"native '{import.Name}' requires capability "
                    + $"'{CapabilityTable.Describe(needed)}', which the module does not declare");

            // Tags first; elements where a tag alone is ambiguous. A reference signature is
            // rejected because no native declares one.
            if (!native.ParamTypes.SequenceEqual(import.ParamTypes.Select(p => p.Tag)) ||
                native.ReturnType != import.ReturnType.Tag)
                throw new LyricRuntimeException(VmDiagnostics.ImportsNotBound,
                    $"native '{import.Name}' has a different signature than the module expects");

            // For an array return type the tag is not enough: 'string[]' and 'char[]' share it.
            if (native.ReturnElement is { } expected
                && import.ReturnType.Element?.Tag != expected)
                throw new LyricRuntimeException(VmDiagnostics.ImportsNotBound,
                    $"native '{import.Name}' returns a different array element type than the "
                    + "module expects");

            // And one level deeper for a '?T[]' (v2.14): the optional's inner is the array, so
            // the element that tells 'lines' from 'bytes' sits below both.
            if (native.ReturnInnerElement is { } inner
                && import.ReturnType.Element?.Element?.Tag != inner)
                throw new LyricRuntimeException(VmDiagnostics.ImportsNotBound,
                    $"native '{import.Name}' returns an optional array of a different element "
                    + "type than the module expects");

            // The same ambiguity for array PARAMETERS (v1.14): the element tag has to match too.
            if (native.ParamElements is { } elements)
                for (var p = 0; p < elements.Length; p++)
                    if (elements[p] is { } el && import.ParamTypes[p].Element?.Tag != el)
                        throw new LyricRuntimeException(VmDiagnostics.ImportsNotBound,
                            $"native '{import.Name}': parameter {p + 1} has a different array "
                            + "element type than the module expects");

            // A struct-returning native writes fields by position, and the layout is the
            // module's. Checking it here turns a host/SDK disagreement into a load error with a
            // name in it rather than a wrong value in a frame.
            if (native.StructResult is { } fields)
            {
                var trailing = import.ParamTypes.Count > 0 ? import.ParamTypes[^1] : default;
                if (trailing.Tag != TypeTag.Struct
                    || trailing.TypeIndex < 0 || trailing.TypeIndex >= module.Types.Count
                    || !module.Types[trailing.TypeIndex].FieldTypes
                        .Select(f => f.Tag).SequenceEqual(fields))
                    throw new LyricRuntimeException(VmDiagnostics.ImportsNotBound,
                        $"native '{import.Name}' fills a struct result whose layout does not "
                        + "match what the module declares");
            }

            // For a host type only the name distinguishes it. Without this check a module
            // expecting one host type could be bound to another, and the mismatch would surface as
            // a cast failure inside the host.
            if (native.FullParamTypes is { } declaredParams)
            {
                for (var p = 0; p < declaredParams.Length; p++)
                    RequireSameHostType(import.Name, declaredParams[p], import.ParamTypes[p],
                        $"parameter {p + 1}");

                RequireSameHostType(import.Name, native.FullReturnType!, import.ReturnType,
                    "the return type");
            }

            bound[i] = new BoundNative(import.ParamTypes.Count,
                import.ReturnType.Tag != TypeTag.Void, native.Implementation);
        }

        return bound;
    }

    private static void RequireSameHostType(string import, BytecodeType native,
        BytecodeType expected, string what)
    {
        if (native.Tag != TypeTag.Host && expected.Tag != TypeTag.Host) return;
        if (string.Equals(native.HostName, expected.HostName, StringComparison.Ordinal)) return;

        throw new LyricRuntimeException(VmDiagnostics.ImportsNotBound,
            $"native '{import}': {what} is host type '{native.HostName ?? "(none)"}', but the "
            + $"module expects '{expected.HostName ?? "(none)"}'");
    }

    /// <summary>
    /// The built-in natives of the standalone CLI. <paramref name="output"/> and
    /// <paramref name="error"/> are parameters so tests can collect the output.
    /// </summary>
    /// <param name="input">Where <c>readLine</c> reads from; defaults to <c>Console.In</c>.
    /// </param>
    public static NativeRegistry CreateDefault(
        TextWriter output, TextWriter error, TextReader? input = null)
    {
        var registry = new NativeRegistry();
        var str = new[] { TypeTag.String };
        var none = Array.Empty<TypeTag>();
        var stdin = input ?? Console.In;

        // Since v1.14 the module declares these as PRIVATE raw* natives behind the Display
        // generics; the old public names stay bound so bytecode compiled before the change keeps
        // running. One host function under both names keeps a single truth about the behavior.
        void ConsoleWriter(string name, Func<LyrValue[], LyrValue> implementation)
        {
            registry.Register("std.io.console.raw" + char.ToUpperInvariant(name[0]) + name[1..],
                str, TypeTag.Void, implementation);
            registry.Register("std.io.console." + name, str, TypeTag.Void, implementation);
        }

        ConsoleWriter("print", args => { output.Write(args[0].AsString); return default; });
        // Always '\n', never Environment.NewLine: the output of a Lyric program does not
        // depend on the operating system.
        ConsoleWriter("println",
            args => { output.Write(args[0].AsString); output.Write('\n'); return default; });
        ConsoleWriter("eprintln",
            args => { error.Write(args[0].AsString); error.Write('\n'); return default; });
        // Writes a diagnostic without a line break.
        ConsoleWriter("eprint", args => { error.Write(args[0].AsString); return default; });

        // ---------------------------------------------------------------- input
        //
        // No capability: reading stdin, like writing stdout, is part of the process rather than an
        // access decision. A host that wants to forbid it passes an empty reader.

        // '?string' because EOF is a state, not an error.
        registry.RegisterOptionalReturning("std.io.console.readLine", none, TypeTag.String,
            _ => Optional(stdin.ReadLine()));

        // Everything up to EOF. Returns "" rather than null: nothing and empty mean the same
        // here.
        registry.Register("std.io.console.readAll", none, TypeTag.String,
            _ => LyrValue.FromString(stdin.ReadToEnd()));

        // A single code point. 'Read()' yields UTF-16 units, so a surrogate pair is combined; half
        // a pair is not a valid char.
        registry.RegisterOptionalReturning("std.io.console.readChar", none, TypeTag.Char,
            _ => ReadCodepoint(stdin));

        // Terminal or pipe: whether a prompt belongs in the stream at all.
        registry.Register("std.io.console.isInteractive", none, TypeTag.Bool,
            _ => LyrValue.FromBool(!Console.IsInputRedirected && !Console.IsOutputRedirected));

        // Needed when a prompt is written without a line break, which would otherwise sit in the
        // buffer while the program waits for the answer.
        registry.Register("std.io.console.flush", none, TypeTag.Void,
            _ => { output.Flush(); return default; });

        registry.Register("std.string.concat", new[] { TypeTag.String, TypeTag.String },
            TypeTag.String, args => LyrValue.FromString(args[0].AsString + args[1].AsString));

        // 'ab' * 3. A negative factor yields the empty string; there is no error case for it.
        registry.Register("std.string.repeat", new[] { TypeTag.String, TypeTag.I64 },
            TypeTag.String, args => LyrValue.FromString(
                args[1].AsI64 <= 0 ? string.Empty
                    : string.Concat(Enumerable.Repeat(args[0].AsString, (int)args[1].AsI64))));

        // A panic is not catchable and never returns, so it throws. The loop that holds the frame
        // stack attaches the backtrace.
        registry.Register("std.core.panic", str, TypeTag.Void,
            args => throw new LyricPanic(VmDiagnostics.Panicked, args[0].AsString));

        // A 'resume' on an exhausted coroutine. It is a panic, not a catchable error.
        registry.Register("std.core.coroutineEnded", Array.Empty<TypeTag>(), TypeTag.Void,
            _ => throw new LyricPanic(VmDiagnostics.Panicked,
                "resume on a coroutine that has already finished"));

        // The done state behind 'co.next()': field 0 of the coroutine's state object is the
        // re-entry marker, and -1 means the body ran through. The compiler emits one import per
        // coroutine signature; the tag comparison in Bind lets this one implementation answer
        // them all, because the marker sits at the same place in every state layout.
        registry.Register("std.core.coroutineIsDone", new[] { TypeTag.Fn }, TypeTag.Bool,
            args => args[0].HasEnvironment
                ? LyrValue.FromBool(unchecked((int)args[0].AsObject[0].AsU64) == -1)
                : throw new LyricPanic(VmDiagnostics.Panicked, "next on an empty coroutine value"));

        // 'std.core' carries PRIVATE duplicates of six of these declarations (fromInt through
        // charAt): the module is the library's root and imports nothing — with an import of
        // std.string there, no module could import std.core back (LYR-RES0005). One host function
        // under both names keeps a single truth about the behavior.
        void Both(string name, TypeTag[] paramTypes, TypeTag returnType,
            Func<LyrValue[], LyrValue> implementation)
        {
            registry.Register("std.string." + name, paramTypes, returnType, implementation);
            registry.Register("std.core." + name, paramTypes, returnType, implementation);
        }

        // Invariant culture: '3.5', not '3,5'. The same .lyrbc produces the same output on every
        // machine.
        Both("fromInt", new[] { TypeTag.I64 }, TypeTag.String,
            args => LyrValue.FromString(args[0].AsI64.ToString(CultureInfo.InvariantCulture)));
        Both("fromFloat", new[] { TypeTag.F64 }, TypeTag.String,
            args => LyrValue.FromString(Floats.Render(args[0].AsF64)));
        Both("fromBool", new[] { TypeTag.Bool }, TypeTag.String,
            args => LyrValue.FromString(args[0].AsBool ? "true" : "false"));
        Both("fromChar", new[] { TypeTag.Char }, TypeTag.String,
            args => LyrValue.FromString(char.ConvertFromUtf32((int)args[0].Bits)));

        // The inverse of fromFloat, and native for the same reason fromFloat is: a correctly
        // rounded conversion needs arbitrary-precision arithmetic in its worst cases, and an
        // approximate parser hands back a float the text did not say. The shape check runs
        // first — the runtime's own parser would also take whitespace and the words
        // "NaN"/"Infinity", which this contract refuses. A magnitude beyond the type rounds to
        // the infinities, as IEEE 754 rounding prescribes (so no finiteness check after).
        registry.RegisterOptionalReturning("std.string.parseFloat",
            new[] { TypeTag.String }, TypeTag.F64,
            args => FloatShaped(args[0].AsString)
                && double.TryParse(args[0].AsString, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var parsed)
                ? LyrValue.Some(LyrValue.FromF64(parsed))
                : LyrValue.None);

        // --- queries --------------------------------------------------------------------
        //
        // Every position and length counts CODE POINTS, not UTF-16 units and not bytes, matching
        // what iteration yields. The cost is O(n) rather than O(1); there is no 's[i]', so a
        // quadratic index loop cannot be written.

        Both("length", str, TypeTag.I64,
            args => LyrValue.FromI64(CodepointCount(args[0].AsString)));

        Both("charAt", new[] { TypeTag.String, TypeTag.I64 }, TypeTag.Char,
            args => LyrValue.FromBits((ulong)CodepointAt(args[0].AsString, args[1].AsI64)));

        // --- the byte bridge and the array-parameter joiners (v1.14) ---------------------

        registry.RegisterArrayReturning("std.string.utf8Encode", str, TypeTag.U8,
            args => Bytes(System.Text.Encoding.UTF8.GetBytes(args[0].AsString)));

        // STRICT, unlike readText: invalid bytes are the caller's question here, and the answer
        // is null rather than a U+FFFD quietly standing in for the data.
        registry.RegisterWithArrayParams("std.string.utf8Decode",
            new[] { TypeTag.Array }, new TypeTag?[] { TypeTag.U8 },
            TypeTag.Optional, args =>
            {
                try
                {
                    return LyrValue.FromString(new System.Text.UTF8Encoding(
                        encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                        .GetString(ToBytes(args[0])));
                }
                catch (ArgumentException e)
                {
                    // The decoder names the byte that broke the sequence; utf8DecodeOrThrow
                    // reads it right after this null, on the same thread.
                    _lastUtf8ErrorOffset = e is System.Text.DecoderFallbackException d ? d.Index : 0;
                    return default;
                }
            }, returnElement: TypeTag.String);

        registry.Register("std.string.utf8DecodeErrorOffset", none, TypeTag.I64,
            _ => LyrValue.FromI64(_lastUtf8ErrorOffset));

        // Behind std.string.join and StringBuilder.build: one native call instead of a copy
        // cascade the language cannot avoid without preallocating strings.
        registry.RegisterWithArrayParams("std.string.joinAll",
            new[] { TypeTag.Array, TypeTag.String }, new TypeTag?[] { TypeTag.String, null },
            TypeTag.String, args =>
            {
                var parts = (LyrValue[])args[0].AsObject;
                var texts = new string[parts.Length];
                for (var i = 0; i < parts.Length; i++) texts[i] = parts[i].AsString;
                return LyrValue.FromString(string.Join(args[1].AsString, texts));
            });

        // The inverse of toChars, native since v1.14: one call instead of one string per
        // character.
        registry.RegisterWithArrayParams("std.string.fromChars",
            new[] { TypeTag.Array }, new TypeTag?[] { TypeTag.Char },
            TypeTag.String, args =>
            {
                var chars = (LyrValue[])args[0].AsObject;
                var builder = new System.Text.StringBuilder(chars.Length);
                for (var i = 0; i < chars.Length; i++)
                    builder.Append(char.ConvertFromUtf32((int)chars[i].Bits));
                return LyrValue.FromString(builder.ToString());
            });

        registry.Register("std.string.substring",
            new[] { TypeTag.String, TypeTag.I64, TypeTag.I64 }, TypeTag.String,
            args => LyrValue.FromString(Substring(args[0].AsString, args[1].AsI64, args[2].AsI64)));

        registry.Register("std.string.indexOf", new[] { TypeTag.String, TypeTag.String },
            TypeTag.I64, args => LyrValue.FromI64(IndexOf(args[0].AsString, args[1].AsString)));

        registry.Register("std.string.contains", new[] { TypeTag.String, TypeTag.String },
            TypeTag.Bool,
            args => LyrValue.FromBool(args[0].AsString.Contains(args[1].AsString, StringComparison.Ordinal)));

        registry.Register("std.string.startsWith", new[] { TypeTag.String, TypeTag.String },
            TypeTag.Bool,
            args => LyrValue.FromBool(args[0].AsString.StartsWith(args[1].AsString, StringComparison.Ordinal)));

        registry.Register("std.string.endsWith", new[] { TypeTag.String, TypeTag.String },
            TypeTag.Bool,
            args => LyrValue.FromBool(args[0].AsString.EndsWith(args[1].AsString, StringComparison.Ordinal)));

        registry.Register("std.string.trim", str, TypeTag.String,
            args => LyrValue.FromString(args[0].AsString.Trim()));

        // Ordinal rather than culture-dependent, so the same program yields the same result on
        // every machine.
        registry.Register("std.string.toUpper", str, TypeTag.String,
            args => LyrValue.FromString(args[0].AsString.ToUpperInvariant()));

        registry.Register("std.string.toLower", str, TypeTag.String,
            args => LyrValue.FromString(args[0].AsString.ToLowerInvariant()));

        registry.RegisterArrayReturning("std.string.split",
            new[] { TypeTag.String, TypeTag.String }, TypeTag.String,
            args => Split(args[0].AsString, args[1].AsString));

        // The anchor for 'for (c in s)': one O(n) pass instead of n calls to 'charAt'.
        registry.RegisterArrayReturning("std.string.toChars", str, TypeTag.Char,
            args => ToChars(args[0].AsString));

        // --- std.os (capability-gated) --------------------------------------------------
        //
        // The natives are always registered. The capability decides whether a module requiring
        // them loads at all, not whether the function exists.
        // --- std.math (ungated) ----------------------------------------------------------
        //
        // Special values follow IEEE 754: 'sqrt(-1.0)' is NaN, not a panic.
        var f1 = new[] { TypeTag.F64 };
        var f2 = new[] { TypeTag.F64, TypeTag.F64 };

        // --- std.random (ungated) --------------------------------------------------------
        //
        // One xorshift64 round. In Lyric it was 53 instructions -- three shifts, three exclusive
        // ors, and the loads and stores between them -- and every one of them costs what a
        // crossing costs. The shift semantics have to match the language exactly: '>>' on a
        // signed integer is arithmetic here as it is there, and '<<' wraps.
        registry.Register("std.random.xorshift", new[] { TypeTag.I64 }, TypeTag.I64, args =>
        {
            var x = args[0].AsI64;
            x ^= x << 13;
            x ^= x >> 7;
            x ^= x << 17;
            return LyrValue.FromI64(x);
        });

        // The one non-deterministic draw (4.0): the OS's cryptographic source. Capability-free
        // like the module — entropy reaches no file, clock or network. A negative count is the
        // caller's bug and panics like a bad slice bound would.
        registry.RegisterArrayReturning("std.random.secureRandom", [TypeTag.I64], TypeTag.U8,
            args =>
            {
                var count = args[0].AsI64;
                if (count < 0)
                    throw new LyricPanic(VmDiagnostics.Panicked,
                        "std.random.secureRandom: negative count");
                var bytes = new byte[(int)Math.Min(count, 1 << 20)];
                System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
                return Bytes(bytes);
            });

        registry.Register("std.math.sqrt", f1, TypeTag.F64,
            args => LyrValue.FromF64(Math.Sqrt(args[0].AsF64)));
        registry.Register("std.math.abs", f1, TypeTag.F64,
            args => LyrValue.FromF64(Math.Abs(args[0].AsF64)));
        registry.Register("std.math.floor", f1, TypeTag.F64,
            args => LyrValue.FromF64(Math.Floor(args[0].AsF64)));
        registry.Register("std.math.ceil", f1, TypeTag.F64,
            args => LyrValue.FromF64(Math.Ceiling(args[0].AsF64)));

        // Round half to even, which avoids the systematic bias of always rounding up.
        registry.Register("std.math.round", f1, TypeTag.F64,
            args => LyrValue.FromF64(Math.Round(args[0].AsF64, MidpointRounding.ToEven)));

        registry.Register("std.math.min", f2, TypeTag.F64,
            args => LyrValue.FromF64(Math.Min(args[0].AsF64, args[1].AsF64)));
        registry.Register("std.math.max", f2, TypeTag.F64,
            args => LyrValue.FromF64(Math.Max(args[0].AsF64, args[1].AsF64)));
        registry.Register("std.math.pow", f2, TypeTag.F64,
            args => LyrValue.FromF64(Math.Pow(args[0].AsF64, args[1].AsF64)));

        registry.Register("std.math.sin", f1, TypeTag.F64,
            args => LyrValue.FromF64(Math.Sin(args[0].AsF64)));
        registry.Register("std.math.cos", f1, TypeTag.F64,
            args => LyrValue.FromF64(Math.Cos(args[0].AsF64)));
        registry.Register("std.math.tan", f1, TypeTag.F64,
            args => LyrValue.FromF64(Math.Tan(args[0].AsF64)));
        // log2 and log10 are native although 'log(x)/log(base)' expresses them: the derived form
        // is imprecise. 'log10(1000.0)' computed that way yields 2.9999999999999996, and 'as int'
        // turns that into 2.
        registry.Register("std.string.fromUint", new[] { TypeTag.U64 }, TypeTag.String,
            args => LyrValue.FromString(args[0].AsU64.ToString(CultureInfo.InvariantCulture)));

        registry.Register("std.fmt.formatUint", new[] { TypeTag.U64, TypeTag.String },
            TypeTag.String, args => Formatted(args[0].AsU64, args[1].AsString));

        registry.Register("std.math.log2", f1, TypeTag.F64,
            args => LyrValue.FromF64(Math.Log2(args[0].AsF64)));
        registry.Register("std.math.log10", f1, TypeTag.F64,
            args => LyrValue.FromF64(Math.Log10(args[0].AsF64)));

        registry.Register("std.math.asin", f1, TypeTag.F64,
            args => LyrValue.FromF64(Math.Asin(args[0].AsF64)));
        registry.Register("std.math.acos", f1, TypeTag.F64,
            args => LyrValue.FromF64(Math.Acos(args[0].AsF64)));
        registry.Register("std.math.atan", f1, TypeTag.F64,
            args => LyrValue.FromF64(Math.Atan(args[0].AsF64)));
        registry.Register("std.math.atan2", new[] { TypeTag.F64, TypeTag.F64 }, TypeTag.F64,
            args => LyrValue.FromF64(Math.Atan2(args[0].AsF64, args[1].AsF64)));

        registry.Register("std.math.log", f1, TypeTag.F64,
            args => LyrValue.FromF64(Math.Log(args[0].AsF64)));

        registry.Register("std.os.platform", Array.Empty<TypeTag>(), TypeTag.String,
            _ => LyrValue.FromString(
                OperatingSystem.IsWindows() ? "windows"
                : OperatingSystem.IsLinux() ? "linux"
                : OperatingSystem.IsMacOS() ? "macos"
                : "unknown"));

        // --- std.fmt --------------------------------------------------------------------
        //
        // The specifier language is .NET's, passed through unchanged: N2, F3, D5, X, E2, P1.
        // Always invariant, so a number does not change shape with the machine's locale.
        registry.Register("std.fmt.formatInt", new[] { TypeTag.I64, TypeTag.String },
            TypeTag.String, args => Formatted(args[0].AsI64, args[1].AsString));

        registry.Register("std.fmt.formatFloat", new[] { TypeTag.F64, TypeTag.String },
            TypeTag.String, args => Formatted(args[0].AsF64, args[1].AsString));

        registry.Register("std.fmt.formatBool", new[] { TypeTag.Bool, TypeTag.String },
            TypeTag.String, args => LyrValue.FromString(Padded(args[0].AsBool ? "true" : "false",
                args[1].AsString)));

        registry.Register("std.fmt.formatChar", new[] { TypeTag.Char, TypeTag.String },
            TypeTag.String, args => LyrValue.FromString(Padded(
                char.ConvertFromUtf32((int)args[0].Bits), args[1].AsString)));

        registry.Register("std.fmt.formatString", new[] { TypeTag.String, TypeTag.String },
            TypeTag.String,
            args => LyrValue.FromString(Padded(args[0].AsString, args[1].AsString)));

        registry.RegisterOptionalReturning("std.os.env", str, TypeTag.String,
            args => Optional(Environment.GetEnvironmentVariable(args[0].AsString)));

        registry.Register("std.os.currentDir", Array.Empty<TypeTag>(), TypeTag.String,
            _ => LyrValue.FromString(Directory.GetCurrentDirectory()));

        // Exits immediately: no 'defer' runs and no 'catch' applies. The return type is 'void'
        // rather than 'never', which would let the compiler treat the following code as
        // unreachable.
        registry.Register("std.os.exit", new[] { TypeTag.I64 }, TypeTag.Void,
            args => { Environment.Exit((int)(args[0].AsI64 & 0xFF)); return default; });

        // --- std.io.file (capability-gated: fileAccess) ------------------------------------
        //
        // Failures are return values, not exceptions: a file that does not exist is an ordinary
        // state. The catch is deliberately broad (IO errors, permissions, invalid paths); to the
        // caller they are the same answer.
        // The three READS answer '?T' since 2.14: null is "could not", and an empty result is an
        // empty file. Before that a read had three conventions between them -- an optional, an
        // empty array, a bool -- and the empty array was the one that lied: an unreadable file
        // and an empty one gave the same answer.
        registry.RegisterOptionalReturning("std.io.file.text", str, TypeTag.String,
            args => Optional(TryIo(() => File.ReadAllText(args[0].AsString))));

        registry.RegisterOptionalArrayReturning("std.io.file.lines", str, TypeTag.String,
            args => OptionalLines(TryIo(() => File.ReadAllText(args[0].AsString))));

        // The raw bytes, undecoded -- the answer 'text' cannot give: its UTF-8 decoding turns
        // invalid bytes into U+FFFD.
        registry.RegisterOptionalArrayReturning("std.io.file.bytes", str, TypeTag.U8,
            args => OptionalBytes(TryIoBytes(() => File.ReadAllBytes(args[0].AsString))));

        // The names before 2.14, kept bound although the shipped stdlib no longer declares them.
        // The set a runtime must implement is what the stdlib declares (spec §11), but a MODULE
        // compiled before 2.14 carries these in its import table, and a '.lyrbc' that loaded
        // yesterday has to load today.
        registry.RegisterOptionalReturning("std.io.file.readText", str, TypeTag.String,
            args => Optional(TryIo(() => File.ReadAllText(args[0].AsString))));

        registry.Register("std.io.file.writeText", new[] { TypeTag.String, TypeTag.String },
            TypeTag.Bool, args => LyrValue.FromBool(
                TryIo(() => { File.WriteAllText(args[0].AsString, args[1].AsString); return ""; }) is not null));

        registry.Register("std.io.file.appendText", new[] { TypeTag.String, TypeTag.String },
            TypeTag.Bool, args => LyrValue.FromBool(
                TryIo(() => { File.AppendAllText(args[0].AsString, args[1].AsString); return ""; }) is not null));

        registry.Register("std.io.file.exists", str, TypeTag.Bool,
            args => LyrValue.FromBool(File.Exists(args[0].AsString)));

        // 'true' when the file is gone afterwards, including when it never existed.
        registry.Register("std.io.file.remove", str, TypeTag.Bool,
            args => LyrValue.FromBool(
                TryIo(() => { File.Delete(args[0].AsString); return ""; }) is not null));

        registry.RegisterArrayReturning("std.io.file.readLines", str, TypeTag.String,
            args => Lines(TryIo(() => File.ReadAllText(args[0].AsString))));

        // The raw bytes, undecoded — the answer readText cannot give: its UTF-8 decoding turns
        // invalid bytes into U+FFFD. Empty when unreadable, the readLines convention; a caller
        // that needs the difference asks 'exists' first.
        registry.RegisterArrayReturning("std.io.file.readBytes", str, TypeTag.U8,
            args => Bytes(TryIoBytes(() => File.ReadAllBytes(args[0].AsString))));

        // The write side (v1.14) — the first natives with an ARRAY parameter. The failure model
        // is writeText's: a bool, through the same broad TryIo.
        registry.RegisterWithArrayParams("std.io.file.writeBytes",
            new[] { TypeTag.String, TypeTag.Array }, new TypeTag?[] { null, TypeTag.U8 },
            TypeTag.Bool, args => LyrValue.FromBool(
                TryIo(() =>
                {
                    File.WriteAllBytes(args[0].AsString, ToBytes(args[1]));
                    return "";
                }) is not null));

        registry.RegisterWithArrayParams("std.io.file.appendBytes",
            new[] { TypeTag.String, TypeTag.Array }, new TypeTag?[] { null, TypeTag.U8 },
            TypeTag.Bool, args => LyrValue.FromBool(
                TryIo(() =>
                {
                    using var stream = new FileStream(args[0].AsString, FileMode.Append,
                        FileAccess.Write);
                    stream.Write(ToBytes(args[1]));
                    return "";
                }) is not null));

        // ------------------------------------------------------------ std.io.file, continued
        //
        // Everything here runs through 'TryIo': a missing file, a locked directory or a full disk
        // are states, reported through the return value.

        // Not through 'TryIo', which carries a string; this returns a number.
        registry.RegisterOptionalReturning("std.io.file.size", str, TypeTag.I64,
            args =>
            {
                try
                {
                    var info = new FileInfo(args[0].AsString);
                    if (!info.Exists)
                    {
                        // Not an exception here — FileInfo answers the question — so the
                        // classification is set by hand, or sizeOrThrow would read a stale one.
                        RecordIoNotFound(args[0].AsString);
                        return LyrValue.None;
                    }

                    // 'Some' rather than 'FromI64': for a '?T' over a scalar the marker in 'Ref'
                    // is what signals presence, since every bit pattern is a valid number.
                    return LyrValue.Some(LyrValue.FromI64(info.Length));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException
                                              or ArgumentException or NotSupportedException)
                {
                    RecordIo(e);
                    return LyrValue.None;
                }
            });

        registry.Register("std.io.file.isFile", str, TypeTag.Bool,
            args => LyrValue.FromBool(File.Exists(args[0].AsString)));

        registry.Register("std.io.file.isDirectory", str, TypeTag.Bool,
            args => LyrValue.FromBool(Directory.Exists(args[0].AsString)));

        registry.Register("std.io.file.copy", new[] { TypeTag.String, TypeTag.String },
            TypeTag.Bool, args => LyrValue.FromBool(
                TryIo(() => { File.Copy(args[0].AsString, args[1].AsString, true); return ""; })
                is not null));

        registry.Register("std.io.file.move", new[] { TypeTag.String, TypeTag.String },
            TypeTag.Bool, args => LyrValue.FromBool(
                TryIo(() => { File.Move(args[0].AsString, args[1].AsString, true); return ""; })
                is not null));

        // An existing directory counts as success: the desired state is reached.
        registry.Register("std.io.file.createDir", str, TypeTag.Bool,
            args => LyrValue.FromBool(
                TryIo(() => { Directory.CreateDirectory(args[0].AsString); return ""; })
                is not null));

        registry.Register("std.io.file.createDirAll", str, TypeTag.Bool,
            args => LyrValue.FromBool(
                TryIo(() => { Directory.CreateDirectory(args[0].AsString); return ""; })
                is not null));

        // Empty directories only ('recursive: false'). There is no recursive delete.
        registry.Register("std.io.file.removeDir", str, TypeTag.Bool,
            args => LyrValue.FromBool(
                TryIo(() => { Directory.Delete(args[0].AsString, false); return ""; })
                is not null));

        registry.RegisterArrayReturning("std.io.file.listDir", str, TypeTag.String,
            args =>
            {
                try
                {
                    var namen = Directory.EnumerateFileSystemEntries(args[0].AsString)
                        .Select(Path.GetFileName)
                        .OfType<string>()
                        .OrderBy(n => n, StringComparer.Ordinal)   // deterministic
                        .Select(LyrValue.FromString)
                        .ToArray();
                    return LyrValue.FromObject(namen);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException
                                              or ArgumentException)
                {
                    return LyrValue.FromObject([]);
                }
            });

        // 'entries' completes the 2.14 line listDir missed: an unreadable directory is null,
        // an empty one is an empty array — and the failure is classified for entriesOrThrow.
        registry.RegisterOptionalArrayReturning("std.io.file.entries", str, TypeTag.String,
            args =>
            {
                try
                {
                    var names = Directory.EnumerateFileSystemEntries(args[0].AsString)
                        .Select(Path.GetFileName)
                        .OfType<string>()
                        .OrderBy(n => n, StringComparer.Ordinal)   // deterministic
                        .Select(LyrValue.FromString)
                        .ToArray();

                    // The array reference itself signals presence, as in OptionalLines — 'Some'
                    // is for scalars, whose every bit pattern is a valid value.
                    return LyrValue.FromObject(names);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException
                                              or ArgumentException or NotSupportedException)
                {
                    RecordIo(e);
                    return LyrValue.None;
                }
            });

        registry.Register("std.io.file.tempDir", none, TypeTag.String,
            _ => LyrValue.FromString(Path.GetTempPath()));

        // The classification of the last failed operation (3.7), for the OrThrow forms. Non-pub
        // on the Lyric side: nothing outside file.lyr ever reads these. A module that binds them
        // needs a 3.7 runtime — the parseFloat forward path.
        registry.Register("std.io.file.lastErrorKind", none, TypeTag.I64,
            _ => LyrValue.FromI64(_lastIoKind));

        registry.Register("std.io.file.lastErrorDetail", none, TypeTag.String,
            _ => LyrValue.FromString(_lastIoDetail ?? ""));

        // ------------------------------------------------------ std.os, extended

        registry.RegisterArrayReturning("std.os.args", none, TypeTag.String,
            _ => LyrValue.FromObject(Environment.GetCommandLineArgs()
                .Select(LyrValue.FromString).ToArray()));

        registry.Register("std.os.setEnv", new[] { TypeTag.String, TypeTag.String }, TypeTag.Bool,
            args =>
            {
                try
                {
                    Environment.SetEnvironmentVariable(args[0].AsString, args[1].AsString);
                    return LyrValue.FromBool(true);
                }
                catch (Exception e) when (e is ArgumentException or System.Security.SecurityException)
                {
                    return LyrValue.FromBool(false);
                }
            });

        registry.RegisterOptionalReturning("std.os.hostName", none, TypeTag.String,
            _ => Optional(TryIo(() => Environment.MachineName)));

        registry.RegisterOptionalReturning("std.os.userName", none, TypeTag.String,
            _ => Optional(TryIo(() => Environment.UserName)));

        registry.RegisterOptionalReturning("std.os.homeDir", none, TypeTag.String,
            _ => Optional(TryIo(() =>
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))));

        registry.Register("std.os.cpuCount", none, TypeTag.I64,
            _ => LyrValue.FromI64(Environment.ProcessorCount));

        // std.time's private clock — the same host function as std.os.nowMillis, two names, one
        // truth.
        registry.Register("std.time.nowMillis", none, TypeTag.I64,
            _ => LyrValue.FromI64(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));

        // std.task's ONE native (4.0): time and readiness in one answer, [now, readyFd...].
        // The clock is monotonic — differences mean something, the origin does not. A negative
        // timeout means "no deadline"; with no descriptors and no interrupt listener either,
        // that would sleep forever, so the scheduler never asks for it and the native refuses
        // rather than hanging a host. The descriptors are std.io.net's; a socket in error
        // reports as READABLE, so the read that follows is what tells the waiter, the same
        // convention select has always had. `wantInterrupt` (a task is parked on
        // Wait.Interrupt) arms the Ctrl+C handler and reports an interrupt as the impossible
        // descriptor -1; the flag is re-read every call, so the swallowing of Ctrl+C lasts
        // exactly as long as somebody is parked.
        var intArray = new BytecodeType(TypeTag.Array, -1) { Element = BytecodeType.Scalar(TypeTag.I64) };
        registry.RegisterWithTypes("std.task.poll",
            [intArray, intArray, BytecodeType.Scalar(TypeTag.I64), BytecodeType.Scalar(TypeTag.Bool)],
            intArray,
            args =>
            {
                var read = args[0].AsObject;
                var write = args[1].AsObject;
                var timeout = args[2].AsI64;
                var wantInterrupt = args[3].AsBool;

                // The count moves on TRANSITIONS only. Assigning per poll let a second VM
                // without a parked task clear the first one's listening, and then Ctrl+C
                // killed a process that was waiting for it.
                if (wantInterrupt != _listeningHere)
                {
                    _listeningHere = wantInterrupt;
                    Interlocked.Add(ref _listeningVms, wantInterrupt ? 1 : -1);
                }
                if (wantInterrupt) EnsureInterruptHandler();

                // A pending interrupt outranks every wait: taken (and reported) only when the
                // scheduler listens, remembered otherwise — a signal, not a broadcast.
                if (wantInterrupt && TakePendingInterrupt())
                    return LyrValue.FromObject(new[]
                    {
                        LyrValue.FromI64(Environment.TickCount64), LyrValue.FromI64(-1),
                    });

                if (read.Length == 0 && write.Length == 0)
                {
                    if (timeout < 0 && !wantInterrupt)
                        throw new LyricPanic(VmDiagnostics.Panicked,
                            "std.task.poll: waiting forever on nothing — no deadline and no "
                            + "descriptor");

                    if (wantInterrupt)
                    {
                        // Reset BEFORE the pending re-check: a raise between the two leaves
                        // the flag set for the check, or the event set for the wait — either
                        // way it is seen. A stale wake without a pending flag only makes the
                        // scheduler recompute and block again, which is correct in two steps.
                        InterruptEvent.Reset();
                        if (!TakePendingInterrupt())
                            InterruptEvent.Wait(timeout < 0
                                ? Timeout.Infinite
                                : (int)Math.Min(timeout, int.MaxValue));
                        if (TakePendingInterrupt())
                            return LyrValue.FromObject(new[]
                            {
                                LyrValue.FromI64(Environment.TickCount64), LyrValue.FromI64(-1),
                            });
                    }
                    else if (timeout > 0)
                    {
                        Thread.Sleep((int)Math.Min(timeout, int.MaxValue));
                    }

                    return LyrValue.FromObject(
                        new[] { LyrValue.FromI64(Environment.TickCount64) });
                }

                var checkRead = new List<System.Net.Sockets.Socket>();
                var checkWrite = new List<System.Net.Sockets.Socket>();
                var checkError = new List<System.Net.Sockets.Socket>();

                // A descriptor the table cannot resolve is closed, and its waiter is named
                // READY at once — the same convention an errored socket follows: the wake
                // exists so the next read tells the waiter what happened. Waiting on it
                // instead would be a wait nothing can end, and selecting on a list that
                // resolved to nothing throws inside the host (4.0 sweep: it did, and the
                // exception escaped as an unhandled crash rather than a panic).
                var dead = new List<LyrValue>();
                foreach (var value in read)
                    if (SocketOf(value.AsI64) is { } s) { checkRead.Add(s); checkError.Add(s); }
                    else dead.Add(value);
                foreach (var value in write)
                    if (SocketOf(value.AsI64) is { } s) { checkWrite.Add(s); checkError.Add(s); }
                    else dead.Add(value);

                if (dead.Count > 0)
                {
                    // Something is actionable now, so nothing is waited for. The interrupt
                    // pipe is not consulted: a pending interrupt is taken by the next poll,
                    // which is where the flag has waited for a listener all along.
                    var immediate = new List<LyrValue> { LyrValue.FromI64(Environment.TickCount64) };
                    var alreadyNamed = new HashSet<long>();
                    if (checkRead.Count > 0 || checkWrite.Count > 0)
                    {
                        System.Net.Sockets.Socket.Select(checkRead, checkWrite, checkError, 0);
                        foreach (var s in checkRead) NameReady(s, immediate, alreadyNamed);
                        foreach (var s in checkWrite) NameReady(s, immediate, alreadyNamed);
                        foreach (var s in checkError) NameReady(s, immediate, alreadyNamed);
                    }
                    foreach (var value in dead)
                        if (alreadyNamed.Add(value.AsI64)) immediate.Add(value);
                    return LyrValue.FromObject(immediate.ToArray());
                }

                // The self-pipe puts the interrupt into the SAME select: Ctrl+C writes a
                // datagram, the socket readies, the select returns. The read side never
                // reaches NameReady — it is drained and removed before the answer is built.
                var pipe = wantInterrupt ? EnsureInterruptPipe() : null;
                if (pipe is not null) checkRead.Add(pipe);

                // Select takes microseconds; clamping a huge timeout wakes the scheduler
                // early, which recomputes and blocks again — correct, just in two steps. The
                // clamp happens BEFORE the multiplication: a sleep of more than ~292 million
                // years overflowed the product into a negative number, which Select reads as
                // "wait forever" — the one wait that is never correct here.
                var micros = timeout < 0
                    ? -1
                    : (int)(Math.Min(timeout, int.MaxValue / 1000) * 1000);
                System.Net.Sockets.Socket.Select(checkRead, checkWrite, checkError, micros);

                var ready = new List<LyrValue> { LyrValue.FromI64(Environment.TickCount64) };
                if (pipe is not null && checkRead.Remove(pipe)) DrainPipe(pipe);
                if (wantInterrupt && TakePendingInterrupt())
                {
                    InterruptEvent.Reset();
                    ready.Add(LyrValue.FromI64(-1));
                }
                var named = new HashSet<long>();
                foreach (var s in checkRead) NameReady(s, ready, named);
                foreach (var s in checkWrite) NameReady(s, ready, named);
                foreach (var s in checkError) NameReady(s, ready, named);
                return LyrValue.FromObject(ready.ToArray());
            });

        // The programmatic Ctrl+C: same flag, same wake, callable from a task. What makes a
        // "quit" command and a signal one mechanism instead of two.
        registry.Register("std.task.interrupt", none, TypeTag.Void,
            _ =>
            {
                RaiseInterrupt();
                return default;
            });

        RegisterNet(registry);

        RegisterProcess(registry);

        RegisterFileHandles(registry);

        registry.Register("std.os.nowMillis", none, TypeTag.I64,
            _ => LyrValue.FromI64(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));

        // Monotonic, with an arbitrary origin, and therefore not comparable with nowMillis: a
        // system clock can jump during a measurement.
        registry.Register("std.os.nowNanos", none, TypeTag.I64,
            _ => LyrValue.FromI64(
                (long)(System.Diagnostics.Stopwatch.GetTimestamp()
                       * (1_000_000_000.0 / System.Diagnostics.Stopwatch.Frequency))));

        registry.Register("std.os.sleep", new[] { TypeTag.I64 }, TypeTag.Void,
            args =>
            {
                var millis = args[0].AsI64;
                if (millis > 0) Thread.Sleep((int)Math.Min(millis, int.MaxValue));
                return default;
            });


        // v1.15: the string METHOD API. Inside 'extend string' a method shadows the free
        // function of the same name, so the methods call raw* twins; the old public names stay
        // bound for bytecode compiled before the change. Same entries, second name each.
        void StringRaw(string name) =>
            registry._natives["std.string.raw" + char.ToUpperInvariant(name[0]) + name[1..]] =
                registry._natives["std.string." + name];

        StringRaw("length");
        StringRaw("charAt");
        StringRaw("substring");
        StringRaw("indexOf");
        StringRaw("contains");
        StringRaw("startsWith");
        StringRaw("endsWith");
        StringRaw("trim");
        StringRaw("toUpper");
        StringRaw("toLower");
        StringRaw("split");
        StringRaw("toChars");
        StringRaw("utf8Encode");

        return registry;
    }

    // ------------------------------------------------------------------ code point helpers

    private static long CodepointCount(string s)
    {
        var n = 0L;
        for (var i = 0; i < s.Length; i += char.IsHighSurrogate(s[i]) ? 2 : 1) n++;
        return n;
    }

    /// <summary>The code point at <paramref name="index"/>, counted in code points. Out of range
    /// is a panic, as for an array index.</summary>
    private static int CodepointAt(string s, long index)
    {
        if (index < 0) throw OutOfRange(index);

        var seen = 0L;
        for (var i = 0; i < s.Length; i += char.IsHighSurrogate(s[i]) ? 2 : 1)
        {
            if (seen == index) return char.ConvertToUtf32(s, i);
            seen++;
        }
        throw OutOfRange(index);
    }

    private static LyricPanic OutOfRange(long index) =>
        new(VmDiagnostics.IndexOutOfRange, $"string index {index} is out of range");

    private static string Substring(string s, long start, long count)
    {
        if (start < 0 || count < 0) throw OutOfRange(start < 0 ? start : count);

        var offsets = Offsets(s);
        if (start > offsets.Count - 1) throw OutOfRange(start);

        var from = offsets[(int)start];
        var end = start + count;
        var to = end >= offsets.Count - 1 ? s.Length : offsets[(int)end];
        return s[from..to];
    }

    /// <summary>The UTF-16 offsets of every code point, plus a sentinel, so a code-point position
    /// becomes a slice without counting from the start each time.</summary>
    private static List<int> Offsets(string s)
    {
        var offsets = new List<int>();
        for (var i = 0; i < s.Length; i += char.IsHighSurrogate(s[i]) ? 2 : 1) offsets.Add(i);
        offsets.Add(s.Length);
        return offsets;
    }

    /// <summary>Position in code points, so the result can be passed to charAt or substring.
    /// Minus one when not found.</summary>
    private static long IndexOf(string s, string needle)
    {
        var at = s.IndexOf(needle, StringComparison.Ordinal);
        if (at < 0) return -1;

        var count = 0L;
        for (var i = 0; i < at; i += char.IsHighSurrogate(s[i]) ? 2 : 1) count++;
        return count;
    }

    private static LyrValue Split(string s, string separator)
    {
        // An empty separator has no sensible answer; it is a panic.
        if (separator.Length == 0)
            throw new LyricPanic(VmDiagnostics.IndexOutOfRange, "split needs a non-empty separator");

        var parts = s.Split(separator, StringSplitOptions.None);
        var values = new LyrValue[parts.Length];
        for (var i = 0; i < parts.Length; i++) values[i] = LyrValue.FromString(parts[i]);
        return LyrValue.FromObject(values);
    }

    // ------------------------------------------------------------------ std.os/std.io.file

    /// <summary>A <c>?string</c> from a possibly absent value: a reference means present, an empty
    /// one means <c>null</c>.</summary>
    private static LyrValue Optional(string? value) =>
        value is null ? default : LyrValue.FromString(value);

    /// <summary>The shape <c>std.string.parseFloat</c> accepts: an optional sign, digits with at
    /// most one point among them — at least one digit in the mantissa, and at least one behind a
    /// point when there is one — and an optional exponent with its own optional sign and at least
    /// one digit. Nothing before, between or after.</summary>
    private static bool FloatShaped(string text)
    {
        var i = 0;
        if (i < text.Length && (text[i] == '+' || text[i] == '-')) i++;

        var mantissaDigits = 0;
        while (i < text.Length && text[i] >= '0' && text[i] <= '9') { mantissaDigits++; i++; }

        if (i < text.Length && text[i] == '.')
        {
            i++;
            var fractionDigits = 0;
            while (i < text.Length && text[i] >= '0' && text[i] <= '9') { fractionDigits++; i++; }
            if (fractionDigits == 0) return false;
            mantissaDigits += fractionDigits;
        }
        if (mantissaDigits == 0) return false;

        if (i < text.Length && (text[i] == 'e' || text[i] == 'E'))
        {
            i++;
            if (i < text.Length && (text[i] == '+' || text[i] == '-')) i++;
            var exponentDigits = 0;
            while (i < text.Length && text[i] >= '0' && text[i] <= '9') { exponentDigits++; i++; }
            if (exponentDigits == 0) return false;
        }
        return i == text.Length;
    }

    /// <summary>A code point from a <see cref="TextReader"/>, combining surrogate pairs.</summary>
    /// <remarks>.NET's <c>Read()</c> yields UTF-16 units while a Lyric <c>char</c> is a code
    /// point. Without combining the pair, a character beyond the BMP would come back as a lone
    /// surrogate half, which is not a valid <c>char</c>.
    /// </remarks>
    private static LyrValue ReadCodepoint(TextReader reader)
    {
        var first = reader.Read();
        if (first < 0) return default;   // EOF

        if (char.IsHighSurrogate((char)first) && reader.Peek() is var next && next >= 0
            && char.IsLowSurrogate((char)next))
            return LyrValue.FromBits((ulong)char.ConvertToUtf32((char)first, (char)reader.Read()));

        return LyrValue.FromBits((ulong)first);
    }

    /// <summary>Runs a file operation and returns <c>null</c> when it fails.
    ///
    /// <para>The catch is broad: missing file, missing permission, invalid path, device gone.
    /// To the caller they are the same answer.</para></summary>
    // The classification of the last FAILED std.io.file operation, read by the
    // lastErrorKind/lastErrorDetail natives — which the OrThrow forms in file.lyr call
    // immediately after a silent form answered null/false. The interpreter runs an execution on
    // one thread, so nothing can run between the failure and the read; ThreadStatic rather than
    // static keeps parallel VMs (the test host) from seeing each other's failures. A SUCCESS
    // does not clear it: only the null/false branch ever reads.
    //
    // The codes are a contract with std/io/file.lyr's kindOf: 1 NotFound, 2 PermissionDenied,
    // 3 InvalidPath, 0 Other.
    [ThreadStatic] private static int _lastIoKind;
    [ThreadStatic] private static string? _lastIoDetail;

    // The byte offset of the last utf8Decode refusal, for std.string.utf8DecodeOrThrow — the
    // same last-failure contract as the pair above.
    [ThreadStatic] private static int _lastUtf8ErrorOffset;

    private static void RecordIo(Exception e)
    {
        _lastIoKind = e switch
        {
            FileNotFoundException or DirectoryNotFoundException => 1,
            UnauthorizedAccessException or System.Security.SecurityException => 2,
            PathTooLongException or ArgumentException or NotSupportedException => 3,
            _ => 0,
        };
        _lastIoDetail = e.Message;
    }

    private static void RecordIoNotFound(string path)
    {
        _lastIoKind = 1;
        _lastIoDetail = $"no file at '{path}'";
    }

    private static string? TryIo(Func<string> operation)
    {
        try
        {
            return operation();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException
                                       or ArgumentException or NotSupportedException)
        {
            RecordIo(e);
            return null;
        }
    }

    /// <summary>Lines without their terminators, so the result does not depend on whether the
    /// file was written with CRLF or LF.</summary>
    /// <summary>As <see cref="TryIo"/>, for an operation whose answer is bytes.</summary>
    private static byte[]? TryIoBytes(Func<byte[]> operation)
    {
        try
        {
            return operation();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException
                                       or ArgumentException or NotSupportedException)
        {
            RecordIo(e);
            return null;
        }
    }

    // ------------------------------------------------------------------ std.io.net (4.0)
    //
    // Sockets live in a per-thread table under integer descriptors — the numbers Wait.Readable
    // carries and std.task.poll selects over. ThreadStatic for the same reason as the io
    // classification: parallel VMs (the test host) must not see each other's sockets. Every
    // socket is non-blocking; the WAITING lives in std.task, a native only ever answers now.

    [ThreadStatic] private static Dictionary<long, System.Net.Sockets.Socket>? _sockets;
    [ThreadStatic] private static long _nextSocketFd;

    private static Dictionary<long, System.Net.Sockets.Socket> Sockets => _sockets ??= new();

    private static System.Net.Sockets.Socket? SocketOf(long fd) =>
        _sockets is { } table && table.TryGetValue(fd, out var socket) ? socket : null;

    private static long AddSocket(System.Net.Sockets.Socket socket)
    {
        var fd = ++_nextSocketFd;
        Sockets[fd] = socket;
        return fd;
    }

    private static void NameReady(System.Net.Sockets.Socket socket, List<LyrValue> ready,
        HashSet<long> named)
    {
        if (_sockets is null) return;
        foreach (var (fd, s) in _sockets)
            if (ReferenceEquals(s, socket) && named.Add(fd))
            {
                ready.Add(LyrValue.FromI64(fd));
                return;
            }
    }

    // --------------------------------------------------------------- std.task interrupt (4.0)
    //
    // TWO pending flags, because there are two events wearing one name. The SIGNAL is
    // process-wide — SIGINT has no idea which VM it means, and a standalone program has one
    // anyway. The programmatic `interrupt()` is PER VM, like the socket table above and for
    // the same reason: parallel VMs (a host, the test runner) must not reach into each
    // other, and one guest raising its own shutdown must not end another's. Either flag
    // survives until a listening poll takes it — an interrupt raised while nobody is parked
    // is remembered, like a pending signal.
    //
    // The WAKE mechanisms stay shared, and may be woken spuriously: an event set or a
    // datagram that belongs to another VM only makes this one recompute and block again,
    // which is the same two-step the timeout clamp already relies on.
    //
    // Three wake paths for the three ways a poll can be waiting: the flag for a poll that has
    // not started waiting, the event for a descriptorless wait, and a self-pipe datagram for
    // a poll inside select.

    [ThreadStatic] private static int _interruptPending;
    private static int _signalPending;

    // Whether THIS VM has a task parked, and how many VMs do — the handler reads the count,
    // because a VM polling without a parked task must not clear another VM's listening.
    [ThreadStatic] private static bool _listeningHere;
    private static int _listeningVms;
    private static int _interruptHandlerInstalled;
    private static System.Net.Sockets.Socket? _interruptPipe;
    private static readonly ManualResetEventSlim InterruptEvent = new(false);

    /// <summary>Takes whichever interrupt is pending for this VM: its own raise, or the
    /// process's signal. Both are taken — one wake answers both.</summary>
    private static bool TakePendingInterrupt()
    {
        var mine = _interruptPending == 1;
        _interruptPending = 0;
        var signal = Interlocked.Exchange(ref _signalPending, 0) == 1;
        return mine || signal;
    }

    /// <summary>Records that this VM's own `interrupt()` was raised, and wakes the waiters.
    /// </summary>
    private static void RaiseInterrupt()
    {
        _interruptPending = 1;
        WakeInterruptWaiters();
    }

    /// <summary>Records the process signal — from the Ctrl+C handler, on whatever thread the
    /// runtime hands it — and wakes the waiters.</summary>
    private static void RaiseSignal()
    {
        Interlocked.Exchange(ref _signalPending, 1);
        WakeInterruptWaiters();
    }

    private static void WakeInterruptWaiters()
    {
        InterruptEvent.Set();
        try
        {
            _interruptPipe?.Send(new byte[] { 1 });
        }
        catch (System.Net.Sockets.SocketException)
        {
            // A full or torn pipe loses only the select wake; the flag still stands, and the
            // next poll's pending check reads it.
        }
    }

    private static void EnsureInterruptHandler()
    {
        if (Interlocked.Exchange(ref _interruptHandlerInstalled, 1) == 1) return;
        Console.CancelKeyPress += (_, e) =>
        {
            // Nobody listening: the default stands and Ctrl+C ends the process — a program
            // without a parked Interrupt task keeps behaving like every other program.
            if (Volatile.Read(ref _listeningVms) == 0) return;
            e.Cancel = true;
            RaiseSignal();
        };
    }

    // A UDP socket sent to itself over loopback: the one self-pipe shape that select accepts
    // on every platform Socket.Select runs on.
    private static System.Net.Sockets.Socket EnsureInterruptPipe()
    {
        if (_interruptPipe is { } existing) return existing;
        var pipe = new System.Net.Sockets.Socket(
            System.Net.Sockets.AddressFamily.InterNetwork,
            System.Net.Sockets.SocketType.Dgram,
            System.Net.Sockets.ProtocolType.Udp);
        pipe.Bind(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0));
        pipe.Connect(pipe.LocalEndPoint!);
        pipe.Blocking = false;
        _interruptPipe = pipe;
        return pipe;
    }

    private static void DrainPipe(System.Net.Sockets.Socket pipe)
    {
        var swallow = new byte[16];
        try
        {
            while (pipe.Available > 0) pipe.Receive(swallow);
        }
        catch (System.Net.Sockets.SocketException)
        {
            // Nothing left to read is exactly the state draining wants.
        }
    }

    // The classification of the last failed std.io.net operation — the same last-failure
    // contract as the io pair above. The codes are a contract with std/io/net.lyr's kindOf:
    // 1 NotFound, 2 PermissionDenied, 4 ConnectionRefused, 5 AddressInUse, 6 WouldBlock,
    // 0 Other.
    [ThreadStatic] private static int _lastNetKind;
    [ThreadStatic] private static string? _lastNetDetail;

    private static void RecordNet(Exception e)
    {
        _lastNetKind = e is System.Net.Sockets.SocketException se
            ? se.SocketErrorCode switch
            {
                System.Net.Sockets.SocketError.ConnectionRefused => 4,
                System.Net.Sockets.SocketError.AddressAlreadyInUse => 5,
                System.Net.Sockets.SocketError.WouldBlock
                    or System.Net.Sockets.SocketError.InProgress => 6,
                System.Net.Sockets.SocketError.HostNotFound
                    or System.Net.Sockets.SocketError.NoData => 1,
                System.Net.Sockets.SocketError.AccessDenied => 2,
                _ => 0,
            }
            : 0;
        _lastNetDetail = e.Message;
    }

    /// <summary>Records "that descriptor is not a socket" and passes the caller's answer
    /// through. A closed handle is a real failure with a real reason, and the <c>OrThrow</c>
    /// twins read the LAST one recorded — without this they named the previous call's.</summary>
    private static long NotASocket(long answer)
    {
        RecordNet(new System.Net.Sockets.SocketException(
            (int)System.Net.Sockets.SocketError.NotSocket));
        return answer;
    }

    private static System.Net.IPAddress ResolveHost(string host)
    {
        if (host is "localhost" or "") return System.Net.IPAddress.Loopback;
        if (System.Net.IPAddress.TryParse(host, out var literal)) return literal;
        var found = System.Net.Dns.GetHostAddresses(host);
        if (found.Length == 0)
            throw new System.Net.Sockets.SocketException(
                (int)System.Net.Sockets.SocketError.HostNotFound);
        return found[0];
    }

    private static System.Net.Sockets.Socket NewTcp(System.Net.IPAddress address) =>
        new(address.AddressFamily, System.Net.Sockets.SocketType.Stream,
            System.Net.Sockets.ProtocolType.Tcp)
        { Blocking = false };

    private static void RegisterNet(NativeRegistry registry)
    {
        var i64 = TypeTag.I64;

        registry.Register("std.io.net.netListen", [TypeTag.String, i64], i64, args =>
        {
            try
            {
                var address = ResolveHost(args[0].AsString);
                var socket = NewTcp(address);
                socket.Bind(new System.Net.IPEndPoint(address, (int)args[1].AsI64));
                socket.Listen(64);
                return LyrValue.FromI64(AddSocket(socket));
            }
            catch (Exception e) { RecordNet(e); return LyrValue.FromI64(-1); }
        });

        registry.Register("std.io.net.netLocalPort", [i64], i64, args =>
            SocketOf(args[0].AsI64)?.LocalEndPoint is System.Net.IPEndPoint at
                ? LyrValue.FromI64(at.Port)
                : LyrValue.FromI64(-1));

        registry.Register("std.io.net.netAccept", [i64], i64, args =>
        {
            if (SocketOf(args[0].AsI64) is not { } listener) return LyrValue.FromI64(NotASocket(-2));
            try
            {
                var accepted = listener.Accept();
                accepted.Blocking = false;
                return LyrValue.FromI64(AddSocket(accepted));
            }
            catch (System.Net.Sockets.SocketException e)
                when (e.SocketErrorCode == System.Net.Sockets.SocketError.WouldBlock)
            {
                RecordNet(e);
                return LyrValue.FromI64(-1);
            }
            catch (Exception e) { RecordNet(e); return LyrValue.FromI64(-2); }
        });

        registry.Register("std.io.net.netConnectStart", [TypeTag.String, i64], i64, args =>
        {
            try
            {
                var address = ResolveHost(args[0].AsString);
                var socket = NewTcp(address);
                try
                {
                    socket.Connect(new System.Net.IPEndPoint(address, (int)args[1].AsI64));
                }
                catch (System.Net.Sockets.SocketException e)
                    when (e.SocketErrorCode is System.Net.Sockets.SocketError.WouldBlock
                        or System.Net.Sockets.SocketError.InProgress) { /* in flight */ }
                return LyrValue.FromI64(AddSocket(socket));
            }
            catch (Exception e) { RecordNet(e); return LyrValue.FromI64(-1); }
        });

        registry.Register("std.io.net.netConnectDone", [i64], i64, args =>
        {
            if (SocketOf(args[0].AsI64) is not { } socket) return LyrValue.FromI64(NotASocket(-1));
            try
            {
                if (socket.Poll(0, System.Net.Sockets.SelectMode.SelectError))
                {
                    var code = (System.Net.Sockets.SocketError)(int)socket.GetSocketOption(
                        System.Net.Sockets.SocketOptionLevel.Socket,
                        System.Net.Sockets.SocketOptionName.Error)!;
                    RecordNet(new System.Net.Sockets.SocketException((int)code));
                    return LyrValue.FromI64(-1);
                }
                return LyrValue.FromI64(
                    socket.Poll(0, System.Net.Sockets.SelectMode.SelectWrite) ? 1 : 0);
            }
            catch (Exception e) { RecordNet(e); return LyrValue.FromI64(-1); }
        });

        registry.RegisterOptionalArrayReturning("std.io.net.netReadReady", [i64, i64],
            TypeTag.U8, args =>
        {
            // A descriptor the table does not know is CLOSED, and that is a failure, not a
            // reason to wait. Answering None without recording one left the previous kind
            // standing — usually would-block, which sent the module back to the scheduler to
            // wait on a descriptor nothing could ever ready (4.0 sweep).
            if (SocketOf(args[0].AsI64) is not { } socket)
            {
                RecordNet(new System.Net.Sockets.SocketException(
                    (int)System.Net.Sockets.SocketError.NotSocket));
                return LyrValue.None;
            }
            var max = (int)Math.Clamp(args[1].AsI64, 1, 1 << 20);
            var buffer = new byte[max];
            try
            {
                var got = socket.Receive(buffer, 0, max, System.Net.Sockets.SocketFlags.None);
                if (got == 0) return Bytes([]); // the peer is done: an EOF, not a failure
                return Bytes(buffer[..got]);
            }
            catch (System.Net.Sockets.SocketException e)
                when (e.SocketErrorCode == System.Net.Sockets.SocketError.WouldBlock)
            {
                RecordNet(e);
                return LyrValue.None;
            }
            catch (Exception e) { RecordNet(e); return LyrValue.None; }
        });

        registry.RegisterWithArrayParams("std.io.net.netWriteFrom",
            [i64, TypeTag.Array, i64], [null, TypeTag.U8, null], i64, args =>
        {
            if (SocketOf(args[0].AsI64) is not { } socket) return LyrValue.FromI64(NotASocket(-2));
            var bytes = ToBytes(args[1]);
            var offset = (int)Math.Clamp(args[2].AsI64, 0, bytes.Length);
            try
            {
                var sent = socket.Send(bytes, offset, bytes.Length - offset,
                    System.Net.Sockets.SocketFlags.None);
                return LyrValue.FromI64(sent);
            }
            catch (System.Net.Sockets.SocketException e)
                when (e.SocketErrorCode == System.Net.Sockets.SocketError.WouldBlock)
            {
                RecordNet(e);
                return LyrValue.FromI64(-1);
            }
            catch (Exception e) { RecordNet(e); return LyrValue.FromI64(-2); }
        });

        registry.Register("std.io.net.netClose", [i64], TypeTag.Void, args =>
        {
            var fd = args[0].AsI64;
            if (SocketOf(fd) is { } socket)
            {
                try { socket.Close(); } catch (Exception) { /* closing twice closes once */ }
                Sockets.Remove(fd);
            }
            return default;
        });

        registry.Register("std.io.net.lastErrorKind", [], i64,
            _ => LyrValue.FromI64(_lastNetKind));
        registry.Register("std.io.net.lastErrorDetail", [], TypeTag.String,
            _ => LyrValue.FromString(_lastNetDetail ?? ""));

        // ---------------------------------------------------------------------------- UDP
        //
        // The same table, the same non-blocking rule, the same select through poll. The
        // received sender parks in a thread-local pair — the last-failure convention applied
        // to a second two-part answer.

        registry.Register("std.io.net.udpBind", [TypeTag.String, i64], i64, args =>
        {
            try
            {
                var address = ResolveHost(args[0].AsString);
                var socket = new System.Net.Sockets.Socket(address.AddressFamily,
                    System.Net.Sockets.SocketType.Dgram,
                    System.Net.Sockets.ProtocolType.Udp)
                { Blocking = false };
                socket.Bind(new System.Net.IPEndPoint(address, (int)args[1].AsI64));
                return LyrValue.FromI64(AddSocket(socket));
            }
            catch (Exception e) { RecordNet(e); return LyrValue.FromI64(-1); }
        });

        registry.RegisterWithArrayParams("std.io.net.udpSendTo",
            [i64, TypeTag.String, i64, TypeTag.Array], [null, null, null, TypeTag.U8], i64,
            args =>
        {
            if (SocketOf(args[0].AsI64) is not { } socket) return LyrValue.FromI64(NotASocket(-2));
            try
            {
                var target = new System.Net.IPEndPoint(
                    ResolveHost(args[1].AsString), (int)args[2].AsI64);
                socket.SendTo(ToBytes(args[3]), target);
                return LyrValue.FromI64(1);
            }
            catch (System.Net.Sockets.SocketException e)
                when (e.SocketErrorCode == System.Net.Sockets.SocketError.WouldBlock)
            {
                RecordNet(e);
                return LyrValue.FromI64(-1);
            }
            catch (Exception e) { RecordNet(e); return LyrValue.FromI64(-2); }
        });

        registry.RegisterOptionalArrayReturning("std.io.net.udpReceive", [i64, i64],
            TypeTag.U8, args =>
        {
            if (SocketOf(args[0].AsI64) is not { } socket)
            {
                RecordNet(new System.Net.Sockets.SocketException(
                    (int)System.Net.Sockets.SocketError.NotSocket));
                return LyrValue.None;
            }
            var max = (int)Math.Clamp(args[1].AsI64, 1, 1 << 20);
            var buffer = new byte[max];
            System.Net.EndPoint sender = new System.Net.IPEndPoint(
                socket.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
                    ? System.Net.IPAddress.IPv6Any
                    : System.Net.IPAddress.Any, 0);
            try
            {
                var got = socket.ReceiveFrom(buffer, ref sender);
                var from = (System.Net.IPEndPoint)sender;
                _lastUdpSenderHost = from.Address.ToString();
                _lastUdpSenderPort = from.Port;
                return Bytes(buffer[..got]);
            }
            catch (System.Net.Sockets.SocketException e)
                when (e.SocketErrorCode == System.Net.Sockets.SocketError.WouldBlock)
            {
                RecordNet(e);
                return LyrValue.None;
            }
            catch (System.Net.Sockets.SocketException e)
                when (e.SocketErrorCode == System.Net.Sockets.SocketError.MessageSize)
            {
                // The OS CUT the datagram to the buffer; the module documents the cut, so
                // what fits is the answer, not a failure. The sender still arrived.
                var from = (System.Net.IPEndPoint)sender;
                _lastUdpSenderHost = from.Address.ToString();
                _lastUdpSenderPort = from.Port;
                return Bytes(buffer);
            }
            catch (Exception e) { RecordNet(e); return LyrValue.None; }
        });

        registry.Register("std.io.net.udpSenderHost", [], TypeTag.String,
            _ => LyrValue.FromString(_lastUdpSenderHost ?? ""));
        registry.Register("std.io.net.udpSenderPort", [], i64,
            _ => LyrValue.FromI64(_lastUdpSenderPort));
    }

    [ThreadStatic] private static string? _lastUdpSenderHost;
    [ThreadStatic] private static int _lastUdpSenderPort;

    // ------------------------------------------------------------------ std.process (4.0)
    //
    // A child lives in a per-thread table like the sockets do, PLUS one notify socket — a UDP
    // self-pipe registered in the SOCKET table, so the scheduler waits on the child with the
    // ordinary Wait.Readable and the one poll native. The host pumps the child's stdout and
    // stderr into buffers on pool threads and posts a datagram per event (a chunk arrived, a
    // stream ended, the child exited, stdin broke); the natives answer from the buffers and
    // never block. stdin is a queue drained by its own writer thread — a write enqueues and
    // returns, and the pipe-full case costs memory rather than a wait.

    private sealed class ChildState
    {
        public required System.Diagnostics.Process Process { get; init; }
        public required System.Net.Sockets.Socket Notify { get; init; }
        public required long NotifyFd { get; init; }
        public readonly object Gate = new();
        public readonly List<byte[]> OutChunks = [];
        public int OutOffset;
        public bool OutEof;
        public bool OutFail;
        public readonly List<byte[]> ErrChunks = [];
        public int ErrOffset;
        public bool ErrEof;
        public bool ErrFail;
        public readonly System.Collections.Concurrent.BlockingCollection<byte[]> StdinQueue =
            new();
        public volatile bool StdinBroken;
        public bool Closed;
    }

    [ThreadStatic] private static Dictionary<long, ChildState>? _children;
    [ThreadStatic] private static long _nextChildKey;
    [ThreadStatic] private static int _lastProcKind;
    [ThreadStatic] private static string? _lastProcDetail;

    private static Dictionary<long, ChildState> Children => _children ??= new();

    private static ChildState? ChildOf(long key) =>
        _children is { } table && table.TryGetValue(key, out var child) ? child : null;

    private static void RecordProc(int kind, string detail)
    {
        _lastProcKind = kind;
        _lastProcDetail = detail;
    }

    // The event side runs on pool threads; a lost datagram only costs a wake, because every
    // native that answers a waiter drains the pipe and re-reads the buffers first.
    private static void Poke(ChildState state) => Poke(state.Notify);

    private static void Poke(System.Net.Sockets.Socket notify)
    {
        try
        {
            notify.Send(new byte[] { 1 });
        }
        catch (System.Net.Sockets.SocketException)
        {
            // The pipe being gone means nobody is waiting on it any more.
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static void StartPump(ChildState state, Stream stream, bool stderr) =>
        Task.Run(async () =>
        {
            var buffer = new byte[8192];
            try
            {
                while (true)
                {
                    var got = await stream.ReadAsync(buffer).ConfigureAwait(false);
                    if (got <= 0) break;
                    var chunk = buffer[..got];
                    lock (state.Gate)
                    {
                        (stderr ? state.ErrChunks : state.OutChunks).Add(chunk);
                    }
                    Poke(state);
                }
                lock (state.Gate)
                {
                    if (stderr) state.ErrEof = true;
                    else state.OutEof = true;
                }
            }
            catch (Exception)
            {
                lock (state.Gate)
                {
                    if (stderr) state.ErrFail = true;
                    else state.OutFail = true;
                }
            }
            Poke(state);
        });

    private static void StartStdinWriter(ChildState state, Stream stdin) =>
        Task.Run(() =>
        {
            try
            {
                foreach (var chunk in state.StdinQueue.GetConsumingEnumerable())
                {
                    stdin.Write(chunk);
                    stdin.Flush();
                }
                stdin.Close();
            }
            catch (Exception)
            {
                state.StdinBroken = true;
                try { stdin.Close(); } catch (Exception) { /* already broken */ }
            }
            Poke(state);
        });

    private static byte[] TakeBuffered(List<byte[]> chunks, ref int offset, int max)
    {
        var taken = new List<byte>(Math.Min(max, 8192));
        while (taken.Count < max && chunks.Count > 0)
        {
            var head = chunks[0];
            var want = Math.Min(head.Length - offset, max - taken.Count);
            for (var i = 0; i < want; i++) taken.Add(head[offset + i]);
            offset += want;
            if (offset == head.Length)
            {
                chunks.RemoveAt(0);
                offset = 0;
            }
        }
        return [.. taken];
    }

    private static void RegisterProcess(NativeRegistry registry)
    {
        var i64 = TypeTag.I64;

        registry.RegisterWithArrayParams("std.process.procStart",
            [TypeTag.String, TypeTag.Array], [null, TypeTag.String], i64, args =>
        {
            var program = args[0].AsString;
            var arguments = (LyrValue[])args[1].AsObject;

            var info = new System.Diagnostics.ProcessStartInfo(program)
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var argument in arguments) info.ArgumentList.Add(argument.AsString);

            var process = new System.Diagnostics.Process
            {
                StartInfo = info,
                EnableRaisingEvents = true,
            };

            System.Net.Sockets.Socket notify;
            try
            {
                if (!process.Start())
                {
                    RecordProc(0, "the process did not start");
                    return LyrValue.FromI64(-1);
                }
            }
            catch (System.ComponentModel.Win32Exception e)
            {
                // Windows 2/3 = file/path not found and 5 = access denied; on Unix the code
                // is errno, 2 ENOENT and 13 EACCES.
                RecordProc(e.NativeErrorCode switch
                {
                    2 or 3 => 1,
                    5 or 13 => 2,
                    _ => 0,
                }, e.Message);
                return LyrValue.FromI64(-1);
            }
            catch (Exception e)
            {
                RecordProc(0, e.Message);
                return LyrValue.FromI64(-1);
            }

            notify = new System.Net.Sockets.Socket(
                System.Net.Sockets.AddressFamily.InterNetwork,
                System.Net.Sockets.SocketType.Dgram,
                System.Net.Sockets.ProtocolType.Udp);
            notify.Bind(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0));
            notify.Connect(notify.LocalEndPoint!);
            notify.Blocking = false;

            var state = new ChildState
            {
                Process = process,
                Notify = notify,
                NotifyFd = AddSocket(notify),
            };
            var key = ++_nextChildKey;
            Children[key] = state;

            StartPump(state, process.StandardOutput.BaseStream, stderr: false);
            StartPump(state, process.StandardError.BaseStream, stderr: true);
            StartStdinWriter(state, process.StandardInput.BaseStream);
            process.Exited += (_, _) => Poke(state);

            return LyrValue.FromI64(key);
        });

        registry.Register("std.process.procNotifyFd", [i64], i64, args =>
            LyrValue.FromI64(ChildOf(args[0].AsI64)?.NotifyFd ?? -1));

        registry.RegisterOptionalArrayReturning("std.process.procTryRead",
            [i64, TypeTag.Bool, i64], TypeTag.U8, args =>
        {
            if (ChildOf(args[0].AsI64) is not { } state)
            {
                RecordProc(0, "no such child");
                return LyrValue.None;
            }
            var stderr = args[1].AsBool;
            var max = (int)Math.Clamp(args[2].AsI64, 1, 1 << 20);

            DrainPipe(state.Notify);
            lock (state.Gate)
            {
                var chunks = stderr ? state.ErrChunks : state.OutChunks;
                if (chunks.Count > 0)
                    return Bytes(stderr
                        ? TakeBuffered(chunks, ref state.ErrOffset, max)
                        : TakeBuffered(chunks, ref state.OutOffset, max));
                if (stderr ? state.ErrFail : state.OutFail)
                {
                    RecordProc(0, "the stream broke");
                    return LyrValue.None;
                }
                if (stderr ? state.ErrEof : state.OutEof) return Bytes([]);
                RecordProc(6, "nothing buffered yet");
                return LyrValue.None;
            }
        });

        registry.RegisterWithArrayParams("std.process.procWrite",
            [i64, TypeTag.Array], [null, TypeTag.U8], TypeTag.Bool, args =>
        {
            if (ChildOf(args[0].AsI64) is not { } state || state.StdinBroken
                || state.StdinQueue.IsAddingCompleted)
            {
                RecordProc(0, "stdin is not writable");
                return LyrValue.FromBool(false);
            }
            state.StdinQueue.Add(ToBytes(args[1]));
            return LyrValue.FromBool(true);
        });

        registry.Register("std.process.procCloseStdin", [i64], TypeTag.Void, args =>
        {
            if (ChildOf(args[0].AsI64) is { } state && !state.StdinQueue.IsAddingCompleted)
                state.StdinQueue.CompleteAdding();
            return default;
        });

        registry.Register("std.process.procHasExited", [i64], TypeTag.Bool, args =>
        {
            if (ChildOf(args[0].AsI64) is not { } state) return LyrValue.FromBool(true);
            DrainPipe(state.Notify);
            try
            {
                return LyrValue.FromBool(state.Process.HasExited);
            }
            catch (InvalidOperationException)
            {
                return LyrValue.FromBool(true);
            }
        });

        registry.Register("std.process.procExitCode", [i64], i64, args =>
        {
            if (ChildOf(args[0].AsI64) is not { } state) return LyrValue.FromI64(-1);
            try
            {
                return LyrValue.FromI64(state.Process.ExitCode);
            }
            catch (InvalidOperationException)
            {
                return LyrValue.FromI64(-1);
            }
        });

        registry.Register("std.process.procKill", [i64], TypeTag.Void, args =>
        {
            if (ChildOf(args[0].AsI64) is { } state)
                try { state.Process.Kill(entireProcessTree: true); }
                catch (Exception) { /* already gone is exactly the goal */ }
            return default;
        });

        registry.Register("std.process.procClose", [i64], TypeTag.Void, args =>
        {
            var key = args[0].AsI64;
            if (ChildOf(key) is not { } state || state.Closed) return default;
            state.Closed = true;
            if (!state.StdinQueue.IsAddingCompleted) state.StdinQueue.CompleteAdding();
            try { state.Notify.Close(); } catch (Exception) { /* twice closes once */ }
            Sockets.Remove(state.NotifyFd);
            try { state.Process.Dispose(); } catch (Exception) { /* disowned, not stopped */ }
            Children.Remove(key);
            return default;
        });

        registry.Register("std.process.lastErrorKind", [], i64,
            _ => LyrValue.FromI64(_lastProcKind));
        registry.Register("std.process.lastErrorDetail", [], TypeTag.String,
            _ => LyrValue.FromString(_lastProcDetail ?? ""));
    }

    // ---------------------------------------------------------- std.io.stream (4.2)
    //
    // The std.process shape rather than the std.io.net one, and the reason is the platform: a
    // regular file is not selectable. Socket.Select takes only sockets, and a POSIX select()
    // over a file descriptor reports it ready whatever its state, so readiness cannot be asked
    // for. An open file therefore carries a notify socket like a child does, the work runs on a
    // pool thread, and the scheduler waits on it with the ordinary Wait.Readable through the one
    // poll native. Every native here answers now; the WAITING lives in std/io/file.lyr.
    //
    // One read and one write may be in flight per handle. The module yields until each settles,
    // so a second start cannot arrive from Lyric -- the check exists for the runtime's own sake.

    private sealed class FileState
    {
        public required FileStream Stream { get; init; }
        public required System.Net.Sockets.Socket Notify { get; init; }
        public required long NotifyFd { get; init; }
        public readonly object Gate = new();
        public readonly List<byte[]> Chunks = [];
        public int Offset;
        public bool Reading;
        public bool Eof;
        public bool ReadFailed;
        public bool Writing;
        public int WriteOutcome;
        public string? Detail;
        public bool Closed;
    }

    [ThreadStatic] private static Dictionary<long, FileState>? _files;
    [ThreadStatic] private static long _nextFileKey;

    private static Dictionary<long, FileState> Files => _files ??= new();

    private static FileState? FileOf(long key) =>
        _files is { } table && table.TryGetValue(key, out var state) ? state : null;

    // Kind 6 is the would-block signal std.io.net and std.process already speak; it never
    // surfaces to a caller, because the module turns it into a wait.
    private static void RecordIoWouldBlock()
    {
        _lastIoKind = 6;
        _lastIoDetail = "the operation has not finished";
    }

    private static void RecordIoNoHandle()
    {
        _lastIoKind = 0;
        _lastIoDetail = "no such open file";
    }

    private static void StartRead(FileState state, int max)
    {
        state.Reading = true;
        _ = Task.Run(async () =>
        {
            var buffer = new byte[max];
            try
            {
                var got = await state.Stream.ReadAsync(buffer.AsMemory(0, max))
                    .ConfigureAwait(false);
                lock (state.Gate)
                {
                    if (got <= 0) state.Eof = true;
                    else state.Chunks.Add(buffer[..got]);
                    state.Reading = false;
                }
            }
            catch (Exception e)
            {
                lock (state.Gate)
                {
                    state.ReadFailed = true;
                    state.Detail = e.Message;
                    state.Reading = false;
                }
            }
            Poke(state.Notify);
        });
    }

    private static void StartWrite(FileState state, byte[] bytes)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await state.Stream.WriteAsync(bytes).ConfigureAwait(false);
                await state.Stream.FlushAsync().ConfigureAwait(false);
                lock (state.Gate)
                {
                    state.WriteOutcome = 1;
                    state.Writing = false;
                }
            }
            catch (Exception e)
            {
                lock (state.Gate)
                {
                    state.WriteOutcome = 0;
                    state.Detail = e.Message;
                    state.Writing = false;
                }
            }
            Poke(state.Notify);
        });
    }

    private static void RegisterFileHandles(NativeRegistry registry)
    {
        var i64 = TypeTag.I64;

        // Mode 0 reads, 1 creates or truncates, 2 appends. One native rather than three: the
        // three differ in a FileMode and in nothing else.
        registry.Register("std.io.stream.streamOpen", [TypeTag.String, i64], i64, args =>
        {
            var path = args[0].AsString;
            FileStream stream;
            try
            {
                stream = args[1].AsI64 switch
                {
                    1 => new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read,
                        4096, useAsync: true),
                    2 => new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read,
                        4096, useAsync: true),
                    _ => new FileStream(path, FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite, 4096, useAsync: true),
                };
            }
            catch (Exception e)
            {
                RecordIo(e);
                return LyrValue.FromI64(-1);
            }

            var notify = new System.Net.Sockets.Socket(
                System.Net.Sockets.AddressFamily.InterNetwork,
                System.Net.Sockets.SocketType.Dgram,
                System.Net.Sockets.ProtocolType.Udp);
            notify.Bind(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0));
            notify.Connect(notify.LocalEndPoint!);
            notify.Blocking = false;

            var key = ++_nextFileKey;
            Files[key] = new FileState
            {
                Stream = stream,
                Notify = notify,
                NotifyFd = AddSocket(notify),
            };
            return LyrValue.FromI64(key);
        });

        registry.Register("std.io.stream.streamNotifyFd", [i64], i64, args =>
            LyrValue.FromI64(FileOf(args[0].AsI64)?.NotifyFd ?? -1));

        registry.RegisterOptionalArrayReturning("std.io.stream.streamTryRead", [i64, i64], TypeTag.U8,
            args =>
        {
            if (FileOf(args[0].AsI64) is not { } state)
            {
                RecordIoNoHandle();
                return LyrValue.None;
            }
            var max = (int)Math.Clamp(args[1].AsI64, 1, 1 << 20);

            DrainPipe(state.Notify);
            lock (state.Gate)
            {
                // Buffered bytes come first even after a failure: what was read was read.
                if (state.Chunks.Count > 0)
                    return Bytes(TakeBuffered(state.Chunks, ref state.Offset, max));
                if (state.ReadFailed)
                {
                    _lastIoKind = 0;
                    _lastIoDetail = state.Detail ?? "the read failed";
                    return LyrValue.None;
                }
                if (state.Eof) return Bytes([]);
                if (!state.Reading) StartRead(state, max);
                RecordIoWouldBlock();
                return LyrValue.None;
            }
        });

        registry.RegisterWithArrayParams("std.io.stream.streamWriteStart",
            [i64, TypeTag.Array], [null, TypeTag.U8], TypeTag.Bool, args =>
        {
            if (FileOf(args[0].AsI64) is not { } state || state.Closed)
            {
                RecordIoNoHandle();
                return LyrValue.FromBool(false);
            }
            lock (state.Gate)
            {
                if (state.Writing)
                {
                    _lastIoKind = 0;
                    _lastIoDetail = "a write is already in flight";
                    return LyrValue.FromBool(false);
                }
                state.Writing = true;
                state.WriteOutcome = -1;
            }
            StartWrite(state, ToBytes(args[1]));
            return LyrValue.FromBool(true);
        });

        // -1 while the write is in flight, 1 when it landed, 0 when it broke.
        registry.Register("std.io.stream.streamWriteReady", [i64], i64, args =>
        {
            if (FileOf(args[0].AsI64) is not { } state)
            {
                RecordIoNoHandle();
                return LyrValue.FromI64(0);
            }
            DrainPipe(state.Notify);
            lock (state.Gate)
            {
                if (state.Writing)
                {
                    RecordIoWouldBlock();
                    return LyrValue.FromI64(-1);
                }
                if (state.WriteOutcome == 0)
                {
                    _lastIoKind = 0;
                    _lastIoDetail = state.Detail ?? "the write failed";
                }
                return LyrValue.FromI64(state.WriteOutcome);
            }
        });

        registry.Register("std.io.stream.streamClose", [i64], TypeTag.Void, args =>
        {
            var key = args[0].AsI64;
            if (FileOf(key) is not { } state || state.Closed) return default;
            state.Closed = true;
            // A read or write still in flight ends against a disposed stream, which its own
            // catch records; nobody is left to read the record.
            try { state.Stream.Dispose(); } catch (Exception) { /* closing twice closes once */ }
            try { state.Notify.Close(); } catch (Exception) { /* the same */ }
            Sockets.Remove(state.NotifyFd);
            Files.Remove(key);
            return default;
        });

        // The same storage std.io.file records into: one RecordIo serves both modules, and a
        // module only ever reads it straight after its own silent form refused.
        registry.Register("std.io.stream.lastErrorKind", [], i64,
            _ => LyrValue.FromI64(_lastIoKind));
        registry.Register("std.io.stream.lastErrorDetail", [], TypeTag.String,
            _ => LyrValue.FromString(_lastIoDetail ?? ""));
    }

    /// <summary>The bytes as a <c>uint8[]</c> value; unreadable reads as empty.</summary>
    private static LyrValue Bytes(byte[]? content)
    {
        if (content is null) return LyrValue.FromObject(Array.Empty<LyrValue>());
        var values = new LyrValue[content.Length];
        for (var i = 0; i < content.Length; i++) values[i] = LyrValue.FromBits(content[i]);
        return LyrValue.FromObject(values);
    }

    /// <summary>A <c>uint8[]</c> argument as host bytes — the inverse of <see cref="Bytes"/>,
    /// copied out under the loan contract.</summary>
    private static byte[] ToBytes(LyrValue argument)
    {
        var values = (LyrValue[])argument.AsObject;
        var bytes = new byte[values.Length];
        for (var i = 0; i < values.Length; i++) bytes[i] = (byte)values[i].Bits;
        return bytes;
    }

    /// <summary>The 2.14 shape of a read: nothing at all, or the lines. The difference to
    /// <see cref="Lines"/> is the whole point of the change — that one answers an empty array to
    /// both "empty file" and "no file".</summary>
    private static LyrValue OptionalLines(string? content) =>
        content is null ? LyrValue.None : Lines(content);

    /// <summary>As <see cref="OptionalLines"/>, for the undecoded bytes.</summary>
    private static LyrValue OptionalBytes(byte[]? content) =>
        content is null ? LyrValue.None : Bytes(content);

    private static LyrValue Lines(string? content)
    {
        if (content is null) return LyrValue.FromObject([]);

        var split = content.ReplaceLineEndings("\n").Split('\n');

        // A file ending in a line break has no empty final line.
        var count = split.Length > 0 && split[^1].Length == 0 ? split.Length - 1 : split.Length;

        var values = new LyrValue[count];
        for (var i = 0; i < count; i++) values[i] = LyrValue.FromString(split[i]);
        return LyrValue.FromObject(values);
    }

    // ------------------------------------------------------------------ std.fmt helpers

    /// <summary>A number formatted by a .NET standard spec, invariantly.
    ///
    /// <para>An unknown specifier is a panic rather than an error value: it is a literal in the
    /// source and does not depend on the input.</para></summary>
    private static LyrValue Formatted(IFormattable value, string spec)
    {
        // A bare number is a WIDTH, including for a numeric value: '{n:8}' pads to eight columns
        // on the right, '{n:-8}' on the left. Passed to .NET, '8' would be a custom digit
        // placeholder and '-8' a literal.
        //
        // Handling it here makes the width form apply to every type rather than only to those
        // without .NET standard formats.
        if (IsWidth(spec)) return LyrValue.FromString(Padded(
            value.ToString(null, CultureInfo.InvariantCulture), spec));

        try
        {
            return LyrValue.FromString(value.ToString(spec, CultureInfo.InvariantCulture));
        }
        catch (FormatException)
        {
            throw new LyricPanic(VmDiagnostics.IndexOutOfRange,
                $"'{spec}' is not a valid format spec");
        }
    }

    /// <summary>For types without .NET standard formats the specifier is a width:
    /// <c>{name:10}</c> pads on the right, <c>{name:-10}</c> on the left. An empty specifier
    /// leaves the text as it is.</summary>
    /// <summary>Is the specifier a bare width: digits, optionally with a leading minus?</summary>
    private static bool IsWidth(string spec)
    {
        if (spec.Length == 0) return false;

        var start = spec[0] == '-' ? 1 : 0;
        if (start >= spec.Length) return false;

        for (var i = start; i < spec.Length; i++)
            if (!char.IsAsciiDigit(spec[i]))
                return false;
        return true;
    }

    private static string Padded(string value, string spec)
    {
        if (spec.Length == 0) return value;

        if (!int.TryParse(spec, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture,
                out var width))
            throw new LyricPanic(VmDiagnostics.IndexOutOfRange,
                $"'{spec}' is not a width — for this type a format spec is a number");

        return width < 0 ? value.PadLeft(-width) : value.PadRight(width);
    }

    private static LyrValue ToChars(string s)
    {
        var chars = new List<LyrValue>();
        for (var i = 0; i < s.Length; i += char.IsHighSurrogate(s[i]) ? 2 : 1)
            chars.Add(LyrValue.FromBits((ulong)char.ConvertToUtf32(s, i)));
        return LyrValue.FromObject(chars.ToArray());
    }
}
