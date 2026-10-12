using Lyric.Core;

namespace Lyric.Ir.Interp;

/// <summary>
/// The compile-time interpreter (design/v5/spec/09 A4, 01 L7 — the door built; 13 M9a P1): runs
/// a function of a lowered <see cref="IrModule"/> and returns its value. Its rules are the C back
/// end's, taken from <c>CEmitter</c> instruction by instruction, so <c>comptime f()</c> is what
/// <c>f()</c> is at run time (Q7).
/// <para>The frames are its own (no C# recursion per call), each with its locals, temps, the
/// error in flight (01 L5) and the stashes of its defers. A step costs one unit of the budget
/// (Q5); a panic, the budget, a call into the world and a form not run yet stop the run with a
/// <see cref="ComptimeFailure"/> carrying the stack.</para>
/// </summary>
public sealed class Interpreter
{
    /// <summary>The frames a run may stack before it is a stack overflow — a recursion that does
    /// not end, as at run time (RT0006).</summary>
    private const int MaxDepth = 4096;

    private readonly IrModule _module;
    private readonly bool _overflowChecks;
    private readonly long _budgetLimit;
    private long _budget;
    private readonly Value[] _globals;
    private readonly Dictionary<(int Type, int Interface), IrImpl> _impls = new();
    private readonly Natives _natives;

    /// <param name="budget">The steps all runs of this interpreter may take together.</param>
    /// <param name="overflowChecks">The profile's setting (07 P3): a checked operation panics
    /// where the binary would, and wraps where it would.</param>
    public Interpreter(IrModule module, long budget, bool overflowChecks)
    {
        _module = module;
        _budgetLimit = budget;
        _budget = budget;
        _overflowChecks = overflowChecks;
        _globals = module.Globals.Select(g => Zero(g.Type)).ToArray();
        foreach (var impl in module.Impls) _impls[(impl.Type.Value, impl.Interface.Value)] = impl;
        _natives = new Natives(this);
    }

    public IrModule Module => _module;

    /// <summary>Steps taken since the last <see cref="Refill"/>.</summary>
    public long Spent => _budgetLimit - _budget;

    /// <summary>The budget whole again: each evaluation has its own (09 Q5 "je Auswertung").</summary>
    public void Refill() => _budget = _budgetLimit;

    private bool _globalsFilled;
    private ComptimeFailure? _globalsFailed;

    /// <summary>The module's globals filled, as a program's are before its entry (07 G2) — at the
    /// first access to one, so a site that reads no global does not stop on one that cannot be
    /// filled at compile time (<c>std.path</c> asks the system whether it runs on Windows). One
    /// that could not be filled stops every access after it, rather than reading a zero.</summary>
    private void FillGlobals()
    {
        if (_globalsFailed is { } failed) throw failed.Copy();
        if (_globalsFilled) return;
        _globalsFilled = true;
        if (_module.GlobalInit is not { } init) return;
        try
        {
            Run(init);
        }
        catch (ComptimeFailure failure)
        {
            _globalsFailed = failure.Copy();
            throw;
        }
    }

    /// <summary>Runs <paramref name="function"/>, which takes no parameters, to its value.</summary>
    /// <exception cref="ComptimeFailure">The run stopped; its trace is the stack where it did.</exception>
    public Value Run(FunctionId function)
    {
        var stack = new List<Frame> { NewFrame(_module.Functions[function.Value], [], null) };
        try
        {
            return Loop(stack);
        }
        catch (ComptimeFailure failure)
        {
            // A run inside a run — the globals filled at a first access — put its frames first.
            failure.Trace = [.. failure.Trace, .. Enumerable.Range(0, stack.Count).Select(i => stack[stack.Count - 1 - i])
                .Select(f => (f.Function.Name, f.Here))];
            throw;
        }
    }

    // --- frames ------------------------------------------------------------------------------------

    private sealed class Frame(IrFunction function, Value[] locals, TempId? dest)
    {
        public IrFunction Function { get; } = function;
        public Value[] Locals { get; } = locals;
        public Value[] Temps { get; } = new Value[function.Temps.Count];
        public TempId? Dest { get; } = dest;
        public IrBlock Block { get; set; } = function.Blocks[function.Entry.Value];
        public int Pc { get; set; }
        public ErrorRecord? Error { get; set; }
        public Dictionary<int, ErrorRecord?> Stashes { get; } = new();

