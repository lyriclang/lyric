using Lyric.AST;
using Lyric.Core;
using Lyric.Lexing;

namespace Lyric.Formatting;

/// <summary>
/// Builds the <see cref="Doc"/> of a parsed module: one method per node shape, none of them
/// measuring a column.
///
/// <para>Two facts about this AST drive the design. LITERALS have lost their spelling — an
/// <c>IntLiteralExpr</c> holds a <c>ulong</c>, not the <c>0xFF</c> or <c>1_000</c> someone
/// wrote — so every literal (f-strings included) is printed from its source span, verbatim.
/// And there are NO parenthesis nodes: <c>(a + b) * c</c> is plain nesting, so the printer
/// re-derives parentheses from the precedence table of Grammar.md §6.1. A group whose written
/// parentheses were redundant loses them; one that needs them gets them back — the reparse
/// invariant of the test suite is what holds that honest.</para>
///
/// <para>COMMENTS travel beside the tree, not in it: a single source-ordered cursor over the
/// merged comment stream (line, block and doc comments alike), consumed at the sequence
/// boundaries — between statements, declarations, members and match arms. A comment on the same
/// line as the element before it stays trailing; every other one stands on its own line, and
/// the blank lines around it follow the source. A comment INSIDE an expression is not lost but
/// surfaces at the next boundary — line-level fidelity, the gofmt trade.</para>
///
/// <para>BLANK LINES between elements are the user's, capped at one — except where the style
/// has an opinion: a blank always follows the module header and separates top-level
/// declarations and members with bodies, and imports sit together whatever the source did.
/// Error nodes throw: the formatter runs only on files the parser accepted.</para>
/// </summary>
public sealed class AstFormatter
{
    /// <summary>What separates two adjacent elements of a sequence.</summary>
    private enum Air
    {
        /// <summary>A blank line, whatever the source says.</summary>
        Forced,

        /// <summary>A blank line exactly when the source has one (or more).</summary>
        User,

        /// <summary>No blank line, whatever the source says.</summary>
        Never,
    }

    private readonly string _source;
    private readonly bool _synthesized; // the nodes were built, not read: no source to copy a spelling from
    private readonly IReadOnlyList<Trivia> _comments;
    private readonly int[] _lineStarts;
    private int _next;   // the comment cursor: everything before it is already printed
    private int _lastEnd = -1; // source end of the last element printed at line level

    /// <summary>The expressions the source wrote in parentheses, with the outermost pair's span;
    /// <c>null</c> where parentheses are derived from precedence alone.</summary>
    private readonly IReadOnlyDictionary<Expr, Span>? _grouped;

    private AstFormatter(string? source, IReadOnlyList<Trivia> comments, IReadOnlyDictionary<Expr, Span>? grouped = null)
    {
        _source = source ?? "";
        _synthesized = source is null;
        _comments = comments;
        _grouped = grouped;

        var starts = new List<int> { 0 };
        for (var i = 0; i < _source.Length; i++)
            if (_source[i] == '\n')
                starts.Add(i + 1);
        _lineStarts = starts.ToArray();
    }

    /// <param name="comments">Every comment of the file, source-ordered — the lexer's trivia
    /// plus the doc-comment tokens, merged by the caller.</param>
    /// <param name="grouped">The parser's written parentheses, kept as written; without them every
    /// pair is derived from precedence.</param>
    public static Doc Build(Module module, string source, IReadOnlyList<Trivia> comments,
        IReadOnlyDictionary<Expr, Span>? grouped = null) =>
        new AstFormatter(source, comments, grouped).ModuleDoc(module);

    /// <summary>A node that was built, not read, <c>std.syntax</c>'s: there is no source
    /// to copy a spelling from, so a literal is written from its value, and there are no comments.
    /// A <see cref="Block"/> is written with its braces.</summary>
    public static string FormatNode(Node node) => DocRenderer.Render(new AstFormatter(null, []).NodeDoc(node));

    /// <summary>Statements one under the other, as a block body holds them, without the braces.</summary>
    public static string FormatStatements(IReadOnlyList<Stmt> statements)
    {
        var formatter = new AstFormatter(null, []);
        return DocRenderer.Render(formatter.SequenceDoc(statements, formatter.StmtDoc, (_, _) => Air.User, 0));
    }

    /// <summary>Declarations as a module's, with the blank lines between them — without the
    /// newline that ends a formatted file: a node's text ends with the node.</summary>
    public static string FormatDeclarations(IReadOnlyList<Decl> declarations) =>
        DocRenderer.Render(new AstFormatter(null, []).ModuleDoc(new Module(null, [.. declarations], default))).TrimEnd('\n');

    private Doc NodeDoc(Node node) => node switch
    {
        Block b => BlockDoc(b),
        Expr e => ExprDoc(e, Assign),
        Stmt s => StmtDoc(s),
        Decl d => DeclDoc(d),
        TypeNode t => TypeDoc(t),
        Pattern p => PatternDoc(p),
        _ => throw new InternalCompilationException($"no document for a {node.GetType().Name}"),
    };

    // ------------------------------------------------------------------ literals from their values

    /// <summary>The spelling of a literal whose node was built, not read: the value as the lexer
    /// reads it back. A sign is a <see cref="UnaryExpr"/> around the literal.</summary>
    private string LiteralText(Expr expr) => expr switch
    {
        IntLiteralExpr i => i.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                            + (i.Suffix is { } s ? s.ToString().ToLowerInvariant() : ""),
        FloatLiteralExpr f => FloatLiteralText(f.Value) + (f.Suffix is { } s ? s.ToString().ToLowerInvariant() : ""),
        StringLiteralExpr s => Quote(s.Value),
        CharLiteralExpr c => "'" + EscapeChar(c.CodePoint, '\'') + "'",
        ByteStringExpr b => "b\"" + string.Concat(b.Bytes.Select(x =>
            x is >= 0x20 and < 0x7F && x != '"' && x != '\\' ? ((char)x).ToString() : $"\\x{x:x2}")) + "\"",
        InterpolatedStringExpr s => "f\"" + string.Concat(s.Segments.Select(segment => segment switch
        {
            InterpText t => t.Text,
            InterpHole h => "{" + DocRenderer.Render(ExprDoc(h.Expr, Assign), int.MaxValue / 2)
                            + (h.FormatSpec is { } spec ? ":" + spec : "") + "}",
            _ => "",
        })) + "\"",
        BoolLiteralExpr b => b.Value ? "true" : "false",
        NullLiteralExpr => "null",
        UnaryExpr { Operator: UnaryOp.Neg } u => "-" + LiteralText(u.Operand),
        _ => throw new InternalCompilationException($"{expr.GetType().Name} is no literal"),
    };

    private static string FloatLiteralText(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            throw new InternalCompilationException("a float literal that is no number");
        var text = Lyric.Ir.Interp.FloatText.Shortest(value);
        return text.Contains('.') || text.Contains('e') || text.Contains('E') ? text : text + ".0";
    }

    private static string Quote(string s)
    {
        var sb = new System.Text.StringBuilder("\"");
        foreach (var rune in s.EnumerateRunes()) sb.Append(EscapeChar(rune.Value, '"'));
        return sb.Append('"').ToString();
    }

    private static string EscapeChar(int c, char quote) => c switch
    {
        '\\' => "\\\\",
        '\n' => "\\n",
        '\r' => "\\r",
        '\t' => "\\t",
        0 => "\\0",
        _ when c == quote => "\\" + quote,
        < 0x20 => $"\\u{{{c:x}}}",
        _ => char.ConvertFromUtf32(c),
    };

    // ------------------------------------------------------------------ lines and comments

    private int LineOf(int offset)
    {
        var index = Array.BinarySearch(_lineStarts, offset);
        return index >= 0 ? index : ~index - 1;
    }

    private bool BlankBetween(int endOffset, int startOffset) =>
        LineOf(startOffset) - LineOf(endOffset) >= 2;

    /// <summary>Where an item begins in the text: at its first attribute, which its span does not
    /// hold. Measured from the span, an attribute the form puts on a line of its own would read as
    /// a blank line before the item the next time the file is formatted.</summary>
    private static int StartOf(Node node) => AttributesOf(node) is [var first, ..]
        ? Math.Min(first.Span.Start, node.Span.Start)
        : node.Span.Start;

    private static AttributeNode[] AttributesOf(Node node) => node switch
    {
        FunctionDecl d => d.Attributes,
        StaticBindingDecl d => d.Attributes,
        FieldDecl d => d.Attributes,
        StructDecl d => d.Attributes,
        ClassDecl d => d.Attributes,
        EnumDecl d => d.Attributes,
        EnumVariant d => d.Attributes,
        InterfaceDecl d => d.Attributes,
        ExtendDecl d => d.Attributes,
        AssociatedTypeDecl d => d.Attributes,
        GlobalBindingDecl d => d.Attributes,
        TypeAliasDecl d => d.Attributes,
        _ => [],
    };

    private bool AnyCommentBefore(int position) =>
        _next < _comments.Count && _comments[_next].Span.Start < position;

    // A multi-line block comment may carry CRLF inside; the output contract is LF-only.
    private string CommentText(Trivia comment) =>
        _source.Substring(comment.Span.Start, comment.Span.Length)
            .Replace("\r\n", "\n").TrimEnd('\r');

    /// <summary>A comment on the line the previous element ended on trails it after one
    /// space; several chain. Bounded, so a comment behind the container's closing brace is not
    /// pulled inside.</summary>
    private void EmitTrailingComments(List<Doc> parts, int limit)
    {
        while (AnyCommentBefore(limit) && _lastEnd >= 0
               && LineOf(_comments[_next].Span.Start) == LineOf(_lastEnd))
        {
            var comment = _comments[_next++];
            parts.Add(Doc.From(" " + CommentText(comment)));
            _lastEnd = comment.Span.End;
        }
    }

