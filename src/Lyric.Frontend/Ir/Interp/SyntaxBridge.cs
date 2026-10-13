using Lyric.AST;
using Lyric.Core;
using Lyric.Parsing;

namespace Lyric.Ir.Interp;

/// <summary>
/// The syntax bridge (design/v5/spec/10 B13 M2; 13 M9b S1, P2): the compiler's tree as values of
/// <c>std.syntax</c> in the interpreter, and such values as a tree again. Type-directed in both
/// directions: a target <see cref="IrType"/> says which enum, variant or struct to build and how its
/// fields lie, so the bridge knows the NAMES of <c>std.syntax</c>'s nodes and fields and nothing of
/// their layout beyond what the module's own type definitions say. A field added to the module
/// without a line here is a compiler bug at the first use, not a silent zero.
///
/// <para>What <c>std.syntax</c> has no form for (the rounds of a <c>comptime for</c> over fields
/// and variants, the recovery nodes) is refused where it is read (<c>LYR-CT0005</c>). A node built
/// from a value stands nowhere: its spans are empty, and the formatter writes it from its values.</para>
/// </summary>
public sealed class SyntaxBridge(IrModule module, SourceManager? sources = null)
{
    /// <summary>Where the bridge parses into and locates in: the program's sources when it has
    /// them (an expansion, M9b S2), its own for a bridge of its own.</summary>
    private readonly SourceManager _sources = sources ?? new() { Lyric5 = true };
    private int _fresh;
    private readonly Dictionary<string, FileId> _fileIds = new(StringComparer.Ordinal);
    private int _indexed;
    private int _parsed;

    // ------------------------------------------------------------------ parsing and writing

    /// <summary>The expression a text holds, parsed as the compiler parses it; a parse error is a
    /// panic of the evaluation, with the parser's first message.</summary>
    public Expr ParseExpr(string text) => Parse(text, p => p.ParseExpression());

    public Stmt[] ParseStmts(string text) => Parse(text, p => p.ParseStatements());

    public Decl[] ParseDecls(string text) => Parse(text, p => p.ParseModule().Declarations);

    private T Parse<T>(string text, Func<Parser, T> read)
    {
        var id = _sources.AddVirtual($"<syntax:{++_parsed}>", text);
        var de = new DiagnosticEngine(_sources);
        var parsed = read(new Parser(_sources, id, de));
        if (de.HasErrors)
        {
            var first = de.Diagnostics.First(d => d.Severity == Severity.Error);
            var at = _sources.Locate(first.Span.File, first.Span.Start);
            throw ComptimeFailure.Panic("LYR-CT0001", $"the text is no syntax: {first.Message} (at {at.Line}:{at.Column} of the text)");
        }
        return parsed;
    }

    public string FreshName(string hint) => $"{hint}__{++_fresh}";

    public string Source(Node node) => Formatting.AstFormatter.FormatNode(node);

    public string SourceOfStatements(Value v, IrType t) => Formatting.AstFormatter.FormatStatements(Many(v, t, StmtOf));

    public string SourceOfDeclarations(Value v, IrType t) => Formatting.AstFormatter.FormatDeclarations(Many(v, t, DeclOf));

    // ------------------------------------------------------------------ the types

    private IrTypeDef Def(TypeId id) => module.Types[id.Value];

    private static TypeId IdOf(IrType type) => type switch
    {
        IrEnumType e => e.Type,
        IrStructType s => s.Type,
        IrRefType r => r.Type,
        IrPlaceType p => IdOf(p.Value),
        _ => throw new InternalCompilationException($"std.syntax: no type id in {type}"),
    };

    private static IrType Elem(IrType t) => t switch
    {
        IrArrayType a => a.Element,
        IrPlaceType p => Elem(p.Value),
        _ => throw new InternalCompilationException($"std.syntax: {t} is no array"),
    };

    private static IrType Inner(IrType t) => t switch
    {
        IrOptionalType o => o.Inner,
        IrPlaceType p => Inner(p.Value),
        _ => throw new InternalCompilationException($"std.syntax: {t} is no optional"),
    };

    /// <summary>The variant of the enum <paramref name="enumType"/> named <paramref name="name"/>:
    /// its tag and its definition.</summary>
    private (int Tag, TypeId Variant) VariantOf(IrType enumType, string name)
    {
        var def = Def(IdOf(enumType));
        for (var i = 0; i < def.Variants.Length; i++)
        {
            var vname = Def(def.Variants[i]).Name;
            if (vname.EndsWith("." + name, StringComparison.Ordinal) || vname == name) return (i, def.Variants[i]);
        }
        throw new InternalCompilationException($"std.syntax: '{def.Name}' has no variant '{name}' in this build");
    }

    private int FieldIndex(TypeId def, string name)
    {
        var d = Def(def);
        var i = System.Array.IndexOf(d.FieldNames, name);
        if (i < 0) throw new InternalCompilationException($"std.syntax: '{d.Name}' has no field '{name}' in this build");
        return i;
    }

    // ------------------------------------------------------------------ values out of a tree

    private delegate Value Make(IrType t);

    private Value Variant(IrType enumType, string name, params Make[] fields)
    {
        var (tag, variant) = VariantOf(enumType, name);
        var def = Def(variant); // a variant's layout: slot 0 the tag, then the payload (TypeTable)
        if (fields.Length != def.FieldTypes.Length - 1)
            throw new InternalCompilationException($"std.syntax: '{def.Name}' takes {def.FieldTypes.Length - 1} fields, the bridge gives {fields.Length}");
        var slots = new Value[def.FieldTypes.Length];
        slots[0] = Value.Uint((ulong)tag);
        for (var i = 0; i < fields.Length; i++) slots[i + 1] = fields[i](def.FieldTypes[i + 1]);
        return Value.Of(new AggObj(AggKind.Enum, IdOf(enumType), slots));
    }

    private Value Struct(IrType structType, params (string Name, Make Make)[] fields)
    {
        var id = IdOf(structType);
        var def = Def(id);
        if (fields.Length != def.FieldTypes.Length)
            throw new InternalCompilationException($"std.syntax: '{def.Name}' has {def.FieldTypes.Length} fields, the bridge gives {fields.Length}");
        var slots = new Value[def.FieldTypes.Length];
        for (var i = 0; i < slots.Length; i++)
        {
            var name = def.FieldNames[i];
            var found = System.Array.FindIndex(fields, f => f.Name == name);
            if (found < 0) throw new InternalCompilationException($"std.syntax: the bridge gives '{def.Name}' no '{name}'");
            slots[i] = fields[found].Make(def.FieldTypes[i]);
        }
        return Value.Of(new AggObj(AggKind.Struct, id, slots));
    }

    private static Value Str(string s) => Value.Of(StrObj.Of(s));
    private static Make S(string s) => _ => Str(s);
    private static Make B(bool b) => _ => Value.Bool(b);
    private static Make I(long i) => _ => Value.Int(i);
    private static Make U(ulong u) => _ => Value.Uint(u);

    private static Value Opt(Value? inner) => Value.Of(inner is { } v ? new OptObj(true, v) : new OptObj(false, default));

    /// <summary>An optional: <paramref name="make"/> for the inner type when there is something.</summary>
    private static Make OptOf(bool has, Make make) => t => Opt(has ? make(Inner(t)) : null);

    private static Make OptS(string? s) => OptOf(s is not null, _ => Str(s!));

    private Make Boxed(Make make) => t =>
    {
        var id = IdOf(t);
        return Value.Of(new ClassObj(id, [make(Def(id).FieldTypes[0])]));
    };

    private static Make Arr<T>(IReadOnlyList<T> items, Func<T, IrType, Value> make) => t =>
    {
        var element = Elem(t);
        return Value.Of(new ArrayObj([.. items.Select(i => make(i, element))]));
    };

    /// <summary>An array of nodes for the natives: <paramref name="t"/> is the array type.</summary>
    public Value Array<T>(IReadOnlyList<T> items, IrType t, Func<T, IrType, Value> make) => Arr(items, make)(t);

    private static Make Strings(IEnumerable<string> items) => Arr([.. items], (s, _) => Str(s));
    private static Make Bools(IEnumerable<bool> items) => Arr([.. items], (b, _) => Value.Bool(b));
    private Make Types(TypeNode[] types) => Arr(types, Type);
    private Make Exprs(Expr[] exprs) => Arr(exprs, Expr);
    private Make Attributes(AttributeNode[] attributes) => Arr(attributes, Attribute);
    private Make OptBoxedExpr(Expr? e) => OptOf(e is not null, Boxed(bt => Expr(e!, bt)));
    private Make OptExpr(Expr? e) => OptOf(e is not null, et => Expr(e!, et));
    private Make OptType(TypeNode? n) => OptOf(n is not null, tt => Type(n!, tt));
    private Make OptTypes(TypeNode[]? n) => OptOf(n is not null, Types(n ?? []));
    private Make OptBlock(Block? b) => OptOf(b is not null, bt => BlockValue(b!, bt));
    private Make OptIdent(string? name, Span span) => OptOf(name is not null, it => Ident(name!, span, it));
    private Make OptPattern(Pattern? p) => OptOf(p is not null, pt => Pattern(p!, pt));

    private Make Sp(Span span) => t =>
    {
        var (file, line, column) = Locate(span);
        return Struct(t, ("file", S(file)), ("line", I(line)), ("column", I(column)));
    };

    /// <summary>Where a span lies, as the diagnostics name it; nothing for a span of nothing, or
    /// of a file the bridge's sources do not hold.</summary>
    private (string File, int Line, int Column) Locate(Span span)
    {
        if (span == default || span.File.Value < 1 || span.File.Value > _sources.FileCount) return ("", 0, 0); // ids count from 1
        if (span.End > _sources.GetText(span.File).Length) return ("", 0, 0);
        var at = _sources.Locate(span.File, span.Start);
        return (_sources.GetPath(span.File), at.Line, at.Column);
    }

    /// <summary>The span a <c>Span</c> value names, as a span of the program: its file by the
    /// name the diagnostics use, the place by line and column; none where the program holds no
    /// such file.</summary>
    public Span SpanAt(string file, int line, int column)
    {
        // the files by their names, once each; a file added since (a synthesized one) is indexed at the next ask
        for (; _indexed < _sources.FileCount; _indexed++)
            _fileIds.TryAdd(_sources.GetPath(new FileId(_indexed + 1)), new FileId(_indexed + 1));
        if (!_fileIds.TryGetValue(file, out var id)) return default;
        var offset = _sources.OffsetAt(id, line, column);
        return offset < 0 ? default : new Span(id, offset, Math.Min(offset + 1, _sources.GetText(id).Length));
    }