        /// <summary>Where the frame stands: the instruction it runs, or its block's end.</summary>
        public Span Here => Pc > 0 && Pc <= Block.Insts.Count ? Block.Insts[Pc - 1].Span : Block.Terminator?.Span ?? default;
    }

    private Frame NewFrame(IrFunction function, Value[] args, TempId? dest)
    {
        var locals = new Value[function.Locals.Count];
        for (var i = 0; i < locals.Length; i++)
            locals[i] = i < function.ParamCount ? args[i] : Zero(function.Locals[i].Type);
        return new Frame(function, locals, dest);
    }

    private static void Jump(Frame f, BlockId target)
    {
        f.Block = f.Function.Blocks[target.Value];
        f.Pc = 0;
    }

    /// <summary>Fresh storage of a type, zeroed as C zeroes it: an aggregate an object of its own
    /// (a temp aliases it), anything else zero bits — no reference.</summary>
    public Value Zero(IrType type) => type switch
    {
        IrStructType s => Value.Of(new AggObj(AggKind.Struct, s.Type, _module.Types[s.Type.Value].FieldTypes.Select(Zero).ToArray())),
        IrEnumType e => Value.Of(new AggObj(AggKind.Enum, e.Type, VariantSlots(e.Type, 0, []))),
        IrInlineArrayType ia => Value.Of(new AggObj(AggKind.Inline, default,
            [.. Enumerable.Range(0, ia.Length).Select(_ => Zero(ia.Element))])),
        IrOptionalType o => Value.Of(new OptObj(false, Zero(o.Inner))),
        _ => default,
    };

    /// <summary>A variant's slots: its tag, then its payload — the given values copied, or zero.</summary>
    private Value[] VariantSlots(TypeId enumType, int tag, Value[] payload)
    {
        var variants = _module.Types[enumType.Value].Variants;
        var fields = _module.Types[variants[tag].Value].FieldTypes;
        var slots = new Value[fields.Length];
        slots[0] = Value.Int(tag);
        for (var i = 1; i < fields.Length; i++)
            slots[i] = i - 1 < payload.Length ? Heap.Copy(payload[i - 1]) : Zero(fields[i]);
        return slots;
    }

    // --- the loop ----------------------------------------------------------------------------------

    private Value Loop(List<Frame> stack)
    {
        while (true)
        {
            var f = stack[^1];
            if (--_budget < 0) throw ComptimeFailure.Budget(_budgetLimit);
            if (f.Pc < f.Block.Insts.Count)
            {
                var op = f.Block.Insts[f.Pc++];
                if (Step(f, op) is { } callee)
                {
                    if (stack.Count >= MaxDepth) throw ComptimeFailure.Panic("LYR-RT0006", "stack overflow");
                    stack.Add(callee);
                }
                continue;
            }
            switch (f.Block.Terminator)
            {
                case Return r:
                {
                    var result = r.Value is { } v ? Heap.Copy(f.Temps[v.Value]) : default;
                    stack.RemoveAt(stack.Count - 1);
                    if (stack.Count == 0) return result;
                    if (f.Dest is { } d) stack[^1].Temps[d.Value] = result;
                    break;
                }
                case Branch b:
                    Jump(f, b.Target);
                    break;
                case CondBranch c:
                    Jump(f, f.Temps[c.Cond.Value].AsBool ? c.IfTrue : c.IfFalse);
                    break;
                case Throw t:
                    f.Error = new ErrorRecord(f.Temps[t.Value.Value]);
                    Jump(f, t.Landing);
                    break;
                case ErrorBranch e:
                    Jump(f, f.Error is not null ? e.OnError : e.Continue);
                    break;
                case Propagate:
                {
                    var error = f.Error;
                    stack.RemoveAt(stack.Count - 1);
                    if (stack.Count == 0)
                        throw ComptimeFailure.Panic("LYR-RT0010", $"the error {Describe(error)} left the evaluation");
                    var caller = stack[^1];
                    caller.Error = error;
                    if (f.Dest is { } d) caller.Temps[d.Value] = Zero(f.Function.ReturnType);
                    break;
                }
                case PanicError:
                    throw ComptimeFailure.Panic("LYR-RT0010", $"'try!' met the error {Describe(f.Error)}");
                case Unreachable:
                    throw ComptimeFailure.Panic("LYR-RT0017", "control reached code the compiler holds unreachable");
                default:
                    throw new InternalCompilationException($"comptime: block {f.Block.Id} of '{f.Function.Name}' has no terminator");
            }
        }
    }

