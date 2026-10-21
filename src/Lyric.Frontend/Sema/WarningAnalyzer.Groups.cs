using System.Text;
using System.Text.RegularExpressions;
using Lyric.AST;
using Lyric.Core;
using Lyric.Resolver;

namespace Lyric.Sema;

/// <summary>
/// The lints of the groups <c>resources</c>, <c>concurrency</c>, <c>style</c>, <c>perf</c> and
/// <c>docs</c> (spec 16 §5). Narrow like the others: each says what it can see for certain, and is
/// silent where seeing it would take a flow analysis or the backend's layout.
/// </summary>
internal sealed partial class WarningAnalyzer
{
    private void RunGroupLints()
    {
        var threaded = false;
        foreach (var module in _comp.Modules)
        {
            if (_comp.Unwatched(module) || module.Path.Length == 0) continue;
            var ast = _comp.AstOf(module);
            if (ast.Declarations.OfType<ImportDecl>().Any(i => i.Path is ["std", "thread"])) threaded = true;
        }
        foreach (var module in _comp.Modules)
        {
            if (_comp.Unwatched(module) || module.Path.Length == 0) continue;
            var ast = _comp.AstOf(module);
            if (_comp.Sources.OriginOf(ast.Span.File) is not null) continue;
            WarnDeviceName(module, ast);
            WarnNaming(module, ast);
            if (threaded) WarnGlobalVars(ast);
            WarnMissingDocs(module, ast);
            WarnDocLinks(module, ast);
            foreach (var decl in ast.Declarations)
                foreach (var fn in FunctionsOf(decl))
                {
                    if (fn.Body is null || fn.IsMacro) continue;
                    WarnLargeParameters(fn);
                    WarnNeedlessReturn(fn.Body);
                    VisitHabits(fn.Body, loop: null);
                }
        }
    }

    // ─── style ────────────────────────────────────────────────────────────