    /// <summary>The span a <c>Span</c> value names, as <see cref="SpanAt"/> finds it; none for a
    /// span of nothing (<c>Span.none()</c>) or of a file the program does not hold.</summary>
    private Span? SpanOf(Value v, IrType t)
    {
        var file = StrOf(FieldOf(v, t, "file"));
        if (file.Length == 0) return null;
        var span = SpanAt(file, (int)FieldOf(v, t, "line").AsInt, (int)FieldOf(v, t, "column").AsInt);
        return span == default ? null : span;
    }

    /// <summary>The span a node value carries: a struct's <c>span</c> field, a variant's field of
    /// type <c>Span</c>; none where the value has no such field, or it names nothing.</summary>
    private Span? SpanIn(Value v, IrType t)
    {
        switch (t)
        {
            case IrStructType s:
            {
                var def = Def(s.Type);
                var i = System.Array.IndexOf(def.FieldNames, "span");
                return i >= 0 && def.FieldTypes[i] is IrStructType ? SpanOf(SlotsOf(v)[i], def.FieldTypes[i]) : null;
            }
            case IrEnumType:
            {
                var (_, f, ft) = Case(v, t);
                for (var i = 0; i < ft.Length; i++)
                    if (ft[i] is IrStructType st && Def(st.Type).Name is var name && (name == "Span" || name.EndsWith(".Span", StringComparison.Ordinal)))
                        return SpanOf(f[i], ft[i]);
                return null;
            }
            default:
                return null;
        }
    }

    /// <summary>Reads a node with its own span as the span the nodes built within it get (M9b
    /// S5): what the caller wrote keeps the place it was written at, through the macro and back;
    /// a node the macro built with <c>Span.none()</c> stands where the node around it stands, and
    /// the outermost such at the site.</summary>
    private T Within<T>(Value v, IrType t, Func<Value, IrType, T> read)
    {
        var saved = _current;
        if (SpanIn(v, t) is { } own) _current = own;
        try { return read(v, t); }
        finally { _current = saved; }
    }

    /// <summary>The failure <c>error(at, msg)</c> raises (13 M9b P7).</summary>
    public ComptimeFailure MacroError(string file, int line, int column, string message) => ComptimeFailure.Macro(SpanAt(file, line, column), message);

    /// <summary>The name <paramref name="name"/> as an <c>Ident</c> value, for a macro's parameter.</summary>
    public Value IdentValue(string name, Span span, IrType t) => Ident(name, span, t);

    /// <summary>A literal expression as a <c>Literal</c> value, for a macro's parameter.</summary>
    public Value LiteralValue(Expr literal, IrType t) => Literal(literal, t);

    public static bool IsLiteralExpr(Expr e) => IsLiteral(e);

    public Stmt[] StmtsOf(Value v, IrType t) => Many(v, t, StmtOf);

    public Decl[] DeclsOf(Value v, IrType t) => Many(v, t, DeclOf);

    /// <summary>An <c>Ident</c>: the name, renamed where the quote being built binds it (hygiene,
    /// 13 M9b P5), the splice's own where the content names it as "#i"; <paramref name="from"/>
    /// the module a free name of a quote belongs to.</summary>
    private Value Ident(string name, Span span, IrType t, string? from = null)
    {
        if (_splices is not null && name.Length > 1 && name[0] == '#' && int.TryParse(name.AsSpan(1), out var index))
            return _splices[index]; // a name splice: the Ident the macro gave, as is
        if (_renames is not null && _renames.TryGetValue(name, out var fresh))
        {
            name = fresh;
            from = null;
        }
        return Struct(t, ("name", S(name)), ("span", Sp(span)), ("from", OptS(from)));
    }

    /// <summary>A binder's name: renamed inside a quote.</summary>
    private Make Id(string name, Span span) => t => Ident(name, span, t);

    /// <summary>A reference: a free name inside a quote is the macro's module's.</summary>
    private Make Ref(IdentifierExpr id) => t =>
        _renames is not null && _renames.ContainsKey(id.Name) ? Ident(id.Name, id.Span, t) : Ident(id.Name, id.Span, t, id.QuotedFrom ?? _quoteModule);

    /// <summary>A label (a member, a field, a declaration's name): never renamed, nobody's.</summary>
    private Make Label(string name, Span span) => t =>
        _splices is not null && name.Length > 1 && name[0] == '#' && int.TryParse(name.AsSpan(1), out var index)
            ? _splices[index]
            : Struct(t, ("name", S(name)), ("span", Sp(span)), ("from", OptS(null)));

    private bool Renamed(string name) => _renames is not null && _renames.ContainsKey(name);

    private Value Visibility(VisibilityWord word, IrType t) => Variant(t, word switch
    {
        VisibilityWord.Pub => "Pub", VisibilityWord.Internal => "Internal", VisibilityWord.Private => "Private", _ => "None",
    });

    private static readonly string[] BinaryNames =
    [
        "Add", "Sub", "Mul", "Div", "Rem", "AddWrap", "SubWrap", "MulWrap", "Shl", "Shr", "BitAnd", "BitXor", "BitOr",
        "Lt", "Le", "Gt", "Ge", "Eq", "Ne", "In", "NotIn", "And", "Or", "Coalesce",
    ];
    private static readonly string[] UnaryNames = ["Not", "Neg", "BitNot", "PreInc", "PreDec", "FromEnd", "Place"];
    private static readonly string[] PostfixNames = ["Inc", "Dec", "Unwrap"];
    private static readonly string[] FormNames = ["Paren", "Bare", "Trailing"];
    private static readonly string[] TryNames = ["Plain", "Optional", "Force"];

    private Make Op(BinaryOp op) => t => Variant(t, BinaryNames[(int)op]);

    private static string Suffix(IntSuffix s) => s.ToString().ToLowerInvariant();

    private Value Literal(Expr e, IrType t) => e switch
    {
        IntLiteralExpr i => Variant(t, "Int", U(i.Value), OptS(i.Suffix is { } s ? Suffix(s) : null)),
        FloatLiteralExpr f => Variant(t, "Float", _ => Value.Double(f.Value), OptS(f.Suffix is { } s ? (s == FloatSuffix.F32 ? "f32" : "f64") : null)),
        StringLiteralExpr s => Variant(t, "Str", S(s.Value)),
        ByteStringExpr b => Variant(t, "Bytes", Arr(b.Bytes, (x, _) => Value.Uint(x))),
        CharLiteralExpr c => Variant(t, "Char", U((ulong)c.CodePoint)),
        BoolLiteralExpr b => Variant(t, "Bool", B(b.Value)),
        NullLiteralExpr => Variant(t, "Null"),
        _ => throw new InternalCompilationException($"std.syntax: {e.GetType().Name} is no literal"),
    };

    private static bool IsLiteral(Expr e) => e is IntLiteralExpr or FloatLiteralExpr or StringLiteralExpr or ByteStringExpr
        or CharLiteralExpr or BoolLiteralExpr or NullLiteralExpr;

    private static string Describe(Node n) => n switch
    {
        FieldByInfoExpr or PayloadByInfoExpr or VariantInitExpr or ComptimeIfExpr => "a round over fields or variants",
        StructInitExpr => "an initializer over infos",
        ErrorExpr or ErrorStmt or ErrorDecl or ErrorPattern or ErrorType => "a node the parser could not read",
        BindingStmt { LocalFunction: true } or GlobalBindingDecl { Binding.LocalFunction: true } => "a local function",
        _ => "a " + n.GetType().Name,
    };

    private static ComptimeFailure NoForm(Node n) => ComptimeFailure.NotYet($"std.syntax has no form for {Describe(n)} yet");