    /// <summary>One comment on its own line, separated by what <paramref name="air"/> and the
    /// source agree on.</summary>
    private void EmitOwnLine(List<Doc> parts, Trivia comment, Air air)
    {
        if (parts.Count > 0)
        {
            parts.Add(Doc.NewLine);
            if (air == Air.Forced
                || (air == Air.User && _lastEnd >= 0 && BlankBetween(_lastEnd, comment.Span.Start)))
                parts.Add(Doc.NewLine);
        }

        parts.Add(Doc.From(CommentText(comment)));
        _lastEnd = comment.Span.End;
    }

    /// <summary>
    /// The sequence engine every line-level container runs on.
    ///
    /// <para>Per item: the previous line's trailing comments, then the pending own-line
    /// comments, then the item. The pending comments split into floating paragraphs, which
    /// keep the user's blank lines, and the group GLUED to the item — no blank line anywhere
    /// between it and the item — which travels with it: the air rule between two declarations
    /// applies before the doc comment of the second, not between the comment and its
    /// declaration. The container's tail comments follow the last item.</para>
    /// </summary>
    private Doc SequenceDoc<T>(IReadOnlyList<T> items, Func<T, Doc> print,
        Func<T, T, Air> airOf, int containerEnd) where T : Node
    {
        var parts = new List<Doc>();
        T? previous = default;

        foreach (var item in items)
        {
            EmitTrailingComments(parts, item.Span.Start);

            var pending = new List<Trivia>();
            while (AnyCommentBefore(item.Span.Start)) pending.Add(_comments[_next++]);

            // The glued suffix: walk back from the item while no blank line intervenes.
            var glued = pending.Count;
            var reach = item.Span.Start;
            while (glued > 0 && !BlankBetween(pending[glued - 1].Span.End, reach))
                reach = pending[--glued].Span.Start;

            for (var i = 0; i < glued; i++) EmitOwnLine(parts, pending[i], Air.User);

            var air = previous is not null ? airOf(previous, item) : Air.User;
            if (glued < pending.Count)
            {
                // A comment heading the unit marks a boundary the user drew: a pair that would
                // collapse (imports) keeps the user's blank line instead. Forced stays forced —
                // the doc comment of a declaration carries its declaration's air.
                if (air == Air.Never) air = Air.User;
                EmitOwnLine(parts, pending[glued], air);
                for (var i = glued + 1; i < pending.Count; i++)
                    EmitOwnLine(parts, pending[i], Air.User);
                air = Air.User; // the unit's separation is spent on its first comment
            }

            if (parts.Count > 0)
            {
                parts.Add(Doc.NewLine);
                if (air == Air.Forced
                    || (air == Air.User && _lastEnd >= 0 && BlankBetween(_lastEnd, StartOf(item))))
                    parts.Add(Doc.NewLine);
            }

            parts.Add(print(item));
            _lastEnd = item.Span.End;
            previous = item;
        }

        EmitTrailingComments(parts, containerEnd);
        while (AnyCommentBefore(containerEnd)) EmitOwnLine(parts, _comments[_next++], Air.User);
        return new Doc.Concat(parts);
    }

    /// <summary>A braced, indented body around a sequence — or <c>{ }</c> when there is truly
    /// nothing, comments included, to put into it.</summary>
    private Doc BracedDoc(Doc head, bool empty, Func<Doc> body)
    {
        if (empty) return Doc.Of(head, Doc.From("{ }"));
        return Doc.Of(head, Doc.From("{"), Doc.IndentOf(Doc.NewLine, body()),
            Doc.NewLine, Doc.From("}"));
    }

    // ------------------------------------------------------------------ module and declarations

    private Doc ModuleDoc(Module module)
    {
        // Attributes, header and declarations in one sequence: the air rule knows the header
        // gets a blank after it, imports sit together, and everything else breathes.
        var items = new List<Node>();
        items.AddRange(module.Attributes);
        if (module.Header is { } header) items.Add(header);
        // the functions of an extern block go back into the block they were written in
        foreach (var decl in module.Declarations)
        {
            if (decl is FunctionDecl { Extern.Block: { } block })
            {
                if (items.Count == 0 || !ReferenceEquals(items[^1], block)) items.Add(block);
                continue;
            }
            items.Add(decl);
        }

        var content = SequenceDoc(items, ItemDoc, ModuleAir, _source.Length);
        return Doc.Of(content, Doc.NewLine); // the trailing newline of every formatted file

        Doc ItemDoc(Node item) => item switch
        {
            AttributeNode a => AttributeDoc(a),
            ModulePath h => Doc.From($"module {string.Join(".", h.Segments)};"),
            Decl d => DeclDoc(d),
            ExternBlock b => ExternBlockDoc(b),
            _ => throw new InternalCompilationException($"unreachable: {item.GetType().Name} at top level"),
        };
    }

    /// <summary><c>extern "C" { … }</c>: the block's head once, its functions inside without it.</summary>
    private Doc ExternBlockDoc(ExternBlock block)
    {
        var first = block.Members[0];
        var head = Doc.Of(Pub(block.Visibility), Doc.From("extern "),
            _synthesized ? Doc.From(Quote(first.Extern!.Abi)) : Src(first.Extern!.AbiSpan), Doc.From(" "));
        _inExternBlock = true;
        try
        {
            _blockVisibility = block.Visibility;
            var body = SequenceDoc(block.Members, FunctionDoc, (_, _) => Air.User, block.Span.End);
            return Doc.Of(head, Doc.From("{"), Doc.IndentOf(Doc.NewLine, body), Doc.NewLine, Doc.From("}"));
        }
        finally { _inExternBlock = false; }
    }

    /// <summary>Set while a block's functions are written: their head is the block's.</summary>
    private bool _inExternBlock;

    /// <summary>The visibility word of the block being written: a function says its own only
    /// where it differs.</summary>
    private VisibilityWord _blockVisibility;

    private static Air ModuleAir(Node previous, Node next) => (previous, next) switch
    {
        (ModulePath, _) => Air.Forced,
        (AttributeNode, _) => Air.Never,             // an attribute belongs to what follows it
        (ImportDecl, ImportDecl) => Air.Never,       // imports form one contiguous head
        // Two bodiless declarations group like interface members do: the standard library's
        // native declarations come in blocks a forced blank would tear apart.
        (FunctionDecl { Body: null }, FunctionDecl { Body: null }) => Air.User,
        (Decl, Decl) => Air.Forced,
        _ => Air.User,
    };

    private Doc DeclDoc(Decl decl) => Doc.Of(decl switch
    {
        StaticBindingDecl d => Attributes(d.Attributes),
        InterfaceDecl d => Attributes(d.Attributes),
        ExtendDecl d => Attributes(d.Attributes),
        AssociatedTypeDecl d => Attributes(d.Attributes),
        GlobalBindingDecl d => Attributes(d.Attributes),
        TypeAliasDecl d => Attributes(d.Attributes),
        _ => Doc.Nil,
    }, DeclBodyDoc(decl));

    private Doc DeclBodyDoc(Decl decl) => decl switch
    {
        ImportDecl d => ImportDoc(d),
        FunctionDecl d => FunctionDoc(d),
        StructDecl d => TypeBodyDoc(Attributes(d.Attributes), d.Visibility, "struct", d.Name,
            d.Generics, d.Interfaces, d.Members, d.Span, d.Delegates),
        ClassDecl d => TypeBodyDoc(Attributes(d.Attributes), d.Visibility, "class", d.Name,
            d.Generics, d.Interfaces, d.Members, d.Span, d.Delegates),
        EnumDecl d => EnumDoc(d),
        InterfaceDecl d => MethodBodyDoc(
            Doc.Of(Pub(d.Visibility), Doc.From($"{(d.IsSealed ? "sealed " : "")}interface {d.Name}"), GenericsDoc(d.Generics),
                InterfaceListDoc(d.Interfaces), Doc.Space),
            InOrder(d.Types, d.Members, d.Statics), d.Span),
        ExtendDecl d => MethodBodyDoc(
            Doc.Of(Pub(d.Visibility), Doc.From("extend"), GenericsDoc(d.Generics), Doc.Space, TypeDoc(d.Target),
                InterfaceListDoc(d.Interfaces), Doc.Space),
            InOrder(d.Types, d.Methods, d.Statics), d.Span),
        GlobalBindingDecl d => Doc.Of(Pub(d.Visibility), StmtDoc(d.Binding)),
        StaticBindingDecl d => Doc.Of(Pub(d.Visibility), Doc.From("static "), StmtDoc(d.Binding)),
        TypeAliasDecl d => Doc.Of(Pub(d.Visibility),
            Doc.From($"{(d.IsOpaque ? "opaque " : "")}type {d.Name}"), GenericsDoc(d.Generics), Doc.From(" = "),
            TypeDoc(d.Aliased), Doc.From(";")),
        // 'type Item;' declares, 'type Item = int;' answers or defaults; 'type Iter ::
        // [Iterator];' bounds.
        AssociatedTypeDecl d => Doc.Of(Doc.From($"type {d.Name}"),
            d.Bounds.Length == 0 ? Doc.Nil
                : Doc.Of(Doc.From(" :: ["), Doc.Join(Doc.From(", "), d.Bounds.Select(TypeDoc).ToArray()), Doc.From("]")),
            d.Type is { } bound ? Doc.Of(Doc.From(" = "), TypeDoc(bound)) : Doc.Nil, Doc.From(";")),
        FieldDecl d => FieldDoc(d),
        _ => throw new InternalCompilationException($"unreachable: unformatted {decl.GetType().Name}"),
    };

    private static Doc Pub(VisibilityWord word) => word switch
    {
        VisibilityWord.Pub => Doc.From("pub "),
        VisibilityWord.Internal => Doc.From("internal "),
        VisibilityWord.Private => Doc.From("private "),
        _ => Doc.Nil,
    };

