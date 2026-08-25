using Lyric.Core;

namespace Lyric.Ir;

//Ir Consts
public abstract record IrConstValue;
public sealed record IntConst(ulong Value) : IrConstValue;
public sealed record FloatConst(double Value) : IrConstValue;
public sealed record BoolConst(bool Value) : IrConstValue;
public sealed record CharConst(int CodePoint) : IrConstValue;
public sealed record StringConst(string Value) : IrConstValue;

public abstract record IrInst(Span Span);
public abstract record IrOp(Span Span) : IrInst(Span);
public abstract record IrTerminator(Span Span) : IrInst(Span);

//Ir Op
public sealed record Const(TempId Dest, IrType Type, IrConstValue Value, Span Span) : IrOp(Span);
public sealed record BinOp(TempId Dest, IrBinKind Kind, IrType Type, TempId Lhs, TempId Rhs, Span Span) : IrOp(Span);
public sealed record UnOp(TempId Dest, IrUnKind Kind, IrType Type, TempId Operand, Span Span) : IrOp(Span);
public sealed record Convert(TempId Dest, IrType From, IrType To, TempId Operand, Span Span) : IrOp(Span);
public sealed record LoadLocal(TempId Dest, LocalId Local, IrType Type, Span Span) : IrOp(Span);
public sealed record StoreLocal(LocalId Local, TempId Value, Span Span) : IrOp(Span);
public sealed record Call(TempId? Dest, FunctionId Target, TempId[] Args, Span Span) : IrOp(Span); // Dest == null means void

// A call to a natively backed function from the stdlib. Its own instruction rather than a shared
// index space with Call: in the IR these are two different things — one has a body, the other does
// not — and the verifier checks each against its own table. The index arithmetic belongs where the
// convention lives: in the bytecode writer.
public sealed record CallImport(TempId? Dest, ImportId Target, TempId[] Args, Span Span) : IrOp(Span);

// Objects. Field access goes through the index rather than the name: Lyric is statically typed and
// has no monkey patching, so the index is fixed at compile time. A name lookup with an inline cache,
// as in CPython or Ruby, solves a problem this language does not have.
// The type stands on each of the three instructions although the object knows it: only that way can
// the bytecode reader check the field index against a layout at load time, without data-flow
// analysis.
/// <param name="Result">Whether this type is bound as a reference or as a value. A copy for the
/// printer; the temp table stays the authority. Without it the dump prints <c>&amp;ty0</c> for a
/// struct instead of <c>val ty0</c> — a line claiming something other than what is executed.</param>
public sealed record NewObject(TempId Dest, TypeId Type, IrType Result, Span Span) : IrOp(Span);
public sealed record LoadField(TempId Dest, TempId Object, TypeId Type, FieldId Field, IrType FieldType, Span Span) : IrOp(Span);
public sealed record StoreField(TempId Object, TypeId Type, FieldId Field, TempId Value, Span Span) : IrOp(Span);

// Arrays. An element index is a RUNTIME VALUE; unlike field and type indices it is not checkable at
// load time. A violation is therefore a panic rather than a load error: "the program miscalculated"
// is something other than "the compiler produced nonsense", and only the second belongs in load-time
// validation.
public sealed record NewArray(TempId Dest, IrType Element, TempId[] Elements, Span Span) : IrOp(Span);
public sealed record LoadElem(TempId Dest, TempId Array, TempId Index, IrType Element, Span Span) : IrOp(Span);
public sealed record StoreElem(TempId Array, TempId Index, TempId Value, Span Span) : IrOp(Span);
public sealed record ArrayLen(TempId Dest, TempId Array, Span Span) : IrOp(Span);

// xs + ys and xs * n — built-in language semantics, not a library. Both yield a NEW array.
public sealed record ArrayConcat(TempId Dest, TempId Left, TempId Right, IrType Element, Span Span) : IrOp(Span);
public sealed record ArrayRepeat(TempId Dest, TempId Array, TempId Count, IrType Element, Span Span) : IrOp(Span);