    public Value Expr(Expr e, IrType t)
    {
        var sp = Sp(e.Span);
        switch (e)
        {
            case var lit when IsLiteral(lit): return Variant(t, "Literal", lt => Literal(lit, lt), sp);
            case IdentifierExpr id: return Variant(t, "Name", Ref(id));
            case ImplicitMemberExpr im: return Variant(t, "Implicit", Id(im.Name, im.Span));
            case ThisExpr: return Variant(t, "This", sp);
            case AtIdentifierExpr at: return Variant(t, "At", S(at.Name), OptOf(at.Arguments is not null, Exprs(at.Arguments ?? [])), sp);
            case UnaryExpr u: return Variant(t, "Unary", ut => Variant(ut, UnaryNames[(int)u.Operator]), Boxed(bt => Expr(u.Operand, bt)), sp);
            case PostfixExpr p: return Variant(t, "Postfix", Boxed(bt => Expr(p.Operand, bt)), pt => Variant(pt, PostfixNames[(int)p.Operator]), sp);
            case BinaryExpr b: return Variant(t, "Binary", Boxed(bt => Expr(b.Left, bt)), Op(b.Operator), Boxed(bt => Expr(b.Right, bt)), sp);
            case AssignExpr a:
                return Variant(t, "Assign", Boxed(bt => Expr(a.Target, bt)), OptOf(a.Operator is not null, ot => Op(a.Operator!.Value)(ot)),
                    Boxed(bt => Expr(a.Value, bt)), sp);
            case RangeExpr r: return Variant(t, "Range", Boxed(bt => Expr(r.Low, bt)), Boxed(bt => Expr(r.High, bt)), B(r.IsInclusive), sp);
            case SliceRangeExpr r: return Variant(t, "SliceRange", OptBoxedExpr(r.Low), OptBoxedExpr(r.High), B(r.IsInclusive), sp);
            case CastExpr c: return Variant(t, "Cast", Boxed(bt => Expr(c.Operand, bt)), tt => Type(c.Type, tt), sp);
            case TypeTestExpr tt0: return Variant(t, "Is", Boxed(bt => Expr(tt0.Operand, bt)), tt => Type(tt0.Type, tt), sp);
            case CallExpr c:
                return Variant(t, "Call", Boxed(bt => Expr(c.Callee, bt)),
                    Arr(c.Arguments.Select((arg, i) => (arg, i)).ToArray(), (pair, at) =>
                    {
                        var name = c.ArgumentNames is { } names && pair.i < names.Length ? names[pair.i] : null;
                        var spread = c.Spreads is { } spreads && System.Array.Exists(spreads, s => ReferenceEquals(s.Argument, pair.arg));
                        return Struct(at, ("name", OptS(name)), ("value", vt => Expr(pair.arg, vt)), ("spread", B(spread)));
                    }),
                    Types(c.TypeArguments ?? []), sp);
            case IndexExpr ix: return Variant(t, "Index", Boxed(bt => Expr(ix.Target, bt)), Boxed(bt => Expr(ix.Index, bt)), sp);
            case MemberExpr m: return Variant(t, "Member", Boxed(bt => Expr(m.Target, bt)), Label(m.Member, m.MemberSpan), B(m.IsOptional), sp);
            case ArrayLitExpr a: return Variant(t, "Array", Exprs(a.Elements), sp);
            case TupleLitExpr tu: return Variant(t, "Tuple", Exprs(tu.Elements), sp);
            case InterpolatedStringExpr s:
                return Variant(t, "Interpolated", Arr(s.Segments, (seg, st) => seg switch
                {
                    InterpText text => Variant(st, "Text", S(text.Text)),
                    InterpHole hole => Variant(st, "Hole", ht => Expr(hole.Expr, ht), OptS(hole.FormatSpec)),
                    _ => throw new InternalCompilationException("std.syntax: an f-string segment of no kind"),
                }), sp);
            case LambdaExpr l:
                return Variant(t, "Lambda",
                    Arr(l.Parameters, (p, pt) => Struct(pt, ("name", Id(p.Name, p.NameSpan)), ("type", OptType(p.Type)),
                        ("pattern", OptPattern(p.Pattern)), ("isPlace", B(p.IsPlace)), ("isImplicit", B(p.Implicit)))),
                    OptType(l.ReturnType), OptTypes(l.Throws?.Types), Boxed(bt => Body(l.Body, bt)),
                    ft => Variant(ft, FormNames[(int)l.Form]), sp);
            case LetCondExpr lc: return Variant(t, "LetCond", pt => Pattern(lc.Pattern, pt), Boxed(bt => Expr(lc.Initializer, bt)), sp);
            case IfExpr i: return Variant(t, "If", Boxed(bt => Expr(i.Condition, bt)), Boxed(bt => Expr(i.Then, bt)), Boxed(bt => Expr(i.Else, bt)), sp);
            case MatchExpr m: return Variant(t, "Match", Boxed(bt => Expr(m.Scrutinee, bt)), Arms(m.Arms), sp);
            case BlockExpr b: return Variant(t, "Block", bt => BlockValue(b.Block, bt), sp);
            case LoopExpr l: return Variant(t, "Loop", bt => BlockValue(l.Body, bt), OptS(l.Label), sp);
            case StructInitExpr si when si.Rounds.Length == 0 && si.Fields.All(f => f.Info is null):
                return Variant(t, "StructInit", Strings(si.Path), Types(si.TypeArguments), FieldInits(si.Fields), B(si.IsImplicit), sp);
            case TypePathExpr tp: return Variant(t, "TypePath", Strings(tp.Path), Types(tp.TypeArguments), sp);
            case WithExpr w:
                return Variant(t, "With", Boxed(bt => Expr(w.Target, bt)),
                    Arr(w.Fields, (f, ft) => Struct(ft, ("path", Strings(f.Path)), ("value", vt => Expr(f.Value, vt)))), sp);
            case ComptimeExpr c: return Variant(t, "Comptime", Boxed(bt => Expr(c.Inner, bt)), sp);
            case ThrowExpr th: return Variant(t, "Throw", Boxed(bt => Expr(th.Value, bt)), sp);
            case TryExpr tr:
                return Variant(t, "Try", Boxed(bt => Expr(tr.Value, bt)), kt => Variant(kt, TryNames[(int)tr.Kind]), Catches(tr.Catches), sp);
            case MacroCallExpr mc:
                return Variant(t, "Macro", S(mc.Name), Exprs(mc.Arguments), OptBlock(mc.Trailing), sp);
            case SpliceExpr s when _splices is not null: return SpliceAsExpr(s, t);
            default:
                throw NoForm(e);
        }
    }

    private Make FieldInits(StructInitField[] fields) =>
        Arr(fields, (f, ft) => Struct(ft, ("name", Label(f.Name, f.NameSpan)), ("value", vt => Expr(f.Value, vt)),
            // 'S { v }' with 'v' renamed by the quote is 'S { v = v__1 }'
            ("isShorthand", B(f.IsShorthand && !(f.Value is IdentifierExpr v && Renamed(v.Name))))));

    private Value Body(Node body, IrType t) => body switch
    {
        Block b => Variant(t, "Block", bt => BlockValue(b, bt)),
        Expr e => Variant(t, "Expr", Boxed(bt => Expr(e, bt))),
        _ => throw new InternalCompilationException($"std.syntax: a body that is {body.GetType().Name}"),
    };

    private Make Arms(MatchArm[] arms) => Arr(arms, (a, at) =>
        Struct(at, ("pattern", pt => Pattern(a.Pattern, pt)), ("guard", OptExpr(a.Guard)), ("body", bt => Body(a.Body, bt)), ("span", Sp(a.Span))));

    private Make Catches(CatchClause[] catches) => Arr(catches, (c, ct) =>
        Struct(ct, ("name", OptIdent(c.BindingName, c.NameSpan)), ("type", OptType(c.BindingType)), ("types", Types(c.BindingTypes)),
            ("body", bt => BlockValue(c.Body, bt)), ("isExpr", B(c.ExpressionBody)), ("span", Sp(c.Span))));

    public Value BlockValue(Block b, IrType t) => Struct(t, ("stmts", st => StatementsValue(b.Statements, st)), ("span", Sp(b.Span)));

    /// <summary>The statements of a block as values. Inside a quote, a splice that stands as a
    /// statement is set in by its kind: statements and a block's statements are spliced, a lone
    /// statement stands, an expression, a name or a literal is the statement or the tail it is.</summary>
    public Value StatementsValue(Stmt[] statements, IrType arrayType)
    {
        var element = Elem(arrayType);
        var items = new List<Value>();
        foreach (var statement in statements)
        {
            var splice = statement switch { ExprStmt { Expr: SpliceExpr s } => s, TailExprStmt { Expr: SpliceExpr s } => s, _ => null };
            if (splice is null || _splices is null)
            {
                items.Add(Stmt(statement, element));
                continue;
            }
            var value = _splices[splice.Index];
            var type = _spliceTypes![splice.Index];
            var tail = statement is TailExprStmt;
            switch (KindName(type))
            {
                case "Stmt[]": items.AddRange(ArrOf(value).Select(v => tail ? v : Untailed(v, element))); break;
                case "Block": items.AddRange(ArrOf(FieldOf(value, type, "stmts")).Select(v => tail ? v : Untailed(v, element))); break;
                case "Stmt": items.Add(tail ? value : Untailed(value, element)); break;
                default: items.Add(Variant(element, tail ? "Tail" : "Expr", et => SpliceAsExpr(splice, et), Sp(statement.Span))); break;
            }
        }
        return Value.Of(new ArrayObj(items.ToArray()));
    }

    /// <summary>A tail spliced where no tail stands is the expression statement it is.</summary>
    private Value Untailed(Value statement, IrType element)
    {
        var (name, f, _) = Case(statement, element);
        return name == "Tail" ? Variant(element, "Expr", _ => f[0], _ => f[1]) : statement;
    }

    public Value Stmt(Stmt s, IrType t)
    {
        var sp = Sp(s.Span);
        switch (s)
        {
            case ExprStmt e: return Variant(t, "Expr", et => Expr(e.Expr, et), sp);
            case TailExprStmt e: return Variant(t, "Tail", et => Expr(e.Expr, et), sp);
            case BindingStmt b when !b.LocalFunction:
                return Variant(t, "Let", B(b.IsMutable), Id(b.Name, b.NameSpan), OptType(b.Type), OptExpr(b.Initializer), B(b.Cleanup is not null), sp);
            case DestructuringStmt d:
                return Variant(t, "LetPattern", B(d.IsMutable), pt => Pattern(d.Pattern, pt), OptType(d.Type), et => Expr(d.Initializer, et), OptBlock(null), sp);
            case LetPatternStmt lp:
                return Variant(t, "LetPattern", B(lp.IsMutable), pt => Pattern(lp.Pattern, pt), OptType(lp.Type), et => Expr(lp.Initializer, et), OptBlock(lp.Else), sp);
            case IfStmt i:
                return Variant(t, "If", ct => Expr(i.Condition, ct), bt => BlockValue(i.Then, bt),
                    OptOf(i.Else is not null, Boxed(st => Stmt(i.Else!, st))), B(i.Comptime), sp);
            case WhileStmt w: return Variant(t, "While", ct => Expr(w.Condition, ct), bt => BlockValue(w.Body, bt), OptS(w.Label), sp);
            case DoWhileStmt dw: return Variant(t, "DoWhile", bt => BlockValue(dw.Body, bt), ct => Expr(dw.Condition, ct), OptS(dw.Label), sp);
            case ForInStmt f:
                return Variant(t, "For", Id(f.Variable, f.NameSpan), OptPattern(f.Pattern), it => Expr(f.Iterable, it), bt => BlockValue(f.Body, bt),
                    B(f.Comptime), OptS(f.Label), sp);
            case BreakStmt br: return Variant(t, "Break", OptS(br.Label), OptExpr(br.Value), sp);
            case ContinueStmt co: return Variant(t, "Continue", OptS(co.Label), sp);
            case ReturnStmt r: return Variant(t, "Return", OptExpr(r.Value), sp);
            case YieldStmt y: return Variant(t, "Yield", OptExpr(y.Value), sp);
            case DeferStmt d: return Variant(t, "Defer", Boxed(st => Stmt(d.Body, st)), sp);
            case ThrowStmt th: return Variant(t, "Throw", et => Expr(th.Value, et), sp);
            case MatchStmt m: return Variant(t, "Match", et => Expr(m.Scrutinee, et), Arms(m.Arms), sp);
            case TryStmt tr: return Variant(t, "Try", bt => BlockValue(tr.Body, bt), Catches(tr.Catches), sp);
            case Block b: return Variant(t, "Block", bt => BlockValue(b, bt), sp);
            default:
                throw NoForm(s);
        }
    }