    private Doc ImportDoc(ImportDecl decl)
    {
        var path = (decl.IsPublic ? "pub " : "") + "import " + string.Join(".", decl.Path);
        return decl.Clause switch
        {
            null => Doc.From($"{path};"),
            ImportAlias a => Doc.From($"{path} as {a.Alias};"),
            ImportSelective s => Doc.GroupOf(
                Doc.From($"{path} {{"),
                Doc.IndentOf(Doc.LineOrSpace,
                    Doc.Join(Doc.Of(Doc.From(","), Doc.LineOrSpace),
                        s.Names.Select((name, i) => Doc.From(s.BoundName(i) == name ? name : $"{name} as {s.BoundName(i)}")).ToArray()),
                    Doc.WhenBroken(Doc.From(","))),
                Doc.LineOrSpace, Doc.From("};")),
            _ => throw new InternalCompilationException("unreachable: unknown import clause"),
        };
    }

    private Doc AttributeDoc(AttributeNode attribute) =>
        Doc.Of(Doc.From("@"), AttributeEntryDoc(attribute));

    private Doc AttributeEntryDoc(AttributeNode attribute)
    {
        var name = Doc.From(string.Join(".", attribute.Path));
        if (attribute.Positional is { } positional)
            return Doc.Of(name, Doc.From("("), ExprDoc(positional, Assign), Doc.From(")"));
        if (attribute.Fields.Length == 0) return name;

        return Doc.GroupOf(name, Doc.From(" {"),
            Doc.IndentOf(Doc.LineOrSpace,
                Doc.Join(Doc.Of(Doc.From(","), Doc.LineOrSpace),
                    attribute.Fields.Select(InitFieldDoc).ToArray()),
                Doc.WhenBroken(Doc.From(","))),
            Doc.LineOrSpace, Doc.From("}"));
    }

    private Doc Attributes(AttributeNode[] attributes)
    {
        if (attributes.Length == 0) return Doc.Nil;
        if (attributes.Length == 1)
            return Doc.Of(AttributeDoc(attributes[0]), Doc.NewLine);
        // Lyric 5 writes each attribute on a line of its own, as its standard library does.
        if (_grouped is not null)
            return new Doc.Concat([.. attributes.Select(a => Doc.Of(AttributeDoc(a), Doc.NewLine))]);

        // Two or more attributes take the GROUP shape — one spelling in the formatted corpus,
        // however the source wrote them. The parser flattens both spellings to the same list,
        // which is what keeps this rewrite reparse-identical.
        return Doc.Of(Doc.GroupOf(Doc.From("@["),
                Doc.IndentOf(Doc.LineOrNothing,
                    Doc.Join(Doc.Of(Doc.From(","), Doc.LineOrSpace),
                        attributes.Select(AttributeEntryDoc).ToArray()),
                    Doc.WhenBroken(Doc.From(","))),
                Doc.LineOrNothing, Doc.From("]")),
            Doc.NewLine);
    }

    private Doc FunctionDoc(FunctionDecl decl)
    {
        var head = new List<Doc>
        {
            Attributes(decl.Attributes),
            _inExternBlock && decl.Visibility == _blockVisibility ? Doc.Nil : Pub(decl.Visibility),
            decl.Extern is { } abi && !_inExternBlock
                ? Doc.Of(Doc.From("extern "), _synthesized ? Doc.From(Quote(abi.Abi)) : Src(abi.AbiSpan), Doc.Space) : Doc.Nil,
            decl.IsStatic ? Doc.From("static ") : Doc.Nil,
            decl.IsInline ? Doc.From("inline ") : Doc.Nil,
            decl.IsMut ? Doc.From("mut ") : Doc.Nil,
            Doc.Of(Doc.From($"{(decl.IsMacro ? "macro" : "fn")} "), NameDoc(decl.Name)),
            GenericsDoc(decl.Generics),
            ParamListDoc(decl.Parameters, decl.CVariadic is not null),
        };

        if (decl.ReturnType is { } ret) head.Add(Doc.Of(Doc.From(": "), ReturnDoc(ret, decl.Throws, decl.ReturnGrouped)));
        if (decl.Throws is { } throws) head.Add(ThrowsDoc(throws));

        if (decl.Extern is { Symbol: { } symbolText } spec)
            head.Add(Doc.Of(Doc.From(" = "), _synthesized || spec.SymbolSpan is null ? Doc.From(Quote(symbolText)) : Src(spec.SymbolSpan.Value)));

        head.Add(decl.Body is { } body ? Doc.Of(Doc.Space, BlockDoc(body)) : Doc.From(";"));
        return new Doc.Concat(head);
    }

    /// <summary>The splices of the quotes around what is being formatted, innermost last.</summary>
    private readonly List<Expr[]> _quoteSplices = new();

    /// <summary>A declared name — or, inside a quote, the splice <c>#{e}</c> that stands for it,
    /// which the parser keeps as <c>#</c> and the splice's index.</summary>
    private Doc NameDoc(string name)
    {
        if (name.Length > 1 && name[0] == '#' && int.TryParse(name.AsSpan(1), out var index)
            && _quoteSplices.Count > 0 && index < _quoteSplices[^1].Length)
            return Doc.Of(Doc.From("#{"), ExprDoc(_quoteSplices[^1][index], Assign), Doc.From("}"));
        return Doc.From(name);
    }

    private Doc GenericsDoc(GenericParam[] generics)
    {
        if (generics.Length == 0) return Doc.Nil;

        var parts = generics.Select(g => Doc.Of(g.Constraints.Length == 0
            ? Doc.From(g.Name)
            : Doc.Of(Doc.From($"{g.Name} :: ["),
                Doc.Join(Doc.From(", "), g.Constraints.Select(TypeDoc).ToArray()),
                Doc.From("]")),
            g.Default is { } fallback ? Doc.Of(Doc.From(" = "), TypeDoc(fallback)) : Doc.Nil));
        return Doc.Of(Doc.From("<"), Doc.Join(Doc.From(", "), parts.ToArray()), Doc.From(">"));
    }

    /// <param name="cVariadic">Whether C's variadic arguments follow, a bare <c>...</c>.</param>
    private Doc ParamListDoc(Param[] parameters, bool cVariadic = false)
    {
        if (parameters.Length == 0 && !cVariadic) return Doc.From("()");

        // No trailing comma: the parameter grammar does not allow one.
        var items = parameters.Select(ParamDoc);
        if (cVariadic) items = items.Append(Doc.From("..."));
        return Doc.GroupOf(Doc.From("("),
            Doc.IndentOf(Doc.LineOrNothing,
                Doc.Join(Doc.Of(Doc.From(","), Doc.LineOrSpace), items.ToArray())),
            Doc.LineOrNothing, Doc.From(")"));
    }

    private Doc ParamDoc(Param parameter)
    {
        var parts = new List<Doc>();
        // '@callerExpr(actual)' stands before the name, on the parameter's line.
        foreach (var attribute in parameter.Attributes) parts.Add(Doc.Of(AttributeDoc(attribute), Doc.Space));
        if (parameter.IsParams && !parameter.IsEllipsis) parts.Add(Doc.From("params "));
        if (parameter.IsPlace) parts.Add(Doc.From("&"));
        parts.Add(Doc.From($"{parameter.Name}: "));
        if (parameter is { IsEllipsis: true, Type: ArrayType variadic })
            parts.Add(Doc.Of(TypeDoc(variadic.Element), Doc.From("...")));
        else
            parts.Add(TypeDoc(parameter.Type));
        if (parameter.Default is { } fallback)
        {
            parts.Add(Doc.From(" = "));
            parts.Add(ExprDoc(fallback, Assign));
        }

        return new Doc.Concat(parts);
    }

    private Doc TypeBodyDoc(Doc attributes, VisibilityWord isPublic, string keyword, string name,
        GenericParam[] generics, TypeNode[] interfaces, Decl[] members, Span whole, string?[]? delegates = null)
    {
        var head = Doc.Of(attributes, Pub(isPublic), Doc.From($"{keyword} {name}"),
            GenericsDoc(generics), InterfaceListDoc(interfaces, delegates), Doc.Space);

        var closing = whole.End - 1;
        return BracedDoc(head, members.Length == 0 && !AnyCommentBefore(closing),
            () => SequenceDoc(members, MemberDoc, MemberAir, closing));
    }

    private static Air MemberAir(Decl previous, Decl next) =>
        HasBody(previous) || HasBody(next) ? Air.Forced : Air.User;

    private static bool HasBody(Decl member) => member is FunctionDecl { Body: not null };

    private Doc MemberDoc(Decl member) => member switch
    {
        // The comma belongs to the FIELD: a self-closing member may omit it, and this formatter
        // takes the permission.
        FieldDecl d => Doc.Of(FieldDoc(d), Doc.From(",")),
        _ => DeclDoc(member),
    };

    private Doc FieldDoc(FieldDecl decl)
    {
        var parts = new List<Doc> { Attributes(decl.Attributes), Pub(decl.Visibility), Doc.From($"{(decl.IsVar ? "var " : "")}{decl.Name}: "), TypeDoc(decl.Type) };
        if (decl.Default is { } fallback)
        {
            parts.Add(Doc.From(" = "));
            parts.Add(ExprDoc(fallback, Assign));
        }

        return new Doc.Concat(parts);
    }

    /// <summary>A declaration's interfaces; too long for the line, one to a line like parameters.</summary>
    /// <param name="delegates">The field an entry delegates to (<c>Walker by legs</c>).</param>
    private Doc InterfaceListDoc(TypeNode[] interfaces, string?[]? delegates = null) =>
        interfaces.Length == 0
            ? Doc.Nil
            : Doc.Of(Doc.From(" :: "), Doc.GroupOf(Doc.From("["),
                Doc.IndentOf(Doc.LineOrNothing,
                    Doc.Join(Doc.Of(Doc.From(","), Doc.LineOrSpace), interfaces.Select((t, i) =>
                        delegates is not null && i < delegates.Length && delegates[i] is { } field
                            ? Doc.Of(TypeDoc(t), Doc.From($" by {field}"))
                            : TypeDoc(t)).ToArray())),
                Doc.LineOrNothing, Doc.From("]")));

