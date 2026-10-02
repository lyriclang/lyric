namespace Lyric.Ir.Lowering;

/// <summary>
/// The body functions of a module's coroutines.
///
/// <para>A written coroutine becomes TWO functions: the FACTORY keeps the regular
/// <see cref="FunctionId"/>, so a caller writes an unchanged <c>call</c> and gets the coroutine back;
/// the BODY is registered here and appended at the end, exactly like a lifted lambda and for the same
/// reason — it arises only in pass 2, but its id has to be settled while the factory is built.
/// Registered as its lowerer, built already: the factory is made of what the lowerer knows before
/// it runs.</para>
/// </summary>
internal sealed class CoroutineTable
{
    private readonly List<(FunctionId Id, FunctionLowerer Body)> _pending = new();
    /// <summary>How far the lowering has come. The table is drained SEVERAL times — an instance can
    /// request a lambda, a lambda an instance — and without this mark everything would arise anew on
    /// every pass.</summary>
    private int _lowered;

    private readonly FunctionIds _ids;

    public CoroutineTable(FunctionIds ids) => _ids = ids;

    public bool IsEmpty => _pending.Count == 0;
    public int Count => _pending.Count;

    /// <summary>Registers a body and returns the id under which the factory references it.</summary>
    public FunctionId Register(FunctionLowerer body)
    {
        var id = _ids.Next();
        _pending.Add((id, body));
        return id;
    }

    public List<(FunctionId Id, IrFunction Function)> LowerAll()
    {
        var lowered = new List<(FunctionId, IrFunction)>(_pending.Count - _lowered);
        for (; _lowered < _pending.Count; _lowered++)
            lowered.Add((_pending[_lowered].Id, _pending[_lowered].Body.Run()));
        return lowered;
    }
}