    private static readonly Regex DeviceName = new("^(con|prn|aux|nul|com[0-9]|lpt[0-9])$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>reserved-device-name: a module whose name Windows keeps for a device — a file
    /// <c>con.lyr</c> cannot be made there, whatever its extension.</summary>
    private void WarnDeviceName(ModuleSymbol module, Module ast)
    {
        foreach (var segment in module.Path)
            if (DeviceName.IsMatch(segment))
            {
                _de.Report("LYR-SEM0216", Severity.Warning, new Span(ast.Span.File, 0, 0),
                    $"the module '{module.FullName}' is named '{segment}', a name Windows keeps for a device",
                    new DiagnosticNote("a file of that name cannot be made on Windows: name the module otherwise"));
                return;
            }
    }

    /// <summary>naming: types, their parameters and an enum's variants in PascalCase; functions,
    /// fields, parameters and locals in camelCase; modules in lower case. A leading <c>_</c> is
    /// left out of the reading, and an <c>extern</c> function keeps the name its library gives it.</summary>
    private void WarnNaming(ModuleSymbol module, Module ast)
    {
        var last = module.Path[^1];
        if (last.Any(char.IsUpper))
            _de.Report("LYR-SEM0215", Severity.Warning, new Span(ast.Span.File, 0, 0),
                $"the module '{module.FullName}' has capitals in its name: a module is named in lower case",
                new DiagnosticNote($"'{last.ToLowerInvariant()}'"));

        void Type(string name, Span at) { if (!Pascal(name)) Say(name, at, "a type", ToPascal(name), "PascalCase"); }
        void Value(string name, Span at, string what) { if (!Camel(name)) Say(name, at, what, ToCamel(name), "camelCase"); }
        void Say(string name, Span at, string what, string better, string form) =>
            _de.Report("LYR-SEM0215", Severity.Warning, at, $"'{name}' is {what}, and {what[2..]}s are named in {form}",
                new DiagnosticNote($"'{better}'"));
        void Generics(GenericParam[] generics) { foreach (var g in generics) Type(g.Name, g.NameSpan); }
        void Function(FunctionDecl f)
        {
            if (f.Extern is not null) return;
            Value(f.Name, f.NameSpan, "a function");
            Generics(f.Generics);
            foreach (var p in f.Parameters) Value(p.Name, p.NameSpan, "a parameter");
            if (f.Body is not null) Locals(f.Body);
        }
        void Members(Decl[] members)
        {
            foreach (var m in members)
                switch (m)
                {
                    case FieldDecl field: Value(field.Name, field.NameSpan, "a field"); break;
                    case FunctionDecl f: Function(f); break;
                    case StaticBindingDecl s: Value(s.Binding.Name, s.Binding.NameSpan, "a constant"); break;
                }
        }
        void Locals(Node node)
        {
            switch (node)
            {
                case BindingStmt b: Value(b.Name, b.NameSpan, "a local"); break;
                case ForInStmt f when f.NameSpan.End > f.NameSpan.Start: Value(f.Variable, f.NameSpan, "a local"); break;
                case CatchClause { BindingName: { } caught } c: Value(caught, c.NameSpan, "a local"); break;
                case LambdaExpr lambda:
                    foreach (var p in lambda.Parameters) if (p.NameSpan.End > p.NameSpan.Start) Value(p.Name, p.NameSpan, "a parameter");
                    break;
            }
            foreach (var child in AstChildren.Of(node)) Locals(child);
        }

        foreach (var decl in ast.Declarations)
            switch (decl)
            {
                case FunctionDecl f when !f.IsMacro: Function(f); break;
                case StructDecl s: Type(s.Name, s.NameSpan); Generics(s.Generics); Members(s.Members); break;
                case ClassDecl c: Type(c.Name, c.NameSpan); Generics(c.Generics); Members(c.Members); break;
                case InterfaceDecl i: Type(i.Name, i.NameSpan); Generics(i.Generics); foreach (var f in i.Members) Function(f); break;
                case EnumDecl e:
                    Type(e.Name, e.NameSpan);
                    Generics(e.Generics);
                    foreach (var v in e.Variants)
                    {
                        if (!Pascal(v.Name)) Say(v.Name, v.NameSpan, "a variant", ToPascal(v.Name), "PascalCase");
                        foreach (var field in v.StructFields ?? []) Value(field.Name, field.NameSpan, "a field");
                    }
                    foreach (var f in e.Methods) Function(f);
                    foreach (var s in e.Statics) Value(s.Binding.Name, s.Binding.NameSpan, "a constant");
                    break;
                case ExtendDecl x: foreach (var f in x.Methods) Function(f); break;
                case TypeAliasDecl t: Type(t.Name, t.NameSpan); break;
                case GlobalBindingDecl g: Value(g.Name, g.NameSpan, "a module binding"); break;
            }
    }

    private static string Bare(string name) => name.TrimStart('_');

    private static bool Pascal(string name) =>
        Bare(name) is var bare && (bare.Length == 0 || char.IsUpper(bare[0]) && !Underscored(bare));

    private static bool Camel(string name) =>
        Bare(name) is var bare && (bare.Length == 0 || char.IsLower(bare[0]) && !Underscored(bare));

    /// <summary>Whether a <c>_</c> joins words; one between two digits, <c>X86_64</c>, keeps a number apart.</summary>
    private static bool Underscored(string name)
    {
        for (var i = 0; i < name.Length; i++)
            if (name[i] == '_' && !(i > 0 && i + 1 < name.Length && char.IsDigit(name[i - 1]) && char.IsDigit(name[i + 1])))
                return true;
        return false;
    }

    /// <summary>The words of a name: split at <c>_</c>, a word in capitals read as a word.</summary>
    private static IEnumerable<string> Words(string name) =>
        Bare(name).Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.All(c => !char.IsLower(c)) ? w.ToLowerInvariant() : w);

    private static string ToPascal(string name) =>
        new string('_', name.Length - Bare(name).Length) + string.Concat(Words(name).Select(w => char.ToUpperInvariant(w[0]) + w[1..]));

    private static string ToCamel(string name)
    {
        var pascal = ToPascal(name);
        var lead = name.Length - Bare(name).Length;
        return pascal.Length > lead ? pascal[..lead] + char.ToLowerInvariant(pascal[lead]) + pascal[(lead + 1)..] : pascal;
    }

    /// <summary>needless-return: a <c>return;</c> that ends a body, which ends there anyway.</summary>
    private void WarnNeedlessReturn(Block body)
    {
        if (body.Statements is [.., ReturnStmt { Value: null } last])
            _de.Report("LYR-SEM0218", Severity.Warning, last.Span, "this 'return' ends a body that ends here anyway",
                new DiagnosticNote("take it out"));
    }