    private Doc EnumDoc(EnumDecl decl)
    {
        var head = Doc.Of(Attributes(decl.Attributes), Pub(decl.Visibility),
            Doc.From($"enum {decl.Name}"), GenericsDoc(decl.Generics),
            InterfaceListDoc(decl.Interfaces), Doc.Space);

        var members = InOrder(decl.Types, decl.Methods, decl.Statics);
        var closing = decl.Span.End - 1;
        if (decl.Variants.Length == 0 && members.Length == 0 && !AnyCommentBefore(closing))
            return Doc.Of(head, Doc.From("{ }"));

        return BracedDoc(head, empty: false, () =>
        {
            var parts = new List<Doc>();

            // The variants own their line up to its end, so a trailing comment stays theirs;
            // a comment on a later line already belongs to the methods below.
            var index = 0;
            var variantsEnd = decl.Variants.Length == 0
                ? closing
                : LineEndOf(decl.Variants[^1].Span.End);
            if (members.Length == 0) variantsEnd = closing;

            parts.Add(SequenceDoc(decl.Variants, Variant, (_, _) => Air.User, variantsEnd));

            if (members.Length > 0)
            {
                parts.Add(Doc.NewLine);
                parts.Add(Doc.NewLine);
                _lastEnd = variantsEnd;
                parts.Add(SequenceDoc(members, DeclDoc, MemberAir, closing));
            }

            return new Doc.Concat(parts);

            // The ';' parts the variants from the methods; without methods every variant ends
            // in ',' — the trailing one is the grammar's own permission.
            Doc Variant(EnumVariant variant)
            {
                var last = ++index == decl.Variants.Length;
                return Doc.Of(VariantDoc(variant),
                    Doc.From(last && members.Length > 0 ? ";" : ","));
            }
        });
    }

    /// <summary>The offset just past the last character of the line <paramref name="offset"/>
    /// lies on — before the newline itself.</summary>
    private int LineEndOf(int offset)
    {
        var line = LineOf(offset);
        return line + 1 < _lineStarts.Length ? _lineStarts[line + 1] - 1 : _source.Length;
    }

    private Doc VariantDoc(EnumVariant variant) => Doc.Of(Attributes(variant.Attributes), VariantBodyDoc(variant));

    private Doc VariantBodyDoc(EnumVariant variant)
    {
        if (variant.TupleFields is { } tuple)
            return Doc.Of(Doc.From(variant.Name), Doc.From("("),
                Doc.Join(Doc.From(", "), tuple.Select(TypeDoc).ToArray()), Doc.From(")"));

        if (variant.StructFields is { } fields)
            return Doc.GroupOf(Doc.From(variant.Name + " {"),
                Doc.IndentOf(Doc.LineOrSpace,
                    Doc.Join(Doc.Of(Doc.From(","), Doc.LineOrSpace), fields.Select(FieldDoc).ToArray()),
                    Doc.WhenBroken(Doc.From(","))),
                Doc.LineOrSpace, Doc.From("}"));

        return Doc.From(variant.Name);
    }

    /// <summary>A body holding methods and associated types — interface, extend and the enum's
    /// second half share the shape. The two lists are parsed apart; the source order is theirs.</summary>
    /// <summary>A body's members as the source wrote them: the lists the parser keeps apart —
    /// associated types, constants, methods — merged back by position.</summary>
    private static Decl[] InOrder(AssociatedTypeDecl[] types, FunctionDecl[] methods, StaticBindingDecl[]? statics = null) =>
        types.Length == 0 && (statics?.Length ?? 0) == 0
            ? methods
            : types.Cast<Decl>().Concat(statics ?? []).Concat(methods).OrderBy(m => m.Span.Start).ToArray();

    private Doc MethodBodyDoc(Doc head, Decl[] members, Span whole)
    {
        var closing = whole.End - 1;
        return BracedDoc(head, members.Length == 0 && !AnyCommentBefore(closing),
            () => SequenceDoc(members, DeclDoc, MemberAir, closing));
    }

    // ------------------------------------------------------------------ statements

    /// <summary>The label before a loop, 'outer: ', or nothing.</summary>
    private static Doc LabelDoc(string? label) => Doc.From(label is null ? "" : label + ": ");

    /// <summary>The labels of the loops around what is being formatted, innermost last: a 'break'
    /// value that is a bare name of one is parenthesized, or it would read back as the label.</summary>
    private readonly List<string> _loopLabels = new();

    /// <summary>A loop body formatted with its label in scope.</summary>
    private Doc Labeled(string? label, Func<Doc> body)
    {
        if (label is null) return body();
        _loopLabels.Add(label);
        try { return body(); }
        finally { _loopLabels.RemoveAt(_loopLabels.Count - 1); }
    }

    /// <summary>'break', its label, its value.</summary>
    private Doc BreakDoc(BreakStmt b)
    {
        var parts = new List<Doc> { Doc.From(b.Label is null ? "break" : $"break {b.Label}") };
        if (b.Value is { } value)
        {
            parts.Add(Doc.Space);
            parts.Add(value is IdentifierExpr { Name: var name } && _loopLabels.Contains(name) && WrittenParentheses(value) == 0
                ? Doc.Of(Doc.From("("), ExprDoc(value, Assign), Doc.From(")"))
                : ExprDoc(value, Assign));
        }
        parts.Add(Doc.From(";"));
        return new Doc.Concat(parts);
    }

    private Doc StmtDoc(Stmt stmt) => stmt switch
    {
        Block b => BlockDoc(b),
        BindingStmt b => BindingDoc(b),
        DestructuringStmt d => DestructuringDoc(d),
        LetPatternStmt lp => LetPatternDoc(lp),
        IfStmt s => IfStmtDoc(s),
        WhileStmt s => Doc.Of(LabelDoc(s.Label), Doc.From("while ("), ExprDoc(s.Condition, Assign),
            Doc.From(") "), Labeled(s.Label, () => BlockDoc(s.Body))),
        DoWhileStmt s => Doc.Of(LabelDoc(s.Label), Doc.From("do "), Labeled(s.Label, () => BlockDoc(s.Body)),
            Doc.From(" while ("), ExprDoc(s.Condition, Assign), Doc.From(");")),
        // Both halves: the head may be a PATTERN ('for ((k, v) in …)') and the loop may carry a
        // LABEL. Written as one, neither is lost when the other is present.
        ForInStmt s => Doc.Of(LabelDoc(s.Label), Doc.From(s.Comptime ? "comptime for (" : "for ("),
            s.Pattern is { } loopPattern ? PatternDoc(loopPattern) : Doc.From(s.Variable),
            Doc.From(" in "), ExprDoc(Site(s.Iterable, s.Comptime), Assign), Doc.From(") "), Labeled(s.Label, () => BlockDoc(s.Body))),
        BreakStmt b => BreakDoc(b),
        ContinueStmt c => Doc.From(c.Label is null ? "continue;" : $"continue {c.Label};"),
        ReturnStmt s => s.Value is null
            ? Doc.From("return;")
            : Doc.Of(Doc.From("return "), ExprDoc(s.Value, Assign), Doc.From(";")),
        YieldStmt s => s.Value is null
            ? Doc.From("yield;")
            : Doc.Of(Doc.From("yield "), ExprDoc(s.Value, Assign), Doc.From(";")),
        DeferStmt s => Doc.Of(Doc.From("defer "), StmtDoc(s.Body)),
        ThrowStmt s => Doc.Of(Doc.From("throw "), ExprDoc(s.Value, Assign), Doc.From(";")),
        MatchStmt s => MatchDoc(s.Scrutinee, s.Arms, s.Span),
        TryStmt s => TryDoc(s),
        ExprStmt { Expr: CallExpr { Arguments: [.., LambdaExpr { Form: LambdaForm.Trailing }] } } s => ExprDoc(s.Expr, Assign),
        // a splice standing as a statement ends at its brace, as the parser reads it
        ExprStmt { Expr: SpliceExpr } s => ExprDoc(s.Expr, Assign),
        // A loop is a statement of its own: no ';' after its block.
        ExprStmt { Expr: LoopExpr } s => ExprDoc(s.Expr, Assign),
        ExprStmt s => Doc.Of(ExprDoc(s.Expr, Assign), Doc.From(";")),
        TailExprStmt t => ExprDoc(t.Expr, Assign), // the tail: no ';', that is what makes it one
        _ => throw new InternalCompilationException($"unreachable: unformatted {stmt.GetType().Name}"),
    };

    /// <summary>What a <c>comptime</c> statement wrote in its head: the parser wraps it in the
    /// <c>comptime</c> site the keyword before the statement stands for.</summary>
    private static Expr Site(Expr expr, bool comptime) => comptime && expr is ComptimeExpr c ? c.Inner : expr;

    private Doc BlockDoc(Block block)
    {
        var closing = block.Span.End - 1;
        return BracedDoc(block.Unsafe is null ? Doc.Nil : Doc.From("unsafe "),
            block.Statements.Length == 0 && !AnyCommentBefore(closing),
            () => SequenceDoc(block.Statements, StmtDoc, (_, _) => Air.User, closing));
    }

