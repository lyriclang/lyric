using System.Text;
using Lyric4.AST;
using Lyric4.Resolver;
using Lyric4.Sema;

namespace Lyric4.From4.Rules;

/// <summary>The conformances and constraints of a module as written, with the type each names as
/// its own: a declaration's name for its conformance list, a type parameter's for its constraints.</summary>
internal static class Conformances
{
    /// <summary>A written interface, the name it is written for, and the declaration it stands on
    /// (<c>null</c> for a constraint).</summary>
    public sealed record Written(NamedType Interface, string Self, Decl? Declaration, Node[] Members);

    public static IEnumerable<Written> Of(Module module, string text)
    {
        foreach (var node in Tree.Below(module))
        {
            switch (node)
            {
                case StructDecl s:
                    foreach (var i in s.Interfaces.OfType<NamedType>()) yield return new(i, s.Name, s, s.Members);
                    break;
                case ClassDecl c:
                    foreach (var i in c.Interfaces.OfType<NamedType>()) yield return new(i, c.Name, c, c.Members);
                    break;
                case EnumDecl e:
                    foreach (var i in e.Interfaces.OfType<NamedType>()) yield return new(i, e.Name, e, e.Methods);
                    break;
                case ExtendDecl x:
                    foreach (var i in x.Interfaces.OfType<NamedType>())
                        yield return new(i, text[x.Target.Span.Start..x.Target.Span.End], x, x.Methods);
                    break;
                case GenericParam g:
                    foreach (var i in g.Constraints.OfType<NamedType>()) yield return new(i, g.Name, null, []);
                    break;
            }
        }
    }

    /// <summary>Which interface of 4.6's own library a written type names, by its name — not a namesake.</summary>
    public static string? Std(RuleContext context, NamedType named) =>
        Tree.Target(context, named) is TypeSymbol { Kind: TypeSymbolKind.Interface } symbol && Tree.InStdlib(context, symbol)
            ? named.Path[^1]
            : null;

    public static string Text(string text, Node node) => text[node.Span.Start..node.Span.End];

    /// <summary>The methods of a declaration with a name, written in it.</summary>
    public static IEnumerable<FunctionDecl> Methods(Node[] members, string name) =>
        members.OfType<FunctionDecl>().Where(f => f.Name == name && f.Body is not null);

    /// <summary>The <c>return</c>s of a body, not those of a lambda in it.</summary>
    public static IEnumerable<ReturnStmt> Returns(Node body)
    {
        foreach (var child in AstChildren.Of(body))
        {
            if (child is LambdaExpr) continue;
            if (child is ReturnStmt r) yield return r;
            foreach (var below in Returns(child)) yield return below;
        }
    }

    /// <summary>The text that inserts lines after the opening brace of a declaration's body, at its
    /// members' indentation; <c>null</c> where the body is not found.</summary>
    public static (int At, string Indent)? BodyStart(string text, Decl declaration, NamedType after, Node[] members)
    {
        var open = text.IndexOf('{', after.Span.End);
        if (open < 0 || open >= declaration.Span.End) return null;
        var indent = members.Length > 0 ? Tree.IndentAt(text, members[0].Span.Start) : Tree.IndentAt(text, declaration.Span.Start) + "    ";
        return (open + 1, indent);
    }
}

/// <summary>T01: a field without a modifier was writable in Lyric 4; Lyric 5 writes a field only
/// where it is declared <c>var</c>. Every field of the program's own types that is written — by
/// <c>=</c>, a compound assignment, <c>++</c>/<c>--</c>, or a <c>mut fn</c> called on it — gets
/// <c>var</c>, and so does each struct field the write reaches through (<c>l.to.y = 1</c> writes
/// <c>to</c> as well: a struct is a value). Said for a proposal: a field written only while the
/// value is built might rather stay without it.</summary>
internal sealed class Fields : IRule
{
    public string Id => "T01";