    /// <summary>The walk for the lints that read loops and calls: loop-instead-of-while-true,
    /// redundant-clone, string-concat-in-loop and boxed-interface-in-hot-loop.</summary>
    private void VisitHabits(Node node, Node? loop)
    {
        switch (node)
        {
            case LambdaExpr lambda:
                if (lambda.Body is Block block) WarnNeedlessReturn(block);
                // a lambda's body runs when it is called, not once per round of the loop it stands in
                VisitHabits(lambda.Body, loop: null);
                return;
            case WhileStmt { Condition: BoolLiteralExpr { Value: true } } w:
                _de.Report("LYR-SEM0219", Severity.Warning, new Span(w.Span.File, w.Span.Start, w.Condition.Span.End + 1),
                    "'while (true)' is a 'loop'", new DiagnosticNote("write 'loop { … }'"));
                break;
            case CallExpr { Callee: MemberExpr { Member: "clone" } member, Arguments: [] } call:
                WarnRedundantClone(call, member);
                break;
            case AssignExpr assign when loop is not null:
                WarnConcatInLoop(assign, loop);
                break;
            case CallExpr call when loop is not null:
                WarnBoxing(call);
                break;
        }
        var inner = node is WhileStmt or DoWhileStmt or ForInStmt or LoopExpr ? node : loop;
        foreach (var child in AstChildren.Of(node)) VisitHabits(child, inner);
    }

    /// <summary>redundant-clone: <c>clone()</c> of a value that an assignment copies whole —
    /// a number, a <c>bool</c>, a <c>char</c>, a <c>string</c> (it never changes), or a struct or
    /// enum made of only such.</summary>
    private void WarnRedundantClone(CallExpr call, MemberExpr member)
    {
        var type = _types.TypeOf(member.Target);
        if (type is ErrorType || !CopiedWhole(type, [])) return;
        _de.Report("LYR-SEM0217", Severity.Warning, call.Span,
            $"'clone()' of a '{TypeFacts.Display(type)}' makes what a copy makes",
            new DiagnosticNote("use the value itself: a copy of it is a value of its own"));
    }

    private bool CopiedWhole(LyrType type, HashSet<TypeSymbol> seen)
    {
        switch (type)
        {
            case PrimitiveType { Kind: not PrimitiveKind.Void }:
                return true;
            case Optional { Inner: var inner }:
                return CopiedWhole(inner, seen);
            case TupleOf { Elements: var elements }:
                return elements.All(e => CopiedWhole(e, seen));
            case NamedRef { Symbol: { Kind: TypeSymbolKind.Struct or TypeSymbolKind.Enum } symbol }:
                if (!seen.Add(symbol)) return false;
                if (symbol.Declaration is EnumDecl e)
                    return e.Variants.All(v => (v.TupleFields ?? []).Length == 0 && (v.StructFields ?? []).Length == 0);
                return _types.MetaFields is { } fields && fields(type, symbol.Home).All(f => CopiedWhole(f.Type, seen));
            default:
                return false;
        }
    }

    // ─── perf ─────────────────────────────────────────────────────────────

    /// <summary>string-concat-in-loop: a string grown by <c>+</c> in a loop, kept across its rounds —
    /// each round copies all that came before.</summary>
    private void WarnConcatInLoop(AssignExpr assign, Node loop)
    {
        static Expr Leftmost(Expr e) => e is BinaryExpr { Operator: BinaryOp.Add, Left: var left } ? Leftmost(left) : e;
        var grows = assign.Operator == BinaryOp.Add
                    || assign.Operator is null && assign.Value is BinaryExpr { Operator: BinaryOp.Add } && Same(assign.Target, Leftmost(assign.Value));
        if (!grows || !TypeFacts.IsString(_types.TypeOf(assign.Target))) return;
        // a local of the round itself starts over each time
        if (assign.Target is IdentifierExpr id && _types.RefOf(id) is LocalSymbol { Declaration: { } declared }
            && declared.Span.Start >= loop.Span.Start && declared.Span.End <= loop.Span.End) return;
        _de.Report("LYR-SEM0220", Severity.Warning, assign.Span,
            "this grows a string with '+' in a loop: each round copies all of it",
            new DiagnosticNote("append to a 'StringBuilder' and take its 'toString()' after the loop"));
    }

