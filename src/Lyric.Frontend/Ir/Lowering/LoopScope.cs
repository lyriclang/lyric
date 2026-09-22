namespace Lyric.Ir.Lowering;

/// <summary>
/// Where the <c>break</c> and <c>continue</c> of the innermost loop jump to.
///
/// <para>THE TARGETS ARISE ON DEMAND. Created eagerly, they turn <c>do { return … } while (…)</c> into
/// a compiler crash: if the body terminates, nobody reaches the condition and the exit, and the
/// verifier rejects unreachable blocks, as there is no <c>SimplifyCfg</c> pass. A block nobody enters
/// is now never created.</para>
///
/// <para>The fall-through flag cannot decide this: <c>do { if (c) { break; } return 1; }</c> does not
/// fall through and reaches the exit all the same. "Is the block reachable" and "does the body fall
/// through" are two different questions, and only the first counts here. This type therefore
/// remembers whether anyone actually requested the target.</para>
/// </summary>
internal sealed class LoopScope(BlockBuilder blocks)
{
    private BlockId? _continue;
    private BlockId? _break;

    /// <summary>The defer-stack depth OUTSIDE this loop. A <c>break</c> or <c>continue</c> leaves
    /// every scope above it and runs their defers first (§7.5) — without the mark it would either
    /// skip them or drain the scopes it does not leave.</summary>
    public int DeferDepth { get; init; }

    /// <summary>The loop's label, when it has one: what 'break L' and 'continue L' look for
    /// walking outward through the loop stack.</summary>
    public string? Label { get; init; }

    /// <summary>
    /// For <c>while</c> and <c>for-in</c>, where both blocks are ALWAYS reachable: the condition through
    /// the entry edge, the exit through its false edge. They also have to exist beforehand, because the
    /// <c>CondBranch</c> names them before the body is lowered.
    ///
    /// <para>Only <c>do-while</c> has the problem: there the condition stands BEHIND the body, and if
    /// that terminates, nobody reaches it.</para>
    /// </summary>
    public LoopScope(BlockBuilder blocks, BlockId continueTarget, BlockId breakTarget)
        : this(blocks)
    {
        _continue = continueTarget;
        _break = breakTarget;
    }

    /// <summary>For <c>while let</c>: the condition is always reachable, but the exit exists only
    /// when the pattern can fail or a <c>break</c> asks for it — an irrefutable pattern without a
    /// break never leaves the loop, and a block for it would be unreachable.</summary>
    public LoopScope(BlockBuilder blocks, BlockId continueTarget) : this(blocks)
    {
        _continue = continueTarget;
    }

    /// <summary>The target of <c>continue</c>: the condition for <c>while</c> and <c>do-while</c>, the
    /// loop head for <c>for-in</c>.</summary>
    public BlockId ContinueTarget => _continue ??= blocks.NewBlock();

    /// <summary>The target of <c>break</c>.</summary>
    public BlockId BreakTarget => _break ??= blocks.NewBlock();

    /// <summary>
    /// Creates a target that the body is known to jump to, so its block id stands BEFORE every block
    /// the body produces.
    ///
    /// <para>A protected region is a CONTIGUOUS range of block ids, ending at the block count after
    /// its body. Created on demand from inside a <c>try</c>, a jump target of the ENCLOSING loop
    /// lands in that range — and then the code after the loop is covered by a handler that belongs
    /// inside it: a <c>throw</c> behind the loop is caught by the <c>catch</c> in its body, control
    /// returns to the condition, and the loop runs forever. <c>while</c> and <c>for-in</c> never had
    /// it, because their targets exist before the body is lowered.</para>
    ///
    /// <para>Only where the jump is REACHED: a target nobody enters is a verifier error, which is
    /// what made these blocks lazy in the first place.</para>
    /// </summary>
    public void Reserve(bool continueTarget)
    {
        if (continueTarget) _ = ContinueTarget;
        else _ = BreakTarget;
    }

    /// <summary>Has anyone requested the target? Only then does the block exist.</summary>
    public bool ContinueRequested => _continue is not null;

    /// <inheritdoc cref="ContinueRequested"/>
    public bool BreakRequested => _break is not null;
}
