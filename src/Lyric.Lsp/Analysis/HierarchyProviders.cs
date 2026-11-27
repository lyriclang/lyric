using Lyric.AST;
using Lyric.Compiler;
using Lyric.Core;
using Lyric.Lsp.Documents;
using Lyric.Lsp.Protocol;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Lsp.Analysis;

/// <summary>
/// LSP 3.17 Call Hierarchy and Type Hierarchy providers plus <c>workspace/willRenameFiles</c>
/// import rewriting for path-addressed Lyric 5 modules.
/// </summary>
public static class HierarchyProviders
{
    public static IReadOnlyList<CallHierarchyItem> PrepareCallHierarchy(
        SemanticModel model, Module root, FileId file, SourceManager sources, int offset)
    {
        var found = ReferenceProvider.SymbolNodeAt(model, root, file, offset);
        if (found is null) return [];
        var sym = ReferenceProvider.Target(found.Value.Symbol);
        if (sym is not FunctionSymbol fn || fn.Declaration is not FunctionDecl decl
            || !decl.Span.File.IsValid || model.Compilation.IsSynthesized(decl.Span.File))
            return [];

        return [ToCallItem(fn, decl, sources)];
    }

    public static IReadOnlyList<CallHierarchyIncomingCall> IncomingCalls(
        IReadOnlyList<AnalysisSnapshot> compilations, CallHierarchyItem item)
    {
        if (!DocumentUri.TryToFilePath(item.Uri, out var targetPath)) return [];
        var results = new List<CallHierarchyIncomingCall>();
        var grouped = new Dictionary<(string Uri, int Line, int Char), (CallHierarchyItem Caller, List<Protocol.Range> Ranges)>();

        foreach (var snap in compilations)
        {
            var file = DiagnosticMapper.FindFile(snap.Sources, targetPath);
            if (!file.IsValid) continue;
            var root = snap.Model.Compilation.Modules
                .Select(m => snap.Model.Compilation.AstOf(m))
                .FirstOrDefault(a => a.Span.File == file);
            if (root is null) continue;

            var offset = TextOffsets.ToOffset(snap.Sources.GetText(file), item.SelectionRange.Start);
            var sites = ReferenceProvider.At(snap.Model, root, file, offset, includeDeclaration: false);
            if (sites is null) continue;

            foreach (var site in sites)
            {
                var sitePath = snap.Sources.GetPath(site.File);
                var modAst = snap.Model.Compilation.Modules
                    .Select(m => snap.Model.Compilation.AstOf(m))
                    .FirstOrDefault(a => a.Span.File == site.File);
                if (modAst is null) continue;

                var path = NodeFinder.PathAt(modAst, site.File, site.Span.Start);
                var enclosingFn = path.OfType<FunctionDecl>().LastOrDefault();
                if (enclosingFn is null) continue;

                var fnSym = NodeFinder.DeclaredSymbol(snap.Model, enclosingFn) as FunctionSymbol;
                var callerItem = fnSym is not null
                    ? ToCallItem(fnSym, enclosingFn, snap.Sources)
                    : new CallHierarchyItem
                    {
                        Name = enclosingFn.Name,
                        Kind = SymbolKind.Function,
                        Uri = DocumentUri.FromFilePath(sitePath),
                        Range = SpanMapper.ToRange(snap.Sources, enclosingFn.Span),
                        SelectionRange = SpanMapper.ToRange(snap.Sources, enclosingFn.NameSpan),
                    };

                var key = (callerItem.Uri, callerItem.SelectionRange.Start.Line, callerItem.SelectionRange.Start.Character);
                if (!grouped.TryGetValue(key, out var entry))
                    grouped[key] = entry = (callerItem, []);
                entry.Ranges.Add(SpanMapper.ToRange(snap.Sources, site.Span));
            }
        }

        foreach (var (caller, ranges) in grouped.Values)
            results.Add(new CallHierarchyIncomingCall { From = caller, FromRanges = ranges });
        return results;
    }

