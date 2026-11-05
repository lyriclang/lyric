using System.Text;
using System.Text.RegularExpressions;
using Lyric4.AST;
using Lyric4.Core;
using Lyric4.Resolver;
using Lyric4.Sema;

namespace Lyric4.From4.Rules;

/// <summary>The nodes under a module, in source order, and the questions the syntax rules share.</summary>
internal static class Tree
{
    public static IEnumerable<Node> Below(Node node)
    {
        foreach (var child in AstChildren.Of(node))
        {
            yield return child;
            foreach (var below in Below(child)) yield return below;
        }
    }

    /// <summary>Whether an expression is written so that a postfix — <c>.next()</c>, <c>.v</c> —
    /// binds to all of it: a name, a member, a call, an index, a literal.</summary>
    public static bool TakesPostfix(Expr expr) => expr is IdentifierExpr or MemberExpr or CallExpr or IndexExpr
        or IntLiteralExpr or FloatLiteralExpr or StringLiteralExpr or CharLiteralExpr or BoolLiteralExpr
        or InterpolatedStringExpr or ArrayLitExpr or TupleLitExpr;

    /// <summary>The symbol a node names, through an import.</summary>
    public static Symbol? Target(RuleContext context, Node node)
    {
        var symbol = context.Model.Types.RefOf(node) ?? context.Model.Binding.Resolve(node);
        return symbol is ImportBindingSymbol import ? import.Target : symbol;
    }

    /// <summary>Whether a symbol is declared in 4.6's own library. A built-in, <c>Throwable</c>, has
    /// a declaration that stands in no file.</summary>
    public static bool InStdlib(RuleContext context, Symbol? symbol) =>
        symbol?.Declaration is { Span.File: var file } && file != FileId.None && From4.IsStdlib(context.Model.Sources.GetPath(file));

    /// <summary>The indentation of the line <paramref name="at"/> stands on.</summary>
    public static string IndentAt(string text, int at)
    {
        var start = text.LastIndexOf('\n', Math.Max(0, at - 1)) + 1;
        var end = start;
        while (end < text.Length && text[end] is ' ' or '\t') end++;
        return text[start..end];
    }
}

/// <summary>S01: <c>params xs: int[]</c> is <c>xs: int...</c>, and an array passed to it as a whole
/// is spread where it is passed: <c>total(xs...)</c>.</summary>
internal sealed partial class Variadic : IRule
{
    public string Id => "S01";

    [GeneratedRegex(@"\Gparams\s+")]
    private static partial Regex Keyword();

    public void Apply(RuleContext context)
    {
        foreach (var module in context.Modules)
        {
            var text = context.TextOf(module);
            foreach (var node in Tree.Below(module))
            {
                if (node is Param { IsParams: true } param)
                {
                    if (Keyword().Match(text, param.Span.Start) is { Success: true } keyword)
                        context.Replace(Id, param.Span.File, keyword.Index, keyword.Index + keyword.Length, "");
                    if (param.Type is ArrayType array)
                        context.Replace(Id, array.Span.File, array.Element.Span.End, array.Span.End, "...");
                }
                else if (node is CallExpr call && PassesWhole(context, call) is { } whole)
                    context.Replace(Id, whole.Span.File, whole.Span.End, whole.Span.End, "...");
            }
        }
    }

    /// <summary>The argument a call passes as the whole variadic array, as 4.6 decided it: exactly
    /// one argument after the fixed ones, of the array's own type — the same depth of arrays.</summary>
    private static Expr? PassesWhole(RuleContext context, CallExpr call)
    {
        if (Tree.Target(context, call.Callee) is not FunctionSymbol { Declaration: FunctionDecl declaration }) return null;
        if (declaration.Parameters is not [.., { IsParams: true, Type: var declared }]) return null;
        var fixedCount = declaration.Parameters.Length - 1;
        if (call.Arguments.Length != fixedCount + 1) return null;
        var argument = call.Arguments[^1];
        return Depth(context.Model.Types.TypeOf(argument)) == Depth(declared) ? argument : null;
    }

    private static int Depth(LyrType type) => type is ArrayOf array ? 1 + Depth(array.Element) : 0;

    private static int Depth(TypeNode type) => type is ArrayType array ? 1 + Depth(array.Element) : 0;
}

/// <summary>S04: <c>resume c</c> is <c>c.next()!</c> — the pull that panics at the end. A
/// <c>Coroutine&lt;void&gt;</c> has no value to unwrap: <c>assert(c.next())</c> panics there.</summary>
internal sealed class Resume : IRule
{
    public string Id => "S04";