    private Doc BindingDoc(BindingStmt binding)
    {
        if (binding is { LocalFunction: true, Initializer: LambdaExpr fn, Type: FunctionType fnType } && fn.Body is Block body)
        {
            var head = new List<Doc>
            {
                Doc.From($"fn {binding.Name}("),
                Doc.Join(Doc.From(", "), fn.Parameters.Select(p => p.Type is { } t
                    ? Doc.Of(Doc.From((p.IsPlace ? "&" : "") + p.Name + ": "), TypeDoc(t))
                    : Doc.From(p.Name)).ToArray()),
                Doc.From(")"),
            };
            if (fn.ReturnType is { } ret) head.Add(Doc.Of(Doc.From(": "), ReturnDoc(ret, fnType.Throws, false)));
            if (fnType.Throws is { } throws) head.Add(ThrowsDoc(throws));
            head.Add(Doc.Space);
            head.Add(BlockDoc(body));
            return new Doc.Concat(head);
        }

        var keyword = binding.Cleanup is not null ? "using let " : binding.IsMutable ? "var " : "let ";
        var parts = new List<Doc> { Doc.From(keyword), Doc.From(binding.Name) };
        if (binding.Type is { } type)
        {
            parts.Add(Doc.From(": "));
            parts.Add(TypeDoc(type));
        }

        if (binding.Initializer is { } init)
        {
            parts.Add(Doc.From(" = "));
            parts.Add(ExprDoc(init, Assign));
        }

        parts.Add(Doc.From(";"));
        return new Doc.Concat(parts);
    }

    private Doc LetPatternDoc(LetPatternStmt stmt)
    {
        var parts = new List<Doc>
        {
            Doc.From(stmt.IsMutable ? "var " : "let "), PatternDoc(stmt.Pattern),
        };
        if (stmt.Type is { } type)
        {
            parts.Add(Doc.From(": "));
            parts.Add(TypeDoc(type));
        }

        parts.Add(Doc.From(" = "));
        parts.Add(ExprDoc(stmt.Initializer, Assign));
        if (stmt.Else is { } els)
        {
            parts.Add(Doc.From(" else "));
            parts.Add(BlockDoc(els));
        }
        parts.Add(Doc.From(";"));
        return new Doc.Concat(parts);
    }

    private Doc DestructuringDoc(DestructuringStmt stmt)
    {
        var parts = new List<Doc>
        {
            Doc.From(stmt.IsMutable ? "var " : "let "), PatternDoc(stmt.Pattern),
        };
        if (stmt.Type is { } type)
        {
            parts.Add(Doc.From(": "));
            parts.Add(TypeDoc(type));
        }

        parts.Add(Doc.From(" = "));
        parts.Add(ExprDoc(stmt.Initializer, Assign));
        parts.Add(Doc.From(";"));
        return new Doc.Concat(parts);
    }

    private Doc IfStmtDoc(IfStmt stmt, bool inLadder = false)
    {
        var parts = new List<Doc>
        {
            Doc.From(stmt.Comptime && !inLadder ? "comptime if (" : "if ("), ExprDoc(Site(stmt.Condition, stmt.Comptime), Assign),
            Doc.From(") "), BlockDoc(stmt.Then),
        };

        if (stmt.Else is { } tail)
        {
            parts.Add(Doc.From(" else "));
            // a Block, or the next IfStmt of an else-if ladder
            parts.Add(tail is IfStmt next && stmt.Comptime ? IfStmtDoc(next, inLadder: true) : StmtDoc(tail));
        }

        return new Doc.Concat(parts);
    }

    private Doc TryDoc(TryStmt stmt)
    {
        var parts = new List<Doc> { Doc.From("try "), BlockDoc(stmt.Body) };
        foreach (var clause in stmt.Catches)
            parts.Add(Doc.Of(Doc.From(" catch ("), CatchBindingDoc(clause), Doc.From(") "), BlockDoc(clause.Body)));

        return new Doc.Concat(parts);
    }

    // '_' is "no name", never "no type": 'catch (_: Boom)' selects by its type without binding, and
    // dropping the annotation would turn it into the catch-all.
    private Doc CatchBindingDoc(CatchClause clause)
    {
        var name = clause.BindingName ?? "_";
        // The set form: one type alone, several in brackets (the list rule).
        if (clause.BindingTypes is [var only]) return Doc.Of(Doc.From($"{name} in "), TypeDoc(only));
        if (clause.BindingTypes.Length > 1)
            return Doc.Of(Doc.From($"{name} in ["),
                Doc.Join(Doc.From(", "), clause.BindingTypes.Select(TypeDoc).ToArray()), Doc.From("]"));
        return clause.BindingType is null
            ? Doc.From(name)
            : Doc.Of(Doc.From($"{name}: "), TypeDoc(clause.BindingType));
    }

    /// <summary><c>try e</c>, <c>try? e</c>, <c>try! e</c> and the expression form with its clauses.
    /// A clause binds to the nearest <c>try</c> on its left, so a <c>try</c> standing as
    /// the operand of one with clauses, or as the body of a clause another follows, keeps its
    /// parentheses — without them the reparse hands it the clauses.</summary>
    private Doc TryExprDoc(TryExpr t)
    {
        var keyword = t.Kind switch { TryKind.Optional => "try? ", TryKind.Force => "try! ", _ => "try " };
        var parts = new List<Doc>
        {
            Doc.From(keyword),
            // A mark to the right of an operator is a prefix of its operand: parentheses
            // around more than that would be another program.
            ExprDoc(t.Value, t.Reach is not null ? Prefix : t.Catches.Length > 0 && t.Value is TryExpr ? Primary : Assign),
        };
        for (var i = 0; i < t.Catches.Length; i++)
        {
            var clause = t.Catches[i];
            var body = clause.ExpressionBody && clause.Body.Tail is { } tail
                ? ExprDoc(tail.Expr, i < t.Catches.Length - 1 && tail.Expr is TryExpr ? Primary : Assign)
                : BlockDoc(clause.Body);
            parts.Add(Doc.Of(Doc.From(" catch ("), CatchBindingDoc(clause), Doc.From(") "), body));
        }
        return new Doc.Concat(parts);
    }

    private Doc MatchDoc(Expr scrutinee, MatchArm[] arms, Span whole)
    {
        var head = Doc.Of(Doc.From("match ("), ExprDoc(scrutinee, Assign), Doc.From(") "));
        var closing = whole.End - 1;
        return BracedDoc(head, arms.Length == 0 && !AnyCommentBefore(closing),
            () => SequenceDoc(arms, ArmDoc, (_, _) => Air.User, closing));
    }

    private Doc ArmDoc(MatchArm arm)
    {
        var line = new List<Doc> { PatternDoc(arm.Pattern) };
        if (arm.Guard is { } guard)
        {
            line.Add(Doc.From(" if "));
            line.Add(ExprDoc(guard, Assign));
        }

        line.Add(Doc.From(" => "));
        if (arm.Body is Block block)
        {
            line.Add(BlockDoc(block)); // a block arm closes itself; no comma
        }
        else
        {
            line.Add(ExprDoc((Expr)arm.Body, Assign));
            line.Add(Doc.From(","));
        }

        return new Doc.Concat(line);
    }

    // ------------------------------------------------------------------ expressions

    // The levels of Grammar.md §6.1; smaller binds tighter. An expression printed where at most
    // 'maxLevel' is allowed gets parentheses when its own level exceeds it.
    private const int Primary = 0;
    private const int Postfix = 1;
    private const int Prefix = 2;
    private const int CastLevel = 3;
    private const int Range = 7;
    private const int Assign = 16;

    private static (string Symbol, int Level) BinaryInfo(BinaryOp op) => op switch
    {
        BinaryOp.Mul => ("*", 4),
        BinaryOp.Div => ("/", 4),
        BinaryOp.Rem => ("%", 4),
        BinaryOp.MulWrap => ("*%", 4),
        BinaryOp.Add => ("+", 5),
        BinaryOp.Sub => ("-", 5),
        BinaryOp.AddWrap => ("+%", 5),
        BinaryOp.SubWrap => ("-%", 5),
        BinaryOp.Shl => ("<<", 6),
        BinaryOp.Shr => (">>", 6),
        BinaryOp.BitAnd => ("&", 8),
        BinaryOp.BitXor => ("^", 9),
        BinaryOp.BitOr => ("|", 10),
        BinaryOp.Lt => ("<", 11),
        BinaryOp.Le => ("<=", 11),
        BinaryOp.Gt => (">", 11),
        BinaryOp.Ge => (">=", 11),
        BinaryOp.In => ("in", 11),
        BinaryOp.NotIn => ("!in", 11),
        BinaryOp.Eq => ("==", 12),
        BinaryOp.Ne => ("!=", 12),
        BinaryOp.LogicalAnd => ("&&", 13),
        BinaryOp.LogicalOr => ("||", 14),
        BinaryOp.Coalesce => ("??", 15),
        _ => throw new InternalCompilationException($"unreachable: unexpected {op}"),
    };

    private static int LevelOf(Expr expr) => expr switch
    {
        BinaryExpr b => BinaryInfo(b.Operator).Level,
        UnaryExpr or ComptimeExpr or ThrowExpr => Prefix,
        PostfixExpr or CallExpr or IndexExpr or MemberExpr => Postfix,
        CastExpr => CastLevel,
        TypeTestExpr => BinaryInfo(BinaryOp.Lt).Level,
        RangeExpr => Range,
        WithExpr => Postfix,
        AssignExpr => Assign,
        // An if or a lambda extends to the end of the expression: as an operand it must be
        // parenthesized or the reparse reads past the operator. Treated as the loosest level.
        // So does 'try', which covers everything to its right — but for the mark that
        // stands to the right of an operator, which is a prefix there and must stay one: in
        // parentheses it would be a mark at the start of them, and cover less.
        TryExpr { Reach: not null } => Prefix,
        IfExpr or LambdaExpr or TryExpr => Assign,
        _ => Primary,
    };

    private Doc ExprDoc(Expr expr, int maxLevel)
    {
        var doc = ExprDocInner(expr);
        if (_grouped is not null && expr is IfExpr { Then: BlockExpr, Else: BlockExpr } && WrittenParentheses(expr) == 0)
            return doc;
        if (WrittenParentheses(expr) is var written and > 0)
        {
            for (var i = 0; i < written; i++) doc = Doc.Of(Doc.From("("), doc, Doc.From(")"));
            return doc;
        }
        return LevelOf(expr) > maxLevel ? Doc.Of(Doc.From("("), doc, Doc.From(")")) : doc;
    }