// Optionals. '??', '??=' and '?.' are NOT here: they evaluate their right side conditionally and
// lower to branches over OptIsSome, like && and ||.
public sealed record OptNone(TempId Dest, IrType Inner, Span Span) : IrOp(Span);
public sealed record OptSome(TempId Dest, TempId Value, IrType Inner, Span Span) : IrOp(Span);
public sealed record OptIsSome(TempId Dest, TempId Option, Span Span) : IrOp(Span);
public sealed record OptGet(TempId Dest, TempId Option, IrType Inner, Span Span) : IrOp(Span);

// Enums. 'match' is NOT here: it reads the tag and branches on it like any other case distinction.
// After the branch, EnumAs narrows to the variant, and a field access on that is an ordinary
// LoadField with the variant's layout — the same division of labour as optissome and optget.
public sealed record NewVariant(TempId Dest, TypeId Variant, TypeId Enum, TempId[] Fields, Span Span) : IrOp(Span);
public sealed record EnumTag(TempId Dest, TempId Value, Span Span) : IrOp(Span);
public sealed record EnumAs(TempId Dest, TempId Value, TypeId Variant, Span Span) : IrOp(Span);

// Interfaces. The same division of labour as with optionals and enums: one instruction materializes
// the representation, another consumes it. MakeInterface attaches the concrete type, fixed at
// compile time, to an object reference; CallVirt fetches its target function from it. There is no
// 'downcast', and without one an interface value needs no runtime type check.
public sealed record MakeInterface(TempId Dest, TempId Value, TypeId Concrete, TypeId Interface,
    Span Span) : IrOp(Span);

/// <param name="Slot">The index into the interface's method slots, not its name. Like a field index
/// it is fixed at compile time, because Lyric is statically typed and has no monkey patching.</param>
/// <param name="ReturnType">A copy for the printer; the temp table stays the authority, and the
/// verifier checks that the two agree. Without it a callvirt line could not be formatted from the
/// instruction alone: unlike <c>Call</c> there is no target function to ask for its return
/// type.</param>
public sealed record CallVirt(TempId? Dest, TypeId Interface, int Slot, TempId[] Args,
    IrType ReturnType, Span Span) : IrOp(Span);

// Structs. The value semantics live entirely in this one instruction: the lowering places it at
// every binding point where a struct value is read from an existing location. A freshly built value
// (newobj, a call result) does not need it, because it belongs to nobody yet.
public sealed record StructCopy(TempId Dest, TempId Value, TypeId Type, Span Span) : IrOp(Span);

// Globals. Like LoadLocal and StoreLocal, but module-wide instead of frame-wide, and written only
// once, by the init function.
public sealed record LoadGlobal(TempId Dest, GlobalId Global, IrType Type, Span Span) : IrOp(Span);
public sealed record StoreGlobal(GlobalId Global, TempId Value, Span Span) : IrOp(Span);

// Closures. The same pair as with interfaces: one instruction materializes a fat pointer, another
// consumes it. The difference is WHERE the function index comes from — for an interface from a slot
// table at runtime, here directly from the instruction.

/// <param name="Environment">The object holding the captured values, or <c>null</c> when nothing is
/// captured. A closure without captures is then a pure function index and costs no allocation — the
/// common case for a filter lambda such as <c>(x) =&gt; x &gt; 0</c>.</param>
/// <param name="Target">The LIFTED function, not the lambda: by this point the lambda is already an
/// ordinary IrFunction whose parameter 0 is the environment. A closure call is therefore the same
/// mechanism as a method call with a receiver, not a second one beside it.</param>
public sealed record MakeClosure(TempId Dest, FunctionId Target, TempId? Environment,
    IrFunctionType Type, Span Span) : IrOp(Span);

/// <param name="Callee">The function value. Its environment is prepended as argument 0 at the call,
/// so the instruction does not name it.</param>
/// <param name="ReturnType">A copy for the printer, for the same reason as on
/// <see cref="CallVirt"/>: there is no target function to ask.</param>
public sealed record CallIndirect(TempId? Dest, TempId Callee, TempId[] Args,
    IrType ReturnType, Span Span) : IrOp(Span);