    public void Apply(RuleContext context)
    {
        foreach (var module in context.Modules)
            foreach (var resume in Tree.Below(module).OfType<ResumeExpr>())
            {
                var operand = resume.Coroutine;
                var bare = Tree.TakesPostfix(operand);
                var steps = context.Model.Types.TypeOf(operand) is CoroutineOf { Yield: PrimitiveType { Kind: PrimitiveKind.Void } };
                var open = (steps ? "assert(" : "") + (bare ? "" : "(");
                var close = (bare ? "" : ")") + (steps ? ".next())" : ".next()!");
                context.Replace(Id, resume.Span.File, resume.Span.Start, operand.Span.Start, open);
                context.Replace(Id, resume.Span.File, operand.Span.End, operand.Span.End, close);
            }
    }
}

/// <summary>S05: <c>opaque type H = int;</c> is <c>struct H { v: int }</c> — an identity of its
/// own in Lyric 5 as well —, and the two <c>as</c> crossings are a field access and a
/// construction. For review: a native signature took the underlying value, and the struct is
/// passed as a struct.</summary>
internal sealed class Opaque : IRule
{
    public string Id => "S05";

    public void Apply(RuleContext context)
    {
        foreach (var module in context.Modules)
        {
            var text = context.TextOf(module);
            foreach (var node in Tree.Below(module))
            {
                if (node is TypeAliasDecl { IsOpaque: true } alias)
                {
                    var start = text.IndexOf("opaque", alias.Span.Start, StringComparison.Ordinal);
                    var end = text.IndexOf(';', alias.Aliased.Span.End);
                    if (start < 0 || end < 0) continue;
                    var underlying = text[alias.Aliased.Span.Start..alias.Aliased.Span.End];
                    context.Replace(Id, alias.Span.File, start, end + 1, $"struct {alias.Name} :: [Equatable] {{ v: {underlying} }}");
                    context.Note(Id, "LYR-MIG0205", NoteKind.Review, alias.Span,
                        $"'{alias.Name}' was an opaque type over '{underlying}'; it is a struct with one field 'v', its 'as' crossings field accesses and constructions",
                        "where the value crosses into C or the host, pass its 'v'");
                }
                else if (node is CastExpr cast)
                {
                    var types = context.Model.Types;
                    var operand = cast.Operand;
                    if (types.TypeOf(cast) is OpaqueRef into && types.TypeOf(operand) is not OpaqueRef)
                    {
                        // 'x as H' builds the struct: '{into} { v = x }' around the operand, the cast's tail gone
                        context.Replace(Id, cast.Span.File, cast.Span.Start, operand.Span.Start, $"{into.Symbol.Name} {{ v = ");
                        context.Replace(Id, cast.Span.File, operand.Span.End, cast.Span.End, " }");
                    }
                    else if (types.TypeOf(operand) is OpaqueRef && types.TypeOf(cast) is not OpaqueRef)
                    {
                        var bare = Tree.TakesPostfix(operand);
                        if (!bare) context.Replace(Id, cast.Span.File, operand.Span.Start, operand.Span.Start, "(");
                        context.Replace(Id, cast.Span.File, operand.Span.End, cast.Span.End, bare ? ".v" : ").v");
                    }
                }
            }
        }
    }
}

/// <summary>S06: a bare variant name in a pattern (<c>Red =&gt;</c>), which 4.6 read as a test where
/// the scrutinee's enum has the variant, is <c>.Red</c> — in Lyric 5 a bare name always binds.</summary>
internal sealed class VariantPatterns : IRule
{
    public string Id => "S06";

    public void Apply(RuleContext context)
    {
        foreach (var module in context.Modules)
            foreach (var pattern in Tree.Below(module).OfType<BindingPattern>())
                if (context.Model.Types.RefOf(pattern) is EnumVariantSymbol)
                    context.Replace(Id, pattern.Span.File, pattern.Span.Start, pattern.Span.Start, ".");
    }
}

/// <summary>S07: <c>\xNN</c> in a string, an f-string or a <c>char</c> is <c>\u{NN}</c> — the same
/// code point; Lyric 5 keeps <c>\x</c> for byte strings.</summary>
internal sealed partial class HexEscapes : IRule
{
    public string Id => "S07";

