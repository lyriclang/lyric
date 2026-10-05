using Lyric.AST;

namespace Lyric.Sema;

/// <summary>
/// Structured control-flow facts over the AST; no CFG is needed, because Lyric has no unstructured
/// jumps. Shared by return coverage and by narrowing on an early exit.
/// </summary>
internal static class Flow
{
    /// <summary>Does this statement leave the function on EVERY path, by return, throw or
    /// divergence? With <paramref name="types"/> a match exhaustiveness proven by the sema counts
    /// too; without it, match falls back to the syntactic '_' arm.</summary>
    public static bool AlwaysReturns(Stmt s, TypeResult? types = null) => s switch
    {
        ReturnStmt => true,
        ThrowStmt => true,
        ExprStmt es => types?.TypeOf(es.Expr) is NeverType, // panic(...) diverges
        // The tail of a value block that gives no value: '{ log(); throw e }'.
        TailExprStmt tail => types?.TypeOf(tail.Expr) is NeverType,
        // A binding whose value gives none never completes either: 'let n: int = fail();'.
        BindingStmt { Initializer: { } init } => types?.TypeOf(init) is NeverType,
        Block b => b.Statements.Any(st => AlwaysReturns(st, types)),
        IfStmt f => f.Else is not null && AlwaysReturns(f.Then, types) && AlwaysReturns(f.Else, types),
        // A block that ends every path speaks for its loop only when no jump of the loop stands
        // in it: a 'break' is a way past the 'return' behind it, and so is a 'continue' — it
        // reaches the condition, which may end the loop.
        DoWhileStmt d => (AlwaysReturns(d.Body, types) && !Jumps(d.Body, d.Label, nested: false, breaks: true, continues: true))
                         || Diverges(d.Condition, d.Body, d.Label),
        WhileStmt w => Diverges(w.Condition, w.Body, w.Label),
        ForInStmt => false, // the loop may not run at all
        TryStmt t => AlwaysReturns(t.Body, types) && t.Catches.All(c => AlwaysReturns(c.Body, types)),
        MatchStmt m => (types?.IsMatchExhaustive(m) == true || m.Arms.Any(a => a.Pattern is WildcardPattern))
                       && m.Arms.All(a => ArmReturns(a, types)),
        _ => false
    };

    /// <summary>
    /// Does control flow leave this block IN ANY CASE, no matter where to?
    ///
    /// <para>A DIFFERENT question from <see cref="AlwaysReturns"/>. For "a return is missing at the
    /// end of the function" a <c>continue</c> must not count as a return; for "is the code after the
    /// if reached" it must. One function for both would answer one of them wrongly.</para>
    ///
    /// <para>Used by flow narrowing: after <c>if (x == null) { continue; }</c> x is narrowed for the
    /// rest of the loop, exactly as after a <c>return</c>.</para>
    /// </summary>
    public static bool AlwaysExits(Stmt s, TypeResult? types = null) => s switch
    {
        BreakStmt => true,
        ContinueStmt => true,
        Block b => b.Statements.Any(st => AlwaysExits(st, types)),
        IfStmt f => f.Else is not null && AlwaysExits(f.Then, types) && AlwaysExits(f.Else, types),
        TryStmt t => AlwaysExits(t.Body, types) && t.Catches.All(c => AlwaysExits(c.Body, types)),
        MatchStmt m => (types?.IsMatchExhaustive(m) == true || m.Arms.Any(a => a.Pattern is WildcardPattern))
                       && m.Arms.All(a => a.Body is Block bl && AlwaysExits(bl, types)),

        // Anything that already satisfies 'AlwaysReturns' leaves the block too.
        _ => AlwaysReturns(s, types),
    };