    public void Apply(RuleContext context)
    {
        var written = new Dictionary<FieldDecl, Node>(ReferenceEqualityComparer.Instance);
        foreach (var module in context.Modules)
            foreach (var node in Tree.Below(module))
            {
                var target = node switch
                {
                    AssignExpr a => a.Target,
                    PostfixExpr { Operator: PostfixOp.Inc or PostfixOp.Dec } p => p.Operand,
                    UnaryExpr { Operator: UnaryOp.PreInc or UnaryOp.PreDec } u => u.Operand,
                    CallExpr { Callee: MemberExpr callee } c when Tree.Target(context, callee) is FunctionSymbol { IsMut: true, IsStatic: false }
                        && IsStruct(context.Model.Types.TypeOf(callee.Target)) => callee.Target,
                    _ => null,
                };
                if (target is not null) Mark(context, target, node, written);
            }

        foreach (var (field, at) in written.OrderBy(w => w.Key.Span.Start))
        {
            context.Replace(Id, field.NameSpan.File, field.NameSpan.Start, field.NameSpan.Start, "var ");
            var where = context.Model.Sources.LocateStart(at.Span);
            context.Note(Id, "LYR-MIG0301", NoteKind.Review, field.NameSpan,
                $"the field '{field.Name}' is written (line {where.Line}); Lyric 5 writes a field only where it is declared 'var'",
                "where only the value's construction should set it, leave out the 'var' and build a new value");
        }
    }

    /// <summary>The field a written expression names, and each struct field it reaches through.</summary>
    private static void Mark(RuleContext context, Expr target, Node at, Dictionary<FieldDecl, Node> written)
    {
        while (target is MemberExpr member && Tree.Target(context, member) is FieldSymbol { Declaration: FieldDecl field })
        {
            if (!From4.IsStdlib(context.Model.Sources.GetPath(field.Span.File))) written.TryAdd(field, at);
            if (!IsStruct(context.Model.Types.TypeOf(member.Target))) return;
            target = member.Target;
        }
    }

    private static bool IsStruct(LyrType type) => type switch
    {
        NamedRef { Symbol.Kind: TypeSymbolKind.Struct } => true,
        GenericInstance { Definition.Kind: TypeSymbolKind.Struct } => true,
        _ => false,
    };
}

/// <summary>T02: <c>Equatable&lt;Point&gt;</c>, <c>Ordered&lt;Version&gt;</c> and <c>Hashable&lt;K&gt;</c>
/// named their own type; in Lyric 5 that is <c>Self</c>, and the argument goes —
/// <c>:: [Equatable]</c>, <c>T :: [Hashable]</c>. One that names another type
/// (<c>Equatable&lt;int&gt;</c> on a <c>Tag</c>) has no counterpart: said by hand.</summary>
internal sealed class SelfInterfaces : IRule
{
    public string Id => "T02";

    public void Apply(RuleContext context)
    {
        foreach (var module in context.Modules)
        {
            var text = context.TextOf(module);
            foreach (var written in Conformances.Of(module, text))
            {
                if (written.Interface is not { TypeArguments: [var argument] } named
                    || Conformances.Std(context, named) is not ("Equatable" or "Ordered" or "Hashable") || named.TypeArguments.Length != 1)
                    continue;
                var name = named.Path[^1];
                if (Conformances.Text(text, argument) == written.Self)
                {
                    var open = text.IndexOf('<', named.Span.Start);
                    context.Replace(Id, named.Span.File, open, named.Span.End, "");
                }
                else
                    context.Note(Id, "LYR-MIG0302", NoteKind.Hand, named.Span,
                        $"'{Conformances.Text(text, named)}' compares '{written.Self}' with '{Conformances.Text(text, argument)}'; Lyric 5's '{name}' compares values of one type",
                        "compare what the two have in common, or give the method a name of its own");
            }
        }
    }
}

/// <summary>T03: an operator interface of Lyric 4 took the right-hand type and the result,
/// <c>Add&lt;Vec2, Vec2&gt;</c>; Lyric 5 takes the right-hand type (<c>Self</c> where it is left out)
/// and names the result by <c>type Out</c> (<c>Self</c> where it is left out). A conformance is
/// <c>:: [Add]</c>, <c>:: [Mul&lt;float&gt;]</c>, with <c>type Out = float;</c> in the body where the
/// result is another type; a constraint is <c>T :: [Add&lt;T, Out = T&gt;]</c>.</summary>
internal sealed class OperatorInterfaces : IRule
{
    public string Id => "T03";

