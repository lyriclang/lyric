using Lyric.AST;
using Lyric.Compiler;
using Lyric.Core;
using Lyric.Lsp.Protocol;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Lsp.Analysis;

/// <summary>
/// Context-aware completions at a cursor offset: member accesses, static/module paths, struct
/// initializer fields, imports, type positions, and local/module/prelude/builtin scopes.
/// </summary>
public static class CompletionProvider
{
    internal const string Marker = "__lyric_completion__";

    public static IReadOnlyList<CompletionItem>? At(
        string path, string text, int offset, CompilerOptions options,
        string? moduleName = null, bool snippetSupport = false)
        => Sanitize(AtCore(path, text, offset, options, moduleName, snippetSupport));

    private static IReadOnlyList<CompletionItem>? AtCore(
        string path, string text, int offset, CompilerOptions options,
        string? moduleName, bool snippetSupport)
    {
        if (offset < 0 || offset > text.Length) return null;

        var marked = text.Insert(offset, Marker);
        var result = SourceCompiler.Check(ScriptSource.FromBuffer(path, marked, moduleName), options);
        if (result.Model is not { } model) return null;

        var file = DiagnosticMapper.FindFile(result.Sources, path);
        if (!file.IsValid) return null;

        var nodes = NodeFinder.PathAt(model.Entry, file, offset);
        var from = EnclosingModule(model, file);

        // 1. Inside an import declaration (path segments or '{ ... }' selective items), including
        // partial import lines that parser recovery did not wrap in ImportDecl.
        if (ImportContext(nodes) is { } import)
            return ImportItems(model, import, nodes, from, options.StdlibRoot);
        if (TryImportPathFallback(text, offset) is { } partialImport)
            return ImportModuleItems(model, partialImport, options.StdlibRoot);

        // 2. Inside a struct initializer's field-name position ('Point { $ }' or 'Point { x = 1, $ }'),
        // including 1-identifier CallExpr+Lambda recovery and unclosed '{' before downstream statements.
        if (StructInitContext(nodes, offset) is { } init)
            return StructInitItems(model, init, from, snippetSupport);
        if (CallInitializerTarget(model, nodes, offset) is { } callType)
            return StructInitItemsForType(model, callType, new HashSet<string>(StringComparer.Ordinal), from, snippetSupport);
        if (TryStructInitFallback(model, text, offset, from) is { } fallbackInit)
            return StructInitItemsForType(model, fallbackInit.Type, fallbackInit.Used, from, snippetSupport);

        // 3. After a dot ('.'), offer members of receiver type/module/value.
        if (MemberContext(nodes, offset) is { } member)
        {
            var receiver = model.Types.TypeOf(member.Target);
            return receiver switch
            {
                NonValueType { Symbol: TypeSymbol type } =>
                    Items(model, MemberFacts.OfType(model.Compilation, type, from), type.Name, snippetSupport, isMember: true),

                NonValueType { Symbol: ModuleSymbol module } =>
                    Items(model, MemberFacts.OfModule(module, from).Select(
                        s => new MemberCandidate(s, MemberSource.Own)), module.FullName, snippetSupport, isMember: false),

                _ => InstanceItems(model, receiver, from, snippetSupport),
            };
        }

        // 4. Suppress scope suggestions on new declaration names ('fn ma|', 'struct Foo|', 'fn f(x|: int)').
        if (IsDeclarationNameSite(nodes, offset))
            return Array.Empty<CompletionItem>();

        // 5. Everything else: scope + prelude + builtins, filtered when in a type position.
        return ScopeCompletion.At(model, nodes, offset, from, snippetSupport);
    }