    /// <summary>How many pairs of parentheses the source wrote around <paramref name="expr"/>: the
    /// opening ones between the outermost pair's start and the expression's.</summary>
    private int WrittenParentheses(Expr expr)
    {
        if (_grouped is null || !_grouped.TryGetValue(expr, out var outer)) return 0;
        var count = 0;
        for (var i = outer.Start; i < expr.Span.Start && i < _source.Length; i++)
            if (_source[i] == '(') count++;
        return Math.Max(count, 1);
    }

    private Doc ExprDocInner(Expr expr) => expr switch
    {
        // Spelling lives in the source, not in the node.
        IntLiteralExpr or FloatLiteralExpr or StringLiteralExpr or CharLiteralExpr
            or InterpolatedStringExpr or ByteStringExpr => _synthesized ? Doc.From(LiteralText(expr)) : Src(expr.Span),
        BoolLiteralExpr b => Doc.From(b.Value ? "true" : "false"),
        NullLiteralExpr => Doc.From("null"),
        ThisExpr => Doc.From("this"),
        LetCondExpr lc => Doc.Of(Doc.From("let "), PatternDoc(lc.Pattern), Doc.From(" = "),
            ExprDoc(lc.Initializer, Assign)),
        IdentifierExpr i => Doc.From(i.Name),
        MacroCallExpr mc => Doc.Of(Doc.From(mc.Name + "!"),
            mc.Arguments.Length > 0 || mc.Trailing is null
                ? Doc.Of(Doc.From("("), Doc.Join(Doc.From(", "), mc.Arguments.Select(a => ExprDoc(a, Assign)).ToArray()), Doc.From(")"))
                : Doc.Nil,
            mc.Trailing is { } block ? Doc.Of(Doc.Space, BlockDoc(block)) : Doc.Nil),
        AtIdentifierExpr a => AtIdentifierDoc(a),
        TypePathExpr t => Doc.Of(Doc.From(string.Join(".", t.Path)), TypeArgsDoc(t.TypeArguments)),

        UnaryExpr u => Doc.Of(Doc.From(PrefixSymbol(u.Operator)), ExprDoc(u.Operand, Prefix)),
        ComptimeExpr c => Doc.Of(Doc.From("comptime "), ExprDoc(c.Inner, Prefix)),
        QuoteExpr q => Quoted(q, () => Doc.Of(Doc.From("quote "), q.Declarations
            ? BracedDoc(Doc.Nil, q.Content.Length == 0, () => SequenceDoc(q.Content.Cast<Decl>().ToArray(), DeclDoc, (_, _) => Air.Forced, q.Span.End - 1))
            : BlockDoc(new Block(q.Content.Cast<Stmt>().ToArray(), q.Span)))),
        SpliceExpr sp => Doc.Of(Doc.From("#{"), ExprDoc(sp.Inner, Assign), Doc.From("}")),
        ThrowExpr t => Doc.Of(Doc.From("throw "), ExprDoc(t.Value, Prefix)),
        TryExpr t => TryExprDoc(t),
        PostfixExpr p => Doc.Of(ExprDoc(p.Operand, Postfix), Doc.From(PostfixSymbol(p.Operator))),
        BinaryExpr b => BinaryDoc(b),
        AssignExpr a => AssignDoc(a),
        RangeExpr r => Doc.Of(ExprDoc(r.Low, Range - 1),
            Doc.From(r.IsInclusive ? "..=" : ".."), ExprDoc(r.High, Range - 1)),
        SliceRangeExpr sr => Doc.Of(sr.Low is null ? Doc.Nil : ExprDoc(sr.Low, Range - 1),
            Doc.From(sr.IsInclusive ? "..=" : ".."), sr.High is null ? Doc.Nil : ExprDoc(sr.High, Range - 1)),
        CastExpr c => Doc.Of(ExprDoc(c.Operand, CastLevel), Doc.From(" as "), TypeDoc(c.Type)),
        TypeTestExpr t => Doc.Of(ExprDoc(t.Operand, BinaryInfo(BinaryOp.Lt).Level), Doc.From(" is "), TypeDoc(t.Type)),

        CallExpr c => CallDoc(c),
        IndexExpr i => Doc.Of(ExprDoc(i.Target, Postfix), Doc.From("["),
            ExprDoc(i.Index, Assign), Doc.From("]")),
        ImplicitMemberExpr im => Doc.From("." + im.Name),
        MemberExpr m => Doc.Of(ExprDoc(m.Target, Postfix),
            Doc.From(m.IsOptional ? "?." : "."), Doc.From(m.Member)),

        ArrayLitExpr a => ArrayDoc(a),
        TupleLitExpr t => Doc.GroupOf(Doc.From("("),
            Doc.IndentOf(Doc.LineOrNothing,
                Doc.Join(Doc.Of(Doc.From(","), Doc.LineOrSpace),
                    t.Elements.Select(e => ExprDoc(e, Assign)).ToArray())),
            Doc.LineOrNothing, Doc.From(")")),

        LambdaExpr l => LambdaDoc(l),
        LoopExpr l => Doc.Of(LabelDoc(l.Label), Doc.From("loop "), Labeled(l.Label, () => BlockDoc(l.Body))),
        // With a value block as its branch an 'if' is laid out as the statement is: '} else {'
        // on one line, an 'else if' ladder straight down.
        IfExpr { Then: BlockExpr } i => Doc.Of(Doc.From("if ("), ExprDoc(i.Condition, Assign), Doc.From(") "),
            ExprDoc(i.Then, Assign), Doc.From(" else "), ExprDoc(i.Else, Assign)),
        // Broken, its 'else' goes on a line of its own one level in, under the 'if' it belongs to.
        IfExpr i => Doc.GroupOf(Doc.From("if ("), ExprDoc(i.Condition, Assign), Doc.From(") "),
            ExprDoc(i.Then, Assign), Doc.IndentOf(Doc.LineOrSpace, Doc.From("else "), ExprDoc(i.Else, Assign))),
        BlockExpr b => BlockDoc(b.Block),
        MatchExpr m => MatchDoc(m.Scrutinee, m.Arms, m.Span),
        StructInitExpr s => StructInitDoc(s),
        WithExpr w => WithDoc(w),
        FieldByInfoExpr f => Doc.Of(ExprDoc(f.Target, Postfix), Doc.From(".["), ExprDoc(f.Info, Assign), Doc.From("]")),
        PayloadByInfoExpr p => Doc.Of(ExprDoc(p.Target, Postfix), Doc.From(".["), ExprDoc(p.Variant, Assign),
            Doc.From(", "), ExprDoc(p.Field, Assign), Doc.From("]")),
        VariantInitExpr v => InitBodyDoc(Doc.Of(ExprDoc(v.Target, Postfix), Doc.From(".["), ExprDoc(v.Variant, Assign), Doc.From("]")),
            v.Fields, v.Rounds),
        ComptimeIfExpr c => Doc.GroupOf(Doc.From("comptime if ("), ExprDoc(Site(c.Condition, true), Assign), Doc.From(") "),
            ExprDoc(c.Then, Assign), Doc.LineOrSpace, Doc.From("else "), ExprDoc(c.Else, Assign)),

        _ => throw new InternalCompilationException($"unreachable: unformatted {expr.GetType().Name}"),
    };

    private Doc Quoted(QuoteExpr quote, Func<Doc> body)
    {
        _quoteSplices.Add(quote.Splices);
        try { return body(); }
        finally { _quoteSplices.RemoveAt(_quoteSplices.Count - 1); }
    }

    private Doc AtIdentifierDoc(AtIdentifierExpr expr)
    {
        if (expr.Arguments is null) return Doc.From(expr.Name);
        return Doc.Of(Doc.From(expr.Name), Doc.From("("),
            Doc.Join(Doc.From(", "), expr.Arguments.Select(a => ExprDoc(a, Assign)).ToArray()),
            Doc.From(")"));
    }

    private static string PrefixSymbol(UnaryOp op) => op switch
    {
        UnaryOp.Not => "!",
        UnaryOp.Neg => "-",
        UnaryOp.BitNot => "~",
        UnaryOp.PreInc => "++",
        UnaryOp.PreDec => "--",
        UnaryOp.FromEnd => "^",
        UnaryOp.Place => "&",
        _ => throw new InternalCompilationException($"unreachable: unexpected {op}"),
    };

    private static string PostfixSymbol(PostfixOp op) => op switch
    {
        PostfixOp.Inc => "++",
        PostfixOp.Dec => "--",
        PostfixOp.ForceUnwrap => "!",
        _ => throw new InternalCompilationException($"unreachable: unexpected {op}"),
    };