    public void Apply(RuleContext context)
    {
        foreach (var module in context.Modules)
        {
            var text = context.TextOf(module);
            var outs = new Dictionary<Decl, string>(ReferenceEqualityComparer.Instance);
            foreach (var written in Conformances.Of(module, text))
            {
                if (written.Interface is not { TypeArguments: [var rhs, var result] } named
                    || Conformances.Std(context, named) is not ("Add" or "Sub" or "Mul" or "Div"))
                    continue;
                var name = named.Path[^1];
                var right = Conformances.Text(text, rhs);
                var output = Conformances.Text(text, result);
                if (written.Declaration is not { } declaration)
                {
                    context.Replace(Id, named.Span, $"{name}<{right}, Out = {output}>");
                    continue;
                }
                context.Replace(Id, named.Span, right == written.Self ? name : $"{name}<{right}>");
                if (output == written.Self) continue;
                if (outs.TryGetValue(declaration, out var other))
                {
                    if (other != output)
                        context.Note(Id, "LYR-MIG0303", NoteKind.Hand, named.Span,
                            $"'{written.Self}' gives '{other}' and '{output}' from its operators; Lyric 5 names one 'type Out' a declaration",
                            "move one of the conformances into an 'extend' block of its own, with its 'type Out'");
                    continue;
                }
                outs[declaration] = output;
                if (Conformances.BodyStart(text, declaration, named, written.Members) is { } start)
                    context.Replace(Id, named.Span.File, start.At, start.At, $"\n{start.Indent}type Out = {output};");
            }
        }
    }
}

/// <summary>T04: Lyric 4's <c>compare</c> returned an <c>int</c> (negative, zero, positive); Lyric 5's
/// returns <c>?Ordering</c>, <c>null</c> where two values have no order, and <c>TotalOrder</c>'s
/// <c>totalCompare</c> is the order <c>sort</c> takes. The method returns an <c>Ordering</c> — a literal
/// <c>-1</c>/<c>0</c>/<c>1</c> its case, another value compared with 0 —, the conformance gains
/// <c>TotalOrder</c> with a <c>totalCompare</c> over it, and a call compared with 0
/// (<c>a.compare(b) &lt; 0</c>) is the operator (<c>a &lt; b</c>). A call whose <c>int</c> is kept
/// is said by hand.</summary>
internal sealed class Compare : IRule
{
    public string Id => "T04";

    public void Apply(RuleContext context)
    {
        foreach (var module in context.Modules)
        {
            var text = context.TextOf(module);
            var returned = new HashSet<Expr>(ReferenceEqualityComparer.Instance);
            foreach (var written in Conformances.Of(module, text))
            {
                if (written.Declaration is not { } declaration || Conformances.Std(context, written.Interface) != "Ordered") continue;
                foreach (var method in Conformances.Methods(written.Members, "compare"))
                {
                    if (method is not { Parameters: [var other], ReturnType: { } type } || Conformances.Text(text, type) != "int") continue;
                    context.Replace(Id, type.Span, "?Ordering");
                    foreach (var r in Conformances.Returns(method.Body!))
                        if (r.Value is { } value)
                        {
                            returned.Add(value);
                            Ordering(context, text, value);
                        }
                    var indent = Tree.IndentAt(text, method.Span.Start);
                    var otherType = Conformances.Text(text, other.Type);
                    context.Replace(Id, method.Span.File, method.Span.End, method.Span.End,
                        $"\n{indent}fn totalCompare({other.Name}: {otherType}): Ordering {{\n{indent}    return this.compare({other.Name})!;\n{indent}}}");
                    context.Replace(Id, written.Interface.Span.File, written.Interface.Span.End, written.Interface.Span.End, ", TotalOrder");
                    context.Note(Id, "LYR-MIG0304", NoteKind.Review, method.NameSpan,
                        $"'{written.Self}.compare' returned an int; Lyric 5's returns '?Ordering', 'null' where two values have no order, and 'totalCompare' gives the total order that 'sort' takes",
                        "where the order has no gap, 'totalCompare' alone is enough");
                }
            }

            var compared = new HashSet<CallExpr>(ReferenceEqualityComparer.Instance);
            foreach (var binary in Tree.Below(module).OfType<BinaryExpr>())
            {
                if (Operator(binary.Operator) is not { } op) continue;
                if (CompareCall(context, binary.Left) is { } left && IsZero(binary.Right))
                    Rewrite(context, text, binary, left, op, compared);
                else if (CompareCall(context, binary.Right) is { } right && IsZero(binary.Left))
                    Rewrite(context, text, binary, right, Mirror(op), compared);
            }
            foreach (var call in Tree.Below(module).OfType<CallExpr>())
                if (CompareCall(context, call) is { } kept && !compared.Contains(kept) && !returned.Contains(kept))
                    context.Note(Id, "LYR-MIG0304", NoteKind.Hand, kept.Span,
                        "'compare' returns '?Ordering' in Lyric 5, not an int",
                        "compare the values with '<', '==' or '>', or match the 'Ordering'");
        }
    }