    public void Apply(RuleContext context)
    {
        foreach (var module in context.Modules)
        {
            var text = context.TextOf(module);
            foreach (var node in Tree.Below(module))
            {
                if (node is not (StringLiteralExpr or CharLiteralExpr or InterpText)) continue;
                for (var i = node.Span.Start; i < node.Span.End - 1; i++)
                {
                    if (text[i] != '\\') continue;
                    if (text[i + 1] == 'x' && i + 3 < node.Span.End && IsHex(text[i + 2]) && IsHex(text[i + 3]))
                        context.Replace(Id, node.Span.File, i, i + 4, $"\\u{{{text[i + 2]}{text[i + 3]}}}");
                    i++; // the escaped character, whatever it is: '\\x' is a backslash and an 'x'
                }
            }
        }
    }

    private static bool IsHex(char c) => char.IsAsciiHexDigit(c);
}

/// <summary>
/// S08: a format spec of 4.6 was .NET's, or a bare width; Lyric 5 has one format language. A
/// width is an alignment (<c>{name:10}</c> → <c>{name:&lt;10}</c>, <c>{n:-8}</c> → <c>{n:&gt;8}</c>);
/// <c>N</c> and <c>F</c> write what 4.6 wrote (<c>{x:N2}</c> → <c>{x:,.2f}</c>, and on an integer
/// the decimals after the hole). <c>D</c> and <c>X</c> are written too and said for review — a
/// negative value comes out otherwise. <c>E</c>, <c>P</c>, <c>C</c>, <c>G</c>, <c>R</c> and
/// custom formats have no counterpart and stay, said by hand.
/// </summary>
internal sealed partial class FormatSpecs : IRule
{
    public string Id => "S08";

    [GeneratedRegex(@"^-?[0-9]+$")]
    private static partial Regex Width();

    [GeneratedRegex(@"^([A-Za-z])([0-9]{0,2})$")]
    private static partial Regex Standard();

    public void Apply(RuleContext context)
    {
        foreach (var module in context.Modules)
        {
            var text = context.TextOf(module);
            foreach (var hole in Tree.Below(module).OfType<InterpHole>())
            {
                if (hole.FormatSpec is not { } spec) continue;
                // the spec runs from the ':' after the expression to the '}'
                var colon = text.IndexOf(':', hole.Expr.Span.End);
                var close = hole.Span.End - 1;
                if (colon < 0 || colon >= close || text[close] != '}') continue;
                var type = context.Model.Types.TypeOf(hole.Expr);
                var (written, after, note) = Translate(spec.Trim(), type);
                if (written is not null) context.Replace(Id, hole.Span.File, colon, close, written.Length == 0 ? "" : ":" + written);
                if (after is not null) context.Replace(Id, hole.Span.File, hole.Span.End, hole.Span.End, after);
                if (note is { } said)
                    context.Note(Id, "LYR-MIG0208", said.Kind, hole.Span, said.Message, said.Suggestion);
            }
        }
    }

    /// <summary>The Lyric 5 spec for a 4.6 one (<c>null</c>: unchanged; empty: none), text to write
    /// after the hole, and what is said.</summary>
    private (string? Spec, string? After, (NoteKind Kind, string Message, string? Suggestion)? Note) Translate(string spec, LyrType type)
    {
        if (Width().IsMatch(spec))
            return (spec.StartsWith('-') ? ">" + spec[1..] : "<" + spec, null, null);
        if (type is not PrimitiveType { Kind: var kind } || Standard().Match(spec) is not { Success: true } standard)
            return (null, null, Hand($"the format '{spec}' was .NET's; Lyric 5 has one format language", null));

        var letter = standard.Groups[1].Value;
        int? digits = standard.Groups[2].Value is { Length: > 0 } d ? int.Parse(d) : null;
        var isFloat = kind is PrimitiveKind.Float or PrimitiveKind.Float32 or PrimitiveKind.Float64;
        var isInteger = IsInteger(kind);
        var unsigned = IsUnsigned(kind);
        switch (char.ToUpperInvariant(letter[0]))
        {
            case 'N' when isFloat:
                return ($",.{digits ?? 2}f", null, null);
            case 'N' when isInteger:
                return (",", Decimals(digits ?? 2), null);
            case 'F' when isFloat:
                return ($".{digits ?? 2}f", null, null);
            case 'F' when isInteger:
                return ("", Decimals(digits ?? 2), null);
            case 'D' when isInteger:
                return digits is null or 0 or 1 ? ("", null, null)
                    : ($"0{digits}", null, unsigned ? null : Review(
                        $"'{spec}' padded the digits to {digits}, after the sign; ':0{digits}' counts the sign in the width",
                        $"for a negative value write ':0{digits + 1}'"));
            case 'X' when isInteger:
                var hex = (digits is null or 0 ? "" : $"0{digits}") + letter;
                return (hex, null, unsigned ? null : Review(
                    $"'{spec}' wrote a negative value's bits (two's complement); ':{hex}' writes its sign and magnitude",
                    "where the bits are meant, convert to 'uint64' first"));
            default:
                return (null, null, Hand($"the format '{spec}' ({Name(letter[0])}) has no counterpart in Lyric 5's format language", Suggestion(letter[0], digits)));
        }
    }