    public Value Pattern(Pattern p, IrType t)
    {
        var sp = Sp(p.Span);
        switch (p)
        {
            case WildcardPattern: return Variant(t, "Wildcard", sp);
            case LiteralPattern l: return Variant(t, "Literal", Boxed(et => Expr(l.Literal, et)), sp);
            case BindingPattern b: return Variant(t, "Binding", Id(b.Name, b.Span));
            case TypePattern tp: return Variant(t, "TypePat", OptIdent(tp.Name, tp.NameSpan), tt => Type(tp.Type, tt), sp);
            case TypeSetPattern ts: return Variant(t, "TypeSet", OptIdent(ts.Name, ts.NameSpan), Types(ts.Types), sp);
            case VariantPattern v:
                return Variant(t, "Variant", Strings(v.Path),
                    OptOf(v.TupleElements is not null, Arr(v.TupleElements ?? [], Pattern)),
                    OptOf(v.StructFields is not null, Arr(v.StructFields ?? [], (f, ft) =>
                        // 'Rect { w }' binds 'w', which a quote renames: the field stays, 'w = w__1' binds
                        Struct(ft, ("name", Label(f.Name, f.Span)), ("pattern", f.Pattern is null && Renamed(f.Name)
                            ? OptOf(true, pt => Variant(pt, "Binding", Id(f.Name, f.Span)))
                            : OptPattern(f.Pattern))))),
                    B(v.IsImplicit), sp);
            case TuplePattern tu: return Variant(t, "Tuple", Arr(tu.Elements, Pattern), sp);
            case RangePattern r: return Variant(t, "Range", Boxed(et => Expr(r.Low, et)), Boxed(et => Expr(r.High, et)), B(r.IsInclusive), sp);
            case OrPattern o: return Variant(t, "Or", Arr(o.Alternatives, Pattern), sp);
            case ArrayPattern a: return Variant(t, "Array", Arr(a.Elements, Pattern), sp);
            case RestPattern r: return Variant(t, "Rest", OptIdent(r.Name, r.NameSpan), sp);
            default:
                throw NoForm(p);
        }
    }

    public Value Type(TypeNode n, IrType t)
    {
        var sp = Sp(n.Span);
        switch (n)
        {
            case NamedType { Path: [var only] } when _splices is not null && only.Length > 1 && only[0] == '#' && int.TryParse(only.AsSpan(1), out var spliced):
                return _splices[spliced]; // a Type spliced in (M9b S4), its kind checked at the quote
            case NamedType nt:
                return Variant(t, "Named", Strings(nt.Path), Types(nt.TypeArguments),
                    Strings(nt.ArgumentNames is { } names ? names.Select(x => x ?? "") : []), OptS(nt.QuotedFrom ?? _quoteModule), sp);
            case NullableType o: return Variant(t, "Optional", Boxed(bt => Type(o.Inner, bt)), sp);
            case ArrayType a: return Variant(t, "Array", Boxed(bt => Type(a.Element, bt)), OptOf(a.Length is not null, _ => Value.Int(a.Length!.Value)), sp);
            case TupleType tu: return Variant(t, "Tuple", Types(tu.Elements), Strings(tu.Labels is { } labels ? labels.Select(x => x ?? "") : []), sp);
            case FunctionType f:
                return Variant(t, "Fn", Types(f.Parameters), Boxed(bt => Type(f.ReturnType, bt)), OptTypes(f.Throws?.Types), Bools(f.Places), sp);
            case ThrowingType th:
                return Variant(t, "Throwing", Boxed(bt => Type(th.Inner, bt)), OptOf(th.Thrown is not null, Boxed(bt => Type(th.Thrown!, bt))), sp);
            default:
                throw NoForm(n);
        }
    }

    private Value Attribute(AttributeNode a, IrType t) =>
        Struct(t, ("path", Strings(a.Path)), ("fields", FieldInits(a.Positional is { } pos
            ? [new StructInitField("", pos, pos.Span) { NameSpan = pos.Span }] : a.Fields)), ("span", Sp(a.Span)));

    private Value Generic(GenericParam g, IrType t) =>
        Struct(t, ("name", Label(g.Name, g.NameSpan)), ("constraints", Types(g.Constraints)), ("default", OptType(g.Default)));

    private Value Param(Param p, IrType t) => Struct(t, ("name", Id(p.Name, p.NameSpan)), ("type", tt => Type(p.Type, tt)), ("default", OptExpr(p.Default)),
        ("isParams", B(p.IsParams)), ("isEllipsis", B(p.IsEllipsis)), ("isPlace", B(p.IsPlace)), ("attributes", Attributes(p.Attributes)), ("span", Sp(p.Span)));

    private Value Fn(FunctionDecl f, IrType t) => Struct(t,
        ("visibility", vt => Visibility(f.Visibility, vt)), ("isStatic", B(f.IsStatic)), ("isMut", B(f.IsMut)), ("isInline", B(f.IsInline)),
        ("name", Label(f.Name, f.NameSpan)), ("generics", Arr(f.Generics, Generic)), ("params", Arr(f.Parameters, Param)),
        ("ret", OptType(f.ReturnType)), ("retGrouped", B(f.ReturnGrouped)), ("throws", OptTypes(f.Throws?.Types)),
        ("body", OptBlock(f.Body)), ("externAbi", OptS(f.Extern?.Abi)), ("externSymbol", OptS(f.Extern?.Symbol)),
        ("attributes", Attributes(f.Attributes)), ("span", Sp(f.Span)));

    private Value Field(FieldDecl f, IrType t) => Struct(t, ("visibility", vt => Visibility(f.Visibility, vt)), ("isVar", B(f.IsVar)),
        ("name", Label(f.Name, f.NameSpan)), ("type", tt => Type(f.Type, tt)), ("default", OptExpr(f.Default)), ("attributes", Attributes(f.Attributes)), ("span", Sp(f.Span)));

    private Value Assoc(AssociatedTypeDecl a, IrType t) =>
        Struct(t, ("name", Label(a.Name, a.NameSpan)), ("type", OptType(a.Type)), ("bounds", Types(a.Bounds)), ("span", Sp(a.Span)));

    private Value Let(VisibilityWord visibility, bool isStatic, BindingStmt b, AttributeNode[] attributes, Span span, IrType t) => Struct(t,
        ("visibility", vt => Visibility(visibility, vt)), ("isVar", B(b.IsMutable)), ("isStatic", B(isStatic)), ("name", Id(b.Name, b.NameSpan)),
        ("type", OptType(b.Type)), ("init", OptExpr(b.Initializer)), ("attributes", Attributes(attributes)), ("span", Sp(span)));

    private Value Static(StaticBindingDecl s, IrType t) => Let(s.Visibility, true, s.Binding, s.Attributes, s.Span, t);

    private Value TypeDecl(VisibilityWord visibility, string name, Span nameSpan, GenericParam[] generics, TypeNode[] interfaces, string?[]? delegates,
        Decl[] members, AttributeNode[] attributes, Span span, IrType t) => Struct(t,
        ("visibility", vt => Visibility(visibility, vt)), ("name", Label(name, nameSpan)), ("generics", Arr(generics, Generic)),
        ("interfaces", Types(interfaces)), ("delegates", Strings(delegates is { } d ? d.Select(x => x ?? "") : [])),
        ("members", Arr(members, Decl)), ("attributes", Attributes(attributes)), ("span", Sp(span)));

    /// <summary>A declaration as the struct of its kind, for an attribute macro's first parameter
    /// (13 M9b S4): <paramref name="t"/> is that struct's type, and <c>null</c> where the kind is
    /// not the declaration's.</summary>
    public Value? DeclPayload(Decl d, IrType t)
    {
        var kind = KindName(t);
        return (d, kind) switch
        {
            (FunctionDecl f, "FnDecl") => Fn(f, t),
            (StructDecl s, "StructDecl") => TypeDecl(s.Visibility, s.Name, s.NameSpan, s.Generics, s.Interfaces, s.Delegates, s.Members, s.Attributes, s.Span, t),
            (ClassDecl c, "ClassDecl") => TypeDecl(c.Visibility, c.Name, c.NameSpan, c.Generics, c.Interfaces, c.Delegates, c.Members, c.Attributes, c.Span, t),
            (EnumDecl, "EnumDecl") or (InterfaceDecl, "InterfaceDecl") or (ExtendDecl, "ExtendDecl") => PayloadOf(Decl(d, EnumTypeOf(t))),
            (GlobalBindingDecl g, "LetDecl") => Let(g.Visibility, false, g.Binding, g.Attributes, g.Span, t),
            _ => null,
        };
    }

    /// <summary>The <c>Decl</c> enum a payload struct belongs to: the module's.</summary>
    private IrType EnumTypeOf(IrType payload)
    {
        for (var i = 0; i < module.Types.Count; i++)
            if (module.Types[i].Module == "std.syntax" && module.Types[i].Name == "Decl") return new IrEnumType(new TypeId(i));
        throw new InternalCompilationException("std.syntax: no 'Decl' in this build");
    }

    private static Value PayloadOf(Value variant) => ((AggObj)variant.Ref!).Slots[1];