    /// <summary>A returned <c>int</c> as the <c>Ordering</c> it stood for.</summary>
    private void Ordering(RuleContext context, string text, Expr value)
    {
        if (Sign(value) is { } sign)
        {
            context.Replace(Id, value.Span, sign switch { < 0 => ".Less", 0 => ".Equal", _ => ".Greater" });
            return;
        }
        if (value is IfExpr choice)
        {
            Ordering(context, text, choice.Then);
            Ordering(context, text, choice.Else);
            return;
        }
        if (CompareCall(context, value) is not null) return;
        if (Tree.TakesPostfix(value))
            context.Replace(Id, value.Span.File, value.Span.End, value.Span.End, ".compare(0)");
        else
        {
            context.Replace(Id, value.Span.File, value.Span.Start, value.Span.Start, "(");
            context.Replace(Id, value.Span.File, value.Span.End, value.Span.End, ").compare(0)");
        }
    }

    /// <summary>The sign of an integer literal, <c>-1</c> written as a negation.</summary>
    private static int? Sign(Expr value) => value switch
    {
        IntLiteralExpr { Value: 0 } => 0,
        IntLiteralExpr => 1,
        UnaryExpr { Operator: UnaryOp.Neg, Operand: IntLiteralExpr { Value: > 0 } } => -1,
        _ => null,
    };

    private static bool IsZero(Expr value) => value is IntLiteralExpr { Value: 0 };

    /// <summary>A call <c>a.compare(b)</c> whose <c>int</c> Lyric 4 returned.</summary>
    private static CallExpr? CompareCall(RuleContext context, Expr expr) =>
        expr is CallExpr { Callee: MemberExpr { Member: "compare" }, Arguments: [_] } call
        && context.Model.Types.TypeOf(call) is PrimitiveType { Kind: PrimitiveKind.Int }
            ? call
            : null;

    private void Rewrite(RuleContext context, string text, BinaryExpr binary, CallExpr call, string op, HashSet<CallExpr> compared)
    {
        var receiver = ((MemberExpr)call.Callee).Target;
        compared.Add(call);
        context.Replace(Id, binary.Span, $"{Conformances.Text(text, receiver)} {op} {Conformances.Text(text, call.Arguments[0])}");
    }

    private static string? Operator(BinaryOp op) => op switch
    {
        BinaryOp.Lt => "<",
        BinaryOp.Le => "<=",
        BinaryOp.Gt => ">",
        BinaryOp.Ge => ">=",
        BinaryOp.Eq => "==",
        BinaryOp.Ne => "!=",
        _ => null,
    };

    /// <summary>The operator with its sides swapped: <c>0 &lt; a.compare(b)</c> is <c>a &gt; b</c>.</summary>
    private static string Mirror(string op) => op switch
    {
        "<" => ">",
        "<=" => ">=",
        ">" => "<",
        ">=" => "<=",
        _ => op,
    };
}

/// <summary>T05: Lyric 4's <c>hash()</c> returned an <c>int</c>, built with <c>combineHash</c>;
/// Lyric 5's writes into a hasher, <c>hash&lt;H :: [Hasher]&gt;(&amp;h: H)</c>. A return of a
/// <c>combineHash</c> chain over <c>x.hash()</c> is each <c>x.hash(&amp;h);</c> in its order; any other
/// returned value is <c>h.writeInt(value);</c>. Said for a proposal: the hash values differ. A call
/// of <c>hash()</c> elsewhere is said by hand.</summary>
internal sealed class Hashing : IRule
{
    public string Id => "T05";

    /// <summary>Whether a module has a <c>hash</c> this rule writes — it then imports <c>Hasher</c>.</summary>
    public static bool NeedsHasher(RuleContext context, Module module) => Methods(context, module, context.TextOf(module)).Any();

    private static IEnumerable<(Conformances.Written Written, FunctionDecl Method)> Methods(RuleContext context, Module module, string text) =>
        from written in Conformances.Of(module, text)
        where written.Declaration is not null && Conformances.Std(context, written.Interface) == "Hashable"
        from method in Conformances.Methods(written.Members, "hash")
        where method is { Parameters: [], ReturnType: { } type } && Conformances.Text(text, type) == "int"
        select (written, method);