    /// <summary>boxed-interface-in-hot-loop: in a loop, a value of a struct or a number passed where
    /// the parameter is an interface — each round copies it into a box.</summary>
    private void WarnBoxing(CallExpr call)
    {
        if (_types.TypeOf(call.Callee) is not FnType fn) return;
        for (var i = 0; i < call.Arguments.Length && i < fn.Parameters.Length; i++)
        {
            if (TypeFacts.KindOf(fn.Parameters[i]) is not TypeSymbolKind.Interface) continue;
            var argument = _types.TypeOf(call.Arguments[i]);
            if (argument is PrimitiveType { Kind: not (PrimitiveKind.String or PrimitiveKind.Void) }
                || TypeFacts.KindOf(argument) is TypeSymbolKind.Struct or TypeSymbolKind.Enum)
                _de.Report("LYR-SEM0222", Severity.Warning, call.Arguments[i].Span,
                    $"in a loop, this '{TypeFacts.Display(argument)}' is boxed as a '{TypeFacts.Display(fn.Parameters[i])}' each round",
                    new DiagnosticNote("take a type parameter constrained by the interface instead, or box it once before the loop"));
        }
    }

    /// <summary>large-value-copy: a parameter of a struct type larger than 64 bytes, copied at each
    /// call. The size counted is a floor — no padding, a reference as one word — so what is said is so.</summary>
    private void WarnLargeParameters(FunctionDecl fn)
    {
        foreach (var p in fn.Parameters)
        {
            if (!_paramSymbols.TryGetValue(p, out var symbol) || symbol is not ParameterSymbol { IsPlace: false, Type: var type }) continue;
            if (TypeFacts.KindOf(type) is not TypeSymbolKind.Struct) continue;
            var size = SizeFloor(type, []);
            if (size > 64)
                _de.Report("LYR-SEM0221", Severity.Warning, p.NameSpan,
                    $"'{p.Name}' is a '{TypeFacts.Display(type)}' of at least {size} bytes, copied at each call",
                    new DiagnosticNote("make the type a class, or pass a smaller part of it"));
        }
    }

    private int SizeFloor(LyrType type, HashSet<TypeSymbol> seen) => type switch
    {
        PrimitiveType { Kind: PrimitiveKind.Int or PrimitiveKind.Uint or PrimitiveKind.Float } => 8,
        PrimitiveType { Kind: PrimitiveKind.Int32 or PrimitiveKind.Uint32 or PrimitiveKind.Float32 or PrimitiveKind.Char } => 4,
        PrimitiveType { Kind: PrimitiveKind.Int16 or PrimitiveKind.Uint16 } => 2,
        PrimitiveType { Kind: PrimitiveKind.Int8 or PrimitiveKind.Uint8 or PrimitiveKind.Bool } => 1,
        PrimitiveType { Kind: PrimitiveKind.Void } => 0,
        TupleOf { Elements: var elements } => elements.Sum(e => SizeFloor(e, seen)),
        InlineArrayOf { Element: var element, Length: var length } => length * SizeFloor(element, seen),
        NamedRef { Symbol: { Kind: TypeSymbolKind.Struct } symbol } when seen.Add(symbol) && _types.MetaFields is { } fields
            => fields(type, symbol.Home).Sum(f => SizeFloor(f.Type, seen)),
        _ => 8,
    };

    // ─── resources ────────────────────────────────────────────────────────

    /// <summary>Whether the type's <c>close()</c> closes one of its fields — a wrapper that does
    /// takes the handle it holds with it. A type whose <c>close()</c> has no body to read is
    /// counted as one that does: the lint says nothing it has not seen.</summary>
    private static bool ClosesItsFields(LyrType type)
    {
        if (TypeFacts.SymbolOf(type)?.Members.LookupLocal("close") is not FunctionSymbol { Declaration: FunctionDecl { Body: { } body } })
            return true;
        static bool Closes(Node node) =>
            // 'this.inner.close()', or through a local that names this: 'me.inner.close()'
            node is CallExpr { Callee: MemberExpr { Member: "close", Target: MemberExpr } }
            || AstChildren.Of(node).Any(Closes);
        return Closes(body);
    }

    // ─── concurrency ──────────────────────────────────────────────────────

    /// <summary>global-var-without-sync: a module <c>var</c> in a program that starts threads — two
    /// threads write it without a lock between them.</summary>
    private void WarnGlobalVars(Module ast)
    {
        foreach (var g in ast.Declarations.OfType<GlobalBindingDecl>())
            if (g.Binding.IsMutable)
                _de.Report("LYR-SEM0213", Severity.Warning, g.NameSpan,
                    $"'{g.Name}' is a module 'var' in a program that starts threads: two of them may write it at once",
                    new DiagnosticNote("hold it in an 'Atomic' or behind a 'Mutex' (std.sync), or keep it to one thread"));
    }

    // ─── docs ─────────────────────────────────────────────────────────────

