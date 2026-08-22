using System.Reflection;
using System.Reflection.Emit;

using Lyric.Bytecode;

namespace Lyric.Vm.Jit;

/// <summary>A compiled function: its parameters in, its result out.</summary>
/// <param name="context">The program around it — globals, the tables, and the way to call
/// anything else. See <see cref="JitContext"/>.</param>
internal delegate LyrValue Compiled(JitContext context, LyrValue[] args);

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
/// line. Standing today: arithmetic, comparisons, branches, locals, globals, arrays, fields,
/// optionals, and calls — to a native, or to another function that compiles. Still declined:
/// closures, exceptions, interfaces, enums, conversions, division (which panics), object
/// construction (which needs the type table), recursion, and the narrow integer widths (which
/// need re-normalising after every operation).</para>
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

    /// <summary>How many arguments a CALL may carry, for the same reason. Lyric's own natives
    /// stop at eight and hand-written signatures are short.</summary>
    private const int MaxCallArgs = 8;

    /// <summary>
    /// Compiles a function, or answers <c>null</c> when it contains something this pass does not
    /// handle. Refusal is normal and is not an error.
    /// </summary>
    /// <param name="reason">Why the function was declined, when it was. A short, countable
    /// phrase rather than a sentence: what a host wants from this is a histogram — which opcode
    /// stands between it and a compiled game — and prose does not tally.</param>
    public static Compiled? TryCompile(
        Interpreter.Prepared prepared, JitContext context, out string reason)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentNullException.ThrowIfNull(context);

        reason = string.Empty;

        var function = prepared.Source;
        var code = prepared.Instructions;
        var blockStart = prepared.BlockStart;

        if (function.ParamCount > MaxParameters || blockStart.Length == 0)
        {
            reason = "shape";
            return null;
        }

        var slotTypes = new Type[function.SlotTypes.Count];
        for (var i = 0; i < slotTypes.Length; i++)
        {
            var mapped = Machine(function.SlotTypes[i]);
            if (mapped is null)
            {
                reason = "slot " + function.SlotTypes[i].Tag;
                return null;
            }

            slotTypes[i] = mapped;
        }

        if (function.ReturnType.Tag != TypeTag.Void && Machine(function.ReturnType) is null)
        {
            reason = "return " + function.ReturnType.Tag;
            return null;
        }

        DynamicMethod method;
        try
        {
            method = new DynamicMethod(
                "lyrjit_" + function.Name,
                typeof(LyrValue),
                [typeof(JitContext), typeof(LyrValue[])],
                typeof(JitCompiler).Module,
                skipVisibility: true);
        }
        catch (PlatformNotSupportedException)
        {
            reason = "no runtime code generation";
            // NativeAOT has no runtime code generation. Refusing is exactly right: the program
            // runs interpreted and nothing else changes. It is worth knowing which way round that
            // is, though -- publishing a game ahead-of-time and compiling its scripts at run time
            // are alternatives, not a pair.
            return null;
        }

        var il = method.GetILGenerator();

        var locals = new LocalBuilder[slotTypes.Length];
        for (var i = 0; i < slotTypes.Length; i++) locals[i] = il.DeclareLocal(slotTypes[i]);

        // The globals array is read once, here, rather than through the context on every access.
        // It is the same array the interpreter holds and is never replaced.
        var globals = il.DeclareLocal(typeof(LyrValue[]));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(JitContext).GetProperty(nameof(JitContext.Globals))!
            .GetGetMethod()!);
        il.Emit(OpCodes.Stloc, globals);

        // Arguments arrive as LyrValue and are unpacked once, here. Everything after this point
        // works on machine types.
        for (var i = 0; i < function.ParamCount; i++)
        {
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4, i);
            il.Emit(OpCodes.Ldelem, typeof(LyrValue));
            if (!EmitUnpack(il, function.SlotTypes[i])) return null;
            il.Emit(OpCodes.Stloc, locals[i]);
        }

        var labels = new Label[blockStart.Length];
        for (var b = 0; b < labels.Length; b++) labels[b] = il.DefineLabel();

        var emitter = new Emitter(il, function, locals, globals, labels, context);
        if (!emitter.EmitBlocks(code, blockStart))
        {
            reason = emitter.Reason;
            return null;
        }

        try
        {
            return method.CreateDelegate<Compiled>();
        }
        catch (InvalidProgramException)
        {
            reason = "unverifiable IL";
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
        LocalBuilder globals,
        Label[] labels,
        JitContext context)
    {
        private IReadOnlyList<BytecodeType> globalTypes => context.GlobalTypes;

        private IReadOnlyList<BytecodeTypeDef> types => context.Types;

        /// <summary>The type of every value on the IL evaluation stack. The bytecode says what
        /// each operation produces, so this is bookkeeping rather than inference.</summary>
        private readonly Stack<BytecodeType> _stack = new();

        /// <summary>Scratch for building an array literal, grown as sites need it and shared
        /// between them.</summary>
        private readonly List<LocalBuilder> _scratch = [];

        private bool _terminated;

        /// <summary>What stopped it. Set once, at the first refusal — later ones are consequences
        /// of the first and would only blur the count.</summary>
        public string Reason { get; private set; } = "unknown";

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
                    if (_terminated)
                    {
                        Reason = "unreachable code after a terminator";
                        return false;
                    }

                    if (!Emit(code[i]))
                    {
                        Reason = code[i].Opcode.ToString();
                        return false;
                    }
                }

                // A block that runs off its end would leave the IL stack in a state the next
                // block does not expect. Lyric's lowering always terminates a block; a module
                // where one does not is one this pass declines rather than guesses about.
                if (!_terminated || _stack.Count != 0)
                {
                    Reason = "block does not end cleanly";
                    return false;
                }
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

                    il.Emit(OpCodes.Ldloc, globals);
                    il.Emit(OpCodes.Ldc_I4, slot);
                    il.Emit(OpCodes.Ldelem, typeof(LyrValue));
                    if (!EmitUnpack(il, globalTypes[slot])) return false;
                    _stack.Push(globalTypes[slot]);
                    return true;
                }

                case Op.StoreGlobal:
                {
                    var slot = (int)op.Immediate;
                    if (slot < 0 || slot >= globalTypes.Count || _stack.Count == 0) return false;

                    // The value is already on the stack and the array and index have to go
                    // UNDER it, which IL cannot do -- so it goes into a temporary first.
                    var held = Temp(Machine(globalTypes[slot]));
                    if (held is null) return false;

                    _stack.Pop();
                    il.Emit(OpCodes.Stloc, held);
                    il.Emit(OpCodes.Ldloc, globals);
                    il.Emit(OpCodes.Ldc_I4, slot);
                    il.Emit(OpCodes.Ldloc, held);
                    if (!EmitPack(il, globalTypes[slot])) return false;
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
                    if (!EmitUnpack(il, element)) return false;
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

                    if (!EmitPack(il, value)) return false;
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

                case Op.Call:
                    return EmitCall((int)op.Immediate);

                // ------------------------------------------------------------ optionals

                case Op.OptNone:
                {
                    il.Emit(OpCodes.Call, typeof(LyrValue)
                        .GetProperty(nameof(LyrValue.None))!.GetGetMethod()!);

                    // No element type is knowable here, and none is needed: an empty optional is
                    // stored into a slot that has one, and every read comes from that slot.
                    _stack.Push(new BytecodeType(TypeTag.Optional, -1));
                    return true;
                }

                case Op.OptSome:
                {
                    if (_stack.Count == 0) return false;
                    var inner = _stack.Pop();
                    if (!EmitPack(il, inner)) return false;

                    il.Emit(OpCodes.Call, typeof(LyrValue)
                        .GetMethod(nameof(LyrValue.Some), [typeof(LyrValue)])!);

                    _stack.Push(new BytecodeType(TypeTag.Optional, -1) { Element = inner });
                    return true;
                }

                case Op.OptIsSome:
                {
                    if (_stack.Count == 0) return false;
                    if (_stack.Pop().Tag != TypeTag.Optional) return false;

                    il.Emit(OpCodes.Call, Runtime(nameof(JitRuntime.HasValue)));
                    _stack.Push(BytecodeType.Scalar(TypeTag.Bool));
                    return true;
                }

                case Op.OptGet:
                {
                    if (_stack.Count == 0) return false;
                    var option = _stack.Pop();
                    if (option.Tag != TypeTag.Optional) return false;
                    if (option.Element is not { } inner) return false;

                    il.Emit(OpCodes.Ldstr, function.Name);
                    il.Emit(OpCodes.Call, Runtime(nameof(JitRuntime.Unwrap)));
                    if (!EmitUnpack(il, inner)) return false;

                    _stack.Push(inner);
                    return true;
                }

                // ------------------------------------------------------------ fields

                case Op.LoadField:
                {
                    if (_stack.Count == 0) return false;
                    var owner = _stack.Pop();
                    if (FieldType(owner, (int)op.Immediate2) is not { } field) return false;

                    il.Emit(OpCodes.Call, Runtime(nameof(JitRuntime.Field)));
                    il.Emit(OpCodes.Ldc_I4, (int)op.Immediate2);
                    il.Emit(OpCodes.Ldelem, typeof(LyrValue));
                    if (!EmitUnpack(il, field)) return false;
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
                    var held = Temp(Machine(value));
                    if (held is null) return false;

                    il.Emit(OpCodes.Stloc, held);
                    il.Emit(OpCodes.Call, Runtime(nameof(JitRuntime.Field)));
                    il.Emit(OpCodes.Ldc_I4, (int)op.Immediate2);
                    il.Emit(OpCodes.Ldloc, held);
                    if (!EmitPack(il, value)) return false;
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
                    if (!EmitPack(il, _stack.Pop())) return false;
                    il.Emit(OpCodes.Ret);
                    _terminated = true;
                    return true;
                }

                default:
                    return false;
            }
        }

        /// <summary>
        /// A call, to a native or to another Lyric function — the shared index space decides
        /// which, and <see cref="JitContext.Call"/> decides it again at run time.
        ///
        /// <para><b>Late-bound on purpose.</b> Binding the callee's delegate here would need a
        /// cycle analysis for recursion and a way to patch a caller whose callee later refuses.
        /// Asking the context costs one indirection, which sits beside a native's own cost and is
        /// invisible next to it.</para>
        /// </summary>
        private bool EmitCall(int index)
        {
            if (index < 0) return false;

            var natives = context.Natives.Length;
            int arity;
            BytecodeType returns;

            if (index < natives)
            {
                if (index >= context.Imports.Count) return false;
                arity = context.Imports[index].ParamTypes.Count;
                returns = context.Imports[index].ReturnType;
            }
            else
            {
                var at = index - natives;
                if (at >= context.Prepared.Length) return false;

                // THE CALLEE HAS TO COMPILE TOO, and this is not about speed.
                //
                // A Lyric exception unwinds along the interpreter's frame stack. Compiled code
                // keeps no frames, so a compiled function sitting between a 'throw' and the
                // 'catch' meant to receive it breaks the chain: the throw finds no handler and
                // becomes an uncaught panic. Refusing here keeps every compiled call inside
                // compiled code, where nothing can throw a Lyric exception in the first place --
                // 'throw' is one of the opcodes this pass declines.
                //
                // Recursion refuses itself, and correctly. 'CodeFor' marks a function as tried
                // BEFORE compiling it, so a function reached again while it is still being
                // compiled answers "no code" and the caller declines. Both ends stay interpreted,
                // which is right but not free: a recursive helper does not compile today.
                if (context.CodeFor(context.Prepared[at]) is null) return false;

                arity = context.Prepared[at].Source.ParamCount;
                returns = context.Prepared[at].Source.ReturnType;
            }

            if (arity > MaxCallArgs || _stack.Count < arity) return false;
            if (returns.Tag != TypeTag.Void && Machine(returns) is null) return false;

            // The arguments are on the stack and the buffer they belong in does not exist yet,
            // so they come off into temporaries -- the same shape as an array literal.
            for (var i = arity - 1; i >= 0; i--)
            {
                if (!EmitPack(il, _stack.Pop())) return false;
                il.Emit(OpCodes.Stloc, ValueSlot(i));
            }

            var buffer = il.DeclareLocal(typeof(LyrValue[]));
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4, arity);
            il.Emit(OpCodes.Call, ContextCall(nameof(JitContext.RentArgs)));
            il.Emit(OpCodes.Stloc, buffer);

            for (var i = 0; i < arity; i++)
            {
                il.Emit(OpCodes.Ldloc, buffer);
                il.Emit(OpCodes.Ldc_I4, i);
                il.Emit(OpCodes.Ldloc, _scratch[i]);
                il.Emit(OpCodes.Stelem, typeof(LyrValue));
            }

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4, index);
            il.Emit(OpCodes.Ldloc, buffer);
            il.Emit(OpCodes.Call, ContextCall(nameof(JitContext.Call)));

            if (returns.Tag == TypeTag.Void)
            {
                il.Emit(OpCodes.Pop);
            }
            else
            {
                if (!EmitUnpack(il, returns)) return false;
                _stack.Push(returns);
            }

            return true;
        }

        /// <summary>An array literal: the elements are already on the stack, and the array they
        /// belong in does not exist yet — so they come off into temporaries first.</summary>
        private bool EmitNewArray(int count)
        {
            if (count < 0 || count > MaxArrayLiteral || _stack.Count < count) return false;

            var element = count > 0 ? _stack.Peek() : BytecodeType.Scalar(TypeTag.I64);

            for (var i = count - 1; i >= 0; i--)
            {
                if (!EmitPack(il, _stack.Pop())) return false;
                il.Emit(OpCodes.Stloc, ValueSlot(i));
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

        /// <summary>A <see cref="LyrValue"/> temporary at a given depth, made once and shared by
        /// every site that needs one that deep.</summary>
        private LocalBuilder ValueSlot(int depth)
        {
            while (_scratch.Count <= depth) _scratch.Add(il.DeclareLocal(typeof(LyrValue)));
            return _scratch[depth];
        }

        /// <summary>A fresh temporary of a machine type. Cheap: RyuJIT coalesces what it can.
        /// </summary>
        private LocalBuilder? Temp(Type? type) => type is null ? null : il.DeclareLocal(type);

        private static System.Reflection.MethodInfo ContextCall(string name) =>
            typeof(JitContext).GetMethod(name)!;

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

        // An optional stays a LyrValue, and that is the whole trick. It is the one shape that
        // needs BOTH halves of a value -- the bits and a marker in the reference field -- so
        // there is no machine type to unpack it into. There does not have to be: a slot may hold
        // the value itself, exactly as the interpreter does, and everything around it stays
        // typed. Packing and unpacking are then the identity.
        //
        // This is what lets a 'for' loop compile. 'for (i in 0..n)' lowers to a RangeIterator
        // handing its value out as a '?int', so before this every function containing an
        // idiomatic loop was declined -- which is nearly every function anyone writes.
        TypeTag.Optional => typeof(LyrValue),

        _ => null,
    };

    /// <summary>
    /// A <see cref="LyrValue"/> on the stack becomes a machine value, or the function is refused.
    ///
    /// <para><b>The refusal lives HERE rather than at each site, and that is the point.</b> An
    /// earlier version checked the mapping where it was convenient and trusted it where it was
    /// not — and a module-level constant holding a HOST OBJECT went through the integer path,
    /// read its bits, and dropped the reference. Nothing failed: the value simply became zero,
    /// and the host got a null where its own object should have been. Every conversion now passes
    /// through one gate that can say no.</para>
    /// </summary>
    private static bool EmitUnpack(ILGenerator il, BytecodeType type)
    {
        if (Machine(type) is null) return false;

        // An optional is already what it is meant to be.
        if (type.Tag == TypeTag.Optional) return true;

        il.Emit(OpCodes.Call, type.Tag switch
        {
            TypeTag.F64 => Runtime(nameof(JitRuntime.ToF64)),
            TypeTag.F32 => Runtime(nameof(JitRuntime.ToF32)),
            TypeTag.Array or TypeTag.Ref => Runtime(nameof(JitRuntime.AsArray)),
            TypeTag.String => Runtime(nameof(JitRuntime.AsText)),
            _ => Runtime(nameof(JitRuntime.ToI64)),
        });

        return true;
    }

    /// <summary>And back: a machine value becomes a <see cref="LyrValue"/>, or the function is
    /// refused.</summary>
    private static bool EmitPack(ILGenerator il, BytecodeType type)
    {
        if (Machine(type) is null) return false;

        if (type.Tag == TypeTag.Optional) return true;

        il.Emit(OpCodes.Call, type.Tag switch
        {
            TypeTag.F64 => Factory(nameof(LyrValue.FromF64), typeof(double)),
            TypeTag.F32 => Factory(nameof(LyrValue.FromF32), typeof(float)),
            TypeTag.Array or TypeTag.Ref or TypeTag.String =>
                Runtime(nameof(JitRuntime.Reference)),
            _ => Factory(nameof(LyrValue.FromI64), typeof(long)),
        });

        return true;
    }

    private static MethodInfo PackBits { get; } =
        Factory(nameof(LyrValue.FromBits), typeof(ulong));

    private static MethodInfo Runtime(string name) =>
        typeof(JitRuntime).GetMethod(name, BindingFlags.Public | BindingFlags.Static)!;

    private static MethodInfo Factory(string name, Type parameter) =>
        typeof(LyrValue).GetMethod(name, [parameter])!;
}
