using Lyric.Core;
using Lyric.Ir;

namespace Lyric5.Compiler;

/// <summary>
/// What of the front end's IR the Lyric 5 compiler takes today (design/v5/spec/13, M2–M3):
/// functions, the number tower (integers, floats, <c>char</c>, <c>bool</c>), strings,
/// <c>if</c>/<c>while</c>/<c>for</c> over a range, calls, <c>let</c>/<c>var</c>, structs as
/// values. Everything else the 4.x front end still lowers
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
    /// The natively backed functions the emitter maps to the runtime: the 4.x front end lowers a
    /// bodyless function of the standard library to a <c>CallImport</c> by qualified name, and
    /// <see cref="Compiler.Intrinsics"/> says which names have a runtime call.
    /// </summary>
    public static IReadOnlySet<string> Intrinsics => Compiler.Intrinsics.Names;

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
            foreach (var global in module.Globals) Type(global.Type, default, $"the type of '{global.Name}'");
            // A module-level finding has no instruction to point at: the entry's first line stands in.
        }

        private static Span FirstSpan(IrFunction function) =>
            function.Blocks.Count > 0 && function.Blocks[0].Insts.Count > 0 ? function.Blocks[0].Insts[0].Span : default;

        private void Function(IrFunction function)
        {
            var at = FirstSpan(function);
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
                case IrScalarType:
                case IrStructType:
                    break;
                case IrRefType r when module.Types[r.Type.Value].IsClass:
                    break;
                // The variant of an enum, behind an 'enumas': a view into the enum's payload.
                case IrRefType r when module.Types.Any(t => t.Variants.Contains(r.Type)):
                    break;
                case IrRefType r:
                    Refuse(span, $"the reference type '{module.Types[r.Type.Value].Name}', {where}", "a later milestone");
                    break;
                case IrArrayType array:
                    Type(array.Element, span, where);
                    break;
                case IrSliceType slice:
                    Type(slice.Element, span, where);
                    break;
                case IrInlineArrayType inline:
                    Type(inline.Element, span, where);
                    break;
                case IrOptionalType optional:
                    Type(optional.Inner, span, where);
                    break;
                case IrEnumType e:
                    foreach (var variant in module.Types[e.Type.Value].Variants)
                        foreach (var field in module.Types[variant.Value].FieldTypes.Skip(1))
                            Type(field, span, where);
                    break;
                case IrInterfaceType:
                    break;
                case IrFunctionType f:
                    foreach (var p in f.Parameters) Type(p, span, where);
                    Type(f.Return, span, where);
                    break;
                case IrCoroutineType c:
                    Type(c.Yield, span, where);
                    Type(c.Result, span, where);
                    break;
                case IrPlaceType p:
                    Type(p.Value, span, where);
                    break;
                case IrHostType:
                    Refuse(span, $"host types, {where}", "M14");
                    break;
                default:
                    Refuse(span, $"the type {type}, {where}", "no milestone: a compiler bug");
                    break;
            }
        }

        private void Instruction(IrOp op)
        {
            switch (op)
            {
                case Const:
                    break;
                case BinOp b:
                    Type(b.Type, op.Span, "the operand type");
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
                    if (!Intrinsics.Contains(module.Imports[i.Target.Value].Name)
                        && !Compiler.Intrinsics.IsForeign(module.Imports[i.Target.Value].Name))
                        // M8a was named here, as if it would bring a program's own natives; nothing
                        // does — a program declares 'extern "C"' (N1b)
                        Refuse(op.Span, $"the native function '{module.Imports[i.Target.Value].Name}'",
                            "no milestone: a function without a body is the standard library's — a program reaches C through 'extern \"C\"'");
                    break;
                case NewObject n:
                    Type(n.Result, op.Span, "the object");
                    break;
                case LoadField or StoreField:
                    break;
                case NewArray n:
                    Type(n.Element, op.Span, "the element type");
                    break;
                case LoadElem or StoreElem or ArrayLen or ArrayConcat or MakeSlice or CopyValue:
                    break;
                case NewInline n:
                    Type(n.Element, op.Span, "the element type");
                    break;
                // '[x] * n' clones every slot (10 C7): a value copies, a string is shared without
                // anyone able to tell, a class or an array — held directly or inside the element
                // — would be one object in every slot. The sema desugars that case to
                // std.core's 'repeatArray' under 'Clone' (M4 S9); an instruction over objects
                // here is one it missed — unless it shares on purpose, std.core's 'filled' (S16).
                case ArrayRepeat r when HoldsObject(r.Element) && !r.Shares:
                    Refuse(op.Span, "'[x] * n' with an element that is or holds an object as an instruction — the sema desugars it through 'Clone'", "no milestone: a compiler bug");
                    break;
                case ArrayRepeat:
                    break;
                case OptNone or OptSome or OptIsSome or OptGet:
                    break;
                case NewVariant or EnumTag or EnumAs:
                    break;
                case MakeInterface or CallVirt or TypeTest or Downcast:
                    break;
                case CurrentError or ClearError or StashError or RestoreError or SuppressError:
                    break;
                case LoadGlobal or StoreGlobal:
                    break;
                // Places (03 T12): a place parameter's.
                case AddrLocal or AddrField or AddrElem or AddrGlobal or LoadPlace or StorePlace:
                    break;
                case MakeClosure m:
                    Type(m.Type, op.Span, "the function type");
                    break;
                case CallIndirect:
                    break;
                // Coroutines (06 N2): a yield of a body, and one outside it that meets the running
                // coroutine at run time (§10a).
                case MakeCoroutine or ResumePull or YieldSuspend or CoroutineDone or CoroutineResult
                    or CoroutineClosing or CoroutineClose:
                    break;
                default:
                    Refuse(op.Span, $"the instruction {op.GetType().Name}", "a later milestone");
                    break;
            }
        }

        /// <summary>Whether a value of the type is or holds a reference to an object with
        /// identity — a class or an array; a string has none to observe.</summary>
        private bool HoldsObject(IrType type) => type switch
        {
            IrRefType or IrArrayType or IrCoroutineType => true,
            IrOptionalType o => HoldsObject(o.Inner),
            IrInlineArrayType ia => HoldsObject(ia.Element),
            IrStructType s => module.Types[s.Type.Value].FieldTypes.Any(HoldsObject),
            IrEnumType e => module.Types[e.Type.Value].Variants
                .Any(v => module.Types[v.Value].FieldTypes.Skip(1).Any(HoldsObject)),
            _ => false,
        };

        private void Terminator(IrTerminator? terminator, Span at)
        {
            switch (terminator)
            {
                case null:
                // The error path (05 E1-E4, 01 L5): explicit edges, native since M5 S1b.
                case Return or Branch or CondBranch or Unreachable or Throw or ErrorBranch or Propagate or PanicError:
                    break;
                default:
                    Refuse(at, $"the terminator {terminator.GetType().Name}", "a later milestone");
                    break;
            }
        }
    }
}
