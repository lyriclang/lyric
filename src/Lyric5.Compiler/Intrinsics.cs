namespace Lyric5.Compiler;

/// <summary>
/// The natively backed functions of <c>stdlib5/</c> and the runtime call each becomes (M2 S3).
/// A provisional table with a date: M8a rewrites <c>std</c> in Lyric, source-first, and what the
/// runtime keeps in C (01 L9) is reached through the C ABI from then on.
/// </summary>
public static class Intrinsics
{
    private static readonly IReadOnlyDictionary<string, string> Runtime = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["std.io.print"] = "lyr_print",
        ["std.io.println"] = "lyr_println",
        ["std.string.concat"] = "lyr_str_concat",
        ["std.string.fromInt"] = "lyr_str_from_int",
        ["std.string.fromUint"] = "lyr_str_from_uint",
        ["std.string.fromBool"] = "lyr_str_from_bool",
        ["std.string.fromFloat"] = "lyr_str_from_float",
        ["std.string.fromChar"] = "lyr_str_from_char",
        ["std.core.panic"] = "lyr_panic_message",
        // The catalogue (05 E8): 'assert' a check at the call, so its trace starts in the program;
        // the two that never return are panics of their own codes.
        ["std.core.assert"] = "LYR_ASSERT",
        ["std.core.unreachable"] = "lyr_panic_unreachable",
        ["std.core.todo"] = "lyr_panic_todo",
        // std.task's side of the scheduler (06 N6 S1; 13 §1.6-1.7): a task's context around a
        // function value, which the macro passes as its code and environment; the thread's
        // scheduler; the park; the poller; the monotonic clock.
        ["std.task.startContext"] = "LYR_TASK_START",
        ["std.task.park"] = "lyr_coro_park",
        ["std.task.currentScheduler"] = "lyr_task_scheduler",
        ["std.task.setCurrentScheduler"] = "lyr_task_set_scheduler",
        ["std.task.waitOnPoller"] = "lyr_task_wait",
        ["std.task.monotonicNanos"] = "lyr_clock_monotonic_ns",
        // A task's panic (05 E8, 06 T4): the scheduler's resume, which keeps a panic the
        // context's; the report it left; the panic again where an await meets it.
        ["std.task.resumeContext"] = "lyr_task_resume",
        ["std.task.panicCode"] = "lyr_task_panic_code",
        ["std.task.panicMessage"] = "lyr_task_panic_message",
        ["std.task.panicTrace"] = "lyr_task_panic_trace",
        ["std.task.repanic"] = "lyr_task_repanic",
        // Threads (06 G1, G2): a thread around a function value; the thread's poller as a number
        // another thread wakes; the pause of a spinning lock.
        ["std.task.startThread"] = "LYR_THREAD_START",
        ["std.task.currentPoller"] = "lyr_task_poller",
        ["std.task.wakePoller"] = "lyr_task_wake",
        ["std.task.spin"] = "lyr_task_spin",
        // Signals as a channel (10 Q9, 06 K5): the abstract names and the system's numbers, the
        // handler on or off, the watcher's poller, the caught ones.
        ["std.task.signalNumber"] = "lyr_signal_number",
        ["std.task.signalKind"] = "lyr_signal_kind",
        ["std.task.catchSignal"] = "lyr_signal_catch",
        ["std.task.attachSignals"] = "lyr_signal_attach",
        ["std.task.takeSignals"] = "lyr_signal_take",
        // std.sync's atomics (06 G4, K6; N7 P2): the C11 builtins on an Atomic's field, as macros
        // that serve every T.
        ["std.sync.atomicLoad"] = "LYR_ATOMIC_LOAD",
        ["std.sync.atomicStore"] = "LYR_ATOMIC_STORE",
        ["std.sync.atomicExchange"] = "LYR_ATOMIC_EXCHANGE",
        ["std.sync.atomicCompareAndSet"] = "LYR_ATOMIC_CAS",
        ["std.sync.atomicFetchAndAdd"] = "LYR_ATOMIC_FETCH_ADD",
        // std.core's numbers (10 B5): whether + - * leave the type, the bit counts and the
        // rotation, as macros that serve every integer width.
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
        // Parsing (10 B5 Z6): a string's length and bytes until StringView (M8a S8), the
        // conversion of `as` toward a type parameter, a float's text read by C.
        ["std.core.byteCount"] = "LYR_STR_LEN",
        ["std.core.byteAt"] = "LYR_STR_BYTE",
        ["std.core.convertWrapping"] = "LYR_CONVERT",
        ["std.core.floatOfText"] = "lyr_str_to_float64",
        ["std.core.float32OfText"] = "lyr_str_to_float32",
    };

    /// <summary>The names the <see cref="SubsetGate"/> lets through as <c>CallImport</c>.</summary>
    public static IReadOnlySet<string> Names { get; } = Runtime.Keys.ToHashSet(StringComparer.Ordinal);

    /// <summary>A C function the program declares (<c>extern "C"</c>, 11 W4): its import is named
    /// <c>C:&lt;symbol&gt;</c>.</summary>
    public static bool IsForeign(string name) => name.StartsWith("C:", StringComparison.Ordinal);

    /// <summary>The C call for a native function with the given argument expressions: the runtime's
    /// for the standard library's, the symbol itself for a C function the program declares.</summary>
    public static string Call(string name, string[] arguments) =>
        $"{(IsForeign(name) ? name[2..] : Runtime[name])}({string.Join(", ", arguments)})";
}
