using Lyric.Core;

namespace Lyric.Ir.Interp;

/// <summary>
/// Why an evaluation stopped without a value: a panic of the
/// program — with the code the run would have ended with —, the budget spent, a call into the
/// world, or a form the interpreter does not run yet. <see cref="Trace"/> is the interpreter's
/// stack where it stopped, innermost first, which the diagnostic shows as the inner backtrace.
/// </summary>
public sealed class ComptimeFailure : Exception
{
    /// <summary><c>LYR-CT0001</c> a panic, <c>LYR-CT0002</c> the budget, <c>LYR-CT0003</c> a call
    /// into the world, <c>LYR-CT0004</c> a file <c>embed</c> cannot read, <c>LYR-CT0005</c> a form
    /// not run at compile time yet.</summary>
    public string Code { get; }

    /// <summary>The run-time code the panic would have ended the program with, for a panic.</summary>
    public string? RuntimeCode { get; }

    public IReadOnlyList<(string Function, Span At)> Trace { get; internal set; } = [];

    private ComptimeFailure(string code, string? runtimeCode, string message) : base(message)
    {
        Code = code;
        RuntimeCode = runtimeCode;
    }

    /// <summary>The same failure with the trace it has now — kept where it is met again.</summary>
    internal ComptimeFailure Copy() => new(Code, RuntimeCode, Message) { Trace = [.. Trace] };

    public static ComptimeFailure Panic(string runtimeCode, string message) => new("LYR-CT0001", runtimeCode, message);
    public static ComptimeFailure Budget(long budget) =>
        new("LYR-CT0002", null, $"the evaluation took more than its budget of {budget} steps ('comptimeBudget' under [package] in lyric.toml sets it)");
    public static ComptimeFailure World(string what) => new("LYR-CT0003", null, what);
    public static ComptimeFailure Embed(string what) => new("LYR-CT0004", null, what);
    public static ComptimeFailure NotYet(string what) => new("LYR-CT0005", null, what);

    /// <summary>A macro's own error: <c>error(at, msg)</c> from its body, the message
    /// the macro's and the span the node's it names - <see cref="At"/>, or none where the
    /// macro named a place the program does not hold.</summary>
    public static ComptimeFailure Macro(Span at, string message) => new("LYR-MAC0001", null, message) { At = at };

    public Span At { get; init; }
}
