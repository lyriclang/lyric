using Lyric.AST;
using Lyric.Compiler;
using Lyric.Core;
using Lyric.Lsp.Documents;
using Lyric.Lsp.Protocol;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Lsp.Analysis;

/// <summary>
/// LSP 3.17 navigation providers: <c>typeDefinition</c>, <c>implementation</c>,
/// <c>documentHighlight</c>, <c>selectionRange</c>, and <c>documentLink</c>.
/// </summary>
public static class NavigationProviders
{
    /// <summary>Jumps to the declaration of the expression or symbol's type.</summary>
    public static DefinitionTarget? TypeDefinitionAt(SemanticModel model, Module root, FileId file, int offset)
    {
        var path = NodeFinder.PathAt(root, file, offset);
        for (var i = path.Count - 1; i >= 0; i--)
        {
            var node = path[i];
            LyrType? type = node is Expr expr ? model.Types.TypeOf(expr) : null;
            if (type is null || type.IsError)
            {
                var sym = model.Types.RefOf(node) ?? model.Binding.Resolve(node);
                if (sym is not null) sym = ReferenceProvider.Target(sym);
                type = sym switch
                {
                    LocalSymbol l => l.Type,
                    ParameterSymbol p => p.Type,
                    GlobalSymbol g => model.Types.TypeOfGlobal(g),
                    TypeSymbol t => new NamedRef(t),
                    _ => null,
                };
            }

            if (type is null) continue;
            var typeSym = UnwrapTypeSymbol(type);
            if (typeSym?.Declaration is { } decl && decl.Span.File.IsValid && !model.Compilation.IsSynthesized(decl.Span.File))
            {
                var nameSpan = decl is INamedDecl named ? named.NameSpan : decl.Span;
                return new DefinitionTarget(decl.Span.File, decl.Span, nameSpan);
            }
        }
        return null;
    }

    /// <summary>Lists structs, classes, enums, and extend blocks conforming to an interface or implementing an interface method.</summary>
    public static IReadOnlyList<DefinitionTarget> ImplementationAt(SemanticModel model, Module root, FileId file, int offset)
    {
        var found = ReferenceProvider.SymbolNodeAt(model, root, file, offset);
        if (found is null) return [];
        var symbol = ReferenceProvider.Target(found.Value.Symbol);

        TypeSymbol? targetIface = symbol as TypeSymbol is { Kind: TypeSymbolKind.Interface } iface ? iface : null;
        FunctionSymbol? ifaceMethod = null;
        if (targetIface is null && symbol is FunctionSymbol fn)
        {
            foreach (var mod in model.Compilation.Modules)
            {
                foreach (var mem in mod.Members.Symbols)
                {
                    if (mem is TypeSymbol { Kind: TypeSymbolKind.Interface } candidate
                        && ReferenceEquals(candidate.Members.LookupLocal(fn.Name), fn))
                    {
                        targetIface = candidate;
                        ifaceMethod = fn;
                        break;
                    }
                }
                if (targetIface is not null) break;
            }
        }
        if (targetIface is null) return [];

        var targets = new List<DefinitionTarget>();
        var seen = new HashSet<Node>(ReferenceEqualityComparer.Instance);

        void AddDecl(Decl decl)
        {
            if (!decl.Span.File.IsValid || model.Compilation.IsSynthesized(decl.Span.File) || !seen.Add(decl)) return;
            var nameSpan = decl is INamedDecl named ? named.NameSpan : decl.Span;
            targets.Add(new DefinitionTarget(decl.Span.File, decl.Span, nameSpan));
        }

        foreach (var mod in model.Compilation.Modules)
        {
            foreach (var mem in mod.Members.Symbols)
            {
                if (mem is TypeSymbol ts && ts.Kind is TypeSymbolKind.Struct or TypeSymbolKind.Class or TypeSymbolKind.Enum
                    && model.Types.Satisfies is { } satisfies && satisfies(new NamedRef(ts), targetIface))
                {
                    if (ifaceMethod is not null)
                    {
                        if (ts.Members.LookupLocal(ifaceMethod.Name) is FunctionSymbol { Declaration: Decl mdecl })
                            AddDecl(mdecl);
                    }
                    else if (ts.Declaration is Decl tdecl)
                    {
                        AddDecl(tdecl);
                    }
                }
            }
        }

        foreach (var block in model.Compilation.Extensions.Blocks)
        {
            var implementsIface = false;
            foreach (var ifaceNode in block.Decl.Interfaces)
            {
                if (ReferenceEquals(model.Binding.Resolve(ifaceNode), targetIface))
                {
                    implementsIface = true;
                    break;
                }
            }
            if (!implementsIface) continue;

            if (ifaceMethod is not null)
            {
                foreach (var m in block.Methods)
                    if (m.Name == ifaceMethod.Name && m.Declaration is Decl mdecl)
                        AddDecl(mdecl);
            }
            else
            {
                AddDecl(block.Decl);
            }
        }

        return targets;
    }