    public static IReadOnlyList<CallHierarchyOutgoingCall> OutgoingCalls(
        IReadOnlyList<AnalysisSnapshot> compilations, CallHierarchyItem item)
    {
        if (!DocumentUri.TryToFilePath(item.Uri, out var sourcePath)) return [];
        foreach (var snap in compilations)
        {
            var file = DiagnosticMapper.FindFile(snap.Sources, sourcePath);
            if (!file.IsValid) continue;
            var modAst = snap.Model.Compilation.Modules
                .Select(m => snap.Model.Compilation.AstOf(m))
                .FirstOrDefault(a => a.Span.File == file);
            if (modAst is null) continue;

            var offset = TextOffsets.ToOffset(snap.Sources.GetText(file), item.SelectionRange.Start);
            var fnDecl = NodeFinder.PathAt(modAst, file, offset).OfType<FunctionDecl>().LastOrDefault();
            if (fnDecl?.Body is null) continue;

            var grouped = new Dictionary<(string Uri, int Line, int Char), (CallHierarchyItem Target, List<Protocol.Range> Ranges)>();
            var stack = new Stack<Node>();
            stack.Push(fnDecl.Body);
            while (stack.Count > 0)
            {
                var node = stack.Pop();
                if (node is CallExpr call)
                {
                    var sym = snap.Model.Types.RefOf(call.Callee) ?? snap.Model.Binding.Resolve(call.Callee);
                    if (sym is not null) sym = ReferenceProvider.Target(sym);
                    if (sym is FunctionSymbol fn && fn.Declaration is FunctionDecl targetDecl
                        && targetDecl.Span.File.IsValid && !snap.Model.Compilation.IsSynthesized(targetDecl.Span.File))
                    {
                        var targetItem = ToCallItem(fn, targetDecl, snap.Sources);
                        var key = (targetItem.Uri, targetItem.SelectionRange.Start.Line, targetItem.SelectionRange.Start.Character);
                        if (!grouped.TryGetValue(key, out var entry))
                            grouped[key] = entry = (targetItem, []);
                        entry.Ranges.Add(SpanMapper.ToRange(snap.Sources, call.Callee.Span));
                    }
                }
                foreach (var child in AstChildren.Of(node)) stack.Push(child);
            }

            var outCalls = new List<CallHierarchyOutgoingCall>(grouped.Count);
            foreach (var (target, ranges) in grouped.Values)
                outCalls.Add(new CallHierarchyOutgoingCall { To = target, FromRanges = ranges });
            return outCalls;
        }
        return [];
    }

    public static IReadOnlyList<TypeHierarchyItem> PrepareTypeHierarchy(
        SemanticModel model, Module root, FileId file, SourceManager sources, int offset)
    {
        var found = ReferenceProvider.SymbolNodeAt(model, root, file, offset);
        if (found is null) return [];
        if (ReferenceProvider.Target(found.Value.Symbol) is not TypeSymbol ts
            || ts.Declaration is not Decl decl || !decl.Span.File.IsValid || model.Compilation.IsSynthesized(decl.Span.File))
            return [];

        return [ToTypeItem(ts, decl, sources)];
    }

    public static IReadOnlyList<TypeHierarchyItem> Supertypes(
        IReadOnlyList<AnalysisSnapshot> compilations, TypeHierarchyItem item)
    {
        var found = FindTypeInCompilations(compilations, item);
        if (found is not var (snap, ts)) return [];

        var results = new List<TypeHierarchyItem>();
        var seen = new HashSet<TypeSymbol>(ReferenceEqualityComparer.Instance);
        foreach (var mod in snap.Model.Compilation.Modules)
        {
            foreach (var mem in mod.Members.Symbols)
            {
                if (mem is TypeSymbol { Kind: TypeSymbolKind.Interface } iface && !ReferenceEquals(iface, ts)
                    && snap.Model.Types.Satisfies is { } satisfies && satisfies(new NamedRef(ts), iface)
                    && iface.Declaration is Decl decl && decl.Span.File.IsValid && !snap.Model.Compilation.IsSynthesized(decl.Span.File)
                    && seen.Add(iface))
                {
                    results.Add(ToTypeItem(iface, decl, snap.Sources));
                }
            }
        }
        return results;
    }

    public static IReadOnlyList<TypeHierarchyItem> Subtypes(
        IReadOnlyList<AnalysisSnapshot> compilations, TypeHierarchyItem item)
    {
        var found = FindTypeInCompilations(compilations, item);
        if (found is not var (snap, iface) || iface.Kind != TypeSymbolKind.Interface) return [];

        var results = new List<TypeHierarchyItem>();
        var seen = new HashSet<TypeSymbol>(ReferenceEqualityComparer.Instance);
        foreach (var mod in snap.Model.Compilation.Modules)
        {
            foreach (var mem in mod.Members.Symbols)
            {
                if (mem is TypeSymbol candidate && !ReferenceEquals(candidate, iface)
                    && snap.Model.Types.Satisfies is { } satisfies && satisfies(new NamedRef(candidate), iface)
                    && candidate.Declaration is Decl decl && decl.Span.File.IsValid && !snap.Model.Compilation.IsSynthesized(decl.Span.File)
                    && seen.Add(candidate))
                {
                    results.Add(ToTypeItem(candidate, decl, snap.Sources));
                }
            }
        }
        return results;
    }

