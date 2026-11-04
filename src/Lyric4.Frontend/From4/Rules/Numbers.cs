using Lyric4.AST;
using Lyric4.Resolver;
using Lyric4.Sema;

namespace Lyric4.From4.Rules;

/// <summary>T08: <c>int64</c>, <c>uint64</c> and <c>float64</c> were types of their own beside
/// <c>int</c>, <c>uint</c> and <c>float</c>, of the same width; in Lyric 5 they are the same types.
/// The written type is the short name, and an <c>as</c> between the two of a pair goes.</summary>
internal sealed class Widths : IRule
{
    public string Id => "T08";

    private static readonly Dictionary<string, string> Short = new(StringComparer.Ordinal)
    {
        ["int64"] = "int",
        ["uint64"] = "uint",
        ["float64"] = "float",
    };

    public void Apply(RuleContext context)
    {
        foreach (var module in context.Modules)
        {
            var nodes = Tree.Below(module).ToList();
            var dropped = new HashSet<TypeNode>(ReferenceEqualityComparer.Instance);
            foreach (var cast in nodes.OfType<CastExpr>())
                if (Pair(context.Model.Types.TypeOf(cast.Operand)) is { } from && Pair(context.Model.Types.TypeOf(cast)) == from)
                {
                    dropped.Add(cast.Type);
                    context.Replace(Id, cast.Span.File, cast.Operand.Span.End, cast.Span.End, "");
                }
            foreach (var named in nodes.OfType<NamedType>())
                if (named is { Path: [var name], TypeArguments: [] } && Short.TryGetValue(name, out var to) && !dropped.Contains(named))
                    context.Replace(Id, named.Span, to);
        }
    }

    /// <summary>The Lyric 5 type a primitive of 4.6 is: <c>int64</c> is <c>int</c>.</summary>
    private static PrimitiveKind? Pair(LyrType type) => type is PrimitiveType { Kind: var kind }
        ? kind switch
        {
            PrimitiveKind.Int64 => PrimitiveKind.Int,
            PrimitiveKind.Uint64 => PrimitiveKind.Uint,
            PrimitiveKind.Float64 => PrimitiveKind.Float,
            _ => kind,
        }
        : null;
}

/// <summary>T09: an integer that overflowed wrapped silently in Lyric 4; in Lyric 5 it panics, and
/// <c>+%</c>, <c>-%</c>, <c>*%</c> wrap. Which arithmetic means to wrap is not written anywhere; a
/// function that computes with a constant beyond 32 bits — a hash, a generator — is said for a
/// proposal, once, at its first such operation.</summary>
internal sealed class Overflow : IRule
{
    public string Id => "T09";

    public void Apply(RuleContext context)
    {
        foreach (var module in context.Modules)
            foreach (var function in Tree.Below(module).OfType<FunctionDecl>())
            {
                if (function.Body is null) continue;
                var found = Tree.Below(function.Body).OfType<BinaryExpr>()
                    .Where(b => b.Operator is BinaryOp.Add or BinaryOp.Sub or BinaryOp.Mul
                        && IsInteger(context.Model.Types.TypeOf(b))
                        && (Large(b.Left) || Large(b.Right)))
                    .OrderBy(b => b.Span.Start)
                    .ToList();
                if (found.Count == 0) continue;
                var ops = found.Select(b => b.Operator switch { BinaryOp.Add => "'+%'", BinaryOp.Sub => "'-%'", _ => "'*%'" }).Distinct();
                context.Note(Id, "LYR-MIG0309", NoteKind.Review, found[0].Span,
                    $"'{function.Name}' computes with a constant beyond 32 bits; an overflow wrapped in Lyric 4 and panics in Lyric 5",
                    $"where the arithmetic is meant to wrap, write {string.Join(" and ", ops)}");
            }
    }

    private static bool Large(Expr expr) => expr is IntLiteralExpr { Value: > uint.MaxValue };

    private static bool IsInteger(LyrType type) => type is PrimitiveType
    {
        Kind: PrimitiveKind.Int or PrimitiveKind.Uint or PrimitiveKind.Int8 or PrimitiveKind.Int16 or PrimitiveKind.Int32
            or PrimitiveKind.Int64 or PrimitiveKind.Uint8 or PrimitiveKind.Uint16 or PrimitiveKind.Uint32 or PrimitiveKind.Uint64
    };
}

/// <summary>T10: an integer literal took the type its place asked for, a <c>float</c> too —
/// <c>abs(-3)</c> called <c>abs(float)</c>; in Lyric 5 an integer literal is an integer, and
/// <c>abs(-3)</c> is <c>int</c>. A literal that 4.6 read as a float is written as one: <c>-3.0</c>.</summary>
internal sealed class FloatLiterals : IRule
{
    public string Id => "T10";

    public void Apply(RuleContext context)
    {
        foreach (var module in context.Modules)
            foreach (var literal in Tree.Below(module).OfType<IntLiteralExpr>())
                if (literal.Suffix is null && context.Model.Types.TypeOf(literal) is PrimitiveType
                    {
                        Kind: PrimitiveKind.Float or PrimitiveKind.Float32 or PrimitiveKind.Float64
                    })
                    context.Replace(Id, literal.Span.File, literal.Span.End, literal.Span.End, ".0");
    }
}

/// <summary>T18: an array as the key of a <c>Map</c> or the element of a <c>Set</c> passed 4.6's
/// checker and failed in its lowering; in Lyric 5 an array is not <c>Hashable</c>, and the type is
/// refused. Said by hand.</summary>
internal sealed class ArrayKeys : IRule
{
    public string Id => "T18";

    public void Apply(RuleContext context)
    {
        foreach (var module in context.Modules)
        {
            var text = context.TextOf(module);
            foreach (var named in Tree.Below(module).OfType<NamedType>())
                if (named is { Path: [.., "Map" or "Set"], TypeArguments: [ArrayType key, ..] }
                    && Tree.Target(context, named) is TypeSymbol { Declaration: { } declaration }
                    && From4.IsStdlib(context.Model.Sources.GetPath(declaration.Span.File)))
                    context.Note(Id, "LYR-MIG0318", NoteKind.Hand, named.Span,
                        $"'{text[key.Span.Start..key.Span.End]}' is no key in Lyric 5: an array is not 'Hashable'",
                        "wrap the array in a struct that conforms to 'Hashable', or key by a value made of its elements");
        }
    }
}