    /// <summary>Highlights read and write occurrences of the symbol under the cursor within <paramref name="file"/>.</summary>
    public static IReadOnlyList<DocumentHighlight> DocumentHighlightsAt(
        SemanticModel model, Module root, FileId file, SourceManager sources, int offset)
    {
        var sites = ReferenceProvider.At(model, root, file, offset, includeDeclaration: true);
        if (sites is null || sites.Count == 0) return [];

        var writes = new HashSet<int>();
        CollectWriteStarts(root, file, writes);

        var highlights = new List<DocumentHighlight>();
        foreach (var site in sites)
        {
            if (site.File != file) continue;
            var kind = writes.Contains(site.Span.Start) ? DocumentHighlightKind.Write : DocumentHighlightKind.Read;
            highlights.Add(new DocumentHighlight
            {
                Range = SpanMapper.ToRange(sources, site.Span),
                Kind = kind,
            });
        }
        return highlights;
    }

    /// <summary>Builds nested LSP selection ranges from innermost node to module root.</summary>
    public static IReadOnlyList<SelectionRange> SelectionRangesAt(
        Module root, FileId file, SourceManager sources, IReadOnlyList<Position> positions)
    {
        var text = sources.GetText(file);
        var result = new List<SelectionRange>(positions.Count);
        foreach (var pos in positions)
        {
            var offset = TextOffsets.ToOffset(text, pos);
            var path = NodeFinder.PathAt(root, file, offset);
            SelectionRange? current = null;
            for (var i = 0; i < path.Count; i++)
            {
                var span = path[i].Span;
                if (span.File != file) continue;
                var range = SpanMapper.ToRange(sources, span);
                if (current is not null && RangesEqual(current.Range, range)) continue;
                current = new SelectionRange { Range = range, Parent = current };
            }
            result.Add(current ?? new SelectionRange { Range = new Protocol.Range { Start = pos, End = pos } });
        }
        return result;
    }

    /// <summary>Computes clickable <c>DocumentLink</c>s for <c>import</c> declarations.</summary>
    public static IReadOnlyList<DocumentLink> DocumentLinksOf(SemanticModel model, Module root, FileId file, SourceManager sources)
    {
        var links = new List<DocumentLink>();
        foreach (var decl in root.Declarations)
        {
            if (decl is not ImportDecl import || import.Span.File != file) continue;
            var target = model.Compilation.FindModule(import.Path);
            if (target?.Declaration is not { } targetDecl || !targetDecl.Span.File.IsValid
                || model.Compilation.IsSynthesized(targetDecl.Span.File))
                continue;

            var targetPath = sources.GetPath(targetDecl.Span.File);
            links.Add(new DocumentLink
            {
                Range = SpanMapper.ToRange(sources, import.Span),
                Target = DocumentUri.FromFilePath(targetPath),
                Tooltip = $"Open {target.FullName}",
            });
        }
        return links;
    }

    private static TypeSymbol? UnwrapTypeSymbol(LyrType type)
    {
        while (true)
        {
            switch (type)
            {
                case Optional opt: type = opt.Inner; break;
                case ArrayOf arr: type = arr.Element; break;
                case SliceOf slice: type = slice.Element; break;
                case InlineArrayOf ia: type = ia.Element; break;
                case NamedRef nr: return nr.Symbol;
                case GenericInstance gi: return gi.Definition;
                default: return null;
            }
        }
    }

    private static void CollectWriteStarts(Node root, FileId file, HashSet<int> writeStarts)
    {
        var stack = new Stack<Node>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            switch (node)
            {
                case AssignExpr assign when assign.Target.Span.File == file:
                    var targetSpan = NameSpans.Of(assign.Target) ?? assign.Target.Span;
                    writeStarts.Add(targetSpan.Start);
                    break;
                case BindingStmt binding when binding.NameSpan.File == file:
                    writeStarts.Add(binding.NameSpan.Start);
                    break;
            }
            foreach (var child in AstChildren.Of(node)) stack.Push(child);
        }
    }

    private static bool RangesEqual(Protocol.Range a, Protocol.Range b) =>
        a.Start.Line == b.Start.Line && a.Start.Character == b.Start.Character
        && a.End.Line == b.End.Line && a.End.Character == b.End.Character;
}
