namespace Lyric.AST;

/// <summary>
/// What makes a function a coroutine (design/v5/spec/08 D11, F5; 06 M6-3): its declared result is
/// <c>Coroutine&lt;…&gt;</c> AND it yields itself — not in a lambda nested in it, which makes that
/// lambda a generator instead — OR it returns no value. So a body that yields through helpers
/// only is a generator, and an empty body the empty one; they were "ordinary functions" that
/// returned nothing (<c>LYR-SEM0017</c>). A function that RETURNS a coroutine is an ordinary
/// one, a factory: <c>sequence</c>, or anything that hands on a coroutine another function made.
/// </summary>
public static class CoroutineShape
{
    public static bool IsCoroutine(FunctionDecl decl) =>
        decl.ReturnType is NamedType { Path: [.., "Coroutine"] } && decl.Body is { } body
        && (YieldsIn(body) || !ReturnsAValueIn(body));

    /// <summary>Whether <paramref name="node"/> gives a value back — a <c>return v;</c>, or a tail
    /// expression — outside every lambda in it.</summary>
    public static bool ReturnsAValueIn(Node node)
    {
        if (node is ReturnStmt { Value: not null } or TailExprStmt) return true;
        if (node is LambdaExpr) return false;
        foreach (var child in AstChildren.Of(node))
            if (ReturnsAValueIn(child)) return true;
        return false;
    }

    /// <summary>Whether a yield stands in <paramref name="node"/> outside every lambda in it.</summary>
    public static bool YieldsIn(Node node)
    {
        if (node is YieldStmt) return true;
        if (node is LambdaExpr) return false;
        foreach (var child in AstChildren.Of(node))
            if (YieldsIn(child)) return true;
        return false;
    }
}