    /// <summary>
    /// A chain of operators of one precedence level, as ONE breaking decision.
    ///
    /// <para>The chain is flattened first: <c>a &amp;&amp; b &amp;&amp; c</c> parses as
    /// <c>((a &amp;&amp; b) &amp;&amp; c)</c>, and formatting that shape as it stands would let the
    /// inner pair fit while the outer one breaks — a staircase nobody writes by hand. Flat, the
    /// operands print as they always did; broken, every operator of the level goes to the front of
    /// its own line, indented one step.</para>
    ///
    /// <para>The operator leads the continuation line rather than trailing the one before it. Both
    /// forms have a following; this one is what PEP 8, rustfmt and the .NET style default settled
    /// on, and it is the one where the eye finds what joins two operands without reading to the end
    /// of a line first.</para>
    ///
    /// <para>Only the associative side is walked, so written parentheses survive: in
    /// <c>a + (b + c)</c> the right operand is a level of its own and takes <c>level - 1</c>, which
    /// is what puts its parentheses back.</para>
    /// </summary>
    private Doc BinaryDoc(BinaryExpr expr)
    {
        var (_, level) = BinaryInfo(expr.Operator);

        // Left-associative throughout, except ?? which associates right. The tighter side takes
        // level-1, so an equal level there regains its written parentheses.
        var rightAssociative = expr.Operator == BinaryOp.Coalesce;

        var operands = new List<Doc>();
        var operators = new List<string>();
        Flatten(expr, level, rightAssociative, operands, operators);

        // An operand that breaks by itself — a match or if expression, a lambda with a block —
        // lays out over several lines whatever the width says. A group around the chain could
        // then never be flat, so every such chain would break for a reason that has nothing to do
        // with the width: 'return base * match (r) { … }' would leave its operator behind on the
        // first line. Those chains stay as they are and let the block-shaped operand break.
        if (operands.Exists(Doc.WillBreak))
        {
            var flat = new List<Doc> { operands[0] };
            for (var i = 0; i < operators.Count; i++)
            {
                flat.Add(Doc.From($" {operators[i]} "));
                flat.Add(operands[i + 1]);
            }

            return Doc.Of([.. flat]);
        }

        var tail = new List<Doc>(operators.Count * 3);
        for (var i = 0; i < operators.Count; i++)
        {
            tail.Add(Doc.LineOrSpace);
            tail.Add(Doc.From(operators[i] + " "));
            tail.Add(operands[i + 1]);
        }

        return Doc.GroupOf(operands[0], Doc.IndentOf([.. tail]));
    }

    /// <summary>Collects the operands and the operators of one precedence level, in source order.
    /// Everything that is not a binary expression of exactly this level is an operand and gets its
    /// own document, groups and all.</summary>
    private void Flatten(Expr expr, int level, bool rightAssociative,
        List<Doc> operands, List<string> operators)
    {
        // The level decides the associativity — only '??' associates right — so a matching level
        // is the whole test.
        if (expr is BinaryExpr binary && BinaryInfo(binary.Operator).Level == level)
        {
            var symbol = BinaryInfo(binary.Operator).Symbol;
            // A written group is an operand of its own, not a link of the chain.
            if (rightAssociative)
            {
                operands.Add(ExprDoc(binary.Left, level - 1));
                operators.Add(symbol);
                if (WrittenParentheses(binary.Right) > 0) operands.Add(ExprDoc(binary.Right, level));
                else Flatten(binary.Right, level, true, operands, operators);
            }
            else
            {
                if (WrittenParentheses(binary.Left) > 0) operands.Add(ExprDoc(binary.Left, level));
                else Flatten(binary.Left, level, false, operands, operators);
                operators.Add(symbol);
                operands.Add(ExprDoc(binary.Right, level - 1));
            }

            return;
        }

        // The end of the chain: the leftmost operand of a left-associative one, the rightmost of a
        // right-associative one. Either way it is the side that may carry the same level again.
        operands.Add(ExprDoc(expr, level));
    }

    private Doc AssignDoc(AssignExpr expr)
    {
        var symbol = expr.Operator is { } op ? BinaryInfo(op).Symbol + "=" : "=";
        // Right-associative: 'a = b = c' keeps its shape, a parenthesized target regains parens.
        return Doc.Of(ExprDoc(expr.Target, Assign - 1), Doc.From($" {symbol} "),
            ExprDoc(expr.Value, Assign));
    }

    private Doc CallDoc(CallExpr call)
    {
        var head = Doc.Of(ExprDoc(call.Callee, Postfix),
            TypeArgsDoc(call.TypeArguments ?? []));

        // 'xs.map { it * 2 }' / 'fold(0) { acc + it }': the trailing lambda stays outside the
        // parentheses, and the parentheses vanish when it was the only argument.
        var arguments = call.Arguments;
        Doc? trailing = null;
        if (arguments is [.., LambdaExpr { Form: LambdaForm.Trailing } last])
        {
            trailing = Doc.Of(Doc.From(" "), LambdaDoc(last));
            arguments = arguments[..^1];
            if (arguments.Length == 0) return Doc.Of(head, trailing);
        }

        if (arguments.Length == 0) return Doc.Of(head, Doc.From("()"));

        // No trailing comma: the call grammar does not allow one.
        var parenthesized = Doc.GroupOf(head, Doc.From("("),
            Doc.IndentOf(Doc.LineOrNothing,
                Doc.Join(Doc.Of(Doc.From(","), Doc.LineOrSpace),
                    arguments.Select((a, i) => ArgumentDoc(call, a, i)).ToArray())),
            Doc.LineOrNothing, Doc.From(")"));
        return trailing is null ? parenthesized : Doc.Of(parenthesized, trailing);
    }

    /// <summary>An argument as written: by name, <c>host: "h"</c>, or positional — and with the
    /// dots that spread it, <c>xs...</c>.</summary>
    private Doc ArgumentDoc(CallExpr call, Expr argument, int index)
    {
        var written = call.ArgumentNames is { } names && index < names.Length && names[index] is { } name
            ? Doc.Of(Doc.From(name + ": "), ExprDoc(argument, Assign))
            : ExprDoc(argument, Assign);
        return call.Spreads is { } spreads && Array.Exists(spreads, s => ReferenceEquals(s.Argument, argument))
            ? Doc.Of(written, Doc.From("..."))
            : written;
    }

    private Doc ArrayDoc(ArrayLitExpr array)
    {
        if (array.Elements.Length == 0) return Doc.From("[]");

        return Doc.GroupOf(Doc.From("["),
            Doc.IndentOf(Doc.LineOrNothing,
                Doc.Join(Doc.Of(Doc.From(","), Doc.LineOrSpace),
                    array.Elements.Select(e => ExprDoc(e, Assign)).ToArray()),
                Doc.WhenBroken(Doc.From(","))),
            Doc.LineOrNothing, Doc.From("]"));
    }

    private Doc LambdaDoc(LambdaExpr lambda)
    {
        if (_loopLabels.Count == 0) return LambdaBodyDoc(lambda);
        var outer = _loopLabels.ToArray();
        _loopLabels.Clear();
        try { return LambdaBodyDoc(lambda); }
        finally { _loopLabels.AddRange(outer); }
    }

    private Doc LambdaBodyDoc(LambdaExpr lambda)
    {
        // '{ acc, x => … }': the parameters stand in the block's head, before '=>'.
        if (lambda.Form == LambdaForm.Trailing && lambda.Parameters is not [{ Implicit: true }] && lambda.Body is Block withParameters)
        {
            var head = Doc.Join(Doc.From(", "), lambda.Parameters.Select(p =>
                p.Pattern is { } pattern ? PatternDoc(pattern) : Doc.From((p.IsPlace ? "&" : "") + p.Name)).ToArray());
            var closing = withParameters.Span.End - 1;
            return Doc.GroupOf(Doc.From("{ "), head, Doc.From(" =>"),
                Doc.IndentOf(Doc.LineOrSpace, SequenceDoc(withParameters.Statements, StmtDoc, (_, _) => Air.User, closing)),
                Doc.LineOrSpace, Doc.From("}"));
        }

        if (lambda.Form == LambdaForm.Trailing)
            return lambda.Body is Block trailingBlock
                ? BlockDoc(trailingBlock)
                : Doc.GroupOf(Doc.From("{"), Doc.IndentOf(Doc.LineOrSpace, ExprDoc((Expr)lambda.Body, Assign)),
                    Doc.LineOrSpace, Doc.From("}"));

        if (lambda.Form == LambdaForm.Bare)
            return Doc.Of(Doc.From($"{lambda.Parameters[0].Name} => "),
                lambda.Body is Block bareBlock ? BlockDoc(bareBlock) : ExprDoc((Expr)lambda.Body, Assign));

        var parts = new List<Doc>
        {
            Doc.From("("),
            Doc.Join(Doc.From(", "), lambda.Parameters.Select(p =>
            {
                var head = p.Pattern is { } pattern ? PatternDoc(pattern) : Doc.From((p.IsPlace ? "&" : "") + p.Name);
                return p.Type is { } type ? Doc.Of(head, Doc.From(": "), TypeDoc(type)) : head;
            }).ToArray()),
            Doc.From(")"),
        };

        if (lambda.ReturnType is { } ret)
        {
            parts.Add(Doc.From(": "));
            parts.Add(ReturnDoc(ret, lambda.Throws, lambda.ReturnGrouped));
        }
        if (lambda.Throws is { } throws) parts.Add(ThrowsDoc(throws));

        parts.Add(Doc.From(" => "));
        parts.Add(lambda.Body is Block block ? BlockDoc(block) : ExprDoc((Expr)lambda.Body, Assign));
        return new Doc.Concat(parts);
    }

    private Doc StructInitDoc(StructInitExpr init) =>
        InitBodyDoc(InitHeadDoc(init), init.Fields, init.Rounds);

    /// <summary>The path and type arguments of an initializer — behind the enum where the source
    /// wrote them there, <c>Ev&lt;int&gt;.Hit</c>, else behind the whole path.</summary>
    private Doc InitHeadDoc(StructInitExpr init)
    {
        var prefix = init.IsImplicit ? "." : "";
        if (init.TypeArguments.Length > 0 && init.Path.Length > 1 && !_synthesized
            && init.NameSpan.Start > init.Span.Start
            && _source.AsSpan(init.Span.Start, init.NameSpan.Start - init.Span.Start).Contains('<'))
            return Doc.Of(Doc.From(prefix + string.Join(".", init.Path[..^1])), TypeArgsDoc(init.TypeArguments),
                Doc.From("." + init.Path[^1]));
        return Doc.Of(Doc.From(prefix + string.Join(".", init.Path)), TypeArgsDoc(init.TypeArguments));
    }

