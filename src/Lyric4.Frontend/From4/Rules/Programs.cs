using Lyric4.AST;
using Lyric4.Resolver;
using Lyric4.Sema;

namespace Lyric4.From4.Rules;

/// <summary>K10: a single file without <c>main</c> was a library to Lyric 4, which a host loaded as
/// bytecode. Lyric 5 asks a single file for its entry; a library is a package.</summary>
internal sealed class NoEntry : IRule
{
    public string Id => "K10";

    public void Apply(RuleContext context)
    {
        if (context.LyricJson is not null) return;
        foreach (var module in context.Modules)
            if (!module.Declarations.OfType<FunctionDecl>().Any(f => f.Name == "main"))
                context.Note(Id, "LYR-MIG0110", NoteKind.Hand, context.PathOf(module),
                    "this file has no 'main': Lyric 4 ran it as a library of a host, and Lyric 5 asks a single file for its entry",
                    "make it a package with a library target, as 'lyric new --lib' does, or give it a 'main'");
    }
}

/// <summary>S12: <c>fn main(args: string[])</c> took the program's arguments. In Lyric 5 <c>main</c>
/// takes none and they come from <c>std.os</c>, the program's own name first, which Lyric 4 left out.</summary>
internal sealed class EntryArguments : IRule
{
    public string Id => "S12";

    /// <summary>The <c>main</c> of a module that takes its arguments.</summary>
    public static FunctionDecl? Of(Module module) =>
        module.Declarations.OfType<FunctionDecl>().FirstOrDefault(f => f is { Name: "main", Parameters.Length: 1, Body: not null });

    /// <summary>The name <c>std.os</c> goes by in a module: its alias, or the last part of the path.
    /// An import that only selects binds no name, so what this needs is another one.</summary>
    public static string Qualifier(Module module) =>
        module.Declarations.OfType<ImportDecl>()
            .Where(d => string.Join('.', d.Path) == "std.os" && d.Clause is not ImportSelective)
            .Select(d => d.Clause is ImportAlias { Alias: var alias } ? alias : "os")
            .FirstOrDefault() ?? "os";

    public void Apply(RuleContext context)
    {
        foreach (var module in context.Modules)
        {
            if (Of(module) is not { Body: { } body } main) continue;
            var text = context.TextOf(module);
            var parameter = main.Parameters[0];
            context.Replace(Id, parameter.Span, "");
            var indent = body.Statements.Length > 0 ? Tree.IndentAt(text, body.Statements[0].Span.Start) : Tree.IndentAt(text, main.Span.Start) + "    ";
            var open = body.Span.Start + 1;
            context.Replace(Id, body.Span.File, open, open, $"\n{indent}let {parameter.Name} = {Qualifier(module)}.args()[1..].iter().toArray();");
        }
    }
}

/// <summary>T19, T20: what a method of Lyric 4 could leave unsaid. A method that answers a public
/// interface for a public type is <c>pub</c> in Lyric 5 (T19). A method of a class that writes the
/// value's own state is <c>mut fn</c>, and so is one that calls such a method on <c>this</c> (T20);
/// where an interface declares the method without <c>mut</c>, the two have to change together, and
/// that is said by hand. Both write before the same keyword, so they are one rule.</summary>
internal sealed class MethodModifiers : IRule
{
    public string Id => "T19";

    public void Apply(RuleContext context)
    {
        foreach (var module in context.Modules)
        {
            var text = context.TextOf(module);
            foreach (var node in Tree.Below(module))
            {
                (bool, TypeNode[], FunctionDecl[])? type = node switch
                {
                    StructDecl s => (s.IsPublic, s.Interfaces, s.Members.OfType<FunctionDecl>().ToArray()),
                    ClassDecl c => (c.IsPublic, c.Interfaces, c.Members.OfType<FunctionDecl>().ToArray()),
                    EnumDecl e => (e.IsPublic, e.Interfaces, e.Methods),
                    _ => null,
                };
                if (type is not var (isPublic, interfaces, members)) continue;
                var answers = Answers(context, interfaces);
                var writing = node is ClassDecl ? Writing(context, members) : [];
                foreach (var method in members)
                {
                    var makePub = isPublic && !method.IsPublic && method.Body is not null && answers.TryGetValue(method.Name, out var answered) && answered.Public;
                    var makeMut = writing.Contains(method);
                    if (makeMut && answers.TryGetValue(method.Name, out var declared))
                    {
                        context.Note("T20", "LYR-MIG0320", NoteKind.Hand, method.NameSpan,
                            $"'{method.Name}' writes the object's state, and the interface '{declared.Interface}' declares it without 'mut'; Lyric 5 has a method that writes declared 'mut fn', and a conformance repeats what the interface says",
                            $"declare it 'mut fn' in '{declared.Interface}' and in every type that answers it");
                        makeMut = false;
                    }
                    if (!makePub && !makeMut) continue;
                    var keyword = text.LastIndexOf("fn", method.NameSpan.Start, StringComparison.Ordinal);
                    var before = keyword;
                    if (method.IsStatic || method.IsMut)
                    {
                        // 'pub' goes before the modifier that stands there
                        var word = method.IsStatic ? "static" : "mut";
                        before = text.LastIndexOf(word, keyword, StringComparison.Ordinal);
                    }
                    if (makePub && makeMut) context.Replace(Id, method.Span.File, keyword, keyword, "pub mut ");
                    else if (makePub) context.Replace(Id, method.Span.File, before, before, "pub ");
                    else context.Replace("T20", method.Span.File, keyword, keyword, "mut ");
                }
            }
        }
    }