    /// <summary>The decimals 4.6 wrote after an integer — <c>N2</c> of <c>1234</c> is <c>1,234.00</c>; Lyric
    /// 5 gives an integer no precision, so they stand after the hole.</summary>
    private static string? Decimals(int digits) => digits == 0 ? null : "." + new string('0', digits);

    private static (NoteKind, string, string?) Review(string message, string? suggestion) => (NoteKind.Review, message, suggestion);

    private static (NoteKind, string, string?) Hand(string message, string? suggestion) => (NoteKind.Hand, message, suggestion);

    private static string Name(char letter) => char.ToUpperInvariant(letter) switch
    {
        'E' => "scientific",
        'P' => "percent",
        'C' => "currency",
        'G' => "general",
        'R' => "round-trip",
        _ => "a .NET format",
    };

    private static string? Suggestion(char letter, int? digits) => char.ToUpperInvariant(letter) switch
    {
        'E' => $"':.{digits ?? 6}{(char.IsUpper(letter) ? 'E' : 'e')}' writes the exponent without its sign and padding",
        'P' => $"':.{digits ?? 2}%' writes the percent without the space",
        'G' or 'R' => "no spec writes the shortest form that reads back",
        _ => null,
    };

    private static bool IsInteger(PrimitiveKind kind) => kind is PrimitiveKind.Int or PrimitiveKind.Int8 or PrimitiveKind.Int16
        or PrimitiveKind.Int32 or PrimitiveKind.Int64 or PrimitiveKind.Uint or PrimitiveKind.Uint8 or PrimitiveKind.Uint16
        or PrimitiveKind.Uint32 or PrimitiveKind.Uint64;

    private static bool IsUnsigned(PrimitiveKind kind) => kind is PrimitiveKind.Uint or PrimitiveKind.Uint8
        or PrimitiveKind.Uint16 or PrimitiveKind.Uint32 or PrimitiveKind.Uint64;
}

/// <summary>S10: an array's <c>length</c> was a field; in Lyric 5 it is <c>length()</c>, as everywhere.</summary>
internal sealed class Length : IRule
{
    public string Id => "S10";

    public void Apply(RuleContext context)
    {
        foreach (var module in context.Modules)
        {
            var nodes = Tree.Below(module).ToList();
            var called = nodes.OfType<CallExpr>().Select(c => c.Callee).ToHashSet(ReferenceEqualityComparer.Instance);
            foreach (var member in nodes.OfType<MemberExpr>())
                if (member is { Member: "length", IsOptional: false } && !called.Contains(member)
                    && context.Model.Types.TypeOf(member.Target) is ArrayOf)
                    context.Replace(Id, member.Span.File, member.Span.End, member.Span.End, "()");
        }
    }
}

/// <summary>
/// S11: the three generic interfaces of 4.6 name their element by an associated type in Lyric 5.
/// <c>Iterator&lt;T&gt;</c> as a type is <c>Iterator&lt;Item = T&gt;</c>; a conformance
/// <c>:: [Iterator&lt;T&gt;]</c> is <c>:: [Iterator]</c> with <c>type Item = T;</c>,
/// <c>Iterable&lt;T&gt;</c> is <c>Iterable</c> with <c>type Iter = Iterator&lt;Item = T&gt;;</c>, and
/// <c>Indexable&lt;T&gt;</c> is <c>IndexSet&lt;int&gt;</c> with <c>type Output = T;</c> and the
/// <c>index</c>/<c>setIndex</c> the brackets call, over the <c>get</c>/<c>set</c> it had (for
/// review). <c>Iterable&lt;T&gt;</c> or <c>Indexable&lt;T&gt;</c> as the type of a value has no
/// counterpart: said for review.
/// </summary>
internal sealed class GenericInterfaces : IRule
{
    public string Id => "S11";

