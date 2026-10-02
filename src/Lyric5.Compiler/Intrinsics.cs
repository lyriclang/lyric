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
        // std.sync's atomics (06 G4, K6; N7 P2): the C11 builtins on an Atomic's field, as macros
        // that serve every T.
        ["std.sync.atomicLoad"] = "LYR_ATOMIC_LOAD",
        ["std.sync.atomicStore"] = "LYR_ATOMIC_STORE",
        ["std.sync.atomicExchange"] = "LYR_ATOMIC_EXCHANGE",
        ["std.sync.atomicCompareAndSet"] = "LYR_ATOMIC_CAS",
        ["std.sync.atomicFetchAndAdd"] = "LYR_ATOMIC_FETCH_ADD",
    };

    /// <summary>The names the <see cref="SubsetGate"/> lets through as <c>CallImport</c>.</summary>
    public static IReadOnlySet<string> Names { get; } = Runtime.Keys.ToHashSet(StringComparer.Ordinal);

    /// <summary>The C call for a native function with the given argument expressions.</summary>
    public static string Call(string name, string[] arguments) =>
        $"{Runtime[name]}({string.Join(", ", arguments)})";
}
