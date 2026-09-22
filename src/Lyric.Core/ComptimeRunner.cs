namespace Lyric.Core;

/// <summary>
/// What evaluates <c>comptime</c> expressions for the compiler.
///
/// <para>The compiler does not execute code. It lowers every <c>comptime</c> site into a function
/// of its own, hands the resulting module to whoever implements this, and reads the values back
/// as literals. The runtime supplies the implementation — the VM is the evaluator, in a sandbox
/// that grants no capability and counts instructions — and a tool that only checks (an editor,
/// <c>lyrc check</c>) supplies none and never runs anything.</para>
///
/// <para>Declared here rather than in the front end so the runtime can implement it without
/// referencing the compiler: the front end depends on the core, the runtime depends on the
/// core, and the two meet in this interface.</para>
/// </summary>
public interface IComptimeRunner
{
    /// <summary>
    /// Evaluates the named functions of a compiled module — one per <c>comptime</c> site, each
    /// without parameters — and returns one outcome per name, in order.
    /// </summary>
    /// <param name="module">The <c>.lyrbc</c> bytes of the evaluation module.</param>
    /// <param name="functions">The qualified function names to invoke.</param>
    IReadOnlyList<ComptimeOutcome> Evaluate(byte[] module, IReadOnlyList<string> functions);
}

/// <summary>
/// One evaluated site: the value as a boxed <c>long</c>, <c>ulong</c>, <c>double</c>,
/// <c>bool</c>, <c>int</c> (a code point) or <c>string</c>, or the reason there is none.
/// </summary>
/// <param name="Value">The value, or <c>null</c> on failure.</param>
/// <param name="Failure">Why the site could not be evaluated — a panic, an exhausted budget,
/// a capability the module would need — or <c>null</c> on success. The compiler reports it at
/// the site.</param>
public sealed record ComptimeOutcome(object? Value, string? Failure)
{
    public static ComptimeOutcome Of(object value) => new(value, null);

    public static ComptimeOutcome Failed(string why) => new(null, why);
}
