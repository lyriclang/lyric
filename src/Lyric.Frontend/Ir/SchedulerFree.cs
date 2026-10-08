namespace Lyric.Ir;

/// <summary>
/// A program that sets no scheduler answers no to <c>std.task.schedulerRuns</c> everywhere (06
/// M6-26, 13 M8b P3) — and keeps nothing of what only its yes would run.
///
/// <para>WHY THIS IS SOUND. A scheduler runs on a thread only after <c>setCurrentScheduler</c> set
/// one there, and only Lyric code calls that native (<c>runMain</c>, std.thread's threads and
/// pools). Before the first such call no thread has a scheduler, so every <c>schedulerRuns()</c>
/// so far said no and only its no edges ran: the first call is reached through unguarded edges
/// alone. Where the walk that leaves the yes edges out (<see cref="Reachability.CallsImport"/>
/// with <c>unless</c>) finds no call of it, there is none, ever.</para>
///
/// <para>WHAT IT SAVES. The yes edge is where a blocking call parks on the I/O pool: std.thread's
/// <c>Pool</c>, the scheduler, their bindings. Every program that writes to the console or
/// touches a file reaches that branch, and it costs a hello world half a megabyte and its goldens
/// the whole scheduler. The branch becomes a jump to its no; the blocks only the yes reached go;
/// the pruning and the global pruning behind it take what nothing reaches any more.</para>
///
/// <para>The init function stays as it is: the global pruning finds a binding's lines there by
/// the places the lowering noted. No initializer asks the question itself — it is asked in the
/// functions an initializer may call.</para>
/// </summary>
internal static class SchedulerFree
{
    public const string Question = "std.task.schedulerRuns";
    public const string Setter = "std.task.setCurrentScheduler";

    /// <summary>Narrows the module where it sets no scheduler; true when something changed.</summary>
    public static bool Run(IrModule module)
    {
        var guard = module.Imports.FindIndex(import => import.Name == Question);
        if (guard < 0 || Reachability.CallsImport(module, Setter, unless: Question)) return false;

        var changed = false;
        for (var f = 0; f < module.Functions.Count; f++)
            if (module.GlobalInit is not { } init || init.Value != f)
                changed |= Narrow(module.Functions[f], guard);
        return changed;
    }

    /// <summary>Every branch over the question's answer becomes its no; what only a yes reached
    /// goes, the blocks renumbered densely and the temps compacted, as the verifier wants.</summary>
    private static bool Narrow(IrFunction function, int guard)
    {
        var any = false;
        foreach (var block in function.Blocks)
            if (block.Terminator is CondBranch branch && block.Insts.Any(op =>
                    op is CallImport call && call.Target.Value == guard && call.Dest == branch.Cond))
            {
                block.Terminator = new Branch(branch.IfFalse, branch.Span);
                any = true;
            }
        if (!any) return false;

        var reached = new HashSet<int>();
        var open = new Stack<int>();
        open.Push(function.Entry.Value);
        while (open.Count > 0)
        {
            var at = open.Pop();
            if (!reached.Add(at)) continue;
            foreach (var next in IrShape.SuccessorsOf(function.Blocks[at].Terminator!))
                open.Push(next.Value);
        }
        if (reached.Count == function.Blocks.Count) return true;

        // the entry is block 0 and stays first: the order kept, the gaps closed
        var map = new Dictionary<int, BlockId>();
        var kept = new List<IrBlock>();
        foreach (var block in function.Blocks)
            if (reached.Contains(block.Id.Value))
            {
                map[block.Id.Value] = new BlockId(kept.Count);
                kept.Add(block);
            }
        var rebuilt = kept.Select(block => new IrBlock(map[block.Id.Value], block.Insts)
        {
            Terminator = IrShape.Rewrite(block.Terminator!, temp => temp, id => map[id.Value]),
        }).ToList();
        function.Blocks.Clear();
        function.Blocks.AddRange(rebuilt);
        function.Entry = map[function.Entry.Value];
        ScalarReplacement.CompactTemps(function);
        CompactLocals(function);
        return true;
    }

    /// <summary>The locals what remains names — the parameters always, first and in order —, the
    /// table dense again. A local only the gone blocks used would hold its type up: the emitter
    /// defines every type a local names, and the yes edge's <c>Task</c> brings the scheduler.</summary>
    private static void CompactLocals(IrFunction function)
    {
        var used = new HashSet<int>(Enumerable.Range(0, function.ParamCount));
        foreach (var block in function.Blocks)
            foreach (var op in block.Insts)
                IrShape.Rewrite(op, temp => temp, local => { used.Add(local.Value); return local; });
        if (used.Count == function.Locals.Count) return;

        var map = new Dictionary<int, LocalId>();
        var kept = new List<IrLocal>();
        foreach (var local in function.Locals)
            if (used.Contains(local.Id.Value))
            {
                map[local.Id.Value] = new LocalId(kept.Count);
                kept.Add(local with { Id = new LocalId(kept.Count) });
            }
        foreach (var block in function.Blocks)
            for (var i = 0; i < block.Insts.Count; i++)
                block.Insts[i] = IrShape.Rewrite(block.Insts[i], temp => temp, local => map[local.Value]);
        function.Locals.Clear();
        function.Locals.AddRange(kept);
    }
}
