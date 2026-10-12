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

    /// <returns>Each site's constant, or <c>null</c> where a site was reported.</returns>
    public static IReadOnlyDictionary<int, IrConstValue>? Evaluate(IrModule hoisted, TypeResult types,
        DiagnosticEngine diagnostics, long budget, bool overflowChecks)
    {
        var interpreter = new Interpreter(hoisted, budget, overflowChecks);
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
                values[i] = Constant(interpreter.Run(new FunctionId(index)), types.TypeOf(sites[i]), hoisted.Functions[index].ReturnType);
            }
            catch (ComptimeFailure failure)
            {
                Report(diagnostics, failure, sites[i].Span);
                failed = true;
            }
        }
        return failed ? null : values;
    }

    /// <summary>A site's value as the literal its type writes (09 Q1; scalars and strings in M9a
    /// S1); an integer in the IR's encoding of its width (<paramref name="lowered"/>).</summary>
    public static IrConstValue Constant(Value value, LyrType type, IrType lowered) => type switch
    {
        PrimitiveType { Kind: PrimitiveKind.Bool } => new BoolConst(value.AsBool),
        PrimitiveType { Kind: PrimitiveKind.Char } => new CharConst((int)value.Bits),
        PrimitiveType { Kind: PrimitiveKind.String } => new StringConst(((StrObj)value.Ref!).ToString()),
        PrimitiveType { Kind: PrimitiveKind.Float } => new FloatConst(value.AsDouble),
        PrimitiveType { Kind: PrimitiveKind.Float32 } => new FloatConst(value.AsSingle),
        PrimitiveType when lowered is IrScalarType scalar => new IntConst(Arith.Pattern(value.Bits, scalar.Kind)),
        _ => throw new InternalCompilationException($"comptime: a site of type '{TypeFacts.Display(type)}' survived the sema"),
    };

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
