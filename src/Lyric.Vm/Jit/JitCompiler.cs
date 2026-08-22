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
/// <para><b>Why this is worth doing at all, in one number.</b> A dispatched instruction costs
/// about 5 ns and costs that no matter what it does — an <c>ldloc</c>, a <c>br</c> and an
/// <c>add f64</c> are within ten percent of each other. The work is free; the dispatch is the
/// whole bill. The same <c>acc += 1.5</c> loop written in C# runs at 0.57 ns an iteration against
/// 70 ns interpreted, and the difference is not that the interpreter loop is slow — it is that a
/// compiled loop does in two machine instructions what the VM does in thirteen dispatches.</para>
///
/// <para><b>Two properties of the bytecode make this straightforward, and both are gifts.</b>
/// The operand stack is EMPTY at every block boundary, which the interpreter already relies on;
/// so Lyric's operand stack maps directly onto IL's own evaluation stack and needs no locals of
/// its own. And every slot carries its type, so a slot becomes a typed IL local rather than a
/// 16-byte <see cref="LyrValue"/> — which is what lets <c>add f64</c> become an IL <c>add</c> on
/// two <c>double</c>s that RyuJIT puts in registers.</para>
///
/// <para><b>What it refuses.</b> Everything it does not yet understand, per function, and the
/// interpreter keeps those. That is the whole safety story: a refusal costs speed, never
/// correctness, and the set can grow one opcode at a time with the differential tests holding the
/// line. This first pass takes integer and floating-point arithmetic, comparisons, branches and
/// locals — enough for the loops that dominate a game's cost — and refuses everything with a
/// reference in it, division (which panics), and the narrow integer widths (which need
/// re-normalising after every operation).</para>
/// </summary>
internal static class JitCompiler
{
    /// <summary>How many arguments a compiled function may take. A guard, not a limit anyone
    /// meets: it exists so a malformed module cannot ask for an array the size of memory.
    /// </summary>
    private const int MaxParameters = 64;

    /// <summary>
    /// Compiles a function, or answers <c>null</c> when it contains something this pass does not
    /// handle. Refusal is normal and is not an error.
    /// </summary>
    public static Compiled? TryCompile(BytecodeFunction function, BytecodeInstruction[] code,
        int[] blockStart)
    {
        ArgumentNullException.ThrowIfNull(function);
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(blockStart);

        if (function.ParamCount > MaxParameters || blockStart.Length == 0) return null;

        // Every slot has to fit a machine type. One that does not — a string, an array, an
        // object — refuses the whole function rather than half of it.
        var slotTypes = new Type[function.SlotTypes.Count];
        for (var i = 0; i < slotTypes.Length; i++)
        {
            var mapped = Machine(function.SlotTypes[i].Tag);
            if (mapped is null) return null;
            slotTypes[i] = mapped;
        }

        if (function.ReturnType.Tag != TypeTag.Void && Machine(function.ReturnType.Tag) is null)
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
            il.Emit(OpCodes.Ldelema, typeof(LyrValue));
            il.Emit(OpCodes.Call, Unpack(function.SlotTypes[i].Tag));
            il.Emit(OpCodes.Stloc, locals[i]);
        }

        var labels = new Label[blockStart.Length];
        for (var b = 0; b < labels.Length; b++) labels[b] = il.DefineLabel();

        if (!EmitBlocks(il, function, code, blockStart, locals, labels)) return null;