    public Value Decl(Decl d, IrType t)
    {
        switch (d)
        {
            case FunctionDecl f: return Variant(t, "Fn", ft => Fn(f, ft));
            case StructDecl s:
                return Variant(t, "Struct", st => TypeDecl(s.Visibility, s.Name, s.NameSpan, s.Generics, s.Interfaces, s.Delegates, s.Members, s.Attributes, s.Span, st));
            case ClassDecl c:
                return Variant(t, "Class", st => TypeDecl(c.Visibility, c.Name, c.NameSpan, c.Generics, c.Interfaces, c.Delegates, c.Members, c.Attributes, c.Span, st));
            case EnumDecl e:
                return Variant(t, "Enum", et => Struct(et, ("visibility", vt => Visibility(e.Visibility, vt)), ("name", Label(e.Name, e.NameSpan)),
                    ("generics", Arr(e.Generics, Generic)), ("interfaces", Types(e.Interfaces)),
                    ("variants", Arr(e.Variants, (v, vt) => Struct(vt, ("name", Label(v.Name, v.NameSpan)),
                        ("tuple", OptTypes(v.TupleFields)),
                        ("fields", OptOf(v.StructFields is not null, Arr(v.StructFields ?? [], Field))),
                        ("attributes", Attributes(v.Attributes)), ("span", Sp(v.Span))))),
                    ("types", Arr(e.Types, Assoc)), ("statics", Arr(e.Statics, Static)), ("methods", Arr(e.Methods, Fn)),
                    ("attributes", Attributes(e.Attributes)), ("span", Sp(e.Span))));
            case InterfaceDecl i:
                return Variant(t, "Interface", it => Struct(it, ("visibility", vt => Visibility(i.Visibility, vt)), ("isSealed", B(i.IsSealed)),
                    ("name", Label(i.Name, i.NameSpan)), ("generics", Arr(i.Generics, Generic)), ("interfaces", Types(i.Interfaces)),
                    ("types", Arr(i.Types, Assoc)), ("statics", Arr(i.Statics, Static)), ("members", Arr(i.Members, Fn)),
                    ("attributes", Attributes(i.Attributes)), ("span", Sp(i.Span))));
            case ExtendDecl x:
                return Variant(t, "Extend", xt => Struct(xt, ("visibility", vt => Visibility(x.Visibility, vt)), ("generics", Arr(x.Generics, Generic)),
                    ("target", tt => Type(x.Target, tt)), ("interfaces", Types(x.Interfaces)), ("types", Arr(x.Types, Assoc)),
                    ("statics", Arr(x.Statics, Static)), ("methods", Arr(x.Methods, Fn)),
                    ("attributes", Attributes(x.Attributes)), ("span", Sp(x.Span))));
            case GlobalBindingDecl g when !g.Binding.LocalFunction:
                return Variant(t, "Let", lt => Let(g.Visibility, false, g.Binding, g.Attributes, g.Span, lt));
            case StaticBindingDecl s: return Variant(t, "Let", lt => Static(s, lt));
            case FieldDecl f: return Variant(t, "Field", ft => Field(f, ft));
            case AssociatedTypeDecl a: return Variant(t, "AssocType", at => Assoc(a, at));
            case TypeAliasDecl a:
                return Variant(t, "Alias", at => Struct(at, ("visibility", vt => Visibility(a.Visibility, vt)), ("isOpaque", B(a.IsOpaque)),
                    ("name", Label(a.Name, a.NameSpan)), ("aliased", tt => Type(a.Aliased, tt)), ("span", Sp(a.Span))));
            case ImportDecl im:
            {
                var selective = im.Clause as ImportSelective;
                return Variant(t, "Import", it => Struct(it, ("path", Strings(im.Path)),
                    ("names", OptOf(selective is not null, Strings(selective?.Names ?? []))),
                    ("renames", Strings(selective?.Renames is { } renames ? renames.Select(x => x ?? "") : [])),
                    ("alias", OptS((im.Clause as ImportAlias)?.Alias)), ("isPub", B(im.IsPublic)), ("span", Sp(im.Span))));
            }
            default:
                throw NoForm(d);
        }
    }

    // ------------------------------------------------------------------ quotes (M9b S3)

    private Value[]? _splices;
    private IrType[]? _spliceTypes;
    private string? _quoteModule;
    private Dictionary<string, string>? _renames;

    private string KindName(IrType t) => t switch
    {
        IrEnumType e => Def(e.Type).Name,
        IrStructType s => Def(s.Type).Name,
        IrArrayType { Element: IrEnumType e } => Def(e.Type).Name + "[]",
        _ => "?",
    };

    /// <summary>The tree a quote builds (13 M9b P5): its content as values of the kind the type
    /// says, the splices set in, every name the content binds renamed fresh (hygiene), every
    /// free name marked as the quote's module's.</summary>
    public Value Quote(IrQuote quote, Value[] splices, IrType[] spliceTypes, IrType resultType)
    {
        var saved = (_splices, _spliceTypes, _quoteModule, _renames);
        _splices = splices;
        _spliceTypes = spliceTypes;
        _quoteModule = quote.Module.Length > 0 ? quote.Module : null;
        _renames = Binders(quote.Node).ToDictionary(n => n, FreshName, StringComparer.Ordinal);
        try
        {
            var content = quote.Node.Content;
            switch (KindName(resultType))
            {
                case "Expr": return Expr(((TailExprStmt)content[0]).Expr, resultType);
                // statements stand where no value is taken: a last expression without ';' is a statement
                case "Block": return BlockValue(new Block(Statements(content), quote.Node.Span), resultType);
                case "Stmt[]": return StatementsValue(Statements(content), resultType);
                case "Decl": return Decl((Decl)content[0], resultType);
                case "Decl[]": return Arr([.. content.Cast<Decl>()], Decl)(resultType);
                default: throw new InternalCompilationException($"std.syntax: a quote of {resultType}");
            }
        }
        finally
        {
            (_splices, _spliceTypes, _quoteModule, _renames) = saved;
        }
    }

    private static Stmt[] Statements(Node[] content) =>
        [.. content.Cast<Stmt>().Select((s, i) => i == content.Length - 1 && s is TailExprStmt tail ? new ExprStmt(tail.Expr, tail.Span) : s)];

    /// <summary>A splice where an expression stands: an Expr as is, an Ident as a name, a Literal
    /// as a literal, a Block as a block expression.</summary>
    private Value SpliceAsExpr(SpliceExpr s, IrType exprType)
    {
        var value = _splices![s.Index];
        var kind = KindName(_spliceTypes![s.Index]);
        return kind switch
        {
            "Expr" => value,
            "Ident" => Variant(exprType, "Name", _ => value),
            "Literal" => Variant(exprType, "Literal", _ => value, Sp(s.Span)),
            "Block" => Variant(exprType, "Block", _ => value, Sp(s.Span)),
            _ => throw ComptimeFailure.NotYet($"std.syntax: a splice of '{kind}' where an expression stands"),
        };
    }

    /// <summary>The names a quote's content binds: a let, a loop variable, a lambda's or a
    /// function's parameter, a catch's binding, a pattern's names. Each is fresh per expansion.</summary>
    private static List<string> Binders(QuoteExpr quote)
    {
        var names = new List<string>();
        void Bind(string name)
        {
            if (name.Length > 0 && (char.IsLower(name[0]) || name[0] == '_') && !names.Contains(name)) names.Add(name);
        }
        void Walk(Node node)
        {
            switch (node)
            {
                case SpliceExpr: return; // its inner is the macro's code
                case BindingStmt b: Bind(b.Name); break;
                case ForInStmt f: Bind(f.Variable); break;
                case LambdaParam { Implicit: false } p: Bind(p.Name); break;
                case Param p: Bind(p.Name); break;
                case CatchClause { BindingName: { } c }: Bind(c); break;
                case BindingPattern bp: Bind(bp.Name); break;
                case TypePattern { Name: { } tn }: Bind(tn); break;
                case TypeSetPattern { Name: { } sn }: Bind(sn); break;
                case RestPattern { Name: { } rn }: Bind(rn); break;
                case FieldPattern { Pattern: null } fp: Bind(fp.Name); break;
            }
            foreach (var child in AstChildren.Of(node)) Walk(child);
        }
        foreach (var n in quote.Content) Walk(n);
        return names;
    }

    // ------------------------------------------------------------------ a tree out of values

    private static Value[] SlotsOf(Value v) => v.Ref switch
    {
        AggObj a => a.Slots,
        ClassObj c => c.Fields,
        BoxObj b => SlotsOf(b.Payload),
        _ => throw new InternalCompilationException($"std.syntax: a node that is {v.Ref?.GetType().Name ?? "a scalar"}"),
    };

    /// <summary>An enum value: the name of its variant, the payload, the payload's types.</summary>
    private (string Name, Value[] Fields, IrType[] Types) Case(Value v, IrType t)
    {
        var slots = SlotsOf(v);
        var def = Def(IdOf(t));
        var tag = (int)slots[0].Bits;
        if (tag < 0 || tag >= def.Variants.Length) throw new InternalCompilationException($"std.syntax: '{def.Name}' has no variant {tag}");
        var variant = Def(def.Variants[tag]);
        return (variant.Name[(variant.Name.LastIndexOf('.') + 1)..], slots[1..], variant.FieldTypes[1..]); // slot 0 is the tag
    }

    private static string StrOf(Value v) => ((StrObj)v.Ref!).ToString();
    private static bool BoolOf(Value v) => v.AsBool;
    private static Value? OptionOf(Value v) => (OptObj)v.Ref! is { Has: true } o ? o.Inner : null;
    private static Value[] ArrOf(Value v) => ((ArrayObj)v.Ref!).Items;

    private (Value Payload, IrType Type) Unbox(Value v, IrType boxType)
    {
        var id = IdOf(boxType);
        var fields = ((ClassObj)v.Ref!).Fields;
        if (fields.Length != 1) throw new InternalCompilationException("std.syntax: a box of several fields");
        return (fields[0], Def(id).FieldTypes[0]);
    }

    private T[] Many<T>(Value v, IrType t, Func<Value, IrType, T> read) => [.. ArrOf(v).Select(x => read(x, Elem(t)))];
    private T? Maybe<T>(Value v, IrType t, Func<Value, IrType, T> read) where T : class => OptionOf(v) is { } inner ? read(inner, Inner(t)) : null;
    private static string? MaybeStr(Value v) => OptionOf(v) is { } inner ? StrOf(inner) : null;
    private static string[] StringsOf(Value v) => [.. ArrOf(v).Select(StrOf)];
    private static string?[]? NullableStrings(Value v) => ArrOf(v).Length == 0 ? null : [.. ArrOf(v).Select(x => StrOf(x) is { Length: > 0 } s ? s : null)];
    private Value FieldOf(Value v, IrType t, string name) => SlotsOf(v)[FieldIndex(IdOf(t), name)];
    private IrType FieldTypeOf(IrType t, string name) => Def(IdOf(t)).FieldTypes[FieldIndex(IdOf(t), name)];
    private Expr BoxedExpr(Value v, IrType t) { var (p, pt) = Unbox(v, t); return ExprOf(p, pt); }
    private TypeNode BoxedType(Value v, IrType t) { var (p, pt) = Unbox(v, t); return TypeOf(p, pt); }
    private Expr? MaybeBoxedExpr(Value v, IrType t) => OptionOf(v) is { } inner ? BoxedExpr(inner, Inner(t)) : null;

