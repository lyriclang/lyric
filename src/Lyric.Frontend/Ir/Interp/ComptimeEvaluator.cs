using Lyric.Core;
using Lyric.Ir.Lowering;
using Lyric.Sema;

namespace Lyric.Ir.Interp;

/// <summary>
/// The evaluation pass (design/v5/spec/09 A4; 13 M9a P1, P11): over the module the hoisting
/// lowering made — each <c>comptime</c> site a function <c>&lt;comptime:i&gt;</c> and the module's
/// only roots, so it holds what the sites reach — the globals are filled and every site run; its
/// value becomes the constant the real lowering writes (<see cref="ComptimeTable"/>). A site that
/// stops is reported where it stands, with the interpreter's stack under it.
/// </summary>
public static class ComptimeEvaluator
{
    /// <summary>The steps one site may take unless the root manifest says otherwise (09 Q5):
    /// a few seconds of interpretation.</summary>
    public const long DefaultBudget = 50_000_000;

    /// <summary>The frames of the inner backtrace a diagnostic shows, innermost first.</summary>
    private const int ShownFrames = 12;

    /// <param name="embed">What <c>embed</c> reads through (09 Q4), which notes each file read.</param>
    /// <returns>Each site's constant, or <c>null</c> where a site was reported.</returns>
    public static IReadOnlyDictionary<int, IrConstValue>? Evaluate(IrModule hoisted, TypeResult types,
        DiagnosticEngine diagnostics, long budget, bool overflowChecks, EmbedReader? embed = null)
    {
        var interpreter = new Interpreter(hoisted, budget, overflowChecks, embed);
        var sites = types.ComptimeSites;
        var values = new Dictionary<int, IrConstValue>();
        var failed = false;
        for (var i = 0; i < sites.Count; i++)
        {
            var name = ComptimeTable.FunctionName(i);
            var index = hoisted.Functions.FindIndex(f => f.Name.EndsWith(name, StringComparison.Ordinal));
            if (index < 0) throw new InternalCompilationException($"comptime: site {i} was not hoisted");
            try
            {
                interpreter.Refill();
                values[i] = Constant(interpreter.Run(new FunctionId(index)), hoisted.Functions[index].ReturnType, hoisted);
            }
            catch (ComptimeFailure failure)
            {
                Report(diagnostics, failure, sites[i].Span);
                failed = true;
            }
        }
        return failed ? null : values;
    }

    /// <summary>A site's value as the constant the real lowering writes (09 Q1; 13 M9a P3).
    /// <paramref name="type"/> is its type in <paramref name="module"/>, the hoisted one, whose
    /// layouts the value's slots follow: an integer in the IR's encoding of its width, an
    /// aggregate as the tree the lowering builds it from — fields in layout order, a variant by
    /// its index, which both lowerings give alike.</summary>
    public static IrConstValue Constant(Value value, IrType type, IrModule module) => type switch
    {
        IrScalarType { Kind: IrScalar.Bool } => new BoolConst(value.AsBool),
        IrScalarType { Kind: IrScalar.Char } => new CharConst((int)value.Bits),
        IrScalarType { Kind: IrScalar.String } => new StringConst(((StrObj)value.Ref!).ToString()),
        IrScalarType { Kind: IrScalar.F64 } => new FloatConst(value.AsDouble),
        IrScalarType { Kind: IrScalar.F32 } => new FloatConst(value.AsSingle),
        IrScalarType scalar => new IntConst(Arith.Pattern(value.Bits, scalar.Kind)),
        IrStructType s => new StructConst(Fields(((AggObj)value.Ref!).Slots, module.Types[s.Type.Value].FieldTypes, 0, module)),
        // a Box's object (02 M13), the one class whose value is a constant: its value is fixed
        IrRefType r => new StructConst(Fields(((ClassObj)value.Ref!).Fields, module.Types[r.Type.Value].FieldTypes, 0, module)),
        IrEnumType e => Variant((AggObj)value.Ref!, module.Types[e.Type.Value], module),
        IrOptionalType o => (OptObj)value.Ref! is { Has: true } some
            ? new OptionalConst(Constant(some.Inner, o.Inner, module))
            : new OptionalConst(null),
        IrArrayType a => new ArrayConst([.. ((ArrayObj)value.Ref!).Items.Select(x => Constant(x, a.Element, module))]),
        IrInlineArrayType i => new ArrayConst([.. ((AggObj)value.Ref!).Slots.Select(x => Constant(x, i.Element, module))]),
        _ => throw new InternalCompilationException($"comptime: a site of the IR type {type} survived the sema"),
    };

    /// <summary>The slots from <paramref name="from"/> on, each by its field's type.</summary>
    private static IrConstValue[] Fields(Value[] slots, IrType[] types, int from, IrModule module)
    {
        var fields = new IrConstValue[types.Length - from];
        for (var i = from; i < types.Length; i++) fields[i - from] = Constant(slots[i], types[i], module);
        return fields;
    }

    /// <summary>An enum's value: slot 0 the tag, the index of its variant, whose layout holds the
    /// tag at 0 and the payload after it.</summary>
    private static VariantConst Variant(AggObj value, IrTypeDef enumDef, IrModule module)
    {
        var tag = (int)value.Slots[0].Bits;
        var layout = module.Types[enumDef.Variants[tag].Value];
        return new VariantConst(tag, Fields(value.Slots, layout.FieldTypes, 1, module));
    }

    private static void Report(DiagnosticEngine diagnostics, ComptimeFailure failure, Span site)
    {
        var message = failure.Code switch
        {
            "LYR-CT0001" => $"the evaluation panicked: {failure.Message} ({failure.RuntimeCode} at run time)",
            _ => failure.Message,
        };
        // the globals are filled at the first access to one: then the stack holds their filling
        if (failure.Trace.Any(t => t.Function.EndsWith("<globals>", StringComparison.Ordinal)))
            message += " — while filling the globals the evaluation reads";
        // The site's own function stands at the bottom and is the site: the frames above it are
        // where the evaluation went.
        var frames = failure.Trace.Where(t => !t.Function.Contains("<comptime:", StringComparison.Ordinal)).ToList();
        var notes = frames.Take(ShownFrames)
            .Select(t => new DiagnosticNote(t.At, $"in '{t.Function}'"))
            .ToList();
        if (frames.Count > ShownFrames) notes.Add(new DiagnosticNote($"… {frames.Count - ShownFrames} frames more"));
        diagnostics.Report(failure.Code, Severity.Error, site, message, [.. notes]);
    }
}
