using Lyric.Bytecode;

namespace Lyric.Vm;

/// <summary>
/// A coroutine at runtime (format 4.0): the frames it is suspended over, and what the first pull
/// needs to build the body's frame.
///
/// <para>The frames are captured OUTERMOST FIRST — the body's frame, then its callees down to
/// the one standing at the <c>yield</c> — because that is the order a resume pushes them back.
/// While the chain runs they are ordinary frames on the interpreter's stack; suspension is the
/// only time they live here. The array is kept across suspensions and regrown only when the
/// chain deepens, so a steady generator allocates nothing per yield after its first.</para>
///
/// <para>The state machine this replaces compiled the suspension into the body: locals in an
/// object, a jump table at entry. It could never suspend a HELPER — the helper's frame belonged
/// to the caller's stack, not to the coroutine — which is the wall §10a (stackful, 4.0) exists
/// to remove.</para>
/// </summary>
internal sealed class CoroutineChain(int body, LyrValue[] args, TypeTag yieldTag,
    byte[] yieldType)
{
    internal enum ChainState : byte
    {
        NotStarted,
        Suspended,
        Running,
        Done,
    }

    public ChainState State = ChainState.NotStarted;

    /// <summary>The suspended segment, outermost first; meaningful for the first
    /// <see cref="SavedCount"/> entries and only while <see cref="State"/> is
    /// <see cref="ChainState.Suspended"/>.</summary>
    public Interpreter.Frame[]? Saved;

    public int SavedCount;

    /// <summary>The body's index into the prepared functions.</summary>
    public readonly int Body = body;

    /// <summary>The captured arguments, handed to the body's frame at the first pull and
    /// released then.</summary>
    public LyrValue[]? Args = args;

    /// <summary>The chain's element type, by its leading tag. What the lenient pull shapes its
    /// answer with — <see cref="TypeTag.Void"/> is a bare-yield chain.</summary>
    public readonly TypeTag YieldTag = yieldTag;

    /// <summary>The chain's element type as its canonical ENCODING, copied from the
    /// <c>mkcoro</c> that built it. §10a rule 3 compares a yield site's encoded type against
    /// this — byte equality is type equality within one module, and the runtime needs no type
    /// model for it.</summary>
    public readonly byte[] YieldType = yieldType;
}