    /// <summary>The span a node built from values gets where the values name none: the site it is
    /// built for (M9b S2), so a diagnostic on expanded code points at the site; none for a bridge
    /// of its own.</summary>
    public Span At { get; set; }

    /// <summary>The span of the node being read, where its value names one (M9b S5).</summary>
    private Span? _current;

    private Span NoSpan => _current ?? At;

    private (string Name, Span Span) IdentOf(Value v, IrType t) =>
        (StrOf(FieldOf(v, t, "name")), SpanOf(FieldOf(v, t, "span"), FieldTypeOf(t, "span")) ?? NoSpan);
    private (string? Name, Span Span) MaybeIdentOf(Value v, IrType t) => OptionOf(v) is { } inner ? IdentOf(inner, Inner(t)) : (null, NoSpan);

    private static BinaryOp BinaryOf(string name) => (BinaryOp)IndexIn(BinaryNames, name);
    private static int IndexIn(string[] names, string name)
    {
        var i = System.Array.IndexOf(names, name);
        return i >= 0 ? i : throw ComptimeFailure.NotYet($"std.syntax: '{name}' has no form in the compiler yet");
    }

    private Expr LiteralOf(string name, Value[] f) => name switch
    {
        "Int" => new IntLiteralExpr(f[0].Bits, MaybeStr(f[1]) is { } s ? Enum.Parse<IntSuffix>(s, ignoreCase: true) : null, NoSpan),
        "Float" => new FloatLiteralExpr(f[0].AsDouble, MaybeStr(f[1]) is { } s ? (s == "f32" ? FloatSuffix.F32 : FloatSuffix.F64) : null, NoSpan),
        "Str" => new StringLiteralExpr(StrOf(f[0]), NoSpan),
        "Bytes" => new ByteStringExpr([.. ArrOf(f[0]).Select(b => (byte)b.Bits)], NoSpan),
        "Char" => new CharLiteralExpr((int)f[0].Bits, NoSpan),
        "Bool" => new BoolLiteralExpr(BoolOf(f[0]), NoSpan),
        "Null" => new NullLiteralExpr(NoSpan),
        _ => throw ComptimeFailure.NotYet($"std.syntax: the literal '{name}' has no form in the compiler yet"),
    };

    public Expr ExprOf(Value v, IrType t) => Within(v, t, ExprOfCore);

    private Expr ExprOfCore(Value v, IrType t)
    {
        var (name, f, ft) = Case(v, t);
        switch (name)
        {
            case "Literal": { var (ln, lf, _) = Case(f[0], ft[0]); return LiteralOf(ln, lf); }
            case "Name": { var (n, s) = IdentOf(f[0], ft[0]); return new IdentifierExpr(n, s) { QuotedFrom = MaybeStr(FieldOf(f[0], ft[0], "from")) }; }
            case "Implicit": { var (n, s) = IdentOf(f[0], ft[0]); return new ImplicitMemberExpr(n, s); }
            case "This": return new ThisExpr(NoSpan);
            case "At": return new AtIdentifierExpr(StrOf(f[0]), OptionOf(f[1]) is { } given ? Many(given, Inner(ft[1]), ExprOf) : null, NoSpan);
            case "Unary": return new UnaryExpr((UnaryOp)IndexIn(UnaryNames, Case(f[0], ft[0]).Name), BoxedExpr(f[1], ft[1]), NoSpan);
            case "Postfix": return new PostfixExpr(BoxedExpr(f[0], ft[0]), (PostfixOp)IndexIn(PostfixNames, Case(f[1], ft[1]).Name), NoSpan);
            case "Binary": return new BinaryExpr(BoxedExpr(f[0], ft[0]), BinaryOf(Case(f[1], ft[1]).Name), BoxedExpr(f[2], ft[2]), NoSpan);
            case "Assign":
                return new AssignExpr(BoxedExpr(f[0], ft[0]), OptionOf(f[1]) is { } op ? BinaryOf(Case(op, Inner(ft[1])).Name) : null, BoxedExpr(f[2], ft[2]), NoSpan);
            case "Range": return new RangeExpr(BoxedExpr(f[0], ft[0]), BoxedExpr(f[1], ft[1]), BoolOf(f[2]), NoSpan);
            case "SliceRange": return new SliceRangeExpr(MaybeBoxedExpr(f[0], ft[0]), MaybeBoxedExpr(f[1], ft[1]), BoolOf(f[2]), NoSpan);
            case "Cast": return new CastExpr(BoxedExpr(f[0], ft[0]), TypeOf(f[1], ft[1]), NoSpan);
            case "Is": return new TypeTestExpr(BoxedExpr(f[0], ft[0]), TypeOf(f[1], ft[1]), NoSpan);
            case "Call":
            {
                var args = ArrOf(f[1]);
                var at = Elem(ft[1]);
                var names = args.Select(a => MaybeStr(FieldOf(a, at, "name"))).ToArray();
                var values = args.Select(a => ExprOf(FieldOf(a, at, "value"), FieldTypeOf(at, "value"))).ToArray();
                var spreads = args.Select((a, i) => (a, i)).Where(p => BoolOf(FieldOf(p.a, at, "spread"))).Select(p => (values[p.i], NoSpan)).ToArray();
                var targs = Many(f[2], ft[2], TypeOf);
                return new CallExpr(BoxedExpr(f[0], ft[0]), values, NoSpan, targs.Length == 0 ? null : targs)
                {
                    ArgumentNames = names.All(n => n is null) ? null : names,
                    Spreads = spreads.Length == 0 ? null : spreads,
                };
            }
            case "Index": return new IndexExpr(BoxedExpr(f[0], ft[0]), BoxedExpr(f[1], ft[1]), NoSpan);
            case "Member": { var (m, ms) = IdentOf(f[1], ft[1]); return new MemberExpr(BoxedExpr(f[0], ft[0]), m, BoolOf(f[2]), NoSpan) { MemberSpan = ms }; }
            case "Array": return new ArrayLitExpr(Many(f[0], ft[0], ExprOf), NoSpan);
            case "Tuple": return new TupleLitExpr(Many(f[0], ft[0], ExprOf), NoSpan);
            case "Interpolated":
                return new InterpolatedStringExpr(Many(f[0], ft[0], (seg, st) =>
                {
                    var (sn, sf, sft) = Case(seg, st);
                    return sn == "Text" ? new InterpText(StrOf(sf[0]), NoSpan) : (InterpSegment)new InterpHole(ExprOf(sf[0], sft[0]), MaybeStr(sf[1]), NoSpan);
                }), NoSpan);
            case "Lambda":
            {
                var ps = Many(f[0], ft[0], (p, pt) =>
                {
                    var (n, s) = IdentOf(FieldOf(p, pt, "name"), FieldTypeOf(pt, "name"));
                    return new LambdaParam(n, Maybe(FieldOf(p, pt, "type"), FieldTypeOf(pt, "type"), TypeOf), s)
                    {
                        NameSpan = s,
                        Pattern = Maybe(FieldOf(p, pt, "pattern"), FieldTypeOf(pt, "pattern"), PatternOf),
                        IsPlace = BoolOf(FieldOf(p, pt, "isPlace")),
                        Implicit = BoolOf(FieldOf(p, pt, "isImplicit")),
                    };
                });
                var (bp, bt) = Unbox(f[3], ft[3]);
                return new LambdaExpr(ps, Maybe(f[1], ft[1], TypeOf), BodyOf(bp, bt), NoSpan)
                {
                    Throws = ThrowsOf(f[2], ft[2]),
                    Form = (LambdaForm)IndexIn(FormNames, Case(f[4], ft[4]).Name),
                };
            }
            case "LetCond": return new LetCondExpr(PatternOf(f[0], ft[0]), BoxedExpr(f[1], ft[1]), NoSpan);
            case "If": return new IfExpr(BoxedExpr(f[0], ft[0]), BoxedExpr(f[1], ft[1]), BoxedExpr(f[2], ft[2]), NoSpan);
            case "Match": return new MatchExpr(BoxedExpr(f[0], ft[0]), ArmsOf(f[1], ft[1]), NoSpan);
            case "Block": return new BlockExpr(BlockOf(f[0], ft[0]), NoSpan);
            case "Loop": return new LoopExpr(BlockOf(f[0], ft[0]), NoSpan) { Label = MaybeStr(f[1]) };
            case "StructInit":
                return new StructInitExpr(StringsOf(f[0]), Many(f[1], ft[1], TypeOf), FieldInitsOf(f[2], ft[2]), NoSpan) { NameSpan = NoSpan, IsImplicit = BoolOf(f[3]) };
            case "TypePath": return new TypePathExpr(StringsOf(f[0]), Many(f[1], ft[1], TypeOf), NoSpan) { NameSpan = NoSpan };
            case "With":
                return new WithExpr(BoxedExpr(f[0], ft[0]), Many(f[1], ft[1], (w, wt) =>
                    new WithField(StringsOf(FieldOf(w, wt, "path")), ExprOf(FieldOf(w, wt, "value"), FieldTypeOf(wt, "value")), NoSpan)), NoSpan);
            case "Comptime": return new ComptimeExpr(BoxedExpr(f[0], ft[0]), NoSpan);
            case "Throw": return new ThrowExpr(BoxedExpr(f[0], ft[0]), NoSpan);
            case "Try":
                return new TryExpr(BoxedExpr(f[0], ft[0]), NoSpan)
                {
                    KeywordSpan = NoSpan,
                    Kind = (TryKind)IndexIn(TryNames, Case(f[1], ft[1]).Name),
                    Catches = CatchesOf(f[2], ft[2]),
                };
            case "Macro":
                return new MacroCallExpr(StrOf(f[0]), Many(f[1], ft[1], ExprOf), Maybe(f[2], ft[2], BlockOf), NoSpan) { NameSpan = NoSpan, BangSpan = NoSpan };
            default: throw ComptimeFailure.NotYet($"std.syntax: the expression '{name}' has no form in the compiler yet");
        }
    }

    private ThrowsClause? ThrowsOf(Value v, IrType t) => OptionOf(v) is { } inner ? new ThrowsClause(Many(inner, Inner(t), TypeOf), NoSpan) : null;

    private StructInitField[] FieldInitsOf(Value v, IrType t) => Many(v, t, (fi, fit) =>
    {
        var (n, s) = IdentOf(FieldOf(fi, fit, "name"), FieldTypeOf(fit, "name"));
        return new StructInitField(n, ExprOf(FieldOf(fi, fit, "value"), FieldTypeOf(fit, "value")), s)
            { NameSpan = s, IsShorthand = BoolOf(FieldOf(fi, fit, "isShorthand")) };
    });

