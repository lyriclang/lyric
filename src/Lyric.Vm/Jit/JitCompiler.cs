using System.Reflection;
using System.Reflection.Emit;

using Lyric.Bytecode;

namespace Lyric.Vm.Jit;

/// <summary>A compiled function: its parameters in, its result out.</summary>
/// <param name="globals">The program's global slots, shared with the interpreter — a compiled
/// function and an interpreted one must see the same module state.</param>
internal delegate LyrValue Compiled(LyrValue[] globals, LyrValue[] args);

/// <summary>
/// Turns a function's bytecode into .NET IL.
///
/// <para><b>Why this is worth doing, in one number.</b> A dispatched instruction costs about 5 ns
/// and costs that no matter what it does — an <c>ldloc</c>, a <c>br</c> and an <c>add f64</c> are
/// within ten percent of each other. The work is free; the dispatch is the whole bill. Compiled,
/// the same floating-point loop runs at 1.08 ns an iteration, which is the latency of the
/// hardware's own add: the loop stops being limited by the VM and starts being limited by the
/// CPU.</para>
///
/// <para><b>Two properties of the bytecode make it straightforward, and both are gifts.</b> The
/// operand stack is EMPTY at every block boundary, which the interpreter already relies on, so
/// Lyric's operand stack maps onto IL's own evaluation stack and needs no locals of its own. And
/// every slot carries its type, so a slot becomes a typed IL local rather than a 16-byte
/// <see cref="LyrValue"/> — which is what lets <c>add f64</c> become an IL <c>add</c> on two
/// <c>double</c>s that RyuJIT puts in a register.</para>
///
/// <para><b>What it refuses.</b> Everything it does not understand, per function, and the
/// interpreter keeps those. That is the whole safety story: a refusal costs speed, never
/// correctness, and the set grows one opcode at a time with the differential tests holding the
/// line. Standing today: arithmetic, comparisons, branches, locals, globals, arrays and fields.
/// Still declined — calls, closures, exceptions, optionals, interfaces, enums, conversions,
/// division (which panics), object construction (which needs the type table), and the narrow
/// integer widths (which need re-normalising after every operation).</para>
/// </summary>
internal static class JitCompiler
{
    /// <summary>How many arguments a compiled function may take. A guard, not a limit anyone
    /// meets.</summary>
    private const int MaxParameters = 64;

    /// <summary>How long an array LITERAL may be. Building one means holding its elements in
    /// temporaries while the array is allocated underneath them, so the emitter needs a local per
    /// element. Long literals are setup code, not hot code; the interpreter keeps them.</summary>
    private const int MaxArrayLiteral = 16;

    /// <summary>
    /// Compiles a function, or answers <c>null</c> when it contains something this pass does not
    /// handle. Refusal is normal and is not an error.
    /// </summary>
    public static Compiled? TryCompile(BytecodeFunction function, BytecodeInstruction[] code,
        int[] blockStart, IReadOnlyList<BytecodeTypeDef> types,
        IReadOnlyList<BytecodeType> globalTypes)
    {
        ArgumentNullException.ThrowIfNull(function);
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(blockStart);

        if (function.ParamCount > MaxParameters || blockStart.Length == 0) return null;

        var slotTypes = new Type[function.SlotTypes.Count];
        for (var i = 0; i < slotTypes.Length; i++)
        {
            var mapped = Machine(function.SlotTypes[i]);
            if (mapped is null) return null;
            slotTypes[i] = mapped;
        }

        if (function.ReturnType.Tag != TypeTag.Void && Machine(function.ReturnType) is null)
            return null;

        var method = new DynamicMethod(
            "lyrjit_" + function.Name,
            typeof(LyrValue),
            [typeof(LyrValue[]), typeof(LyrValue[])],
            typeof(JitCompiler).Module,
            skipVisibility: true);

        var il = method.GetILGenerator();

        var locals = new LocalBuilder[slotTypes.Length];
        for (var i = 0; i < slotTypes.Length; i++) locals[i] = il.DeclareLocal(slotTypes[i]);

        // Arguments arrive as LyrValue and are unpacked once, here. Everything after this point
        // works on machine types.
        for (var i = 0; i < function.ParamCount; i++)
        {
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4, i);
            il.Emit(OpCodes.Ldelem, typeof(LyrValue));
            EmitUnpack(il, function.SlotTypes[i]);
            il.Emit(OpCodes.Stloc, locals[i]);
        }