    /// <summary>The thrown value's type, for a message.</summary>
    private string Describe(ErrorRecord? error) =>
        error?.Value.Ref is IfaceObj i ? $"'{_module.Types[i.Concrete.Value].Name}'" : "of the program";

    // --- one instruction ---------------------------------------------------------------------------

    /// <summary>Runs one instruction; a call of a function with a body returns its new frame.</summary>
    private Frame? Step(Frame f, IrOp op)
    {
        Value T(TempId t) => f.Temps[t.Value];
        void Set(TempId t, Value v) => f.Temps[t.Value] = v;
        IrType TypeOf(TempId t) => f.Function.Temps[t.Value].Type;

        switch (op)
        {
            case Const c:
                Set(c.Dest, Constant(c));
                return null;
            case BinOp b:
            {
                var kind = TypeOf(b.Lhs) is IrScalarType s ? s.Kind : IrScalar.Void;
                var right = TypeOf(b.Rhs) is IrScalarType rs ? rs.Kind : IrScalar.Void;
                Set(b.Dest, Arith.Binary(b.Kind, new IrScalarType(kind), T(b.Lhs), T(b.Rhs), right, _overflowChecks));
                return null;
            }
            case UnOp u:
                Set(u.Dest, Arith.Unary(u.Kind, (IrScalarType)u.Type, T(u.Operand), _overflowChecks));
                return null;
            case Convert v:
                Set(v.Dest, Arith.Convert(((IrScalarType)v.From).Kind, ((IrScalarType)v.To).Kind, T(v.Operand)));
                return null;

            case LoadLocal l:
                Set(l.Dest, f.Locals[l.Local.Value]);
                return null;
            case StoreLocal s:
                f.Locals[s.Local.Value] = Heap.Store(f.Locals[s.Local.Value], T(s.Value));
                return null;
            case LoadGlobal l:
                FillGlobals();
                Set(l.Dest, _globals[l.Global.Value]);
                return null;
            case StoreGlobal s:
                FillGlobals();
                _globals[s.Global.Value] = Heap.Store(_globals[s.Global.Value], T(s.Value));
                return null;

            case Call c:
            {
                var callee = _module.Functions[c.Target.Value];
                var args = new Value[c.Args.Length];
                for (var i = 0; i < args.Length; i++)
                    args[i] = callee.ReceiverByRef && i == 0 ? T(c.Args[i]) : Heap.Copy(T(c.Args[i]));
                return NewFrame(callee, args, c.Dest);
            }
            case CallImport c:
            {
                var import = _module.Imports[c.Target.Value];
                var args = c.Args.Select(T).ToArray();
                var types = c.Args.Select(TypeOf).ToArray();
                var result = _natives.Call(import, args, types);
                if (c.Dest is { } d) Set(d, result);
                return null;
            }
            case CallVirt c:
            {
                var value = (IfaceObj)T(c.Args[0]).Ref!;
                var callee = _module.Functions[Impl(value.Concrete, c.Interface).Methods[c.Slot].Value];
                var args = new Value[c.Args.Length];
                args[0] = Receiver(T(c.Args[0]), value, callee);
                for (var i = 1; i < args.Length; i++) args[i] = Heap.Copy(T(c.Args[i]));
                return NewFrame(callee, args, c.Dest);
            }
            case CallIndirect c:
            {
                var closure = (ClosureObj)T(c.Callee).Ref!;
                var callee = _module.Functions[closure.Target.Value];
                var takesEnvironment = callee.ParamCount > 0 && callee.Locals[0].Name == "<env>";
                var args = new Value[c.Args.Length + (takesEnvironment ? 1 : 0)];
                var at = 0;
                if (takesEnvironment) args[at++] = closure.Environment;
                foreach (var a in c.Args) args[at++] = Heap.Copy(T(a));
                return NewFrame(callee, args, c.Dest);
            }

            case NewObject n:
                Set(n.Dest, n.Result is IrRefType reference
                    ? Value.Of(new ClassObj(reference.Type, _module.Types[reference.Type.Value].FieldTypes.Select(Zero).ToArray()))
                    : Zero(n.Result));
                return null;
            case LoadField l:
                Set(l.Dest, Slots(T(l.Object))[l.Field.Value]);
                return null;
            case StoreField s:
            {
                var slots = Slots(T(s.Object));
                slots[s.Field.Value] = Heap.Store(slots[s.Field.Value], T(s.Value));
                return null;
            }
            case StructCopy c:
                Set(c.Dest, Heap.Copy(T(c.Value)));
                return null;
            case CopyValue c:
                Set(c.Dest, Heap.Copy(T(c.Value)));
                return null;

            case NewArray n:
                Set(n.Dest, Value.Of(new ArrayObj([.. n.Elements.Select(x => Heap.Copy(T(x)))])));
                return null;
            case LoadElem e:
            {
                var view = View(T(e.Array));
                var i = Index(T(e.Index), view.Length);
                Set(e.Dest, view.Str is { } str ? Value.Uint(str.Bytes[view.Offset + i]) : view.Items![view.Offset + i]);
                return null;
            }
            case StoreElem s:
            {
                var view = View(T(s.Array));
                var i = Index(T(s.Index), view.Length);
                var items = view.Items ?? throw new InternalCompilationException("comptime: a store into a string's bytes");
                items[view.Offset + i] = Heap.Store(items[view.Offset + i], T(s.Value));
                return null;
            }
            case ArrayLen a:
                Set(a.Dest, Value.Int(View(T(a.Array)).Length));
                return null;
            case MakeSlice s:
                Set(s.Dest, Slice(T(s.Array), T(s.Low).AsInt, T(s.High).AsInt, s.Chars));
                return null;
            case NewInline n:
                Set(n.Dest, Value.Of(new AggObj(AggKind.Inline, default, n.Repeat
                    ? [.. Enumerable.Range(0, n.Length).Select(_ => Heap.Copy(T(n.Elements[0])))]
                    : [.. n.Elements.Select(x => Heap.Copy(T(x)))])));
                return null;
            case ArrayConcat c:
            {
                var left = View(T(c.Left));
                var right = View(T(c.Right));
                Set(c.Dest, Value.Of(new ArrayObj([.. Elements(left).Concat(Elements(right)).Select(Heap.Copy)])));
                return null;
            }
            case ArrayRepeat r:
            {
                var count = T(r.Count).AsInt;
                if (count < 0) throw ComptimeFailure.Panic("LYR-RT0007", $"repeat count {count} is negative");
                var items = Elements(View(T(r.Array))).ToArray();
                if (items.Length != 0 && count > int.MaxValue / items.Length)
                    throw ComptimeFailure.Panic("LYR-RT0005", $"an array of {items.Length} elements repeated {count} times does not fit at compile time");
                Set(r.Dest, Value.Of(new ArrayObj([.. Enumerable.Range(0, (int)count).SelectMany(_ => items).Select(Heap.Copy)])));
                return null;
            }

            case OptNone n:
                Set(n.Dest, Value.Of(new OptObj(false, Zero(n.Inner))));
                return null;
            case OptSome s:
                Set(s.Dest, Value.Of(new OptObj(true, Heap.Copy(T(s.Value)))));
                return null;
            case OptIsSome i:
                Set(i.Dest, Value.Bool(((OptObj)T(i.Option).Ref!).Has));
                return null;
            case OptGet g:
            {
                var option = (OptObj)T(g.Option).Ref!;
                if (!option.Has && g.Checked) throw ComptimeFailure.Panic("LYR-RT0004", "unwrapped a null value");
                Set(g.Dest, option.Inner);
                return null;
            }

            case NewVariant v:
            {
                var tag = Array.IndexOf(_module.Types[v.Enum.Value].Variants, v.Variant);
                Set(v.Dest, Value.Of(new AggObj(AggKind.Enum, v.Enum, VariantSlots(v.Enum, tag, [.. v.Fields.Select(T)]))));
                return null;
            }
            case EnumTag t:
                Set(t.Dest, ((AggObj)T(t.Value).Ref!).Slots[0]);
                return null;
            case EnumAs a:
                Set(a.Dest, T(a.Value));
                return null;

            case MakeInterface m:
                Set(m.Dest, Value.Of(_module.Types[m.Concrete.Value].IsClass
                    ? new IfaceObj(T(m.Value), m.Concrete, m.Interface)
                    : new IfaceObj(Value.Of(new BoxObj(Heap.Copy(T(m.Value)))), m.Concrete, m.Interface)));
                return null;
            case TypeTest t:
            {
                var value = (IfaceObj)T(t.Value).Ref!;
                Set(t.Dest, Value.Bool(_module.Types[t.Target.Value].IsInterface
                    ? _impls.ContainsKey((value.Concrete.Value, t.Target.Value))
                    : value.Concrete == t.Target));
                return null;
            }
            case Downcast d:
            {
                var value = (IfaceObj)T(d.Value).Ref!;
                var target = _module.Types[d.Target.Value];
                Set(d.Dest, target.IsInterface ? Value.Of(new IfaceObj(value.Data, value.Concrete, d.Target))
                    : target.IsClass ? value.Data
                    : Heap.Copy(((BoxObj)value.Data.Ref!).Payload));
                return null;
            }

            case MakeClosure m:
                Set(m.Dest, Value.Of(new ClosureObj(m.Target, m.Environment is { } env ? T(env) : default)));
                return null;

            case AddrLocal a:
            {
                var index = a.Local.Value;
                Set(a.Dest, Value.Of(new PlaceObj(() => f.Locals[index], v => f.Locals[index] = Heap.Store(f.Locals[index], v))));
                return null;
            }
            case AddrField a:
            {
                var slots = Slots(T(a.Object));
                var index = a.Field.Value;
                Set(a.Dest, Value.Of(new PlaceObj(() => slots[index], v => slots[index] = Heap.Store(slots[index], v))));
                return null;
            }
            case AddrElem a:
            {
                var view = View(T(a.Array));
                var at = view.Offset + Index(T(a.Index), view.Length);
                var items = view.Items ?? throw new InternalCompilationException("comptime: the place of a string's byte");
                Set(a.Dest, Value.Of(new PlaceObj(() => items[at], v => items[at] = Heap.Store(items[at], v))));
                return null;
            }
            case AddrGlobal a:
                FillGlobals();
            {
                var index = a.Global.Value;
                Set(a.Dest, Value.Of(new PlaceObj(() => _globals[index], v => _globals[index] = Heap.Store(_globals[index], v))));
                return null;
            }
            case LoadPlace l:
                Set(l.Dest, ((PlaceObj)T(l.Place).Ref!).Get());
                return null;
            case StorePlace s:
                ((PlaceObj)T(s.Place).Ref!).Set(T(s.Value));
                return null;

            case CurrentError e:
                Set(e.Dest, f.Error?.Value ?? throw new InternalCompilationException("comptime: no error in flight"));
                return null;
            case ClearError:
                f.Error = null;
                return null;
            case StashError s:
                f.Stashes[s.Stash] = f.Error;
                f.Error = null;
                return null;
            case RestoreError r:
                f.Error = f.Stashes.GetValueOrDefault(r.Stash);
                return null;
            case SuppressError s:
            {
                var kept = f.Stashes.GetValueOrDefault(s.Stash);
                if (kept is not null && f.Error is not null) kept.Suppressed.Add(f.Error);
                f.Error = kept;
                return null;
            }
            case SuppressedErrors s:
                Set(s.Dest, Value.Of(new ArrayObj([.. (f.Stashes.GetValueOrDefault(s.Stash)?.Suppressed ?? []).Select(record => record.Value)])));
                return null;
            case ErrorTrace t:
                Set(t.Dest, Value.Of(new ArrayObj([])));   // no frames kept at compile time, as in release
                return null;

            case BlackBox b:
                Set(b.Dest, Heap.Copy(T(b.Value)));
                return null;

            case MakeCoroutine or ResumePull or CoroutineDone or CoroutineResult or CoroutineClosing or CoroutineClose or YieldSuspend:
                throw ComptimeFailure.NotYet("a coroutine — a generator, 'sequence { }', a task — does not run at compile time yet");

            default:
                throw new InternalCompilationException($"comptime: no rule for {op.GetType().Name}");
        }
    }