    private static IReadOnlyList<CompletionItem>? Sanitize(IReadOnlyList<CompletionItem>? items)
    {
        if (items is null || items.Count == 0) return items;
        List<CompletionItem>? cleaned = null;
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (ContainsMarker(item.Label) || ContainsMarker(item.InsertText) || ContainsMarker(item.FilterText))
            {
                if (cleaned is null)
                {
                    cleaned = new List<CompletionItem>(items.Count - 1);
                    for (var j = 0; j < i; j++) cleaned.Add(items[j]);
                }
                continue;
            }
            cleaned?.Add(item);
        }
        return cleaned ?? items;
    }

    private static bool ContainsMarker(string? s) =>
        !string.IsNullOrEmpty(s) && s.Contains(Marker, StringComparison.Ordinal);

    private static bool IsDeclarationNameSite(IReadOnlyList<Node> nodes, int offset)
    {
        for (var i = nodes.Count - 1; i >= 0; i--)
        {
            switch (nodes[i])
            {
                case Expr:
                case TypeNode:
                case Block:
                case ImportDecl:
                case StructInitField:
                    return false;
                case INamedDecl decl:
                    return ContainsMarker(decl.Name)
                        || (decl.NameSpan.File.IsValid && offset >= decl.NameSpan.Start && offset <= decl.NameSpan.End);
            }
        }
        return false;
    }

    private static MemberExpr? MemberContext(IReadOnlyList<Node> nodes, int offset)
    {
        for (var i = nodes.Count - 1; i >= 0; i--)
            if (nodes[i] is MemberExpr member)
                return offset > member.Target.Span.End ? member : null;

        return null;
    }

    private static ImportDecl? ImportContext(IReadOnlyList<Node> nodes)
    {
        for (var i = nodes.Count - 1; i >= 0; i--)
            if (nodes[i] is ImportDecl import)
                return import;
        return null;
    }

    private static StructInitExpr? StructInitContext(IReadOnlyList<Node> nodes, int offset)
    {
        for (var i = nodes.Count - 1; i >= 0; i--)
        {
            if (nodes[i] is StructInitField field)
            {
                // Cursor past field name into '= <value>' expects a value/scope expression, not a field name.
                if (!field.IsShorthand && offset > field.NameSpan.End)
                    return null;
                continue;
            }
            if (nodes[i] is StructInitExpr init && offset > init.NameSpan.End)
                return init;
        }
        return null;
    }

    private static IReadOnlyList<CompletionItem> ImportItems(
        SemanticModel model, ImportDecl import, IReadOnlyList<Node> nodes, ModuleSymbol? from, string? stdlibRoot)
    {
        var items = new List<CompletionItem>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        if (nodes.Any(n => n is ImportSelective))
        {
            var target = model.Compilation.FindModule(import.Path);
            if (target is null) return items;

            var existing = new HashSet<string>(StringComparer.Ordinal);
            if (import.Clause is ImportSelective sel)
                foreach (var n in sel.Names)
                    if (!n.Contains(Marker, StringComparison.Ordinal)) existing.Add(n);

            foreach (var sym in MemberFacts.OfModule(target, from))
            {
                if (sym is ErrorSymbol || existing.Contains(sym.Name) || !seen.Add(sym.Name)) continue;
                items.Add(BuildItem(model, sym, sym.Name, target.FullName, "0", allowSnippet: false));
            }
            return items;
        }

        // Complete next module segment (or full module name) in `import a.b.$`.
        var rawPath = import.Path;
        var cleanCount = rawPath.Length > 0 && rawPath[^1].Contains(Marker, StringComparison.Ordinal)
            ? rawPath.Length - 1
            : rawPath.Length;
        return ImportModuleItems(model, rawPath[..cleanCount], stdlibRoot);
    }

    private static string[]? TryImportPathFallback(string text, int offset)
    {
        var lineStart = offset;
        while (lineStart > 0 && text[lineStart - 1] != '\n' && text[lineStart - 1] != ';' && text[lineStart - 1] != '{' && text[lineStart - 1] != '}')
            lineStart--;

        var seg = text[lineStart..offset].TrimStart();
        if (seg.StartsWith("pub ", StringComparison.Ordinal))
            seg = seg[4..].TrimStart();
        if (!seg.StartsWith("import", StringComparison.Ordinal))
            return null;
        if (seg.Length > 6 && char.IsLetterOrDigit(seg[6]))
            return null;

        var rest = seg[6..].TrimStart();
        if (rest.Length == 0)
            return [];

        // If selective '{ ... }' opened, let ImportContext or selective fallback handle exports; for paths, take segments before trailing dot/ident.
        if (rest.Contains('{'))
            return null;

        var parts = rest.Split('.');
        if (parts.Length == 0) return [];
        var last = parts[^1].Trim();
        var segCount = rest.EndsWith('.') ? parts.Length - 1 : parts.Length - 1;
        var outSegs = new List<string>(segCount);
        for (var i = 0; i < segCount; i++)
        {
            var p = parts[i].Trim();
            if (p.Length == 0) return null;
            outSegs.Add(p);
        }
        _ = last;
        return outSegs.ToArray();
    }

    private static IReadOnlyList<CompletionItem> ImportModuleItems(
        SemanticModel model, string[] cleanSegs, string? stdlibRoot)
    {
        var items = new List<CompletionItem>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var cleanCount = cleanSegs.Length;
        var prefix = cleanCount > 0 ? string.Join('.', cleanSegs) + "." : "";

        foreach (var mod in model.Compilation.Modules)
        {
            var full = mod.FullName;
            if (full.Length == 0 || !full.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var rest = full[prefix.Length..];
            if (rest.Length == 0) continue;
            var dot = rest.IndexOf('.');
            var seg = dot < 0 ? rest : rest[..dot];
            if (!seen.Add(seg)) continue;

            items.Add(new CompletionItem
            {
                Label = seg,
                Kind = CompletionItemKind.Module,
                Detail = dot < 0 ? full : "package",
                LabelDetails = new CompletionItemLabelDetails
                {
                    Detail = dot < 0 ? " module" : " package",
                    Description = full,
                },
                SortText = $"0_{seg}",
                FilterText = seg,
            });
        }

        // Also scan unloaded standard library modules on disk under stdlibRoot (e.g. 'import std.$' -> 'os', 'io').
        if (!string.IsNullOrEmpty(stdlibRoot) && (cleanCount == 0 || cleanSegs[0] == "std"))
        {
            var dir = stdlibRoot;
            if (cleanCount == 0)
            {
                if (Directory.Exists(Path.Combine(dir, "std")) && seen.Add("std"))
                {
                    items.Add(new CompletionItem
                    {
                        Label = "std",
                        Kind = CompletionItemKind.Module,
                        Detail = "package",
                        LabelDetails = new CompletionItemLabelDetails { Detail = " package", Description = "std" },
                        SortText = "0_std",
                        FilterText = "std",
                    });
                }
            }
            else
            {
                var relDir = Path.Combine([dir, .. cleanSegs]);
                if (Directory.Exists(relDir))
                {
                    foreach (var subDir in Directory.EnumerateDirectories(relDir))
                    {
                        var seg = Path.GetFileName(subDir);
                        if (string.IsNullOrEmpty(seg) || seg.StartsWith('.') || !seen.Add(seg)) continue;
                        var full = prefix + seg;
                        items.Add(new CompletionItem
                        {
                            Label = seg,
                            Kind = CompletionItemKind.Module,
                            Detail = "package",
                            LabelDetails = new CompletionItemLabelDetails { Detail = " package", Description = full },
                            SortText = $"1_{seg}",
                            FilterText = seg,
                        });
                    }
                    foreach (var file in Directory.EnumerateFiles(relDir, "*.lyr"))
                    {
                        var seg = Path.GetFileNameWithoutExtension(file);
                        if (string.IsNullOrEmpty(seg) || seg == "lib" || !seen.Add(seg)) continue;
                        var full = prefix + seg;
                        items.Add(new CompletionItem
                        {
                            Label = seg,
                            Kind = CompletionItemKind.Module,
                            Detail = full,
                            LabelDetails = new CompletionItemLabelDetails { Detail = " module", Description = full },
                            SortText = $"1_{seg}",
                            FilterText = seg,
                        });
                    }
                }
            }
        }
        return items;
    }

    private static IReadOnlyList<CompletionItem>? StructInitItems(
        SemanticModel model, StructInitExpr init, ModuleSymbol? from, bool snippetSupport)
    {
        var typeSym = model.Types.TypeOf(init) switch
        {
            NamedRef n => n.Symbol,
            GenericInstance g => g.Definition,
            _ => ResolveInitTypeFallback(model, init.Path, from),
        };
        if (typeSym is null) return null;

        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in init.Fields)
            if (!f.Name.Contains(Marker, StringComparison.Ordinal))
                used.Add(f.Name);

        return StructInitItemsForType(model, typeSym, used, from, snippetSupport);
    }

    private static IReadOnlyList<CompletionItem> StructInitItemsForType(
        SemanticModel model, TypeSymbol typeSym, HashSet<string> used, ModuleSymbol? from, bool snippetSupport)
    {
        var items = new List<CompletionItem>();
        foreach (var sym in typeSym.Members.Symbols)
        {
            if (sym is not FieldSymbol field || used.Contains(field.Name)) continue;
            if (from is not null && !model.Compilation.Visible(field, from)) continue;
            var sig = SignatureDetail(model, field);
            var insertText = snippetSupport ? $"{field.Name} = $1" : $"{field.Name} = ";
            items.Add(new CompletionItem
            {
                Label = field.Name,
                Kind = CompletionItemKind.Field,
                Detail = typeSym.Name,
                LabelDetails = new CompletionItemLabelDetails { Detail = sig, Description = typeSym.Name },
                Documentation = Documentation(model, field),
                SortText = $"0_{field.Name}",
                FilterText = field.Name,
                InsertText = insertText,
                InsertTextFormat = snippetSupport ? 2 : null,
            });
        }
        return items;
    }

    private static TypeSymbol? CallInitializerTarget(SemanticModel model, IReadOnlyList<Node> nodes, int offset)
    {
        for (var i = nodes.Count - 1; i >= 1; i--)
        {
            if (nodes[i] is LambdaExpr && nodes[i - 1] is CallExpr call && offset > call.Callee.Span.End)
            {
                var calleeType = model.Types.TypeOf(call.Callee);
                if (calleeType is NonValueType { Symbol: TypeSymbol t })
                    return t;
                if (call.Callee is IdentifierExpr id)
                    return ResolveInitTypeFallback(model, [id.Name], null);
            }
        }
        return null;
    }

    private static (TypeSymbol Type, HashSet<string> Used)? TryStructInitFallback(
        SemanticModel model, string text, int offset, ModuleSymbol? from)
    {
        var i = Math.Min(offset, text.Length);
        var paren = 0;
        var brace = 0;
        var crossedComma = false;
        var sawEqInCurrentSegment = false;
        var openBrace = -1;

        while (i > 0)
        {
            i--;
            var ch = text[i];
            if (ch == ')') paren++;
            else if (ch == '(')
            {
                if (paren == 0) return null;
                paren--;
            }
            else if (ch == '}') brace++;
            else if (ch == '{')
            {
                if (brace > 0)
                {
                    brace--;
                    continue;
                }
                openBrace = i;
                break;
            }
            else if (paren == 0 && brace == 0 && !crossedComma)
            {
                if (ch == '=' || ch == ';' || ch == ':')
                    sawEqInCurrentSegment = true;
                else if (ch == ',')
                    crossedComma = true;
            }
        }

        if (openBrace < 0 || sawEqInCurrentSegment) return null;

        // Parse '<mod>.<Type>' (skipping whitespace & optional '<...>' type args) directly before '{'.
        var end = openBrace;
        while (end > 0 && char.IsWhiteSpace(text[end - 1])) end--;
        if (end > 0 && text[end - 1] == '>')
        {
            var depth = 0;
            while (end > 0)
            {
                end--;
                if (text[end] == '>') depth++;
                else if (text[end] == '<')
                {
                    depth--;
                    if (depth == 0) break;
                }
            }
            while (end > 0 && char.IsWhiteSpace(text[end - 1])) end--;
        }

        var pathStart = end;
        while (pathStart > 0 && (char.IsLetterOrDigit(text[pathStart - 1]) || text[pathStart - 1] == '_' || text[pathStart - 1] == '.'))
            pathStart--;
        if (pathStart >= end) return null;

        var rawPath = text[pathStart..end];
        if (rawPath is "if" or "else" or "while" or "for" or "match" or "fn" or "struct" or "class" or "enum" or "interface" or "extend" or "comptime")
            return null;

        var segs = rawPath.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var typeSym = ResolveInitTypeFallback(model, segs, from);
        if (typeSym is null || (typeSym.Kind != TypeSymbolKind.Struct && typeSym.Kind != TypeSymbolKind.Class))
            return null;

        // Collect already-assigned field identifiers inside '{ ...offset }'.
        var used = new HashSet<string>(StringComparer.Ordinal);
        var inside = text[(openBrace + 1)..Math.Min(offset, text.Length)];
        var depthB = 0;
        var depthP = 0;
        var segStart = 0;
        for (var k = 0; k <= inside.Length; k++)
        {
            var atEnd = k == inside.Length;
            var c = atEnd ? ',' : inside[k];
            if (!atEnd)
            {
                if (c == '(') depthP++;
                else if (c == ')' && depthP > 0) depthP--;
                else if (c == '{') depthB++;
                else if (c == '}' && depthB > 0) depthB--;
            }
            if ((atEnd || (c == ',' && depthB == 0 && depthP == 0)))
            {
                var part = inside[segStart..k];
                var eq = part.IndexOf('=');
                if (eq > 0)
                {
                    var fname = part[..eq].Trim();
                    if (fname.Length > 0 && fname.All(ch => char.IsLetterOrDigit(ch) || ch == '_'))
                        used.Add(fname);
                }
                segStart = k + 1;
            }
        }

        return (typeSym, used);
    }

    private static TypeSymbol? ResolveInitTypeFallback(SemanticModel model, string[] path, ModuleSymbol? from)
    {
        if (path.Length == 0) return null;
        var sym = from?.Members.Lookup(path[0])
            ?? model.Compilation.PreludeMember(path[0]);
        for (var i = 1; i < path.Length && sym is not null; i++)
        {
            sym = Unwrap(sym);
            sym = sym is ModuleSymbol m ? m.Members.LookupLocal(path[i]) : null;
        }
        sym = sym is null ? null : Unwrap(sym);
        return sym as TypeSymbol;
    }

    private static IReadOnlyList<CompletionItem>? InstanceItems(
        SemanticModel model, LyrType receiver, ModuleSymbol? from, bool snippetSupport)
    {
        var definition = receiver switch
        {
            NamedRef named => named.Symbol,
            GenericInstance generic => generic.Definition,
            PrimitiveType primitive =>
                model.Compilation.Builtins.LookupLocal(TypeFacts.Display(primitive)) as TypeSymbol,
            _ => null,
        };

        if (definition is null) return null;

        return Items(
            model,
            MemberFacts.OfInstance(model.Compilation, model.Binding, definition, from),
            definition.Name,
            snippetSupport,
            isMember: true);
    }

    private static IReadOnlyList<CompletionItem> Items(
        SemanticModel model, IEnumerable<MemberCandidate> candidates, string owner, bool snippetSupport, bool isMember)
    {
        var items = new List<CompletionItem>();
        foreach (var candidate in candidates)
        {
            var detail = Detail(candidate, owner);
            var band = candidate.Source switch
            {
                MemberSource.Own => candidate.Symbol is FieldSymbol ? "0" : "1",
                MemberSource.Extension => "3",
                MemberSource.InterfaceDefault => "3",
                _ => "2",
            };
            items.Add(BuildItem(model, candidate.Symbol, candidate.Symbol.Name, detail, band, snippetSupport, isMember));
        }
        return items;
    }

    internal static CompletionItem BuildItem(
        SemanticModel model, Symbol symbol, string label, string detail, string sortBand, bool allowSnippet, bool isMember = false)
    {
        var target = Unwrap(symbol);
        var sig = SignatureDetail(model, target);
        string? insertText = null;
        int? insertFormat = null;
        string[]? commitChars = null;

        if (target is FunctionSymbol fn)
        {
            if (allowSnippet)
            {
                var hasParams = fn.Declaration is FunctionDecl { Parameters.Length: > 0 };
                insertText = hasParams ? $"{label}($1)$0" : $"{label}()";
                insertFormat = 2;
                commitChars = ["."];
            }
            else
            {
                commitChars = [".", "("];
            }
        }
        else if (target is LocalSymbol or ParameterSymbol or FieldSymbol or ModuleSymbol or TypeSymbol)
        {
            commitChars = ["."];
        }

        return new CompletionItem
        {
            Label = label,
            Kind = KindOf(target, isMember),
            Detail = detail,
            LabelDetails = sig is not null || detail.Length > 0
                ? new CompletionItemLabelDetails { Detail = sig, Description = detail }
                : null,
            Documentation = Documentation(model, target),
            SortText = $"{sortBand}_{label}",
            FilterText = label,
            InsertText = insertText,
            InsertTextFormat = insertFormat,
            CommitCharacters = commitChars,
        };
    }

    internal static Symbol Unwrap(Symbol symbol)
    {
        while (symbol is ImportBindingSymbol { Target: { } next })
            symbol = next;
        return symbol;
    }

    internal static string? SignatureDetail(SemanticModel model, Symbol symbol) => symbol switch
    {
        LocalSymbol l => $": {TypeFacts.Display(l.Type)}",
        ParameterSymbol p => $": {TypeFacts.Display(p.Type)}",
        FieldSymbol { Declaration: FieldDecl fd } when fd.Type.Span.File.IsValid =>
            $": {Flat(model.Compilation.Sources.Slice(fd.Type.Span).ToString())}",
        FunctionSymbol { Declaration: FunctionDecl fd } => FunctionSigDetail(model.Compilation.Sources, fd),
        GlobalSymbol { Declaration: GlobalBindingDecl { Binding.Type: { } gt } } when gt.Span.File.IsValid =>
            $": {Flat(model.Compilation.Sources.Slice(gt.Span).ToString())}",
        GlobalSymbol g => $": {TypeFacts.Display(model.Types.TypeOfGlobal(g))}",
        TypeSymbol ts => ts.Kind switch
        {
            TypeSymbolKind.Struct => " struct",
            TypeSymbolKind.Enum => " enum",
            TypeSymbolKind.Interface => " interface",
            TypeSymbolKind.Class => " class",
            TypeSymbolKind.Alias => " type",
            _ => " builtin",
        },
        EnumVariantSymbol { Declaration: EnumVariant ev } => VariantDetail(model.Compilation.Sources, ev),
        ModuleSymbol => " module",
        _ => null,
    };

    private static string FunctionSigDetail(SourceManager sm, FunctionDecl fd)
    {
        var ps = fd.Parameters
            .Where(p => p.Span.File.IsValid)
            .Select(p => Flat(sm.Slice(p.Span).ToString()));
        var ret = fd.ReturnType is { } r && r.Span.File.IsValid
            ? $" -> {Flat(sm.Slice(r.Span).ToString())}"
            : "";
        return $"({string.Join(", ", ps)}){ret}";
    }

    private static string VariantDetail(SourceManager sm, EnumVariant ev)
    {
        if (ev.TupleFields is { Length: > 0 } tf)
            return $"({string.Join(", ", tf.Where(t => t.Span.File.IsValid).Select(t => Flat(sm.Slice(t.Span).ToString())))})";
        if (ev.StructFields is { Length: > 0 } sf)
            return $" {{ {string.Join(", ", sf.Select(f => f.Name))} }}";
        return " variant";
    }

    private static string Flat(string text) =>
        string.Join(' ', text.Split(['\r', '\n', '\t', ' '], StringSplitOptions.RemoveEmptyEntries));

    private static string Detail(MemberCandidate candidate, string owner) => candidate.Source switch
    {
        MemberSource.Extension => $"extension on {owner}",
        MemberSource.InterfaceDefault => "interface default",
        _ => owner,
    };

    internal static MarkupContent? Documentation(SemanticModel model, Symbol symbol)
    {
        var target = Unwrap(symbol);
        if (target.Declaration is not { } declaration) return null;
        if (model.Documentation.Of(declaration) is not { } text) return null;
        return new MarkupContent { Value = text };
    }

    internal static CompletionItemKind KindOf(Symbol symbol, bool isMember = false) => symbol switch
    {
        ImportBindingSymbol import => KindOf(import.Target, isMember),

        FieldSymbol => CompletionItemKind.Field,
        LocalSymbol or ParameterSymbol => CompletionItemKind.Variable,
        FunctionSymbol => isMember ? CompletionItemKind.Method : CompletionItemKind.Function,
        EnumVariantSymbol => CompletionItemKind.EnumMember,
        GlobalSymbol => CompletionItemKind.Constant,
        ModuleSymbol => CompletionItemKind.Module,

        GenericParamSymbol => CompletionItemKind.Interface,

        TypeSymbol { Kind: TypeSymbolKind.Struct } => CompletionItemKind.Struct,
        TypeSymbol { Kind: TypeSymbolKind.Enum } => CompletionItemKind.Enum,
        TypeSymbol { Kind: TypeSymbolKind.Interface } => CompletionItemKind.Interface,
        TypeSymbol => CompletionItemKind.Class,

        _ => CompletionItemKind.Variable,
    };

    private static ModuleSymbol? EnclosingModule(SemanticModel model, FileId file)
    {
        foreach (var module in model.Compilation.Modules)
            if (model.Compilation.AstOf(module).Span.File == file)
                return module;

        return null;
    }
}
