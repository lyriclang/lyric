using Lyric4.AST;
using Lyric4.Resolver;
using Lyric4.Sema;

namespace Lyric4.From4.Rules;

/// <summary>
/// E04: a call that throws is marked in Lyric 5, <c>try f()</c>, unless a <c>try</c> block holds it. One
/// mark covers its expression, and a second is a warning, so an expression gets one: at the start of
/// the smallest part that holds every call that throws — <c>try h(g())</c>, <c>p(try g())</c>,
/// <c>1 + try g()</c>, <c>try g() + h(1)</c>. The branches of an <c>if</c> and a <c>match</c> are
/// expressions of their own, and a lambda's body is no part of the <c>try</c> around it.
/// </summary>
internal sealed class TryMarks : IRule
{
    public string Id => "E04";

    public void Apply(RuleContext context)
    {
        foreach (var module in context.Modules)
        {
            _statements = Tree.Below(module).OfType<ExprStmt>().Select(s => s.Expr).ToHashSet<Expr>(ReferenceEqualityComparer.Instance);
            Visit(context, module, covered: false);
        }
    }

    private HashSet<Expr> _statements = [];

    private void Visit(RuleContext context, Node node, bool covered)
    {
        switch (node)
        {
            case TryStmt attempt:
                Visit(context, attempt.Body, covered: true);
                foreach (var clause in attempt.Catches) Visit(context, clause, covered);
                return;
            case FunctionDecl function:
                if (function.Body is { } body) Visit(context, body, covered: false);
                return;
            case LambdaExpr lambda:
                Visit(context, lambda.Body, covered: false);
                return;
            case Expr expression:
                if (!covered && Cover(context, expression) is { } part)
                    context.Replace(Id, part.Span.File, part.Span.Start, part.Span.Start, "try ");
                Inside(context, expression, covered);
                return;
        }
        foreach (var child in AstChildren.Of(node)) Visit(context, child, covered);
    }

    /// <summary>What within an expression is marked for itself: branches, lambdas, nested statements.</summary>
    private void Inside(RuleContext context, Node node, bool covered)
    {
        foreach (var child in AstChildren.Of(node))
        {
            if (child is LambdaExpr or Stmt || IsBranch(node, child)) Visit(context, child, covered);
            else Inside(context, child, covered);
        }
    }

    private static bool IsBranch(Node parent, Node child) =>
        parent is IfExpr choice ? ReferenceEquals(child, choice.Then) || ReferenceEquals(child, choice.Else) : parent is MatchArm;

    /// <summary>The smallest part of an expression that holds all its calls that throw.</summary>
    private Node? Cover(RuleContext context, Node node)
    {
        if (node is LambdaExpr or Stmt) return null;
        var parts = AstChildren.Of(node)
            .Where(child => !IsBranch(node, child))
            .Select(child => Cover(context, child))
            .OfType<Node>()
            .ToList();
        if (Throws(context, node) || parts.Count >= 2) return node;
        // a prefix binds tighter than the mark: 'try -g()'
        return parts.Count == 1 ? node is UnaryExpr ? node : parts[0] : null;
    }

    private bool Throws(RuleContext context, Node node) => node switch
    {
        CallExpr call => context.Model.Types.ThrownByPull(call) is not null
            || Tree.Target(context, call.Callee) is FunctionSymbol { Declaration: FunctionDecl { Throws: not null } }
            // silent in 4.6, and written to throw (E02); as a value it is left as it stands, and said
            || (LibraryTable.KeyOf(context, call.Callee) is { } key && LibraryTable.Of(key) is { Throws: true } entry
                && (!entry.Statement || _statements.Contains(call))),
        ResumeExpr resume => context.Model.Types.ThrownByPull(resume) is not null,
        _ => false,
    };
}

/// <summary>E05: the built-in <c>Throwable</c> is <c>Error</c>, as a conformance, a <c>catch</c> and a
/// <c>throws</c>.</summary>
internal sealed class Throwables : IRule
{
    public string Id => "E05";

    public void Apply(RuleContext context)
    {
        foreach (var module in context.Modules)
            foreach (var named in Tree.Below(module).OfType<NamedType>())
                if (named is { Path: ["Throwable"], TypeArguments: [] }) context.Replace(Id, named.Span, "Error");
    }
}

/// <summary>E06: <c>Exception { text = "…" }</c> is <c>Exception("…")</c>. 4.6's has that field alone.</summary>
internal sealed class Exceptions : IRule
{
    public string Id => "E06";

    public void Apply(RuleContext context)
    {
        foreach (var module in context.Modules)
            foreach (var literal in Tree.Below(module).OfType<StructInitExpr>())
            {
                if (Tree.Target(context, literal) is not TypeSymbol { Name: "Exception" } symbol || !Tree.InStdlib(context, symbol)
                    || literal.Fields is not [{ Name: "text" } text]) continue;
                context.Replace(Id, literal.Span.File, literal.Span.Start, text.Value.Span.Start, "Exception(");
                context.Replace(Id, literal.Span.File, text.Value.Span.End, literal.Span.End, ")");
            }
    }
}

/// <summary>E08: <c>main</c> could not throw in Lyric 4, so a <c>try</c> around all of it caught what
/// was left; in Lyric 5 it may. Said for a proposal: what the <c>catch</c> does may be meant.</summary>
internal sealed class ThrowingMain : IRule
{
    public string Id => "E08";

    public void Apply(RuleContext context)
    {
        foreach (var module in context.Modules)
            foreach (var main in module.Declarations.OfType<FunctionDecl>())
                if (main is { Name: "main", Body.Statements: [TryStmt whole, ..] rest } && rest.Skip(1).All(s => s is ReturnStmt))
                    context.Note(Id, "LYR-MIG0408", NoteKind.Review, whole.Span,
                        "a 'try' around all of 'main' caught what 'main' could not throw in Lyric 4; in Lyric 5 'main' may throw",
                        "where the 'catch' only reports, declare 'throws' on 'main' and take the 'try' away");
    }
}

/// <summary>E09: <c>defer stream.close(f)</c> is what <c>using let f = …</c> says in Lyric 5, for a
/// closeable of 4.6's library. Said for a proposal.</summary>
internal sealed class DeferredClose : IRule
{
    public string Id => "E09";

    public void Apply(RuleContext context)
    {
        foreach (var module in context.Modules)
            foreach (var deferred in Tree.Below(module).OfType<DeferStmt>())
                if (deferred.Body is ExprStmt { Expr: CallExpr { Callee: var callee, Arguments: [IdentifierExpr closed] } }
                    && Tree.Target(context, callee) is FunctionSymbol { Name: "close" } symbol && Tree.InStdlib(context, symbol))
                    context.Note(Id, "LYR-MIG0409", NoteKind.Review, deferred.Span,
                        $"'defer' closes '{closed.Name}' by hand; Lyric 5 closes what a 'using' binding holds",
                        $"write 'using let {closed.Name} = …' and take the 'defer' away");
    }
}