    private Value Constant(Const c) => Constant(c.Value, c.Type);

    private Value Constant(IrConstValue value, IrType type) => (value, type) switch
    {
        (IntConst i, IrScalarType s) => Value.Uint(Arith.Norm(i.Value, s.Kind)),
        (IntConst i, _) => Value.Uint(i.Value),
        (FloatConst x, IrScalarType { Kind: IrScalar.F32 }) => Value.Single((float)x.Value),
        (FloatConst x, _) => Value.Double(x.Value),
        (BoolConst b, _) => Value.Bool(b.Value),
        (CharConst ch, _) => Value.Uint((ulong)ch.CodePoint),
        (StringConst s, _) => Value.Of(StrObj.Of(s.Value)),
        // a comptime array's static data: a fresh array, or the inline array's storage, each time
        (ArrayConst a, IrArrayType t) => Value.Of(new ArrayObj([.. a.Elements.Select(e => Constant(e, t.Element))])),
        (ArrayConst a, IrInlineArrayType t) => Value.Of(new AggObj(AggKind.Inline, default, [.. a.Elements.Select(e => Constant(e, t.Element))])),
        _ => throw new InternalCompilationException($"comptime: no constant {value}"),
    };

    private IrImpl Impl(TypeId concrete, TypeId iface) =>
        _impls.TryGetValue((concrete.Value, iface.Value), out var row) ? row
            : throw new InternalCompilationException($"comptime: no table of '{_module.Types[concrete.Value].Name}' for '{_module.Types[iface.Value].Name}'");