    /// <summary>The methods the written interfaces declare, each with the interface and whether it is
    /// public; a parent's come along.</summary>
    private static Dictionary<string, (string Interface, bool Public)> Answers(RuleContext context, TypeNode[] interfaces)
    {
        var answers = new Dictionary<string, (string, bool)>(StringComparer.Ordinal);
        void Add(TypeNode written, HashSet<Symbol> seen)
        {
            if (written is not NamedType named || Tree.Target(context, named) is not TypeSymbol { Kind: TypeSymbolKind.Interface } symbol || !seen.Add(symbol)) return;
            if (symbol.Declaration is not InterfaceDecl declaration) return;
            foreach (var member in declaration.Members)
                answers.TryAdd(member.Name, (symbol.Name, symbol.Visibility == Lyric4.Resolver.Visibility.Public));
            foreach (var parent in declaration.Interfaces) Add(parent, seen);
        }
        foreach (var written in interfaces) Add(written, new HashSet<Symbol>(ReferenceEqualityComparer.Instance));
        return answers;
    }

    /// <summary>The methods of a class that write its state without saying so, and those that call one
    /// of them on <c>this</c>, until no more come.</summary>
    private static HashSet<FunctionDecl> Writing(RuleContext context, FunctionDecl[] members)
    {
        var writing = new HashSet<FunctionDecl>(ReferenceEqualityComparer.Instance);
        var candidates = members.Where(m => m is { IsMut: false, IsStatic: false, Body: not null }).ToList();
        for (var changed = true; changed;)
        {
            changed = false;
            foreach (var method in candidates)
                if (!writing.Contains(method) && Tree.Below(method.Body!).Any(n => Writes(context, n, writing)) && writing.Add(method))
                    changed = true;
        }
        return writing;
    }

    private static bool Writes(RuleContext context, Node node, HashSet<FunctionDecl> writing) => node switch
    {
        AssignExpr a => ReachesThis(context, a.Target),
        PostfixExpr { Operator: PostfixOp.Inc or PostfixOp.Dec } p => ReachesThis(context, p.Operand),
        UnaryExpr { Operator: UnaryOp.PreInc or UnaryOp.PreDec } u => ReachesThis(context, u.Operand),
        CallExpr { Callee: MemberExpr callee } when Tree.Target(context, callee) is FunctionSymbol { IsStatic: false } function
            && (function.IsMut || function.Declaration is FunctionDecl declared && writing.Contains(declared)) => ReachesThis(context, callee.Target, viaCall: true),
        _ => false,
    };

    /// <summary>Whether writing an expression writes the object <c>this</c> holds: a field of it, or a
    /// field of a struct in it. A class in a field is another object, whose state is its own; a call
    /// on <c>this</c> itself counts only for a method, which <paramref name="viaCall"/> says.</summary>
    private static bool ReachesThis(RuleContext context, Expr target, bool viaCall = false)
    {
        if (target is ThisExpr) return viaCall;
        while (target is MemberExpr member)
        {
            if (member.Target is ThisExpr) return !viaCall || IsStruct(context.Model.Types.TypeOf(member));
            if (!IsStruct(context.Model.Types.TypeOf(member.Target))) return false;
            target = member.Target;
        }
        return false;
    }

    internal static bool IsStruct(LyrType type) => type switch
    {
        NamedRef { Symbol.Kind: TypeSymbolKind.Struct } => true,
        GenericInstance { Definition.Kind: TypeSymbolKind.Struct } => true,
        _ => false,
    };
}

/// <summary>T21: a struct parameter was a copy the function could change, its caller seeing nothing.
/// In Lyric 5 a parameter is a <c>let</c>, and it cannot be bound anew under its own name, so the copy
/// gets a name of its own. Which name, and where the uses go, is for a person; Lyric 5 says the rest.</summary>
internal sealed class WrittenParameters : IRule
{
    public string Id => "T21";