    public void Apply(RuleContext context)
    {
        foreach (var module in context.Modules)
        {
            var text = context.TextOf(module);
            var inside = new List<Block>();
            var methods = Methods(context, module, text).ToList();
            foreach (var (written, method) in methods)
            {
                var body = method.Body!;
                inside.Add(body);
                context.Replace(Id, method.NameSpan.File, method.NameSpan.Start, method.ReturnType!.Span.End, "hash<H :: [Hasher]>(&h: H): void");
                var last = body.Statements.Length > 0 ? body.Statements[^1] : null;
                foreach (var r in Conformances.Returns(body))
                {
                    var indent = Tree.IndentAt(text, r.Span.Start);
                    var lines = r.Value is { } value ? Write(context, text, value) : [];
                    if (!ReferenceEquals(r, last)) lines.Add("return;");
                    context.Replace(Id, r.Span, string.Join("\n" + indent, lines));
                }
                context.Note(Id, "LYR-MIG0305", NoteKind.Review, method.NameSpan,
                    $"'{written.Self}.hash' returned an int; Lyric 5's writes the value into a hasher, so the hashes differ from Lyric 4's",
                    "where it hashes every field the way 'equals' compares them, leave the method out: Lyric 5 writes it");
            }

            if (methods.Count > 0 && !HasStdCoreImport(module))
            {
                var at = module.Declarations.OfType<ImportDecl>().LastOrDefault() is { } import ? LineEnd(text, import.Span.End) : 0;
                context.Replace(Id, module.Span.File, at, at, "import std.core { Hasher };\n");
            }

            foreach (var call in Tree.Below(module).OfType<CallExpr>())
                if (IsCombine(context, call) && !inside.Any(b => b.Span.Start <= call.Span.Start && call.Span.End <= b.Span.End))
                    context.Note(Id, "LYR-MIG0305", NoteKind.Hand, call.Span,
                        $"'{((FunctionSymbol)Tree.Target(context, call.Callee)!).Name}' is gone in Lyric 5: a hash is written into a hasher",
                        "write the parts into a hasher: 'var h = FixedHasher.new(); a.hash(&h); b.hash(&h); h.finish()'");
                else if (call is { Callee: MemberExpr { Member: "hash" }, Arguments: [] }
                    && context.Model.Types.TypeOf(call) is PrimitiveType { Kind: PrimitiveKind.Int }
                    && !inside.Any(b => b.Span.Start <= call.Span.Start && call.Span.End <= b.Span.End))
                    context.Note(Id, "LYR-MIG0305", NoteKind.Hand, call.Span,
                        "'hash()' returned an int in Lyric 4; Lyric 5's 'hash' writes into a hasher",
                        "make a hasher and read its value: 'var h = FixedHasher.new(); x.hash(&h); h.finish()'");
        }
    }

    /// <summary>The statements that write a returned value into <c>h</c>.</summary>
    private List<string> Write(RuleContext context, string text, Expr value)
    {
        var hashed = new List<Expr>();
        if (IsCombine(context, value) && Combined(context, value, hashed))
            return [.. hashed.Select(e => $"{Conformances.Text(text, e)}.hash(&h);")];
        if (Tree.Below(value).Prepend(value).Any(n => n is CallExpr { Callee: MemberExpr { Member: "hash" } } || IsCombine(context, n)))
            context.Note(Id, "LYR-MIG0305", NoteKind.Hand, value.Span,
                "the hash is computed from other hashes, which Lyric 5 writes into the hasher instead",
                "write each part with 'part.hash(&h)'");
        return [$"h.writeInt({Conformances.Text(text, value)});"];
    }

    /// <summary>Whether a value is a <c>combineHash</c> chain from 0 over <c>x.hash()</c>; the
    /// hashed values in their order.</summary>
    private static bool Combined(RuleContext context, Expr value, List<Expr> hashed)
    {
        if (value is IntLiteralExpr { Value: 0 }) return true;
        if (value is not CallExpr { Arguments: [var seed, var part] } call || !IsCombine(context, call)) return false;
        if (part is not CallExpr { Callee: MemberExpr { Member: "hash" } member, Arguments: [] }) return false;
        if (!Combined(context, seed, hashed)) return false;
        hashed.Add(member.Target);
        return true;
    }

    private static bool IsCombine(RuleContext context, Node node) =>
        node is CallExpr call && Tree.Target(context, call.Callee) is FunctionSymbol { Name: "combineHash" or "hashCombine" } symbol
        && Tree.InStdlib(context, symbol);

    private static bool HasStdCoreImport(Module module) =>
        module.Declarations.OfType<ImportDecl>().Any(i => i.Clause is ImportSelective s && string.Join('.', i.Path) is "std.core" or "std.hash"
            && s.Names.Any(n => n is "combineHash" or "hashCombine"));

    private static int LineEnd(string text, int end)
    {
        if (end < text.Length && text[end] == '\r') end++;
        if (end < text.Length && text[end] == '\n') end++;
        return end;
    }
}