    /// <summary>What a slot's function takes as its receiver (<c>CEmitter.Table</c>): a default the
    /// interface value itself, a class's method the object, a value's method the box's payload — as
    /// the place where it writes through it, else a copy.</summary>
    private static Value Receiver(Value interfaceValue, IfaceObj value, IrFunction callee)
    {
        if (callee.ParamCount > 0 && callee.Locals[0].Type is IrInterfaceType) return interfaceValue;
        if (value.Data.Ref is ClassObj) return value.Data;
        var payload = ((BoxObj)value.Data.Ref!).Payload;
        return callee.ReceiverByRef ? payload : Heap.Copy(payload);
    }

    /// <summary>The field slots of an object or of a struct or variant a temp aliases.</summary>
    private static Value[] Slots(Value v) => v.Ref switch
    {
        ClassObj c => c.Fields,
        AggObj a => a.Slots,
        _ => throw new InternalCompilationException($"comptime: no fields in a {v.Ref?.GetType().Name ?? "scalar"}"),
    };

    /// <summary>What an index, a length or a view reads: the slots and the window of an array, a
    /// view or an inline array, or a string's bytes.</summary>
    internal readonly record struct Window(Value[]? Items, StrObj? Str, int Offset, int Length);

    internal static Window View(Value v) => v.Ref switch
    {
        ArrayObj a => new(a.Items, null, 0, a.Items.Length),
        AggObj { Kind: AggKind.Inline } g => new(g.Slots, null, 0, g.Slots.Length),
        StrObj s => new(null, s, 0, s.Bytes.Length),
        SliceObj s => s.Source switch
        {
            ArrayObj a => new(a.Items, null, s.Offset, s.Length),
            AggObj g => new(g.Slots, null, s.Offset, s.Length),
            StrObj str => new(null, str, s.Offset, s.Length),
            _ => throw new InternalCompilationException("comptime: a view of nothing"),
        },
        _ => throw new InternalCompilationException($"comptime: no elements in a {v.Ref?.GetType().Name ?? "scalar"}"),
    };

