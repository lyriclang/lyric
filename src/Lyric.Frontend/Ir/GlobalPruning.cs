namespace Lyric.Ir;

/// <summary>
/// Design 01 B13: a module binding nothing reads, whose initializer only gives its value, goes —
/// its slot, and its lines in the init function. Every other binding of a reached module stays,
/// and runs in order (07 G2): an initializer that calls may do anything.
///
/// <para>Read means touched by a function the program reaches — a load, a store, an address — or
/// by the initializer of a global that stays: the fixpoint over the init function, since one
/// constant may be built of another. Which initializer only gives its value the lowering knows
/// (<see cref="Lowering.GlobalTable"/>); where it stands in the init function it noted then.</para>
///
/// <para>Until N1b only a <c>static let</c> of a literal had no slot, and every program carried
/// std.collections' <c>emptyGroup</c>, <c>lowBits</c> and <c>highBits</c>.</para>
/// </summary>
internal static class GlobalPruning
{
    /// <summary>Drops what B13 drops; true when something went.</summary>
    public static bool Run(IrModule module, IReadOnlyDictionary<int, (int Block, int Start, int End)> droppable)
    {
        if (module.GlobalInit is not { } initId || droppable.Count == 0) return false;

        var reachable = Reachability.Collect(module);
        var init = module.Functions[initId.Value];
        var owner = new Dictionary<(int Block, int Index), int>();
        foreach (var (global, range) in droppable)
            for (var i = range.Start; i < range.End; i++)
                owner[(range.Block, i)] = global;

        var kept = new HashSet<int>();
        foreach (var f in reachable)
        {
            if (f == initId.Value) continue;
            foreach (var block in module.Functions[f].Blocks)
                foreach (var op in block.Insts)
                    Touch(op, kept);
        }
        for (var b = 0; b < init.Blocks.Count; b++)
            for (var i = 0; i < init.Blocks[b].Insts.Count; i++)
                if (!owner.ContainsKey((b, i)))
                    Touch(init.Blocks[b].Insts[i], kept);

        // a kept global's initializer keeps what it reads, and that one's in turn
        var done = new HashSet<int>();
        for (var changed = true; changed;)
        {
            changed = false;
            foreach (var (global, range) in droppable)
            {
                if (!kept.Contains(global) || !done.Add(global)) continue;
                changed = true;
                for (var i = range.Start; i < range.End; i++)
                    Touch(init.Blocks[range.Block].Insts[i], kept);
            }
        }

        var dropped = droppable.Keys.Where(g => !kept.Contains(g)).ToHashSet();
        if (dropped.Count == 0) return false;

        // the ranges out, the last one of a block first, so the earlier ones keep their places
        foreach (var (global, range) in droppable.Where(d => dropped.Contains(d.Key)).OrderByDescending(d => d.Value.Start))
            init.Blocks[range.Block].Insts.RemoveRange(range.Start, range.End - range.Start);
        // their temps went with them: the table dense again, every reference renumbered
        ScalarReplacement.CompactTemps(init);

        // what nobody reaches may still touch a dropped slot: the pruning takes it out first
        Reachability.Prune(module);

        var map = new int[module.Globals.Count];
        var stay = new List<IrGlobal>();
        for (var i = 0; i < module.Globals.Count; i++)
        {
            map[i] = dropped.Contains(i) ? -1 : stay.Count;
            if (map[i] >= 0) stay.Add(module.Globals[i]);
        }
        module.Globals.Clear();
        module.Globals.AddRange(stay);

        GlobalId Renumbered(GlobalId id) => map[id.Value] >= 0
            ? new GlobalId(map[id.Value])
            : throw new InvalidOperationException($"global {id.Value} was dropped and is still touched");
        foreach (var function in module.Functions)
            foreach (var block in function.Blocks)
                for (var i = 0; i < block.Insts.Count; i++)
                    block.Insts[i] = block.Insts[i] switch
                    {
                        LoadGlobal load => load with { Global = Renumbered(load.Global) },
                        StoreGlobal store => store with { Global = Renumbered(store.Global) },
                        AddrGlobal addr => addr with { Global = Renumbered(addr.Global) },
                        var op => op,
                    };
        return true;
    }

    private static void Touch(IrOp op, HashSet<int> kept)
    {
        switch (op)
        {
            case LoadGlobal load: kept.Add(load.Global.Value); break;
            case StoreGlobal store: kept.Add(store.Global.Value); break;
            case AddrGlobal addr: kept.Add(addr.Global.Value); break;
        }
    }
}
