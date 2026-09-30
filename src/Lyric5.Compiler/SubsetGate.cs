using Lyric.Core;
using Lyric.Ir;

namespace Lyric5.Compiler;

/// <summary>
/// What of the front end's IR the Lyric 5 compiler takes today (design/v5/spec/13, M2): functions,
/// the integer types and <c>bool</c>, strings, <c>if</c>/<c>while</c>/<c>for</c> over a range,
/// calls, <c>let</c>/<c>var</c>, structs as values. Everything else the 4.x front end still lowers
/// is refused here with the milestone that brings it — one diagnostic per construct, never a
/// silent miscompile and never a fork of the front end.
///
/// <para>The gate looks at the IR, not the syntax: it is the IR the emitter reads, so it is the
/// IR whose every instruction and type must be one the emitter understands.</para>
/// </summary>
public static class SubsetGate
{
    /// <summary>The code of the one diagnostic: a construct the compiler does not translate yet.</summary>
    public const string NotYet = "LYR-CG0001";

    /// <summary>
    /// The natively backed functions the emitter maps to the runtime (M2 S3). The 4.x front end
    /// lowers a bodyless function of the standard library to a <c>CallImport</c> by qualified name;
    /// these names are the provisional intrinsic table, which falls with M8a when <c>std</c> is
    /// rewritten in Lyric.
    /// </summary>
    public static readonly IReadOnlySet<string> Intrinsics = new HashSet<string>(StringComparer.Ordinal)
    {
        "std.core.panic",
        "std.string.concat", "std.string.fromInt", "std.string.fromUint", "std.string.fromBool",
        "std.io.print", "std.io.println",
    };

    /// <summary>Reports every construct outside the subset and answers whether there was none.</summary>
    public static bool Check(IrModule module, DiagnosticEngine diagnostics)
    {
        var gate = new Walk(module, diagnostics);
        gate.Module();
        return !gate.Refused;
    }

    private sealed class Walk(IrModule module, DiagnosticEngine diagnostics)
    {
        public bool Refused { get; private set; }

        private void Refuse(Span span, string what, string milestone)
        {
            Refused = true;
            diagnostics.Report(NotYet, Severity.Error, span, $"not yet in Lyric 5: {what} ({milestone})");
        }

        public void Module()
        {
            foreach (var function in module.Functions) Function(function);
            if (module.Globals.Count > 0 && module.GlobalInit is { } init)
                Refuse(FirstSpan(module.Functions[init.Value]), "module-level 'let'", "M3");
            // A module-level finding has no instruction to point at: the entry's first line stands in.
            var anywhere = module.Functions.Select(FirstSpan).FirstOrDefault(s => s != default);
            if (module.Impls.Count > 0) Refuse(anywhere, "interfaces", "M4");
        }

        private static Span FirstSpan(IrFunction function) =>
            function.Blocks.Count > 0 && function.Blocks[0].Insts.Count > 0 ? function.Blocks[0].Insts[0].Span : default;

        private void Function(IrFunction function)
        {
            var at = FirstSpan(function);
            if (function.Handlers.Count > 0) Refuse(at, "'try' and 'catch'", "M5");
            Type(function.ReturnType, at, "the return type");
            foreach (var local in function.Locals) Type(local.Type, at, $"the type of '{local.Name}'");
            foreach (var block in function.Blocks)
            {
                foreach (var inst in block.Insts) Instruction(inst);
                Terminator(block.Terminator, at);
            }
        }

        private void Type(IrType type, Span span, string where)
        {
            switch (type)
            {
                case IrScalarType { Kind: IrScalar.F32 or IrScalar.F64 }:
                    Refuse(span, $"floating-point numbers, {where}", "M3");
                    break;
                case IrScalarType { Kind: IrScalar.Char }:
                    Refuse(span, $"'char', {where}", "M3");
                    break;
                case IrScalarType:
                case IrStructType:
                    break;
                case IrRefType:
                    Refuse(span, $"classes, {where}", "M3");
                    break;
                case IrArrayType:
                    Refuse(span, $"arrays, {where}", "M3");
                    break;
                case IrOptionalType:
                    Refuse(span, $"optionals, {where}", "M3");
                    break;
                case IrEnumType:
                    Refuse(span, $"enums, {where}", "M3");
                    break;
                case IrInterfaceType:
                    Refuse(span, $"interfaces, {where}", "M4");
                    break;
                case IrFunctionType:
                    Refuse(span, $"function values, {where}", "M3");
                    break;
                case IrHostType:
                    Refuse(span, $"host types, {where}", "M14");
                    break;
                default:
                    Refuse(span, $"the type {type}, {where}", "M3");
                    break;
            }
        }

        private void Instruction(IrOp op)
        {
            switch (op)
            {
                case Const c:
                    if (c.Value is FloatConst) Refuse(op.Span, "floating-point numbers", "M3");
                    else if (c.Value is CharConst) Refuse(op.Span, "'char'", "M3");
                    break;
                case BinOp b:
                    if (b.Type is IrScalarType { Kind: IrScalar.String } && b.Kind != IrBinKind.Add)
                        Refuse(op.Span, "comparing strings with an operator", "M3");
                    else Type(b.Type, op.Span, "the operand type");
                    break;
                case UnOp u:
                    Type(u.Type, op.Span, "the operand type");
                    break;
                case Lyric.Ir.Convert v:
                    Type(v.From, op.Span, "a conversion");
                    Type(v.To, op.Span, "a conversion");
                    break;
                case LoadLocal or StoreLocal or Call or StructCopy:
                    break;
                case CallImport i:
                    if (!Intrinsics.Contains(module.Imports[i.Target.Value].Name))
                        Refuse(op.Span, $"the native function '{module.Imports[i.Target.Value].Name}'", "M8a");
                    break;
                case NewObject n:
                    if (n.Result is not IrStructType) Refuse(op.Span, "classes", "M3");
                    break;
                case LoadField or StoreField:
                    break;
                case NewArray or LoadElem or StoreElem or ArrayLen or ArrayConcat or ArrayRepeat:
                    Refuse(op.Span, "arrays", "M3");
                    break;
                case OptNone or OptSome or OptIsSome or OptGet:
                    Refuse(op.Span, "optionals", "M3");
                    break;
                case NewVariant or EnumTag or EnumAs:
                    Refuse(op.Span, "enums", "M3");
                    break;
                case MakeInterface or CallVirt:
                    Refuse(op.Span, "interfaces", "M4");
                    break;
                case LoadGlobal or StoreGlobal:
                    Refuse(op.Span, "module-level 'let'", "M3");
                    break;
                case MakeClosure or CallIndirect:
                    Refuse(op.Span, "closures", "M3");
                    break;
                case MakeCoroutine or ResumePull or YieldSuspend:
                    Refuse(op.Span, "coroutines", "M6");
                    break;
                default:
                    Refuse(op.Span, $"the instruction {op.GetType().Name}", "a later milestone");
                    break;
            }
        }

        private void Terminator(IrTerminator? terminator, Span at)
        {
            switch (terminator)
            {
                case null:
                case Return or Branch or CondBranch or Unreachable:
                    break;
                case Throw:
                    Refuse(terminator.Span, "'throw'", "M5");
                    break;
                case EndFinally:
                    Refuse(terminator.Span, "'try' and 'finally'", "M5");
                    break;
                default:
                    Refuse(at, $"the terminator {terminator.GetType().Name}", "a later milestone");
                    break;
            }
        }
    }
}