    private static IEnumerable<Value> Elements(Window w) =>
        w.Str is { } s ? s.Bytes.Skip(w.Offset).Take(w.Length).Select(b => Value.Uint(b)) : w.Items!.Skip(w.Offset).Take(w.Length);

    /// <summary>An index checked against a length (03 T14 N5, <c>LYR_CHECK_INDEX</c>).</summary>
    internal static int Index(Value index, int length)
    {
        if (index.Bits >= (ulong)length)
            throw ComptimeFailure.Panic("LYR-RT0003", $"index {index.AsInt} out of bounds for length {length}");
        return (int)index.Bits;
    }

    /// <summary>A view of <c>[low, high)</c> (<c>LYR_CHECK_RANGE</c>, and for a string's bytes
    /// <c>lyr_check_char_range</c>), sharing the source.</summary>
    internal static Value Slice(Value source, long low, long high, bool chars)
    {
        var w = View(source);
        if ((ulong)low > (ulong)high || (ulong)high > (ulong)w.Length)
            throw ComptimeFailure.Panic("LYR-RT0003", $"range {low}..{high} out of bounds for length {w.Length}");
        if (chars)
        {
            bool Continues(long at) => at < w.Length && ((w.Str is { } s ? s.Bytes[w.Offset + at] : (byte)w.Items![w.Offset + at].Bits) & 0xC0) == 0x80;
            if (Continues(low) || Continues(high))
                throw ComptimeFailure.Panic("LYR-RT0003", $"byte range {low}..{high} does not fall on character boundaries");
        }
        object root = w.Str is not null ? w.Str : source.Ref switch
        {
            SliceObj s => s.Source,
            var other => other!,
        };
        return Value.Of(new SliceObj(root, w.Offset + (int)low, (int)(high - low)));
    }
}
