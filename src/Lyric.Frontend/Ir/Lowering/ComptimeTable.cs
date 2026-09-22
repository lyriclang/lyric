using Lyric.Core;
using Lyric.Sema;

namespace Lyric.Ir.Lowering;

/// <summary>
/// How the lowering treats a <c>comptime</c> site. Three modes, and the same site lowers
/// differently under each:
///
/// <list type="bullet">
/// <item><b>No table</b> (a check, an editor): the inner expression is lowered as if the prefix
/// were not there. The value is the same; only the moment differs, and a check has no moment.
/// </item>
/// <item><b>Hoist</b> (the evaluation pass): the inner expression is lowered in place as above,
/// AND every site becomes a parameterless function <c>&lt;comptime:i&gt;</c> that returns it.
/// Those functions are the module's only roots, so what reaches the evaluator is exactly what
/// the sites need — and the capability bits are computed from that rather than from the whole
/// program.</item>
/// <item><b>Values</b> (the real pass): the site is replaced by the literal the evaluator
/// produced for it. A missing value is a compiler bug, not a diagnostic.</item>
/// </list>
/// </summary>
public sealed class ComptimeTable
{
    /// <summary>The name of site <paramref name="index"/>'s evaluation function. Angle brackets,
    /// like <c>&lt;globals&gt;</c>: no source can spell it.</summary>
    public static string FunctionName(int index) => $"<comptime:{index}>";

    public bool Hoist { get; init; }

    public IReadOnlyDictionary<int, IrConstValue>? Values { get; init; }

    /// <summary>A runner's boxed value as the constant the site's type expects. The runner
    /// answers by tag; the site's sema type says which literal to write.</summary>
    public static IrConstValue Constant(object value, LyrType type) => type switch
    {
        PrimitiveType { Kind: PrimitiveKind.Bool } => new BoolConst((bool)value),
        PrimitiveType { Kind: PrimitiveKind.Char } => new CharConst(System.Convert.ToInt32(value)),
        PrimitiveType { Kind: PrimitiveKind.String } => new StringConst((string)value),
        PrimitiveType { Kind: PrimitiveKind.Float or PrimitiveKind.Float32 or PrimitiveKind.Float64 }
            => new FloatConst(System.Convert.ToDouble(value)),
        PrimitiveType => new IntConst(value switch
        {
            ulong u => u,
            long l => unchecked((ulong)l),
            _ => unchecked((ulong)System.Convert.ToInt64(value)),
        }),
        _ => throw new InternalCompilationException(
            $"comptime: a site of type '{TypeFacts.Display(type)}' survived the sema"),
    };
}