    public void Apply(RuleContext context)
    {
        foreach (var module in context.Modules)
            foreach (var function in Tree.Below(module).OfType<FunctionDecl>())
            {
                if (function.Body is null) continue;
                var names = function.Parameters.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
                var told = new HashSet<string>(StringComparer.Ordinal);
                foreach (var node in Tree.Below(function.Body))
                {
                    var written = node switch
                    {
                        AssignExpr { Target: MemberExpr a } => Root(a),
                        PostfixExpr { Operator: PostfixOp.Inc or PostfixOp.Dec, Operand: MemberExpr p } => Root(p),
                        UnaryExpr { Operator: UnaryOp.PreInc or UnaryOp.PreDec, Operand: MemberExpr u } => Root(u),
                        CallExpr { Callee: MemberExpr callee } when Tree.Target(context, callee) is FunctionSymbol { IsMut: true, IsStatic: false } => Root(callee.Target),
                        _ => null,
                    };
                    if (written is not { } name || !names.Contains(name.Name) || !MethodModifiers.IsStruct(context.Model.Types.TypeOf(name)) || !told.Add(name.Name)) continue;
                    context.Note(Id, "LYR-MIG0321", NoteKind.Hand, node.Span,
                        $"the parameter '{name.Name}' is written; it was a copy in Lyric 4, and in Lyric 5 a parameter is a 'let'",
                        $"copy it into a 'var' of another name, 'var copy = {name.Name};', and write that");
                }
            }
    }

    /// <summary>The name an expression reaches through its struct fields.</summary>
    private static IdentifierExpr? Root(Expr expr)
    {
        while (expr is MemberExpr member) expr = member.Target;
        return expr as IdentifierExpr;
    }
}

/// <summary>T22: an enum of Lyric 4 could hold itself, its payloads being references. An enum of Lyric 5
/// is a value and would have no size. A class in the cycle ends it, and patterns reach through a class
/// of the program's own, which makes that a rewrite of the matches for a person.</summary>
internal sealed class RecursiveEnums : IRule
{
    public string Id => "T22";

    public void Apply(RuleContext context)
    {
        foreach (var module in context.Modules)
            foreach (var declared in Tree.Below(module).OfType<EnumDecl>())
                if (Contains(context, declared, declared, new HashSet<Node>(ReferenceEqualityComparer.Instance)))
                    context.Note(Id, "LYR-MIG0322", NoteKind.Hand, declared.Span,
                        $"the enum '{declared.Name}' holds itself; an enum is a value in Lyric 5 and would have no size",
                        "put the recursive part behind a class: one of the program's own, whose fields a pattern can name, or 'Box<T>'");
    }

    private static bool Contains(RuleContext context, Node holder, EnumDecl wanted, HashSet<Node> seen)
    {
        if (!seen.Add(holder)) return false;
        IEnumerable<TypeNode> held = holder switch
        {
            EnumDecl e => e.Variants.SelectMany(v => (v.TupleFields ?? []).Concat((v.StructFields ?? []).Select(f => f.Type))),
            StructDecl s => s.Members.OfType<FieldDecl>().Select(f => f.Type),
            _ => [],
        };
        foreach (var type in held)
            foreach (var named in Named(type))
            {
                if (Tree.Target(context, named) is not TypeSymbol { Kind: TypeSymbolKind.Struct or TypeSymbolKind.Enum, Declaration: { } next }) continue;
                if (ReferenceEquals(next, wanted) || Contains(context, next, wanted, seen)) return true;
            }
        return false;
    }

    /// <summary>The types a type holds by value: itself, what an optional holds, a tuple's parts.</summary>
    private static IEnumerable<NamedType> Named(TypeNode type) => type switch
    {
        NamedType named => [named],
        NullableType { Inner: var inner } => Named(inner),
        TupleType { Elements: var elements } => elements.SelectMany(Named),
        _ => [],
    };
}

/// <summary>B16: <c>extern "dotnet"</c> bound a function of the .NET library. Lyric 5 binds C alone,
/// and the library it stood for is either in its own std or in another C library.</summary>
internal sealed class DotnetFunctions : IRule
{
    public string Id => "B16";

    public void Apply(RuleContext context)
    {
        foreach (var module in context.Modules)
            foreach (var function in Tree.Below(module).OfType<FunctionDecl>())
                if (function.Extern is { Abi: "dotnet" } foreign)
                    context.Note(Id, "LYR-MIG0616", NoteKind.Hand, foreign.AbiSpan,
                        $"'{function.Name}' binds a function of .NET, and Lyric 5 runs without it",
                        "use the function of std that does this, or bind a C library with 'extern \"C\"'");
    }
}