        var labels = new Label[blockStart.Length];
        for (var b = 0; b < labels.Length; b++) labels[b] = il.DefineLabel();

        var context = new Emitter(il, function, locals, labels, types, globalTypes);
        if (!context.EmitBlocks(code, blockStart)) return null;

        try
        {
            return method.CreateDelegate<Compiled>();
        }
        catch (InvalidProgramException)
        {
            // The IL did not verify. That is a bug here rather than in the module, but a shipped
            // game should limp on the interpreter rather than fall over — and the differential
            // tests are what turn this into a red build instead of a silent slowdown.
            return null;
        }
    }

    /// <summary>One function's emission, and the little state it needs while it runs.</summary>
    private sealed class Emitter(
        ILGenerator il,
        BytecodeFunction function,
        LocalBuilder[] locals,
        Label[] labels,
        IReadOnlyList<BytecodeTypeDef> types,
        IReadOnlyList<BytecodeType> globalTypes)
    {
        /// <summary>The type of every value on the IL evaluation stack. The bytecode says what
        /// each operation produces, so this is bookkeeping rather than inference.</summary>
        private readonly Stack<BytecodeType> _stack = new();

        /// <summary>Scratch for building an array literal, grown as sites need it and shared
        /// between them.</summary>
        private readonly List<LocalBuilder> _scratch = [];

        private bool _terminated;

        public bool EmitBlocks(BytecodeInstruction[] code, int[] blockStart)
        {
            for (var block = 0; block < blockStart.Length; block++)
            {
                il.MarkLabel(labels[block]);
                _stack.Clear();
                _terminated = false;

                var from = blockStart[block];
                var upTo = block + 1 < blockStart.Length ? blockStart[block + 1] : code.Length;

                for (var i = from; i < upTo; i++)
                {
                    if (_terminated) return false;
                    if (!Emit(code[i])) return false;
                }

                // A block that runs off its end would leave the IL stack in a state the next
                // block does not expect. Lyric's lowering always terminates a block; a module
                // where one does not is one this pass declines rather than guesses about.
                if (!_terminated || _stack.Count != 0) return false;
            }

            return true;
        }

        private bool Emit(BytecodeInstruction op)
        {
            switch (op.Opcode)
            {
                case Op.Const:
                {
                    if (op.Type is not { } tag || !EmitConst(tag, op)) return false;
                    _stack.Push(BytecodeType.Scalar(tag));
                    return true;
                }

                case Op.LoadLocal:
                {
                    var slot = (int)op.Immediate;
                    if (slot < 0 || slot >= locals.Length) return false;
                    il.Emit(OpCodes.Ldloc, locals[slot]);
                    _stack.Push(function.SlotTypes[slot]);
                    return true;
                }

                case Op.StoreLocal:
                {
                    var slot = (int)op.Immediate;
                    if (slot < 0 || slot >= locals.Length || _stack.Count == 0) return false;
                    _stack.Pop();
                    il.Emit(OpCodes.Stloc, locals[slot]);
                    return true;
                }

                case Op.LoadGlobal:
                {
                    var slot = (int)op.Immediate;
                    if (slot < 0 || slot >= globalTypes.Count) return false;

                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldc_I4, slot);
                    il.Emit(OpCodes.Ldelem, typeof(LyrValue));
                    EmitUnpack(il, globalTypes[slot]);
                    _stack.Push(globalTypes[slot]);
                    return true;
                }

                case Op.StoreGlobal:
                {
                    var slot = (int)op.Immediate;
                    if (slot < 0 || slot >= globalTypes.Count || _stack.Count == 0) return false;

                    // The value is already on the stack and the array and index have to go
                    // UNDER it, which IL cannot do -- so it goes into a temporary first.
                    var held = Scratch(0, Machine(globalTypes[slot]));
                    if (held is null) return false;

                    _stack.Pop();
                    il.Emit(OpCodes.Stloc, held);
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldc_I4, slot);
                    il.Emit(OpCodes.Ldloc, held);
                    EmitPack(il, globalTypes[slot]);
                    il.Emit(OpCodes.Stelem, typeof(LyrValue));
                    return true;
                }

                case Op.Pop:
                    if (_stack.Count == 0) return false;
                    _stack.Pop();
                    il.Emit(OpCodes.Pop);
                    return true;

                case Op.Add or Op.Sub or Op.Mul or Op.Shl or Op.Shr or
                     Op.BitAnd or Op.BitOr or Op.BitXor:
                {
                    if (op.Type is not { } tag || _stack.Count < 2) return false;
                    _stack.Pop();
                    _stack.Pop();
                    if (!EmitBinary(op.Opcode, tag)) return false;
                    _stack.Push(BytecodeType.Scalar(tag));
                    return true;
                }

                case Op.Lt or Op.Le or Op.Gt or Op.Ge or Op.Eq or Op.Ne:
                {
                    if (op.Type is not { } tag || _stack.Count < 2) return false;
                    _stack.Pop();
                    _stack.Pop();
                    if (!EmitCompare(op.Opcode, tag)) return false;

                    // IL's comparisons produce an int32; a Lyric bool is a 64-bit 0 or 1 like
                    // every other integer here.
                    il.Emit(OpCodes.Conv_I8);
                    _stack.Push(BytecodeType.Scalar(TypeTag.Bool));
                    return true;
                }

                case Op.Neg:
                {
                    if (op.Type is not { } tag || _stack.Count == 0) return false;
                    if (tag is not (TypeTag.I64 or TypeTag.F64 or TypeTag.F32)) return false;
                    _stack.Pop();
                    il.Emit(OpCodes.Neg);
                    _stack.Push(BytecodeType.Scalar(tag));
                    return true;
                }

                case Op.Not:
                {
                    if (_stack.Count == 0) return false;
                    _stack.Pop();
                    il.Emit(OpCodes.Ldc_I8, 0L);
                    il.Emit(OpCodes.Ceq);
                    il.Emit(OpCodes.Conv_I8);
                    _stack.Push(BytecodeType.Scalar(TypeTag.Bool));
                    return true;
                }

                case Op.BitNot:
                {
                    if (op.Type is not { } tag || _stack.Count == 0) return false;
                    if (tag is not (TypeTag.I64 or TypeTag.U64)) return false;
                    _stack.Pop();
                    il.Emit(OpCodes.Not);
                    _stack.Push(BytecodeType.Scalar(tag));
                    return true;
                }

                // ------------------------------------------------------------ arrays

                case Op.LoadElem:
                {
                    if (_stack.Count < 2) return false;
                    _stack.Pop();
                    var array = _stack.Pop();
                    if (array.Element is not { } element) return false;

                    il.Emit(OpCodes.Ldstr, function.Name);
                    il.Emit(OpCodes.Call, Runtime(nameof(JitRuntime.Element)));
                    EmitUnpack(il, element);
                    _stack.Push(element);
                    return true;
                }

                case Op.StoreElem:
                {
                    if (_stack.Count < 3) return false;
                    var value = _stack.Pop();
                    _stack.Pop();
                    var array = _stack.Pop();
                    if (array.Element is null) return false;

                    EmitPack(il, value);
                    il.Emit(OpCodes.Ldstr, function.Name);
                    il.Emit(OpCodes.Call, Runtime(nameof(JitRuntime.SetElement)));
                    return true;
                }

                case Op.ArrayLen:
                {
                    if (_stack.Count == 0) return false;
                    _stack.Pop();
                    il.Emit(OpCodes.Call, Runtime(nameof(JitRuntime.Length)));
                    _stack.Push(BytecodeType.Scalar(TypeTag.I64));
                    return true;
                }

                case Op.ArrayConcat:
                {
                    if (_stack.Count < 2) return false;
                    _stack.Pop();
                    var left = _stack.Pop();
                    il.Emit(OpCodes.Call, Runtime(nameof(JitRuntime.Concat)));
                    _stack.Push(left);
                    return true;
                }

                case Op.ArrayRepeat:
                {
                    if (_stack.Count < 2) return false;
                    _stack.Pop();
                    var source = _stack.Pop();
                    il.Emit(OpCodes.Call, Runtime(nameof(JitRuntime.Repeat)));
                    _stack.Push(source);
                    return true;
                }

                case Op.NewArray:
                    return EmitNewArray((int)op.Immediate);

                // ------------------------------------------------------------ fields

                case Op.LoadField:
                {
                    if (_stack.Count == 0) return false;
                    var owner = _stack.Pop();
                    if (FieldType(owner, (int)op.Immediate2) is not { } field) return false;

                    il.Emit(OpCodes.Call, Runtime(nameof(JitRuntime.Field)));
                    il.Emit(OpCodes.Ldc_I4, (int)op.Immediate2);
                    il.Emit(OpCodes.Ldelem, typeof(LyrValue));
                    EmitUnpack(il, field);
                    _stack.Push(field);
                    return true;
                }

                case Op.StoreField:
                {
                    if (_stack.Count < 2) return false;
                    var value = _stack.Pop();
                    var owner = _stack.Pop();
                    if (FieldType(owner, (int)op.Immediate2) is null) return false;

                    // The reference lies UNDER the value and the store wants it on top, so the
                    // value waits in a temporary.
                    var held = Scratch(0, Machine(value));
                    if (held is null) return false;

                    il.Emit(OpCodes.Stloc, held);
                    il.Emit(OpCodes.Call, Runtime(nameof(JitRuntime.Field)));
                    il.Emit(OpCodes.Ldc_I4, (int)op.Immediate2);
                    il.Emit(OpCodes.Ldloc, held);
                    EmitPack(il, value);
                    il.Emit(OpCodes.Stelem, typeof(LyrValue));
                    return true;
                }

                // ------------------------------------------------------------ control

                case Op.Branch:
                {
                    var target = (int)op.Immediate;
                    if (target < 0 || target >= labels.Length || _stack.Count != 0) return false;
                    il.Emit(OpCodes.Br, labels[target]);
                    _terminated = true;
                    return true;
                }

                case Op.CondBranch:
                {
                    var whenTrue = (int)op.Immediate;
                    var whenFalse = (int)op.Immediate2;
                    if (whenTrue < 0 || whenTrue >= labels.Length) return false;
                    if (whenFalse < 0 || whenFalse >= labels.Length) return false;
                    if (_stack.Count != 1) return false;

                    _stack.Pop();
                    il.Emit(OpCodes.Brtrue, labels[whenTrue]);
                    il.Emit(OpCodes.Br, labels[whenFalse]);
                    _terminated = true;
                    return true;
                }

                case Op.Return:
                {
                    if (_stack.Count != 0) return false;
                    il.Emit(OpCodes.Ldc_I8, 0L);
                    il.Emit(OpCodes.Call, PackBits);
                    il.Emit(OpCodes.Ret);
                    _terminated = true;
                    return true;
                }

                case Op.ReturnValue:
                {
                    if (_stack.Count != 1) return false;
                    EmitPack(il, _stack.Pop());
                    il.Emit(OpCodes.Ret);
                    _terminated = true;
                    return true;
                }

                default:
                    return false;
            }
        }

        /// <summary>An array literal: the elements are already on the stack, and the array they
        /// belong in does not exist yet — so they come off into temporaries first.</summary>
        private bool EmitNewArray(int count)
        {
            if (count < 0 || count > MaxArrayLiteral || _stack.Count < count) return false;

            var element = count > 0 ? _stack.Peek() : BytecodeType.Scalar(TypeTag.I64);

            for (var i = count - 1; i >= 0; i--)
            {
                var value = _stack.Pop();
                var held = Scratch(i, typeof(LyrValue));
                if (held is null) return false;

                EmitPack(il, value);
                il.Emit(OpCodes.Stloc, held);
            }

            il.Emit(OpCodes.Ldc_I4, count);
            il.Emit(OpCodes.Newarr, typeof(LyrValue));

            for (var i = 0; i < count; i++)
            {
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4, i);
                il.Emit(OpCodes.Ldloc, _scratch[i]);
                il.Emit(OpCodes.Stelem, typeof(LyrValue));
            }

            _stack.Push(new BytecodeType(TypeTag.Array, -1) { Element = element });
            return true;
        }

        /// <summary>A temporary of a given type at a given depth, made once and reused.</summary>
        private LocalBuilder? Scratch(int depth, Type? type)
        {
            if (type is null) return null;

            while (_scratch.Count <= depth) _scratch.Add(il.DeclareLocal(typeof(LyrValue)));

            // A slot already made for LyrValue serves any packed value; one asked for as a
            // machine type needs its own, so those get a fresh local each time. They are cheap
            // and the JIT coalesces what it can.
            return type == typeof(LyrValue) ? _scratch[depth] : il.DeclareLocal(type);
        }

        private BytecodeType? FieldType(BytecodeType owner, int index)
        {
            if (owner.Tag != TypeTag.Ref || owner.TypeIndex < 0) return null;
            if (owner.TypeIndex >= types.Count) return null;

            var layout = types[owner.TypeIndex];
            return index >= 0 && index < layout.FieldTypes.Count ? layout.FieldTypes[index] : null;
        }

        private bool EmitConst(TypeTag tag, BytecodeInstruction op)
        {
            switch (tag)
            {
                case TypeTag.F64: il.Emit(OpCodes.Ldc_R8, op.FloatValue); return true;
                case TypeTag.F32: il.Emit(OpCodes.Ldc_R4, (float)op.FloatValue); return true;
                case TypeTag.Bool: il.Emit(OpCodes.Ldc_I8, op.BoolValue ? 1L : 0L); return true;

                case TypeTag.I64 or TypeTag.U64 or TypeTag.Char:
                    il.Emit(OpCodes.Ldc_I8, unchecked((long)op.Immediate));
                    return true;

                default:
                    return false;
            }
        }

        private bool EmitBinary(Op op, TypeTag tag)
        {
            if (tag is not (TypeTag.I64 or TypeTag.U64 or TypeTag.F64 or TypeTag.F32)) return false;

            switch (op)
            {
                case Op.Add: il.Emit(OpCodes.Add); return true;
                case Op.Sub: il.Emit(OpCodes.Sub); return true;
                case Op.Mul: il.Emit(OpCodes.Mul); return true;
            }

            if (tag is TypeTag.F64 or TypeTag.F32) return false;

            switch (op)
            {
                case Op.BitAnd: il.Emit(OpCodes.And); return true;
                case Op.BitOr: il.Emit(OpCodes.Or); return true;
                case Op.BitXor: il.Emit(OpCodes.Xor); return true;

                // The shift count is taken modulo the operand width. At 64 bits that is what the
                // hardware does anyway, but IL wants an int32 amount, so the mask makes the
                // truncation explicit rather than relying on it.
                case Op.Shl:
                    il.Emit(OpCodes.Ldc_I8, 63L);
                    il.Emit(OpCodes.And);
                    il.Emit(OpCodes.Conv_I4);
                    il.Emit(OpCodes.Shl);
                    return true;

                case Op.Shr:
                    il.Emit(OpCodes.Ldc_I8, 63L);
                    il.Emit(OpCodes.And);
                    il.Emit(OpCodes.Conv_I4);
                    il.Emit(tag == TypeTag.I64 ? OpCodes.Shr : OpCodes.Shr_Un);
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>
        /// A comparison, matching the interpreter exactly — which for floating point means
        /// matching its NaN behaviour.
        ///
        /// <para>The interpreter compares with ordinary C# operators, so <c>a &lt;= b</c> is false
        /// when either side is NaN. IL's <c>clt</c>/<c>cgt</c> are ordered and <c>.un</c> is the
        /// unordered form, so <c>&lt;=</c> is <c>!(a &gt; b unordered)</c> — the shape the C#
        /// compiler emits, and NOT the negation of <c>cgt</c>, which answers true for NaN.</para>
        /// </summary>
        private bool EmitCompare(Op op, TypeTag tag)
        {
            if (LyrValue.IsFloat(tag))
            {
                switch (op)
                {
                    case Op.Lt: il.Emit(OpCodes.Clt); return true;
                    case Op.Gt: il.Emit(OpCodes.Cgt); return true;
                    case Op.Eq: il.Emit(OpCodes.Ceq); return true;
                    case Op.Le: il.Emit(OpCodes.Cgt_Un); Negate(); return true;
                    case Op.Ge: il.Emit(OpCodes.Clt_Un); Negate(); return true;
                    case Op.Ne: il.Emit(OpCodes.Ceq); Negate(); return true;
                    default: return false;
                }
            }

            // bool and char compare as unsigned, as they do in the interpreter.
            if (tag is not (TypeTag.I64 or TypeTag.U64 or TypeTag.Bool or TypeTag.Char))
                return false;

            var unsigned = tag is TypeTag.U64 or TypeTag.Bool or TypeTag.Char;

            switch (op)
            {
                case Op.Lt: il.Emit(unsigned ? OpCodes.Clt_Un : OpCodes.Clt); return true;
                case Op.Gt: il.Emit(unsigned ? OpCodes.Cgt_Un : OpCodes.Cgt); return true;
                case Op.Eq: il.Emit(OpCodes.Ceq); return true;
                case Op.Le: il.Emit(unsigned ? OpCodes.Cgt_Un : OpCodes.Cgt); Negate(); return true;
                case Op.Ge: il.Emit(unsigned ? OpCodes.Clt_Un : OpCodes.Clt); Negate(); return true;
                case Op.Ne: il.Emit(OpCodes.Ceq); Negate(); return true;
                default: return false;
            }
        }

        private void Negate()
        {
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ceq);
        }
    }

    // ------------------------------------------------------------------ the machine mapping

    /// <summary>
    /// The machine type a value of this Lyric type becomes, or <c>null</c> when this pass does not
    /// handle it.
    ///
    /// <para>The narrow integer widths are absent on purpose: Lyric widens every integer to 64
    /// bits and re-normalises after each operation, and getting that wrong would be a silent wrong
    /// answer rather than a refusal. Optionals, interfaces and closures are absent because their
    /// values carry BOTH halves of a <see cref="LyrValue"/> — a marker or a type index beside the
    /// reference — and a single machine type cannot hold that.</para>
    /// </summary>
    private static Type? Machine(BytecodeType type) => type.Tag switch
    {
        TypeTag.I64 or TypeTag.U64 or TypeTag.Bool or TypeTag.Char => typeof(long),
        TypeTag.F64 => typeof(double),
        TypeTag.F32 => typeof(float),
        TypeTag.Array or TypeTag.Ref => typeof(LyrValue[]),
        TypeTag.String => typeof(string),
        _ => null,
    };

    /// <summary>A <see cref="LyrValue"/> on the stack becomes a machine value.</summary>
    private static void EmitUnpack(ILGenerator il, BytecodeType type) =>
        il.Emit(OpCodes.Call, type.Tag switch
        {
            TypeTag.F64 => Runtime(nameof(JitRuntime.ToF64)),
            TypeTag.F32 => Runtime(nameof(JitRuntime.ToF32)),
            TypeTag.Array or TypeTag.Ref => Runtime(nameof(JitRuntime.AsArray)),
            TypeTag.String => Runtime(nameof(JitRuntime.AsText)),
            _ => Runtime(nameof(JitRuntime.ToI64)),
        });

    /// <summary>And back: a machine value becomes a <see cref="LyrValue"/>.</summary>
    private static void EmitPack(ILGenerator il, BytecodeType type) =>
        il.Emit(OpCodes.Call, type.Tag switch
        {
            TypeTag.F64 => Factory(nameof(LyrValue.FromF64), typeof(double)),
            TypeTag.F32 => Factory(nameof(LyrValue.FromF32), typeof(float)),
            TypeTag.Array or TypeTag.Ref or TypeTag.String =>
                Runtime(nameof(JitRuntime.Reference)),
            _ => Factory(nameof(LyrValue.FromI64), typeof(long)),
        });

    private static MethodInfo PackBits { get; } =
        Factory(nameof(LyrValue.FromBits), typeof(ulong));

    private static MethodInfo Runtime(string name) =>
        typeof(JitRuntime).GetMethod(name, BindingFlags.Public | BindingFlags.Static)!;

    private static MethodInfo Factory(string name, Type parameter) =>
        typeof(LyrValue).GetMethod(name, [parameter])!;
}