    /// <summary>
    /// Does a <c>break</c> (<paramref name="wantContinue"/> false) or <c>continue</c> (true) of THIS
    /// loop stand on a path the lowering still walks?
    ///
    /// <para>A different question from <see cref="HasBreak"/>, which asks only whether the keyword
    /// occurs. The lowering stops at the first statement that leaves the block, so a jump behind one
    /// is never lowered — <c>do { return 1; break; }</c> has a <c>break</c> and jumps nowhere. That
    /// is the same code <c>LYR-SEM0073</c> calls unreachable.</para>
    ///
    /// <para>The answer decides whether <c>do-while</c> creates its jump target BEFORE the body. It
    /// is deliberately a LOWER bound: saying no where the answer is yes costs the target its early
    /// position, while saying yes where it is no would leave a block nobody enters, which the
    /// verifier refuses.</para>
    ///
    /// <para><paramref name="label"/> is the loop's own label, if it has one. A jump that names a
    /// DIFFERENT label leaves this loop rather than landing in it, so counting it would reserve a
    /// target nobody enters — which is the one answer this walk may not give.</para>
    /// </summary>
    public static bool ReachesJump(Stmt s, bool wantContinue, string? label, TypeResult? types = null) => s switch
    {
        BreakStmt b => !wantContinue && Targets(b.Label, label),
        ContinueStmt c => wantContinue && Targets(c.Label, label),
        Block b => ReachesJumpInSequence(b.Statements, wantContinue, label, types),
        IfStmt f => ReachesJump(f.Then, wantContinue, label, types)
                    || (f.Else is not null && ReachesJump(f.Else, wantContinue, label, types)),
        TryStmt t => ReachesJump(t.Body, wantContinue, label, types)
                     || t.Catches.Any(c => ReachesJump(c.Body, wantContinue, label, types)),
        MatchStmt m => m.Arms.Any(a => a.Body is Block bl && ReachesJump(bl, wantContinue, label, types)),
        _ => false // while, do-while, for-in: their own break and continue. A defer body runs at the
                   // end of its scope rather than where it stands, so its jumps are not this walk's.
    };

    /// <summary>Does a jump carrying <paramref name="written"/> target a loop labeled
    /// <paramref name="own"/> at this depth? An unlabeled jump takes the innermost loop, which at
    /// this depth is this one.</summary>
    private static bool Targets(string? written, string? own) =>
        written is null || (own is not null && written == own);

    private static bool ReachesJumpInSequence(Stmt[] statements, bool wantContinue, string? label,
        TypeResult? types)
    {
        foreach (var st in statements)
        {
            if (ReachesJump(st, wantContinue, label, types)) return true;
            if (AlwaysExits(st, types)) return false;
        }
        return false;
    }

    private static bool Diverges(Expr cond, Block body, string? label) =>
        cond is BoolLiteralExpr { Value: true } && !Jumps(body, label, nested: false, breaks: true, continues: false);

    private static bool ArmReturns(MatchArm a, TypeResult? types) => a.Body is Block b && AlwaysReturns(b, types);

    /// <summary>
    /// Does a jump to THIS loop stand anywhere in <paramref name="node"/>: a plain 'break' or
    /// 'continue' at this depth, or one naming this loop's label at ANY depth — inside a nested
    /// loop only the labeled form reaches out, so the descent into one counts labeled jumps alone.
    ///
    /// <para>ANYWHERE: the walk goes through <see cref="AstChildren"/>, which is total over the
    /// node types. It was a list of statement forms with a default that said no, and so it
    /// passed by every block an expression holds — the arm of a 'match' expression, a clause of
    /// a 'try' expression — and the 'else' of a 'let … else'. 'while (true) { let .Some(v) =
    /// next() else { break; }; … }' was then a loop nothing leaves: the 'return' behind it was
    /// warned about as unreachable, and without that 'return' the function passed as one that
    /// returns on every path, and ran off its end.</para>
    ///
    /// <para>A lambda's body is a function of its own (08 Y11 F5): its jumps are its loops'.</para>
    /// </summary>
    private static bool Jumps(Node node, string? label, bool nested, bool breaks, bool continues) => node switch
    {
        BreakStmt b => (breaks && Reaches(b.Label, label, nested))
                       || (b.Value is { } value && Jumps(value, label, nested, breaks, continues)),
        ContinueStmt c => continues && Reaches(c.Label, label, nested),
        LambdaExpr => false,
        WhileStmt or DoWhileStmt or ForInStmt or LoopExpr =>
            label is not null && AstChildren.Of(node).Any(child => Jumps(child, label, nested: true, breaks, continues)),
        _ => AstChildren.Of(node).Any(child => Jumps(child, label, nested, breaks, continues)),
    };

    /// <summary>Does a jump carrying <paramref name="written"/> reach the loop labeled
    /// <paramref name="own"/>? A plain one takes the innermost loop — this one unless the jump
    /// stands in a loop nested in it.</summary>
    private static bool Reaches(string? written, string? own, bool nested) =>
        written is null ? !nested : own is not null && written == own;
}