    /// <summary>Updates <c>import</c> declarations across workspace snapshots when `.lyr` files are renamed/moved.</summary>
    public static WorkspaceEdit? WillRenameFiles(
        IReadOnlyList<AnalysisSnapshot> compilations, Func<string, PackageUnit?> unitForPath, RenameFilesParams parameters)
    {
        var moduleRenames = new List<(string OldMod, string NewMod)>();
        foreach (var file in parameters.Files)
        {
            if (!DocumentUri.TryToFilePath(file.OldUri, out var oldPath)
                || !DocumentUri.TryToFilePath(file.NewUri, out var newPath))
                continue;
            var oldUnit = unitForPath(oldPath);
            var newUnit = unitForPath(newPath);
            if (oldUnit?.ModuleOf(oldPath) is { } oldMod && newUnit?.ModuleOf(newPath) is { } newMod
                && oldMod != newMod)
            {
                moduleRenames.Add((oldMod, newMod));
            }
        }
        if (moduleRenames.Count == 0) return null;

        var changes = new Dictionary<string, List<TextEdit>>(StringComparer.Ordinal);
        foreach (var snap in compilations)
        {
            foreach (var mod in snap.Model.Compilation.Modules)
            {
                if (snap.Model.Compilation.IsNative(mod)) continue;
                var ast = snap.Model.Compilation.AstOf(mod);
                if (!ast.Span.File.IsValid || snap.Model.Compilation.IsSynthesized(ast.Span.File)) continue;

                var fileText = snap.Sources.GetText(ast.Span.File);
                var uri = DocumentUri.FromFilePath(snap.Sources.GetPath(ast.Span.File));
                foreach (var decl in ast.Declarations)
                {
                    if (decl is not ImportDecl imp) continue;
                    var written = string.Join('.', imp.Path);
                    foreach (var (oldMod, newMod) in moduleRenames)
                    {
                        if (written != oldMod) continue;
                        var at = fileText.IndexOf(oldMod, imp.Span.Start, StringComparison.Ordinal);
                        if (at < 0 || at + oldMod.Length > imp.Span.End) continue;
                        if (!changes.TryGetValue(uri, out var list)) changes[uri] = list = [];
                        list.Add(new TextEdit
                        {
                            Range = SpanMapper.ToRange(snap.Sources, new Span(imp.Span.File, at, at + oldMod.Length)),
                            NewText = newMod,
                        });
                    }
                }
            }
        }

        return changes.Count == 0 ? null : new WorkspaceEdit { Changes = changes };
    }

    private static (AnalysisSnapshot Snapshot, TypeSymbol Type)? FindTypeInCompilations(
        IReadOnlyList<AnalysisSnapshot> compilations, TypeHierarchyItem item)
    {
        if (!DocumentUri.TryToFilePath(item.Uri, out var path)) return null;
        foreach (var snap in compilations)
        {
            var file = DiagnosticMapper.FindFile(snap.Sources, path);
            if (!file.IsValid) continue;
            var modAst = snap.Model.Compilation.Modules
                .Select(m => snap.Model.Compilation.AstOf(m))
                .FirstOrDefault(a => a.Span.File == file);
            if (modAst is null) continue;
            var offset = TextOffsets.ToOffset(snap.Sources.GetText(file), item.SelectionRange.Start);
            var found = ReferenceProvider.SymbolNodeAt(snap.Model, modAst, file, offset);
            if (found?.Symbol is { } raw && ReferenceProvider.Target(raw) is TypeSymbol ts)
                return (snap, ts);
        }
        return null;
    }

    private static CallHierarchyItem ToCallItem(FunctionSymbol fn, FunctionDecl decl, SourceManager sources) => new()
    {
        Name = fn.Name,
        Kind = SymbolKind.Function,
        Detail = null,
        Uri = DocumentUri.FromFilePath(sources.GetPath(decl.Span.File)),
        Range = SpanMapper.ToRange(sources, decl.Span),
        SelectionRange = SpanMapper.ToRange(sources, decl.NameSpan),
    };

    private static TypeHierarchyItem ToTypeItem(TypeSymbol ts, Decl decl, SourceManager sources) => new()
    {
        Name = ts.Name,
        Kind = ts.Kind switch
        {
            TypeSymbolKind.Struct => SymbolKind.Struct,
            TypeSymbolKind.Enum => SymbolKind.Enum,
            TypeSymbolKind.Interface => SymbolKind.Interface,
            _ => SymbolKind.Class,
        },
        Uri = DocumentUri.FromFilePath(sources.GetPath(decl.Span.File)),
        Range = SpanMapper.ToRange(sources, decl.Span),
        SelectionRange = SpanMapper.ToRange(sources, decl is INamedDecl n ? n.NameSpan : decl.Span),
    };
}