    private Node BodyOf(Value v, IrType t)
    {
        var (name, f, ft) = Case(v, t);
        return name == "Block" ? BlockOf(f[0], ft[0]) : BoxedExpr(f[0], ft[0]);
    }

    private MatchArm[] ArmsOf(Value v, IrType t) => Many(v, t, (a, at) =>
        new MatchArm(PatternOf(FieldOf(a, at, "pattern"), FieldTypeOf(at, "pattern")), Maybe(FieldOf(a, at, "guard"), FieldTypeOf(at, "guard"), ExprOf),
            BodyOf(FieldOf(a, at, "body"), FieldTypeOf(at, "body")), NoSpan));

    private CatchClause[] CatchesOf(Value v, IrType t) => Many(v, t, (c, ct) =>
    {
        var (n, s) = MaybeIdentOf(FieldOf(c, ct, "name"), FieldTypeOf(ct, "name"));
        return new CatchClause(n, Maybe(FieldOf(c, ct, "type"), FieldTypeOf(ct, "type"), TypeOf), BlockOf(FieldOf(c, ct, "body"), FieldTypeOf(ct, "body")), NoSpan)
        {
            NameSpan = s,
            BindingTypes = Many(FieldOf(c, ct, "types"), FieldTypeOf(ct, "types"), TypeOf),
            ExpressionBody = BoolOf(FieldOf(c, ct, "isExpr")),
        };
    });

    public Block BlockOf(Value v, IrType t) => Within(v, t, BlockOfCore);

    private Block BlockOfCore(Value v, IrType t)
    {
        return new Block(Many(FieldOf(v, t, "stmts"), FieldTypeOf(t, "stmts"), StmtOf), NoSpan);
    }

    public Stmt StmtOf(Value v, IrType t) => Within(v, t, StmtOfCore);

    private Stmt StmtOfCore(Value v, IrType t)
    {
        var (name, f, ft) = Case(v, t);
        switch (name)
        {
            case "Expr": return new ExprStmt(ExprOf(f[0], ft[0]), NoSpan);
            case "Tail": return new TailExprStmt(ExprOf(f[0], ft[0]), NoSpan);
            case "Let":
            {
                var (n, s) = IdentOf(f[1], ft[1]);
                var binding = new BindingStmt(BoolOf(f[0]), n, Maybe(f[2], ft[2], TypeOf), Maybe(f[3], ft[3], ExprOf), NoSpan) { NameSpan = s };
                if (!BoolOf(f[4])) return binding;
                // 'using let': the close the parser writes beside the binding (Parser.ParseUsing)
                var close = new CallExpr(new MemberExpr(new IdentifierExpr(n, NoSpan), "close", false, NoSpan) { MemberSpan = NoSpan }, [], NoSpan);
                return binding with
                {
                    IsMutable = false,
                    Cleanup = new DeferStmt(new ExprStmt(new TryExpr(close, NoSpan) { KeywordSpan = NoSpan }, NoSpan), NoSpan),
                };
            }
            case "LetPattern":
            {
                var pattern = PatternOf(f[1], ft[1]);
                var type = Maybe(f[2], ft[2], TypeOf);
                var init = ExprOf(f[3], ft[3]);
                var els = Maybe(f[4], ft[4], BlockOf);
                return pattern is TuplePattern tp && els is null
                    ? new DestructuringStmt(BoolOf(f[0]), tp, type, init, NoSpan)
                    : new LetPatternStmt(BoolOf(f[0]), pattern, type, init, els, NoSpan);
            }
            case "If":
            {
                Stmt? els = null;
                if (OptionOf(f[2]) is { } e) { var (p, pt) = Unbox(e, Inner(ft[2])); els = StmtOf(p, pt); }
                return new IfStmt(ExprOf(f[0], ft[0]), BlockOf(f[1], ft[1]), els, NoSpan) { Comptime = BoolOf(f[3]) };
            }
            case "While": return new WhileStmt(ExprOf(f[0], ft[0]), BlockOf(f[1], ft[1]), NoSpan) { Label = MaybeStr(f[2]) };
            case "DoWhile": return new DoWhileStmt(BlockOf(f[0], ft[0]), ExprOf(f[1], ft[1]), NoSpan) { Label = MaybeStr(f[2]) };
            case "For":
            {
                var (n, s) = IdentOf(f[0], ft[0]);
                return new ForInStmt(n, ExprOf(f[2], ft[2]), BlockOf(f[3], ft[3]), NoSpan)
                {
                    NameSpan = s, Pattern = Maybe(f[1], ft[1], PatternOf), Comptime = BoolOf(f[4]), Label = MaybeStr(f[5]),
                };
            }
            case "Break": return new BreakStmt(NoSpan) { Label = MaybeStr(f[0]), Value = Maybe(f[1], ft[1], ExprOf) };
            case "Continue": return new ContinueStmt(NoSpan) { Label = MaybeStr(f[0]) };
            case "Return": return new ReturnStmt(Maybe(f[0], ft[0], ExprOf), NoSpan);
            case "Yield": return new YieldStmt(Maybe(f[0], ft[0], ExprOf), NoSpan);
            case "Defer": { var (p, pt) = Unbox(f[0], ft[0]); return new DeferStmt(StmtOf(p, pt), NoSpan); }
            case "Throw": return new ThrowStmt(ExprOf(f[0], ft[0]), NoSpan);
            case "Match": return new MatchStmt(ExprOf(f[0], ft[0]), ArmsOf(f[1], ft[1]), NoSpan);
            case "Try": return new TryStmt(BlockOf(f[0], ft[0]), CatchesOf(f[1], ft[1]), NoSpan);
            case "Block": return BlockOf(f[0], ft[0]);
            default: throw ComptimeFailure.NotYet($"std.syntax: the statement '{name}' has no form in the compiler yet");
        }
    }

    public Pattern PatternOf(Value v, IrType t) => Within(v, t, PatternOfCore);

    private Pattern PatternOfCore(Value v, IrType t)
    {
        var (name, f, ft) = Case(v, t);
        switch (name)
        {
            case "Wildcard": return new WildcardPattern(NoSpan);
            case "Literal": return new LiteralPattern(BoxedExpr(f[0], ft[0]), NoSpan);
            case "Binding": { var (n, s) = IdentOf(f[0], ft[0]); return new BindingPattern(n, s); }
            case "TypePat": { var (n, s) = MaybeIdentOf(f[0], ft[0]); return new TypePattern(n, TypeOf(f[1], ft[1]), NoSpan) { NameSpan = s }; }
            case "TypeSet": { var (n, s) = MaybeIdentOf(f[0], ft[0]); return new TypeSetPattern(n, Many(f[1], ft[1], TypeOf), NoSpan) { NameSpan = s }; }
            case "Variant":
                return new VariantPattern(StringsOf(f[0]),
                    OptionOf(f[1]) is { } elems ? Many(elems, Inner(ft[1]), PatternOf) : null,
                    OptionOf(f[2]) is { } fields ? Many(fields, Inner(ft[2]), (fp, fpt) =>
                    {
                        var (n, s) = IdentOf(FieldOf(fp, fpt, "name"), FieldTypeOf(fpt, "name"));
                        return new FieldPattern(n, Maybe(FieldOf(fp, fpt, "pattern"), FieldTypeOf(fpt, "pattern"), PatternOf), s);
                    }) : null, NoSpan) { IsImplicit = BoolOf(f[3]) };
            case "Tuple": return new TuplePattern(Many(f[0], ft[0], PatternOf), NoSpan);
            case "Range": return new RangePattern(BoxedExpr(f[0], ft[0]), BoxedExpr(f[1], ft[1]), BoolOf(f[2]), NoSpan);
            case "Or": return new OrPattern(Many(f[0], ft[0], PatternOf), NoSpan);
            case "Array": return new ArrayPattern(Many(f[0], ft[0], PatternOf), NoSpan);
            case "Rest": { var (n, s) = MaybeIdentOf(f[0], ft[0]); return new RestPattern(n, NoSpan) { NameSpan = s }; }
            default: throw ComptimeFailure.NotYet($"std.syntax: the pattern '{name}' has no form in the compiler yet");
        }
    }

    public TypeNode TypeOf(Value v, IrType t) => Within(v, t, TypeOfCore);

    private TypeNode TypeOfCore(Value v, IrType t)
    {
        var (name, f, ft) = Case(v, t);
        switch (name)
        {
            case "Named": return new NamedType(StringsOf(f[0]), Many(f[1], ft[1], TypeOf), NoSpan) { NameSpan = NoSpan, ArgumentNames = NullableStrings(f[2]), QuotedFrom = MaybeStr(f[3]) };
            case "Optional": return new NullableType(BoxedType(f[0], ft[0]), NoSpan);
            case "Array": return new ArrayType(BoxedType(f[0], ft[0]), NoSpan) { Length = OptionOf(f[1]) is { } len ? (int)len.AsInt : null };
            case "Tuple": return new TupleType(Many(f[0], ft[0], TypeOf), NoSpan) { Labels = NullableStrings(f[1]) };
            case "Fn":
                return new FunctionType(Many(f[0], ft[0], TypeOf), BoxedType(f[1], ft[1]), NoSpan)
                    { Throws = ThrowsOf(f[2], ft[2]), Places = [.. ArrOf(f[3]).Select(BoolOf)] };
            case "Throwing": return new ThrowingType(BoxedType(f[0], ft[0]), OptionOf(f[1]) is { } th ? BoxedType(th, Inner(ft[1])) : null, NoSpan);
            default: throw ComptimeFailure.NotYet($"std.syntax: the type '{name}' has no form in the compiler yet");
        }
    }

    private AttributeNode[] AttributesOf(Value v, IrType t) => Many(v, t, (a, at) =>
    {
        var fields = FieldInitsOf(FieldOf(a, at, "fields"), FieldTypeOf(at, "fields"));
        return new AttributeNode(StringsOf(FieldOf(a, at, "path")), [.. fields.Where(f => f.Name.Length > 0)], NoSpan)
            { PathSpan = NoSpan, NameSpan = NoSpan, Positional = fields.FirstOrDefault(f => f.Name.Length == 0)?.Value };
    });

