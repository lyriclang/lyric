namespace Lyric5.Compiler;

/// <summary>
/// The natively backed functions of <c>stdlib5/</c> and the runtime call each becomes.
/// <c>std</c> is written in Lyric; what the runtime keeps in C it reaches through this table.
/// </summary>
public static class Intrinsics
{
    private static readonly IReadOnlyDictionary<string, string> Runtime = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // The console's buffers (lyr/console.h): std.io's print family puts, and flushes
        // where a flush is due — on the I/O pool where a scheduler runs.
        // The terminal (std.term): whether a standard stream is one, and its size.
        ["std.string.concat"] = "lyr_str_concat",
        ["std.string.fromInt"] = "lyr_str_from_int",
        ["std.string.fromUint"] = "lyr_str_from_uint",
        ["std.string.fromBool"] = "lyr_str_from_bool",
        ["std.string.fromFloat"] = "lyr_str_from_float",
        ["std.string.fromFloat32"] = "lyr_str_from_float32",
        ["std.core.shortestFloat32"] = "lyr_str_from_float32",
        ["std.string.fromChar"] = "lyr_str_from_char",
        ["std.core.panic"] = "lyr_panic_message",
        // A benchmark's measure: the clock, this thread's allocations and their bytes.
        ["std.core.benchClock"] = "lyr_clock_monotonic_ns",
        ["std.core.threadAllocations"] = "lyr_gc_thread_allocations",
        ["std.core.threadAllocatedBytes"] = "lyr_gc_thread_allocated_bytes",
        // Where an error was thrown, shown: a Backtrace's frames named.
        ["std.core.traceText"] = "lyr_trace_text",
        // The panics: 'assert' is Lyric, set in place at the call, so its trace starts
        // in the program; its failure and the two that never return are panics of their own codes.
        ["std.core.assertFailed"] = "lyr_panic_assert",
        ["std.core.unreachable"] = "lyr_panic_unreachable",
        ["std.core.todo"] = "lyr_panic_todo",
        // A collection changed while it was walked: a panic of its own code.
        ["std.collections.changedWhileWalked"] = "lyr_panic_walked",
        // std.task's side of the scheduler: a task's context around a
        // function value, which the macro passes as its code and environment; the thread's
        // scheduler; the park; the poller; the monotonic clock.
        ["std.task.startContext"] = "LYR_TASK_START",
        ["std.task.park"] = "lyr_coro_park",
        ["std.task.deadlocked"] = "lyr_panic_deadlock",
        ["std.task.poisonedBy"] = "lyr_panic_poisoned",
        ["std.task.currentScheduler"] = "lyr_task_scheduler",
        ["std.task.setCurrentScheduler"] = "lyr_task_set_scheduler",
        // The question whether one runs, apart from currentScheduler: asking makes no task of
        // main (Reachability.CallsImport's `unless`).
        ["std.task.schedulerRuns"] = "LYR_TASK_SCHEDULER_RUNS",
        ["std.task.waitOnPoller"] = "lyr_task_wait",
        ["std.task.monotonicNanos"] = "lyr_clock_monotonic_ns",
        // A task's panic: the scheduler's resume, which keeps a panic the
        // context's; the report it left; the panic again where an await meets it.
        ["std.task.resumeContext"] = "lyr_task_resume",
        ["std.task.panicCode"] = "lyr_task_panic_code",
        ["std.task.panicMessage"] = "lyr_task_panic_message",
        ["std.task.panicTrace"] = "lyr_task_panic_trace",
        ["std.task.repanic"] = "lyr_task_repanic",
        // Threads: a thread around a function value; the thread's poller as a number
        // another thread wakes; the pause of a spinning lock.
        ["std.task.startThread"] = "LYR_THREAD_START",
        // The processors, for the program's pool (std.thread's parallelMap).
        // The file system's calls (lyr/fs.h): one system call each, run on std.fs's I/O
        // pool; a view crosses as its pointer and length.
        // The zone database and TZ (std.time): std.fs and std.os import std.time, so it
        // reads the file and the variable through the same calls itself.
        ["std.task.currentPoller"] = "lyr_task_poller",
        ["std.task.wakePoller"] = "lyr_task_wake",
        // A descriptor's readiness on the thread's poller (lyr/task.h): std.net's waits.
        ["std.task.armOnPoller"] = "lyr_task_arm",
        ["std.task.forgetOnPoller"] = "lyr_task_forget",
        ["std.task.takeFromPoller"] = "LYR_TASK_TAKE",
        // The network (lyr/net.h): std.net's sockets, non-blocking, waited for on the
        // poller.
        // Processes (lyr/process.h): a child started, reaped, signalled; its pipes.
        ["std.task.spin"] = "lyr_task_spin",
        // Signals as a channel: the abstract names and the system's numbers, the
        // handler on or off, the watcher's poller, the caught ones.
        // The system (lyr/os.h): a text through the caller's buffer.
        // The atomic operations: the C11 builtins on a
        // place — a field of the scheduler's, the value of std.sync's Atomic<T> —, as macros that
        // serve every T.
        ["std.task.atomicLoad"] = "LYR_ATOMIC_LOAD",
        ["std.task.atomicStore"] = "LYR_ATOMIC_STORE",
        ["std.task.atomicExchange"] = "LYR_ATOMIC_EXCHANGE",
        ["std.task.atomicCompareAndSet"] = "LYR_ATOMIC_CAS",
        ["std.task.atomicFetchAndAdd"] = "LYR_ATOMIC_FETCH_ADD",
        // std.core's numbers: whether + - * leave the type, the bit counts and the
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
        // The Unicode Character Database's answers for a char (lyr/unicode.h).
        ["std.core.unicodeCategory"] = "lyr_unicode_category",
        ["std.core.unicodeUpper"] = "lyr_unicode_upper",
        ["std.core.unicodeLower"] = "lyr_unicode_lower",
        ["std.core.unicodeWhiteSpace"] = "lyr_unicode_white_space",
        // A view's length and bytes — a string's through the view it gives —,
        // the conversion of `as` toward a type parameter, a float's text read by C.
        ["std.core.byteCount"] = "LYR_VIEW_LEN",
        ["std.core.byteAt"] = "LYR_VIEW_BYTE",
        ["std.core.viewMatchesAt"] = "LYR_VIEW_MATCHES_AT",
        ["std.core.viewFind"] = "LYR_VIEW_FIND",
        ["std.core.viewWordAt"] = "LYR_VIEW_WORD",
        ["std.core.compareBytes"] = "lyr_str_cmp",
        ["std.core.byteSlice"] = "lyr_str_slice",
        ["std.core.stringOfBytes"] = "lyr_str_from_byte_array",
        ["std.core.asTypeOf"] = "LYR_CONVERT",
        // An object's address, the hash of an 'Identity'.
        ["std.core.identityOf"] = "LYR_IDENTITY",
        // Float: the bits of either width and IEEE's total order as a key.
        ["std.core.floatBits"] = "LYR_FLOAT_BITS",
        ["std.core.floatFromBits"] = "LYR_FLOAT_FROM_BITS",
        ["std.core.totalOrderKey"] = "LYR_TOTAL_ORDER_KEY",
        ["std.core.floatOfText"] = "lyr_str_to_float64",
        ["std.core.float32OfText"] = "lyr_str_to_float32",
        // The format language: a float with a precision, rounded by C.
        ["std.core.floatText"] = "lyr_str_float_text",
        // The StringBuilder's bulk copies (measurement point 3): memcpy.
        ["std.core.putBytes"] = "LYR_BYTES_PUT_VIEW",
        ["std.core.copyBytes"] = "lyr_bytes_copy",
        ["std.core.moveElements"] = "LYR_SLICE_MOVE",
        ["std.core.panicRange"] = "lyr_panic_range",
        // StringView: a view's bytes compared, copied and decoded at a byte, and
        // read as the 'Slice<uint8>' it is below the checker.
        ["std.core.viewsHoldTheSameBytes"] = "LYR_VIEW_EQ",
        ["std.core.compareViewBytes"] = "LYR_VIEW_CMP",
        ["std.core.viewToString"] = "LYR_VIEW_STR",
        ["std.core.charOfView"] = "LYR_VIEW_CHAR",
        ["std.core.viewBytes"] = "LYR_VIEW_BYTES",
        // Bytes to text: checked, copied, decoded with replacement.
        ["std.core.utf8Invalid"] = "LYR_UTF8_INVALID",
        // f-strings into one builder: a float's text, an integer's digits in place.
        ["std.core.textOfFloat"] = "lyr_str_from_float",
        ["std.core.textOfFloat32"] = "lyr_str_from_float32",
        ["std.core.putIntDigits"] = "lyr_bytes_put_int",
        ["std.core.putUintDigits"] = "lyr_bytes_put_uint",
        ["std.core.stringOfSlice"] = "LYR_STR_OF_SLICE",
        ["std.core.stringOfUtf8Lossy"] = "LYR_STR_UTF8_LOSSY",
    };

    /// <summary>The names the <see cref="SubsetGate"/> lets through as <c>CallImport</c>.</summary>
    public static IReadOnlySet<string> Names { get; } = Runtime.Keys.ToHashSet(StringComparer.Ordinal);

    /// <summary>A C function the program declares (<c>extern "C"</c>): its import is named
    /// <c>C:&lt;symbol&gt;</c>.</summary>
    public static bool IsForeign(string name) => name.StartsWith("C:", StringComparison.Ordinal);

    /// <summary>The C call for a native function with the given argument expressions: the runtime's
    /// for the standard library's, the symbol itself for a C function the program declares.</summary>
    public static string Call(string name, string[] arguments) =>
        $"{(IsForeign(name) ? name[2..] : Runtime[name])}({string.Join(", ", arguments)})";
}
