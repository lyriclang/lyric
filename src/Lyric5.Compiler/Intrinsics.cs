namespace Lyric5.Compiler;

/// <summary>
/// The natives of <c>stdlib5/</c> that are no C function, and the runtime macro each becomes.
/// std reaches the runtime's functions as any program reaches C — <c>extern "C"</c>; what is left
/// here a C function cannot be: a macro that serves every type, that takes a function value, or
/// that the compiler asks a question of by name.
/// </summary>
public static class Intrinsics
{
    private static readonly IReadOnlyDictionary<string, string> Runtime = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // A task's context and a thread around a function value: the macro passes its code and
        // its environment, which a C parameter of one type cannot.
        ["std.task.startContext"] = "LYR_TASK_START",
        ["std.task.startThread"] = "LYR_THREAD_START",
        // The question whether a scheduler runs, apart from currentScheduler: the compiler narrows
        // a program that sets none by it (SchedulerFree; Reachability.CallsImport's `unless`).
        ["std.task.schedulerRuns"] = "LYR_TASK_SCHEDULER_RUNS",
        // The atomic operations: the C11 builtins on a place — a field of the scheduler's, the
        // value of std.sync's Atomic<T> —, for every T.
        ["std.task.atomicLoad"] = "LYR_ATOMIC_LOAD",
        ["std.task.atomicStore"] = "LYR_ATOMIC_STORE",
        ["std.task.atomicExchange"] = "LYR_ATOMIC_EXCHANGE",
        ["std.task.atomicCompareAndSet"] = "LYR_ATOMIC_CAS",
        ["std.task.atomicFetchAndAdd"] = "LYR_ATOMIC_FETCH_ADD",
        // std.core's numbers: whether + - * leave the type, the bit counts and the rotation, for
        // every integer width.
        ["std.core.addOverflows"] = "LYR_ADD_OVERFLOWS",
        ["std.core.subOverflows"] = "LYR_SUB_OVERFLOWS",
        ["std.core.mulOverflows"] = "LYR_MUL_OVERFLOWS",
        ["std.core.addWrapping"] = "LYR_ADD_WRAPPING",
        ["std.core.subWrapping"] = "LYR_SUB_WRAPPING",
        ["std.core.mulWrapping"] = "LYR_MUL_WRAPPING",
        ["std.core.countLeadingZeros"] = "LYR_CLZ",
        ["std.core.countTrailingZeros"] = "LYR_CTZ",
        ["std.core.countOnes"] = "LYR_POPCOUNT",
        ["std.core.rotateBitsLeft"] = "LYR_ROTL",
        // The conversion of `as` toward a type parameter; an object's address, the hash of an
        // 'Identity'; a float's bits of either width and IEEE's total order as a key.
        ["std.core.asTypeOf"] = "LYR_CONVERT",
        ["std.core.identityOf"] = "LYR_IDENTITY",
        ["std.core.floatBits"] = "LYR_FLOAT_BITS",
        ["std.core.floatFromBits"] = "LYR_FLOAT_FROM_BITS",
        ["std.core.totalOrderKey"] = "LYR_TOTAL_ORDER_KEY",
        // The StringBuilder's bulk copies (measurement point 3): memmove of any element type.
        ["std.core.moveElements"] = "LYR_SLICE_MOVE",
        // StringView: read as the 'Slice<uint8>' it is below the checker; the search, which the
        // header keeps inline — a C function would cost every step of a search loop its call —,
        // and a little-endian word at a byte.
        ["std.core.viewBytes"] = "LYR_VIEW_BYTES",
        ["std.core.viewMatchesAt"] = "LYR_VIEW_MATCHES_AT",
        ["std.core.viewFind"] = "LYR_VIEW_FIND",
        ["std.core.viewWordAt"] = "LYR_VIEW_WORD",
    };

    /// <summary>The names the <see cref="SubsetGate"/> lets through as <c>CallImport</c>.</summary>
    public static IReadOnlySet<string> Names { get; } = Runtime.Keys.ToHashSet(StringComparer.Ordinal);

    /// <summary>A C function the program declares (<c>extern "C"</c>): its import is named
    /// <c>C:&lt;symbol&gt;</c>.</summary>
    public static bool IsForeign(string name) => name.StartsWith("C:", StringComparison.Ordinal);

    /// <summary>The C call for a native function with the given argument expressions: the runtime's
    /// macro for the standard library's, the symbol itself for a C function the program declares.</summary>
    public static string Call(string name, string[] arguments) =>
        $"{(IsForeign(name) ? name[2..] : Runtime[name])}({string.Join(", ", arguments)})";
}