    /// <summary>missing-docs and missing-module-docs: a <c>pub</c> declaration without <c>///</c>, a
    /// member a conformance answers aside (the interface documents it); a module that declares
    /// something <c>pub</c> without <c>//!</c> at its top.</summary>
    private void WarnMissingDocs(ModuleSymbol module, Module ast)
    {
        var docs = _comp.Documentation;
        var anyPub = false;
        void Need(Node node, string name, Span at, string what)
        {
            anyPub = true;
            if (docs.Of(node) is null)
                _de.Report("LYR-SEM0223", Severity.Warning, at, $"the pub {what} '{name}' has no documentation",
                    new DiagnosticNote("say what it is in a '///' above it"));
        }
        HashSet<string> Answered(TypeNode[] interfaces)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            var seen = new HashSet<InterfaceDecl>(ReferenceEqualityComparer.Instance);
            void Add(TypeNode node)
            {
                if ((_binding.Resolve(node) ?? _types.RefOf(node)) is not TypeSymbol { Declaration: InterfaceDecl i } || !seen.Add(i)) return;
                foreach (var m in i.Members) names.Add(m.Name);
                foreach (var parent in i.Interfaces) Add(parent);
            }
            foreach (var i in interfaces) Add(i);
            return names;
        }
        void Members(Decl[] members, HashSet<string> answered)
        {
            foreach (var m in members)
                switch (m)
                {
                    case FieldDecl { Visibility: VisibilityWord.Pub } field: Need(field, field.Name, field.NameSpan, "field"); break;
                    case FunctionDecl { IsPublic: true } f when !answered.Contains(f.Name): Need(f, f.Name, f.NameSpan, "method"); break;
                }
        }
        foreach (var decl in ast.Declarations)
            switch (decl)
            {
                case FunctionDecl { IsPublic: true } f: Need(f, f.Name, f.NameSpan, "function"); break;
                case StructDecl { Visibility: VisibilityWord.Pub } s: Need(s, s.Name, s.NameSpan, "struct"); Members(s.Members, Answered(s.Interfaces)); break;
                case ClassDecl { Visibility: VisibilityWord.Pub } c: Need(c, c.Name, c.NameSpan, "class"); Members(c.Members, Answered(c.Interfaces)); break;
                case EnumDecl { IsPublic: true } e:
                    Need(e, e.Name, e.NameSpan, "enum");
                    Members(e.Methods, Answered(e.Interfaces));
                    break;
                case InterfaceDecl { Visibility: VisibilityWord.Pub } i: Need(i, i.Name, i.NameSpan, "interface"); break;
                case TypeAliasDecl { Visibility: VisibilityWord.Pub } t: Need(t, t.Name, t.NameSpan, "type"); break;
                case GlobalBindingDecl { IsPublic: true } g: Need(g, g.Name, g.NameSpan, "binding"); break;
                case ExtendDecl { Interfaces.Length: 0 } x: Members(x.Methods, []); break;
            }
        if (!anyPub) return;
        var text = _comp.Sources.GetText(ast.Span.File);
        if (!text.TrimStart('﻿', ' ', '\t', '\r', '\n').StartsWith("//!", StringComparison.Ordinal))
            _de.Report("LYR-SEM0225", Severity.Warning, new Span(ast.Span.File, 0, 0),
                $"the module '{module.FullName}' declares pub names and has no documentation",
                new DiagnosticNote("say what the module is for in '//!' lines at the top of the file"));
    }

    private static readonly Regex DocLink = new(@"(?<![\w\]\)!])\[([A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)\](?![\(\[:])", RegexOptions.CultureInvariant);

    /// <summary>broken-doc-link: <c>[Type]</c> or <c>[Type.member]</c> in a <c>///</c> that names
    /// nothing the module can see. Code — in backticks or a fence — is not read.</summary>
    private void WarnDocLinks(ModuleSymbol module, Module ast)
    {
        var docs = _comp.Documentation;
        var modules = ModuleNames(ast);
        void Check(Node node, TypeSymbol? owner, GenericParam[] outer)
        {
            if (docs.Of(node) is not { } text || node is not INamedDecl named) return;
            // a type parameter of the declaration, or of the type around it, is a name its text may use
            var generics = outer.Concat(GenericsOf(node)).Select(g => g.Name).ToHashSet(StringComparer.Ordinal);
            foreach (var target in LinksIn(text))
                if (!generics.Contains(target) && !Resolves(target.Split('.'), module, modules, owner))
                    _de.Report("LYR-SEM0224", Severity.Warning, named.NameSpan,
                        $"the documentation of '{named.Name}' links '[{target}]', which names nothing here",
                        new DiagnosticNote("name a type, a function or a member the module sees, or write it in backticks"));
        }
        foreach (var decl in ast.Declarations)
        {
            Check(decl, null, []);
            var owner = decl is INamedDecl n ? module.Members.LookupLocal(n.Name) as TypeSymbol : null;
            IEnumerable<Node> members = decl switch
            {
                StructDecl s => s.Members,
                ClassDecl c => c.Members,
                EnumDecl e => [.. e.Variants, .. e.Methods],
                InterfaceDecl i => i.Members,
                ExtendDecl x => x.Methods,
                _ => [],
            };
            if (decl is ExtendDecl extend) owner = (_binding.Resolve(extend.Target) ?? _types.RefOf(extend.Target)) as TypeSymbol;
            foreach (var member in members) Check(member, owner, GenericsOf(decl));
        }
    }

    private static GenericParam[] GenericsOf(Node node) => node switch
    {
        FunctionDecl f => f.Generics,
        StructDecl s => s.Generics,
        ClassDecl c => c.Generics,
        EnumDecl e => e.Generics,
        InterfaceDecl i => i.Generics,
        _ => [],
    };

    /// <summary>The link targets of a doc text, its code spans and fences left out.</summary>
    private static IEnumerable<string> LinksIn(string text)
    {
        var prose = new StringBuilder();
        var fenced = false;
        foreach (var line in text.Split('\n'))
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal)) { fenced = !fenced; prose.Append('\n'); continue; }
            if (fenced) { prose.Append('\n'); continue; }
            var inCode = false;
            foreach (var c in line)
            {
                if (c == '`') { inCode = !inCode; prose.Append(' '); continue; }
                prose.Append(inCode ? ' ' : c);
            }
            prose.Append('\n');
        }
        return DocLink.Matches(prose.ToString()).Select(m => m.Groups[1].Value);
    }

    private bool Resolves(string[] path, ModuleSymbol module, Dictionary<string, string> modules, TypeSymbol? owner)
    {
        Symbol? at = owner?.Members.LookupLocal(path[0])
                     ?? module.Members.Lookup(path[0])
                     ?? _comp.PreludeMember(path[0])
                     ?? _comp.Builtins.Lookup(path[0]);
        if (at is null && modules.TryGetValue(path[0], out var full)) at = _comp.FindModule(full.Split('.'));
        if (at is null && _comp.FindModule([path[0]]) is { } top) at = top;
        for (var i = 1; at is not null && i < path.Length; i++)
        {
            while (at is ImportBindingSymbol shell) at = shell.Target;
            at = at switch
            {
                ModuleSymbol m => m.Members.LookupLocal(path[i]) ?? _comp.FindModule([.. m.Path, path[i]]),
                TypeSymbol t => t.Members.LookupLocal(path[i]) ?? (HasMember(t, path[i]) ? t : null),
                _ => null,
            };
        }
        return at is not null;
    }

    /// <summary>Whether the type has the member beyond its own table: a method an <c>extend</c> block
    /// declares for it, or a member of an interface it conforms to — in its declaration or in a block —,
    /// a default among them.</summary>
    private bool HasMember(TypeSymbol type, string name)
    {
        var interfaces = new List<TypeNode>(type.Declaration switch
        {
            StructDecl s => s.Interfaces,
            ClassDecl c => c.Interfaces,
            EnumDecl e => e.Interfaces,
            InterfaceDecl i => i.Interfaces,
            _ => [],
        });
        foreach (var module in _comp.Modules)
            foreach (var extend in _comp.AstOf(module).Declarations.OfType<ExtendDecl>())
                if (ReferenceEquals(_binding.Resolve(extend.Target) ?? _types.RefOf(extend.Target), type))
                {
                    if (extend.Methods.Any(m => m.Name == name)) return true;
                    interfaces.AddRange(extend.Interfaces);
                }
        var seen = new HashSet<InterfaceDecl>(ReferenceEqualityComparer.Instance);
        bool Declares(TypeNode node) =>
            (_binding.Resolve(node) ?? _types.RefOf(node)) is TypeSymbol { Declaration: InterfaceDecl i } iface && seen.Add(i)
            && (iface.Members.LookupLocal(name) is not null || i.Members.Any(m => m.Name == name) || i.Interfaces.Any(Declares));
        return interfaces.Any(Declares);
    }
}