    private GenericParam[] GenericsOf(Value v, IrType t) => Many(v, t, (g, gt) =>
    {
        var (n, s) = IdentOf(FieldOf(g, gt, "name"), FieldTypeOf(gt, "name"));
        return new GenericParam(n, Many(FieldOf(g, gt, "constraints"), FieldTypeOf(gt, "constraints"), TypeOf), s)
            { NameSpan = s, Default = Maybe(FieldOf(g, gt, "default"), FieldTypeOf(gt, "default"), TypeOf) };
    });

    private Param[] ParamsOf(Value v, IrType t) => Many(v, t, (p, pt) =>
    {
        var (n, s) = IdentOf(FieldOf(p, pt, "name"), FieldTypeOf(pt, "name"));
        return new Param(BoolOf(FieldOf(p, pt, "isParams")), n, TypeOf(FieldOf(p, pt, "type"), FieldTypeOf(pt, "type")),
            Maybe(FieldOf(p, pt, "default"), FieldTypeOf(pt, "default"), ExprOf), NoSpan)
        {
            NameSpan = s,
            IsPlace = BoolOf(FieldOf(p, pt, "isPlace")),
            IsEllipsis = BoolOf(FieldOf(p, pt, "isEllipsis")),
            Attributes = AttributesOf(FieldOf(p, pt, "attributes"), FieldTypeOf(pt, "attributes")),
        };
    });

    private VisibilityWord VisibilityOf(Value v, IrType t) => Case(v, t).Name switch
    {
        "Pub" => VisibilityWord.Pub, "Internal" => VisibilityWord.Internal, "Private" => VisibilityWord.Private, _ => VisibilityWord.None,
    };

    private FunctionDecl FnOf(Value v, IrType t)
    {
        Value F(string n) => FieldOf(v, t, n);
        IrType T(string n) => FieldTypeOf(t, n);
        var (name, nameSpan) = IdentOf(F("name"), T("name"));
        var abi = MaybeStr(F("externAbi"));
        return new FunctionDecl(VisibilityOf(F("visibility"), T("visibility")), BoolOf(F("isMut")), BoolOf(F("isStatic")), name,
            GenericsOf(F("generics"), T("generics")), ParamsOf(F("params"), T("params")), Maybe(F("ret"), T("ret"), TypeOf), ThrowsOf(F("throws"), T("throws")),
            Maybe(F("body"), T("body"), BlockOf), NoSpan)
        {
            NameSpan = nameSpan,
            IsInline = BoolOf(F("isInline")),
            ReturnGrouped = BoolOf(F("retGrouped")),
            Attributes = AttributesOf(F("attributes"), T("attributes")),
            Extern = abi is null ? null : new ExternSpec(abi, MaybeStr(F("externSymbol")), NoSpan) { AbiSpan = NoSpan },
        };
    }

    private FieldDecl FieldDeclOf(Value v, IrType t)
    {
        Value F(string n) => FieldOf(v, t, n);
        IrType T(string n) => FieldTypeOf(t, n);
        var (name, nameSpan) = IdentOf(F("name"), T("name"));
        return new FieldDecl(name, TypeOf(F("type"), T("type")), Maybe(F("default"), T("default"), ExprOf), NoSpan)
        {
            NameSpan = nameSpan,
            IsVar = BoolOf(F("isVar")),
            Visibility = VisibilityOf(F("visibility"), T("visibility")),
            Attributes = AttributesOf(F("attributes"), T("attributes")),
        };
    }

    private AssociatedTypeDecl AssocOf(Value v, IrType t)
    {
        var (name, nameSpan) = IdentOf(FieldOf(v, t, "name"), FieldTypeOf(t, "name"));
        return new AssociatedTypeDecl(name, Maybe(FieldOf(v, t, "type"), FieldTypeOf(t, "type"), TypeOf), NoSpan)
            { NameSpan = nameSpan, Bounds = Many(FieldOf(v, t, "bounds"), FieldTypeOf(t, "bounds"), TypeOf) };
    }

    private (BindingStmt Binding, VisibilityWord Visibility, bool IsStatic, AttributeNode[] Attributes) LetOf(Value v, IrType t)
    {
        Value F(string n) => FieldOf(v, t, n);
        IrType T(string n) => FieldTypeOf(t, n);
        var (name, nameSpan) = IdentOf(F("name"), T("name"));
        var binding = new BindingStmt(BoolOf(F("isVar")), name, Maybe(F("type"), T("type"), TypeOf), Maybe(F("init"), T("init"), ExprOf), NoSpan) { NameSpan = nameSpan };
        return (binding, VisibilityOf(F("visibility"), T("visibility")), BoolOf(F("isStatic")), AttributesOf(F("attributes"), T("attributes")));
    }

    private StaticBindingDecl StaticOf(Value v, IrType t)
    {
        var (binding, visibility, _, attributes) = LetOf(v, t);
        return new StaticBindingDecl(visibility, binding, NoSpan) { Attributes = attributes };
    }

    public Decl DeclOf(Value v, IrType t)
    {
        var (_, f, ft) = Case(v, t);
        return Within(f[0], ft[0], (p, pt) => DeclOfCore(v, t, p, pt));
    }

    private Decl DeclOfCore(Value v, IrType t, Value p, IrType pt)
    {
        var (name, f, ft) = Case(v, t);
        Value F(string n) => FieldOf(p, pt, n);
        IrType T(string n) => FieldTypeOf(pt, n);
        switch (name)
        {
            case "Fn": return FnOf(p, pt);
            case "Struct":
            case "Class":
            {
                var (n, s) = IdentOf(F("name"), T("name"));
                var visibility = VisibilityOf(F("visibility"), T("visibility"));
                var generics = GenericsOf(F("generics"), T("generics"));
                var interfaces = Many(F("interfaces"), T("interfaces"), TypeOf);
                var delegates = NullableStrings(F("delegates"));
                var members = Many(F("members"), T("members"), DeclOf);
                var attributes = AttributesOf(F("attributes"), T("attributes"));
                return name == "Struct"
                    ? new StructDecl(visibility, n, generics, interfaces, members, NoSpan) { NameSpan = s, Attributes = attributes, Delegates = delegates }
                    : new ClassDecl(visibility, n, generics, interfaces, members, NoSpan) { NameSpan = s, Attributes = attributes, Delegates = delegates };
            }
            case "Enum":
            {
                var (n, s) = IdentOf(F("name"), T("name"));
                var variants = Many(F("variants"), T("variants"), (vv, vt) =>
                {
                    var (vn, vs) = IdentOf(FieldOf(vv, vt, "name"), FieldTypeOf(vt, "name"));
                    return new EnumVariant(vn,
                        OptionOf(FieldOf(vv, vt, "tuple")) is { } tu ? Many(tu, Inner(FieldTypeOf(vt, "tuple")), TypeOf) : null,
                        OptionOf(FieldOf(vv, vt, "fields")) is { } fs ? Many(fs, Inner(FieldTypeOf(vt, "fields")), FieldDeclOf) : null,
                        NoSpan)
                        { NameSpan = vs, Attributes = AttributesOf(FieldOf(vv, vt, "attributes"), FieldTypeOf(vt, "attributes")) };
                });
                return new EnumDecl(VisibilityOf(F("visibility"), T("visibility")), n, GenericsOf(F("generics"), T("generics")), Many(F("interfaces"), T("interfaces"), TypeOf),
                    variants, Many(F("methods"), T("methods"), FnOf), NoSpan)
                {
                    NameSpan = s,
                    Types = Many(F("types"), T("types"), AssocOf),
                    Statics = Many(F("statics"), T("statics"), StaticOf),
                    Attributes = AttributesOf(F("attributes"), T("attributes")),
                };
            }
            case "Interface":
            {
                var (n, s) = IdentOf(F("name"), T("name"));
                return new InterfaceDecl(VisibilityOf(F("visibility"), T("visibility")), n, GenericsOf(F("generics"), T("generics")), Many(F("interfaces"), T("interfaces"), TypeOf),
                    Many(F("members"), T("members"), FnOf), NoSpan)
                {
                    NameSpan = s,
                    IsSealed = BoolOf(F("isSealed")),
                    Types = Many(F("types"), T("types"), AssocOf),
                    Statics = Many(F("statics"), T("statics"), StaticOf),
                    Attributes = AttributesOf(F("attributes"), T("attributes")),
                };
            }
            case "Extend":
                return new ExtendDecl(VisibilityOf(F("visibility"), T("visibility")), TypeOf(F("target"), T("target")), Many(F("interfaces"), T("interfaces"), TypeOf),
                    Many(F("methods"), T("methods"), FnOf), NoSpan)
                {
                    Generics = GenericsOf(F("generics"), T("generics")),
                    Types = Many(F("types"), T("types"), AssocOf),
                    Statics = Many(F("statics"), T("statics"), StaticOf),
                    Attributes = AttributesOf(F("attributes"), T("attributes")),
                };
            case "Let":
            {
                var (binding, visibility, isStatic, attributes) = LetOf(p, pt);
                return isStatic
                    ? new StaticBindingDecl(visibility, binding, NoSpan) { Attributes = attributes }
                    : new GlobalBindingDecl(visibility, binding, NoSpan) { Attributes = attributes };
            }
            case "Field": return FieldDeclOf(p, pt);
            case "AssocType": return AssocOf(p, pt);
            case "Alias":
            {
                var (n, s) = IdentOf(F("name"), T("name"));
                return new TypeAliasDecl(VisibilityOf(F("visibility"), T("visibility")), BoolOf(F("isOpaque")), n, TypeOf(F("aliased"), T("aliased")), NoSpan) { NameSpan = s };
            }
            case "Import":
            {
                var names = OptionOf(F("names")) is { } ns ? StringsOf(ns) : null;
                var renames = NullableStrings(F("renames"));
                var alias = MaybeStr(F("alias"));
                ImportClause? clause = names is not null
                    ? new ImportSelective(names, NoSpan) { NameSpans = [.. names.Select(_ => NoSpan)], Renames = renames }
                    : alias is not null ? new ImportAlias(alias, NoSpan) : null;
                return new ImportDecl(StringsOf(F("path")), clause, NoSpan) { IsPublic = BoolOf(F("isPub")) };
            }
            default: throw ComptimeFailure.NotYet($"std.syntax: the declaration '{name}' has no form in the compiler yet");
        }
    }
}