// Coroutines as CHAINS (format 4.0). A coroutine value holds captured frames, not a compiled
// state machine: mkcoro builds it suspended-before-start, resume pushes its frames back onto
// the interpreter's stack, and yield slices them off again down to the nearest running resume.
// The state-machine lowering these replace could never suspend a helper — the frame it would
// have to capture belonged to the CLR, not to the coroutine.

/// <param name="Body">The body function. Like <see cref="MakeClosure.Target"/> it is a root:
/// nothing else names it, and the chain calls it by index at the first pull.</param>
/// <param name="Args">The captured arguments — <c>this</c> first when the coroutine is a
/// method — which the first resume hands to the body's frame as its parameters.</param>
public sealed record MakeCoroutine(TempId Dest, FunctionId Body, TempId[] Args,
    IrFunctionType Type, Span Span) : IrOp(Span);

/// <param name="Lenient">The pull form: <c>false</c> is <c>resume</c> — exhaustion panics —
/// and <c>true</c> is <c>next()</c>, which writes <c>?T</c> (or, for a void chain, whether it
/// advanced). The whole 2.2 contract in one instruction; the state-machine era assembled it
/// from a flag parameter, a native import and three blocks of wrapping.</param>
/// <param name="YieldType">What the chain yields — the type <c>Dest</c> is written as before
/// any lenient wrapping, and a copy for the printer like <see cref="CallIndirect.ReturnType"/>.
/// </param>
public sealed record ResumePull(TempId? Dest, TempId Coroutine, bool Lenient,
    IrType YieldType, Span Span) : IrOp(Span);

/// <param name="Value">What the pull receives, or <c>null</c> for a void chain's bare
/// <c>yield;</c>.</param>
/// <param name="YieldType">The static type of <see cref="Value"/> at the yield site. Through
/// 3.x the sema admits yield only inside a coroutine body, where it already checked this
/// against the chain; the operand is in the encoding from day one because §10a rule 3 (4.0)
/// compares it against the RUNNING chain's element type at the suspension itself.</param>
/// <remarks>An op, not a terminator: when the chain is resumed, execution continues at the
/// next instruction — the suspension is the interpreter's business, not the CFG's.</remarks>
public sealed record YieldSuspend(TempId? Value, IrType YieldType, Span Span) : IrOp(Span);

//Ir Terminator
public sealed record Return(TempId? Value, Span Span) : IrTerminator(Span); // Value == null means a void return
public sealed record Branch(BlockId Target, Span Span) : IrTerminator(Span);
public sealed record CondBranch(TempId Cond, BlockId IfTrue, BlockId IfFalse, Span Span) : IrTerminator(Span);
public sealed record Unreachable(Span Span) : IrTerminator(Span);

// Exceptions. 'throw' is a terminator rather than an op: nothing runs after it in this block, and
// holding that structurally is the same decision as for 'return'.
/// <param name="Concrete">The concrete type of the thrown value, or <c>null</c> when it is only
/// known at runtime, meaning the value is interface-typed and carries it along as a fat pointer.
///
/// <para>The static type suffices here because Lyric has NO INHERITANCE. A class is exactly its
/// type and there are no subtypes, so the type at the throw site is the one a <c>catch</c> compares.
/// In C# or Java that would be wrong and a tag in the object would be needed.</para></param>
public sealed record Throw(TempId Value, TypeId? Concrete, Span Span) : IrTerminator(Span);

/// <summary>
/// The end of a <c>finally</c> region: unwinding continues where it was interrupted.
///
/// <para>Lyric has no <c>finally</c>; this region arises solely from <c>defer</c>. At the bytecode
/// level "runs while unwinding too" needs exactly this mechanism, so the language keeps one keyword
/// and the format gets the carrier for it.
/// </para>
/// </summary>
public sealed record EndFinally(Span Span) : IrTerminator(Span);
