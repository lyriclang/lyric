namespace Lyric.AST;

/// <summary>
/// What makes a function a coroutine (design/v5/spec/08 D11, F5): its declared result is
/// <c>Coroutine&lt;…&gt;</c> AND a yield of its own stands in its body — not one in a lambda nested
/// in it, which makes that lambda a generator instead. A function that returns a coroutine without
/// yielding is an ordinary one: <c>sequence</c>, or anything that hands on a coroutine another
/// function made.
/// </summary>
public static class CoroutineShape
{
    public static bool IsCoroutine(FunctionDecl decl) =>
        decl.ReturnType is NamedType { Path: [.., "Coroutine"] } && decl.Body is { } body && YieldsIn(body);

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
