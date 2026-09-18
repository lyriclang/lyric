namespace Lyric.Ir;

/// <summary>
/// The IR optimizations, one bit each.
///
/// <para>A profile switches them together; a diagnostic switch takes one out alone. The second
/// form exists for bisecting: when a program answers differently or runs slower optimized, the
/// question is which pass, and answering it by editing the compiler is an afternoon where a flag
/// is a minute.</para>
/// </summary>
[Flags]
public enum IrPasses
{
    None = 0,

    /// <summary>Splices small callees into their callers (<see cref="Inliner"/>).</summary>
    Inline = 1 << 0,

    /// <summary>Dissolves objects that never leave their frame into locals
    /// (<see cref="ScalarReplacement"/>).</summary>
    ScalarReplacement = 1 << 1,

    /// <summary>Turns an interface call whose receiver's concrete type is proven into a direct
    /// call (<see cref="Devirtualizer"/>).</summary>
    Devirtualize = 1 << 2,

    All = Inline | ScalarReplacement | Devirtualize,
}