    /// <summary>An initializer's braces: its fields and its rounds, in the order they were written.</summary>
    private Doc InitBodyDoc(Doc head, StructInitField[] fields, StructInitRound[] rounds)
    {
        if (fields.Length == 0 && rounds.Length == 0) return Doc.Of(head, Doc.From(" { }"));

        var items = fields.Select(f => (f.Span.Start, Doc: InitFieldDoc(f)))
            .Concat(rounds.Select(r => (r.Span.Start, Doc: Doc.Of(Doc.From($"comptime for ({r.Variable} in "),
                ExprDoc(Site(r.Iterable, true), Assign), Doc.From(") "), InitFieldDoc(r.Item)))))
            .OrderBy(i => i.Start).Select(i => i.Doc).ToArray();
        return Doc.GroupOf(head, Doc.From(" {"),
            Doc.IndentOf(Doc.LineOrSpace,
                Doc.Join(Doc.Of(Doc.From(","), Doc.LineOrSpace), items),
                Doc.WhenBroken(Doc.From(","))),
            Doc.LineOrSpace, Doc.From("}"));
    }

    private Doc InitFieldDoc(StructInitField field) => field switch
    {
        { Info: { } info } => Doc.Of(Doc.From("["), ExprDoc(info, Assign), Doc.From("] = "), ExprDoc(field.Value, Assign)),
        { IsShorthand: true } => Doc.From(field.Name),
        _ => Doc.Of(Doc.From($"{field.Name} = "), ExprDoc(field.Value, Assign)),
    };

    /// <summary><c>p with { x = 1, pos.y = 2 }</c>: the postfix and its fields like an initializer's.</summary>
    private Doc WithDoc(WithExpr with)
    {
        var head = Doc.Of(ExprDoc(with.Target, Postfix), Doc.From(" with"));
        if (with.Fields.Length == 0) return Doc.Of(head, Doc.From(" { }"));
        return Doc.GroupOf(head, Doc.From(" {"),
            Doc.IndentOf(Doc.LineOrSpace,
                Doc.Join(Doc.Of(Doc.From(","), Doc.LineOrSpace),
                    with.Fields.Select(f => Doc.Of(Doc.From(string.Join(".", f.Path) + " = "), ExprDoc(f.Value, Assign))).ToArray()),
                Doc.WhenBroken(Doc.From(","))),
            Doc.LineOrSpace, Doc.From("}"));
    }

    // ------------------------------------------------------------------ patterns

    private Doc PatternDoc(Pattern pattern) => pattern switch
    {
        WildcardPattern => Doc.From("_"),
        ArrayPattern a => Doc.Of(Doc.From("["),
            Doc.Join(Doc.From(", "), a.Elements.Select(PatternDoc).ToArray()), Doc.From("]")),
        RestPattern r => Doc.From(r.Name is null ? ".." : ".." + r.Name),
        LiteralPattern l => LiteralPatternDoc(l),
        BindingPattern b => Doc.From(b.Name),
        TypePattern tp => Doc.Of(Doc.From((tp.Name ?? "_") + ": "), TypeDoc(tp.Type)),
        // One type bare, several in brackets: the list rule, as on a catch clause.
        TypeSetPattern { Types: [var only] } ts => Doc.Of(Doc.From((ts.Name ?? "_") + " in "), TypeDoc(only)),
        TypeSetPattern ts => Doc.Of(Doc.From((ts.Name ?? "_") + " in ["),
            Doc.Join(Doc.From(", "), ts.Types.Select(TypeDoc).ToArray()), Doc.From("]")),
        VariantPattern v => VariantPatternDoc(v),
        TuplePattern t => Doc.Of(Doc.From("("),
            Doc.Join(Doc.From(", "), t.Elements.Select(PatternDoc).ToArray()), Doc.From(")")),
        RangePattern r => Doc.Of(ExprDoc(r.Low, Prefix),
            Doc.From(r.IsInclusive ? "..=" : ".."), ExprDoc(r.High, Prefix)),
        OrPattern o => Doc.Join(Doc.From(" | "), o.Alternatives.Select(PatternDoc).ToArray()),
        _ => throw new InternalCompilationException($"unreachable: unformatted {pattern.GetType().Name}"),
    };

    private Doc LiteralPatternDoc(LiteralPattern pattern) => pattern.Literal switch
    {
        BoolLiteralExpr b => Doc.From(b.Value ? "true" : "false"),
        NullLiteralExpr => Doc.From("null"),
        // A sign is a UnaryExpr around the literal; both spellings live in the span.
        _ => _synthesized ? Doc.From(LiteralText(pattern.Literal)) : Src(pattern.Literal.Span),
    };

    private Doc VariantPatternDoc(VariantPattern pattern)
    {
        var head = Doc.From((pattern.IsImplicit ? "." : "") + string.Join(".", pattern.Path));
        if (pattern.TupleElements is { } tuple)
            return Doc.Of(head, Doc.From("("),
                Doc.Join(Doc.From(", "), tuple.Select(PatternDoc).ToArray()), Doc.From(")"));

        if (pattern.StructFields is { } fields)
            return Doc.Of(head, Doc.From(" { "),
                Doc.Join(Doc.From(", "), fields.Select(f => f.Pattern is { } inner
                    ? Doc.Of(Doc.From($"{f.Name} = "), PatternDoc(inner))
                    : Doc.From(f.Name)).ToArray()),
                Doc.From(" }"));

        return head;
    }

    // ------------------------------------------------------------------ types

    private Doc TypeDoc(TypeNode type) => type switch
    {
        NamedType n => Doc.Of(Doc.From(string.Join(".", n.Path)), TypeArgsDoc(n.TypeArguments, n.ArgumentNames)),
        NullableType n => Doc.Of(Doc.From("?"), TypeDoc(n.Inner)),
        FieldTypeByInfo n => Doc.Of(TypeDoc(n.Owner), Doc.From(".[" + n.Info + "]")),
        ThrowingType n => n.Thrown is { } thrown
            ? Doc.Of(TypeDoc(n.Inner), Doc.From(" throws "), TypeDoc(thrown))
            : Doc.Of(TypeDoc(n.Inner), Doc.From(" throws")),
        // '(?T)[]', '(fn(..) -> R)[]' and '(Task<T> throws E)[]': without the parentheses the
        // suffix would rebind.
        ArrayType a => Doc.Of(
            a.Element is NullableType or FunctionType or ThrowingType
                ? Doc.Of(Doc.From("("), TypeDoc(a.Element), Doc.From(")"))
                : TypeDoc(a.Element),
            Doc.From(a.Length is { } len ? $"[{len}]" : "[]")),
        TupleType t => Doc.Of(Doc.From("("),
            Doc.Join(Doc.From(", "), t.Elements.Select((e, i) => t.Labels?[i] is { } label
                ? Doc.Of(Doc.From(label + ": "), TypeDoc(e)) : TypeDoc(e)).ToArray()), Doc.From(")")),
        FunctionType f => Doc.Of(Doc.From("fn("),
            Doc.Join(Doc.From(", "), f.Parameters.Select((p, i) => i < f.Places.Length && f.Places[i]
                ? Doc.Of(Doc.From("&"), TypeDoc(p)) : TypeDoc(p)).ToArray()),
            Doc.From(") -> "), ReturnDoc(f.ReturnType, f.Throws, f.ReturnGrouped), f.Throws is { } thrown ? ThrowsDoc(thrown) : Doc.Nil),
        _ => throw new InternalCompilationException($"unreachable: unformatted {type.GetType().Name}"),
    };

    /// <summary>
    /// A return type, with the parentheses that say whose a 'throws' is.
    /// They are written where the type ends in a set of its own — a function type
    /// that throws, a throwing task or coroutine: '(fn() -> int throws E)', '(Task&lt;int&gt; throws
    /// E)'; without them the set would be read as the clause of whoever returns the type. And
    /// where a set stands after a type that could have taken it: '(fn() -> int) throws E', and
    /// '(Task&lt;int&gt;) throws E' as the source wrote it (<paramref name="grouped"/> — whether a
    /// name is a task is not the formatter's to know).
    /// </summary>
    private Doc ReturnDoc(TypeNode ret, ThrowsClause? after, bool grouped) =>
        EndsInASet(ret) || (after is not null && (grouped || EndsInAFunction(ret)))
            ? Doc.Of(Doc.From("("), TypeDoc(ret), Doc.From(")"))
            : TypeDoc(ret);

    /// <summary>Whether a type's text ends in a set: its own, or — a '?' stands in front — that of
    /// what it is the optional of.</summary>
    private static bool EndsInASet(TypeNode type) => type switch
    {
        NullableType optional => EndsInASet(optional.Inner),
        FunctionType { Throws: not null } or ThrowingType => true,
        _ => false,
    };

    /// <summary>Whether a type's text ends in a function type, which takes a 'throws' behind it.</summary>
    private static bool EndsInAFunction(TypeNode type) => type switch
    {
        NullableType optional => EndsInAFunction(optional.Inner),
        FunctionType => true,
        _ => false,
    };

    /// <summary>A thrown set: one type alone, several in brackets (the list rule),
    /// none for the bare form.</summary>
    private Doc ThrowsDoc(ThrowsClause throws) => throws.Types switch
    {
        [] => Doc.From(" throws"),
        [var only] => Doc.Of(Doc.From(" throws "), TypeDoc(only)),
        var several => Doc.Of(Doc.From(" throws ["), Doc.Join(Doc.From(", "), several.Select(TypeDoc).ToArray()), Doc.From("]")),
    };

    /// <param name="names">The associated type an argument fixes, 'Iterator&lt;Item = int&gt;'.</param>
    private Doc TypeArgsDoc(TypeNode[] arguments, string?[]? names = null) =>
        arguments.Length == 0
            ? Doc.Nil
            : Doc.Of(Doc.From("<"),
                Doc.Join(Doc.From(", "), arguments.Select((a, i) =>
                    names is not null && i < names.Length && names[i] is { } fixes
                        ? Doc.Of(Doc.From($"{fixes} = "), TypeDoc(a))
                        : TypeDoc(a)).ToArray()), Doc.From(">"));

    private Doc Src(Span span) => Doc.From(_source.Substring(span.Start, span.Length));
}