    public void Apply(RuleContext context)
    {
        foreach (var module in context.Modules)
        {
            var text = context.TextOf(module);
            var conformances = new HashSet<TypeNode>(ReferenceEqualityComparer.Instance);
            foreach (var declaration in Tree.Below(module).OfType<Decl>())
            {
                (TypeNode[] Interfaces, Node[] Members) conforming = declaration switch
                {
                    StructDecl s => (s.Interfaces, s.Members),
                    ClassDecl c => (c.Interfaces, c.Members),
                    EnumDecl e => (e.Interfaces, e.Methods),
                    ExtendDecl x => (x.Interfaces, x.Methods),
                    _ => ([], []),
                };
                var (interfaces, members) = conforming;
                foreach (var written in interfaces)
                {
                    conformances.Add(written);
                    if (written is not NamedType { TypeArguments: [var element] } named || Which(context, named) is not { } name) continue;
                    Conform(context, text, declaration, named, name, Text(text, element), members);
                }
            }

            foreach (var node in Tree.Below(module))
            {
                if (node is not NamedType { TypeArguments: [var element] } named || conformances.Contains(named)) continue;
                switch (Which(context, named))
                {
                    case "Iterator":
                        context.Replace(Id, named.Span.File, element.Span.Start, element.Span.Start, "Item = ");
                        break;
                    case "Iterable" or "Indexable":
                        context.Note(Id, "LYR-MIG0211", NoteKind.Review, named.Span,
                            $"'{Text(text, named)}' as a type has no counterpart: Lyric 5's '{named.Path[^1]}' names its element by an associated type",
                            named.Path[^1] == "Iterable"
                                ? $"take an 'Iterator<Item = {Text(text, element)}>', or a type parameter '<I :: [Iterable]>'"
                                : "take the concrete type, or a type parameter with '[IndexSet<int>]'");
                        break;
                }
            }
        }
    }

    /// <summary>A conformance: the interface's arguments go, and what they said becomes members at
    /// the top of the body.</summary>
    private void Conform(RuleContext context, string text, Decl declaration, NamedType named, string name, string element, Node[] members)
    {
        var open = text.IndexOf('{', named.Span.End);
        if (open < 0 || open >= declaration.Span.End) return;
        var indent = members.Length > 0 ? Tree.IndentAt(text, members[0].Span.Start) : Tree.IndentAt(text, declaration.Span.Start) + "    ";
        var written = name switch
        {
            "Iterator" => ("Iterator", new[] { $"type Item = {element};" }),
            "Iterable" => ("Iterable", [$"type Iter = Iterator<Item = {element}>;"]),
            _ => ("IndexSet<int>", [$"type Output = {element};"]),
        };
        context.Replace(Id, named.Span.File, named.Span.Start, named.Span.End, written.Item1);
        var lines = new StringBuilder();
        foreach (var line in written.Item2) lines.Append('\n').Append(indent).Append(line);
        context.Replace(Id, named.Span.File, open + 1, open + 1, lines.ToString());

        if (name != "Indexable") return;
        // the brackets call 'index' and 'setIndex' in Lyric 5; they forward to what 4.6 called
        var close = text.LastIndexOf('}', declaration.Span.End - 1);
        if (close <= open) return;
        var at = text.LastIndexOf('\n', close - 1) + 1;
        var forward = new StringBuilder();
        forward.Append(indent).Append($"fn index(k: int): {element} {{ return this.get(k); }}\n");
        forward.Append(indent).Append($"mut fn setIndex(k: int, v: {element}): void {{ this.set(k, v); }}\n");
        context.Replace(Id, named.Span.File, at, at, forward.ToString());
        context.Note(Id, "LYR-MIG0211", NoteKind.Review, named.Span,
            $"'{Text(text, named)}' is 'IndexSet<int>' in Lyric 5: 'x[i]' calls 'index', 'x[i] = v' calls 'setIndex', which forward to 'get' and 'set'",
            "fold 'get' and 'set' into them where nothing else calls them");
    }

    /// <summary>Which of the three a written type names — one of 4.6's own library, not a namesake.</summary>
    private static string? Which(RuleContext context, NamedType named) =>
        named.Path[^1] is "Iterator" or "Iterable" or "Indexable"
        && Tree.Target(context, named) is TypeSymbol { Kind: TypeSymbolKind.Interface } symbol
        && Tree.InStdlib(context, symbol)
            ? named.Path[^1]
            : null;

    private static string Text(string text, Node node) => text[node.Span.Start..node.Span.End];
}
