using Lyric.AST;
using Lyric.Core;
using Lyric.Ir;
using Lyric.Ir.Lowering;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Compiler;

/// <summary>
/// An active use of what the build leaves out: the name is
/// known — the inactive declaration is checked as every other —, but nothing of it is built, so a
/// use the program would run is <c>LYR-CT0006</c>. A use within something left out is none, and
/// neither is one in the branch a <c>comptime if</c> does not take: that is how a program chooses
/// between what each target builds. An import names, it does not use.
/// </summary>
internal static class InactiveUses
{
    public static void Check(Compilation compilation, BindingResult binding, TypeResult types,
        DiagnosticEngine diagnostics, Inactivity inactive, IReadOnlyDictionary<int, IrConstValue> values)
    {
        if (inactive.IsEmpty) return;

        // where a use is no use: the untaken branches of 'comptime if', the imports
        var untaken = new List<Span>();
        var imports = new List<Span>();
        void Walk(Node node)
        {
            if (node is IfStmt { Comptime: true, Condition: ComptimeExpr site } choice
                && values.GetValueOrDefault(types.ComptimeSites.IndexOf(site)) is BoolConst taken)
            {
                if (!taken.Value) untaken.Add(choice.Then.Span);
                else if (choice.Else is { } otherwise) untaken.Add(otherwise.Span);
            }
            foreach (var child in AstChildren.Of(node)) Walk(child);
        }
        foreach (var module in compilation.Modules)
        {
            var ast = compilation.AstOf(module);
            imports.AddRange(ast.Declarations.OfType<ImportDecl>().Select(d => d.Span));
            if (!inactive.Covers(ast.Span)) Walk(ast);
        }
        bool Within(List<Span> extents, Span use) =>
            extents.Exists(e => e.File == use.File && use.Start >= e.Start && use.End <= e.End);

        var reported = new HashSet<Node>(ReferenceEqualityComparer.Instance);
        foreach (var table in new[] { (IEnumerable<KeyValuePair<Node, Symbol>>)types.AllReferences, binding.All })
            foreach (var (node, symbol) in table)
            {
                var target = symbol is ImportBindingSymbol shell ? shell.Target : symbol;
                if (target.Declaration is not { } declaration || ReferenceEquals(declaration, node)) continue;
                if (!inactive.Covers(declaration.Span)) continue;
                // what the compiler wrote for a declaration is that declaration's, left out with it
                if (compilation.IsSynthesized(node.Span.File)) continue;
                if (inactive.Covers(node.Span) || Within(untaken, node.Span) || Within(imports, node.Span)) continue;
                if (!reported.Add(node)) continue;
                diagnostics.Report("LYR-CT0006", Severity.Error, node.Span,
                    $"'{target.Name}' is not built for this target — a '@When' leaves it out; choose it with 'comptime if'");
            }
    }
}