        try
        {
            return method.CreateDelegate<Compiled>();
        }
        catch (InvalidProgramException)
        {
            // The IL did not verify. That is a bug here rather than in the module, but a shipped
            // game should limp on the interpreter rather than fall over, and the differential
            // tests are what turn this into a red build instead of a silent slowdown.
            return null;
        }
    }

    private static bool EmitBlocks(ILGenerator il, BytecodeFunction function,
        BytecodeInstruction[] code, int[] blockStart, LocalBuilder[] locals, Label[] labels)
    {
        var stack = new Stack<TypeTag>();

        for (var block = 0; block < blockStart.Length; block++)
        {
            il.MarkLabel(labels[block]);
            stack.Clear();

            var from = blockStart[block];
            var upTo = block + 1 < blockStart.Length ? blockStart[block + 1] : code.Length;
            var terminated = false;

            for (var i = from; i < upTo; i++)
            {
                if (terminated) return false;
                if (!Emit(il, code[i], locals, labels, function, stack, ref terminated))
                    return false;
            }

            // A block that runs off its end would leave the IL stack in a state the next block
            // does not expect. Lyric's lowering always terminates a block; a module where one
            // does not is one this pass declines rather than guesses about.
            if (!terminated || stack.Count != 0) return false;
        }

        return true;
    }

    private static bool Emit(ILGenerator il, BytecodeInstruction op, LocalBuilder[] locals,
        Label[] labels, BytecodeFunction function, Stack<TypeTag> stack, ref bool terminated)
    {
        switch (op.Opcode)
        {
            case Op.Const:
            {
                if (op.Type is not { } tag) return false;
                if (!EmitConst(il, tag, op)) return false;
                stack.Push(tag);
                return true;
            }

            case Op.LoadLocal:
            {
                var slot = (int)op.Immediate;
                if (slot < 0 || slot >= locals.Length) return false;
                il.Emit(OpCodes.Ldloc, locals[slot]);
                stack.Push(function.SlotTypes[slot].Tag);
                return true;
            }

            case Op.StoreLocal:
            {
                var slot = (int)op.Immediate;
                if (slot < 0 || slot >= locals.Length || stack.Count == 0) return false;
                stack.Pop();
                il.Emit(OpCodes.Stloc, locals[slot]);
                return true;
            }

            case Op.Pop:
                if (stack.Count == 0) return false;
                stack.Pop();
                il.Emit(OpCodes.Pop);
                return true;

            case Op.Add or Op.Sub or Op.Mul or Op.Shl or Op.Shr or
                 Op.BitAnd or Op.BitOr or Op.BitXor:
            {
                if (op.Type is not { } tag || stack.Count < 2) return false;
                stack.Pop();
                stack.Pop();
                if (!EmitBinary(il, op.Opcode, tag)) return false;
                stack.Push(tag);
                return true;
            }

            case Op.Lt or Op.Le or Op.Gt or Op.Ge or Op.Eq or Op.Ne:
            {
                if (op.Type is not { } tag || stack.Count < 2) return false;
                stack.Pop();
                stack.Pop();
                if (!EmitCompare(il, op.Opcode, tag)) return false;

                // A comparison yields a bool, and a bool is a 64-bit 0 or 1 like every other
                // integer here — IL's compare instructions produce an int32, so it is widened.
                il.Emit(OpCodes.Conv_I8);
                stack.Push(TypeTag.Bool);
                return true;
            }

            case Op.Neg:
            {
                if (op.Type is not { } tag || stack.Count == 0) return false;
                if (tag is not (TypeTag.I64 or TypeTag.F64 or TypeTag.F32)) return false;
                stack.Pop();
                il.Emit(OpCodes.Neg);
                stack.Push(tag);
                return true;
            }

            case Op.Not:
            {
                if (stack.Count == 0) return false;
                stack.Pop();
                il.Emit(OpCodes.Ldc_I8, 0L);
                il.Emit(OpCodes.Ceq);
                il.Emit(OpCodes.Conv_I8);
                stack.Push(TypeTag.Bool);
                return true;
            }

            case Op.BitNot:
            {
                if (op.Type is not { } tag || stack.Count == 0) return false;
                if (tag is not (TypeTag.I64 or TypeTag.U64)) return false;
                stack.Pop();
                il.Emit(OpCodes.Not);
                stack.Push(tag);
                return true;
            }

            case Op.Branch:
            {
                var target = (int)op.Immediate;
                if (target < 0 || target >= labels.Length || stack.Count != 0) return false;
                il.Emit(OpCodes.Br, labels[target]);
                terminated = true;
                return true;
            }

            case Op.CondBranch:
            {
                var whenTrue = (int)op.Immediate;
                var whenFalse = (int)op.Immediate2;
                if (whenTrue < 0 || whenTrue >= labels.Length) return false;
                if (whenFalse < 0 || whenFalse >= labels.Length) return false;
                if (stack.Count != 1) return false;

                stack.Pop();
                il.Emit(OpCodes.Brtrue, labels[whenTrue]);
                il.Emit(OpCodes.Br, labels[whenFalse]);
                terminated = true;
                return true;
            }

            case Op.Return:
            {
                if (stack.Count != 0) return false;
                il.Emit(OpCodes.Ldc_I8, 0L);
                il.Emit(OpCodes.Call, PackBits);
                il.Emit(OpCodes.Ret);
                terminated = true;
                return true;
            }

            case Op.ReturnValue:
            {
                if (stack.Count != 1) return false;
                var tag = stack.Pop();
                il.Emit(OpCodes.Call, Pack(tag));
                il.Emit(OpCodes.Ret);
                terminated = true;
                return true;
            }

            // Everything else -- calls, fields, arrays, closures, exceptions, conversions and
            // division -- keeps its function on the interpreter for now.
            default:
                return false;
        }
    }

    private static bool EmitConst(ILGenerator il, TypeTag tag, BytecodeInstruction op)
    {
        switch (tag)
        {
            case TypeTag.F64:
                il.Emit(OpCodes.Ldc_R8, op.FloatValue);
                return true;

            case TypeTag.F32:
                il.Emit(OpCodes.Ldc_R4, (float)op.FloatValue);
                return true;

            case TypeTag.Bool:
                il.Emit(OpCodes.Ldc_I8, op.BoolValue ? 1L : 0L);
                return true;

            case TypeTag.I64 or TypeTag.U64 or TypeTag.Char:
                il.Emit(OpCodes.Ldc_I8, unchecked((long)op.Immediate));
                return true;

            default:
                return false;
        }
    }

    private static bool EmitBinary(ILGenerator il, Op op, TypeTag tag)
    {
        if (tag is not (TypeTag.I64 or TypeTag.U64 or TypeTag.F64 or TypeTag.F32)) return false;

        var isFloat = tag is TypeTag.F64 or TypeTag.F32;

        switch (op)
        {
            case Op.Add: il.Emit(OpCodes.Add); return true;
            case Op.Sub: il.Emit(OpCodes.Sub); return true;
            case Op.Mul: il.Emit(OpCodes.Mul); return true;
        }

        if (isFloat) return false;

        switch (op)
        {
            case Op.BitAnd: il.Emit(OpCodes.And); return true;
            case Op.BitOr: il.Emit(OpCodes.Or); return true;
            case Op.BitXor: il.Emit(OpCodes.Xor); return true;

            // The shift count is taken modulo the operand width, which for 64 bits is what the
            // hardware does anyway -- but IL wants an int32 shift amount, and masking makes the
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
    /// A comparison, matching the interpreter exactly — which for floating point means matching
    /// its NaN behaviour.
    ///
    /// <para>The interpreter compares with ordinary C# operators, so <c>a &lt;= b</c> is false
    /// when either side is NaN. IL's <c>clt</c>/<c>cgt</c> are ordered and <c>.un</c> is the
    /// unordered form, so <c>&lt;=</c> is <c>!(a &gt; b unordered)</c> — the same shape the C#
    /// compiler itself emits, and NOT the negation of <c>cgt</c>, which would answer true for
    /// NaN.</para>
    /// </summary>
    private static bool EmitCompare(ILGenerator il, Op op, TypeTag tag)
    {
        if (LyrValue.IsFloat(tag))
        {
            switch (op)
            {
                case Op.Lt: il.Emit(OpCodes.Clt); return true;
                case Op.Gt: il.Emit(OpCodes.Cgt); return true;
                case Op.Eq: il.Emit(OpCodes.Ceq); return true;
                case Op.Le: il.Emit(OpCodes.Cgt_Un); Negate(il); return true;
                case Op.Ge: il.Emit(OpCodes.Clt_Un); Negate(il); return true;
                case Op.Ne: il.Emit(OpCodes.Ceq); Negate(il); return true;
                default: return false;
            }
        }

        // bool and char compare as unsigned, as they do in the interpreter.
        var unsigned = tag is TypeTag.U64 or TypeTag.Bool or TypeTag.Char;
        if (tag is not (TypeTag.I64 or TypeTag.U64 or TypeTag.Bool or TypeTag.Char)) return false;

        switch (op)
        {
            case Op.Lt: il.Emit(unsigned ? OpCodes.Clt_Un : OpCodes.Clt); return true;
            case Op.Gt: il.Emit(unsigned ? OpCodes.Cgt_Un : OpCodes.Cgt); return true;
            case Op.Eq: il.Emit(OpCodes.Ceq); return true;
            case Op.Le: il.Emit(unsigned ? OpCodes.Cgt_Un : OpCodes.Cgt); Negate(il); return true;
            case Op.Ge: il.Emit(unsigned ? OpCodes.Clt_Un : OpCodes.Clt); Negate(il); return true;
            case Op.Ne: il.Emit(OpCodes.Ceq); Negate(il); return true;
            default: return false;
        }
    }

    private static void Negate(ILGenerator il)
    {
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ceq);
    }

    /// <summary>The machine type a slot of this tag becomes, or <c>null</c> when this pass does
    /// not handle it. The narrow integer widths are absent on purpose: Lyric widens every integer
    /// to 64 bits and re-normalises after each operation, and getting that wrong would be a
    /// silent wrong answer rather than a refusal.</summary>
    private static Type? Machine(TypeTag tag) => tag switch
    {
        TypeTag.I64 or TypeTag.U64 or TypeTag.Bool or TypeTag.Char => typeof(long),
        TypeTag.F64 => typeof(double),
        TypeTag.F32 => typeof(float),
        _ => null,
    };

    private static MethodInfo Unpack(TypeTag tag) => tag switch
    {
        TypeTag.F64 => Getter(nameof(LyrValue.AsF64)),
        TypeTag.F32 => Getter(nameof(LyrValue.AsF32)),
        _ => Getter(nameof(LyrValue.AsI64)),
    };

    private static MethodInfo Pack(TypeTag tag) => tag switch
    {
        TypeTag.F64 => Factory(nameof(LyrValue.FromF64), typeof(double)),
        TypeTag.F32 => Factory(nameof(LyrValue.FromF32), typeof(float)),
        _ => Factory(nameof(LyrValue.FromI64), typeof(long)),
    };

    private static MethodInfo PackBits { get; } =
        Factory(nameof(LyrValue.FromBits), typeof(ulong));

    private static MethodInfo Getter(string name) =>
        typeof(LyrValue).GetProperty(name)!.GetGetMethod()!;

    private static MethodInfo Factory(string name, Type parameter) =>
        typeof(LyrValue).GetMethod(name, [parameter])!;
}
