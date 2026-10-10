namespace Lyric5.Compiler;

/// <summary>
/// The natively backed functions of <c>stdlib5/</c> and the runtime call each becomes (M2 S3).
/// A provisional table with a date: M8a wrote <c>std</c> in Lyric, source-first, and what the
/// runtime keeps in C (01 L9) is reached through <c>extern "C"</c> from M14 on (M8b P1).
/// </summary>
public static class Intrinsics
{
    private static readonly IReadOnlyDictionary<string, string> Runtime = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // The console's buffers (M8b S8a, lyr/console.h): std.io's print family puts, and flushes
        // where a flush is due — on the I/O pool where a scheduler runs.
        ["std.io.consolePut"] = "LYR_CONSOLE_PUT",
        ["std.io.consoleFlush"] = "lyr_console_flush",
        ["std.io.consoleRead"] = "LYR_CONSOLE_READ",
        ["std.io.consoleGone"] = "lyr_console_gone",
        ["std.string.concat"] = "lyr_str_concat",
        ["std.string.fromInt"] = "lyr_str_from_int",
        ["std.string.fromUint"] = "lyr_str_from_uint",
        ["std.string.fromBool"] = "lyr_str_from_bool",
        ["std.string.fromFloat"] = "lyr_str_from_float",
        ["std.string.fromFloat32"] = "lyr_str_from_float32",
        ["std.core.shortestFloat32"] = "lyr_str_from_float32",
        ["std.string.fromChar"] = "lyr_str_from_char",
        ["std.core.panic"] = "lyr_panic_message",
        // The catalogue (05 E8): 'assert' a check at the call, so its trace starts in the program;
        // the two that never return are panics of their own codes.
        ["std.core.assert"] = "LYR_ASSERT",
        ["std.core.unreachable"] = "lyr_panic_unreachable",
        ["std.core.todo"] = "lyr_panic_todo",
        // A collection changed while it was walked (10 I9): a panic of its own code.
        ["std.collections.changedWhileWalked"] = "lyr_panic_walked",
        // std.task's side of the scheduler (06 N6 S1; 13 §1.6-1.7): a task's context around a
        // function value, which the macro passes as its code and environment; the thread's
        // scheduler; the park; the poller; the monotonic clock.
        ["std.task.startContext"] = "LYR_TASK_START",
        ["std.task.park"] = "lyr_coro_park",
        ["std.task.deadlocked"] = "lyr_panic_deadlock",
        ["std.task.poisonedBy"] = "lyr_panic_poisoned",
        ["std.task.currentScheduler"] = "lyr_task_scheduler",
        ["std.task.setCurrentScheduler"] = "lyr_task_set_scheduler",
        // The question whether one runs, apart from currentScheduler: asking makes no task of
        // main (06 M6-26, M8b P3; Reachability.CallsImport's `unless`).
        ["std.task.schedulerRuns"] = "LYR_TASK_SCHEDULER_RUNS",
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
        // The processors, for the program's pool (std.thread's parallelMap, M8b S1).
        ["std.thread.processorCount"] = "lyr_os_cpu_count",
        // The file system's calls (M8b P1, lyr/fs.h): one system call each, run on std.fs's I/O
        // pool; a view crosses as its pointer and length.
        ["std.fs.fsOpen"] = "lyr_fs_open",
        ["std.fs.fsRead"] = "LYR_FS_READ",
        ["std.fs.fsWrite"] = "LYR_FS_WRITE",
        ["std.fs.fsSeek"] = "lyr_fs_seek",
        ["std.fs.fsClose"] = "lyr_fs_close",
        ["std.fs.fsStat"] = "LYR_FS_STAT",
        ["std.fs.fsMakeDir"] = "lyr_fs_mkdir",
        ["std.fs.fsRemoveDir"] = "lyr_fs_rmdir",
        ["std.fs.fsRemove"] = "lyr_fs_unlink",
        ["std.fs.fsRename"] = "lyr_fs_rename",
        ["std.fs.fsDirOpen"] = "lyr_fs_dir_open",
        ["std.fs.fsDirNext"] = "LYR_FS_DIR_NEXT",
        ["std.fs.fsDirClose"] = "lyr_fs_dir_close",
        ["std.fs.fsCanonical"] = "LYR_FS_CANONICAL",
        ["std.fs.fsAbsolute"] = "LYR_FS_ABSOLUTE",
        ["std.task.currentPoller"] = "lyr_task_poller",
        ["std.task.wakePoller"] = "lyr_task_wake",
        // A descriptor's readiness on the thread's poller (M8b S10b, lyr/task.h): std.net's waits.
        ["std.task.armOnPoller"] = "lyr_task_arm",
        ["std.task.forgetOnPoller"] = "lyr_task_forget",
        ["std.task.takeFromPoller"] = "LYR_TASK_TAKE",
        // The network (M8b S10b, lyr/net.h): std.net's sockets, non-blocking, waited for on the
        // poller.
        ["std.net.netSocket"] = "lyr_net_socket",
        ["std.net.netBind"] = "LYR_NET_BIND",
        ["std.net.netListen"] = "lyr_net_listen",
        ["std.net.netAccept"] = "lyr_net_accept",
        ["std.net.netConnect"] = "LYR_NET_CONNECT",
        ["std.net.netConnected"] = "lyr_net_connected",
        ["std.net.netRecv"] = "LYR_NET_RECV",
        ["std.net.netSend"] = "LYR_NET_SEND",
        ["std.net.netShutdown"] = "lyr_net_shutdown",
        ["std.net.netSetNoDelay"] = "lyr_net_set_nodelay",
        ["std.net.netName"] = "LYR_NET_NAME",
        ["std.net.netClose"] = "lyr_net_close",
        ["std.net.netSendTo"] = "LYR_NET_SENDTO",
        ["std.net.netRecvFrom"] = "LYR_NET_RECVFROM",
        ["std.net.netResolve"] = "LYR_NET_RESOLVE",
        // Processes (M8b S12, lyr/process.h): a child started, reaped, signalled; its pipes.
        ["std.process.processSpawn"] = "LYR_PROCESS_SPAWN",
        ["std.process.processAttach"] = "lyr_process_attach",
        ["std.process.processNudge"] = "lyr_process_nudge",
        ["std.process.processReap"] = "lyr_process_reap",
        ["std.process.processSignal"] = "lyr_process_signal",
        ["std.process.processRead"] = "LYR_PROCESS_READ",
        ["std.process.processWrite"] = "LYR_PROCESS_WRITE",
        ["std.process.processClose"] = "lyr_process_close",
        ["std.process.processPipesBlock"] = "lyr_process_pipes_block",
        ["std.task.spin"] = "lyr_task_spin",
        // Signals as a channel (10 Q9, 06 K5): the abstract names and the system's numbers, the
        // handler on or off, the watcher's poller, the caught ones.
        ["std.os.signalNumber"] = "lyr_signal_number",
        ["std.os.signalKind"] = "lyr_signal_kind",
        ["std.os.catchSignal"] = "lyr_signal_catch",
        ["std.os.attachSignals"] = "lyr_signal_attach",
        ["std.os.takeSignals"] = "lyr_signal_take",
        // The system (10 Q9, M8b S9; lyr/os.h): a text through the caller's buffer.
        ["std.os.osPlatform"] = "lyr_os_platform",
        ["std.os.osArch"] = "lyr_os_arch",
        ["std.os.osPid"] = "lyr_os_pid",
        ["std.os.osArgCount"] = "lyr_os_arg_count",
        ["std.os.osArg"] = "LYR_OS_ARG",
        ["std.os.osEnv"] = "LYR_OS_ENV",
        ["std.os.osEnvs"] = "LYR_OS_ENVS",
        ["std.os.osSetEnv"] = "lyr_os_set_env",
        ["std.os.osCwd"] = "LYR_OS_CWD",
        ["std.os.osSetCwd"] = "lyr_os_set_cwd",
        ["std.os.osHostname"] = "LYR_OS_HOSTNAME",
        ["std.os.osExit"] = "lyr_os_exit",
        ["std.os.osTempRoot"] = "LYR_FS_TEMP_ROOT",
        // The atomic operations (06 G4, K6; N7 P2; the review's M7-5): the C11 builtins on a
        // place — a field of the scheduler's, the value of std.sync's Atomic<T> —, as macros that
        // serve every T.
        ["std.task.atomicLoad"] = "LYR_ATOMIC_LOAD",
        ["std.task.atomicStore"] = "LYR_ATOMIC_STORE",
        ["std.task.atomicExchange"] = "LYR_ATOMIC_EXCHANGE",
        ["std.task.atomicCompareAndSet"] = "LYR_ATOMIC_CAS",
        ["std.task.atomicFetchAndAdd"] = "LYR_ATOMIC_FETCH_ADD",
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
        // The Unicode Character Database's answers for a char (10 B9 S4, C2; M8c S1, lyr/unicode.h).
        ["std.core.unicodeCategory"] = "lyr_unicode_category",
        ["std.core.unicodeUpper"] = "lyr_unicode_upper",
        ["std.core.unicodeLower"] = "lyr_unicode_lower",
        ["std.core.unicodeWhiteSpace"] = "lyr_unicode_white_space",
        // A view's length and bytes (10 S1; M8a S12) — a string's through the view it gives —,
        // the conversion of `as` toward a type parameter, a float's text read by C.
        ["std.core.byteCount"] = "LYR_VIEW_LEN",
        ["std.core.byteAt"] = "LYR_VIEW_BYTE",
        ["std.core.viewMatchesAt"] = "LYR_VIEW_MATCHES_AT",
        ["std.core.compareBytes"] = "lyr_str_cmp",
        ["std.core.byteSlice"] = "lyr_str_slice",
        ["std.core.stringOfBytes"] = "lyr_str_from_byte_array",
        ["std.core.asTypeOf"] = "LYR_CONVERT",
        // An object's address, the hash of an 'Identity' (N2b).
        ["std.core.identityOf"] = "LYR_IDENTITY",
        // Float (10 B5): the bits of either width and IEEE's total order as a key.
        ["std.core.floatBits"] = "LYR_FLOAT_BITS",
        ["std.core.floatFromBits"] = "LYR_FLOAT_FROM_BITS",
        ["std.core.totalOrderKey"] = "LYR_TOTAL_ORDER_KEY",
        ["std.core.floatOfText"] = "lyr_str_to_float64",
        ["std.core.float32OfText"] = "lyr_str_to_float32",
        // The format language (08 Y7, 12 §2): a float with a precision, rounded by C.
        ["std.core.floatText"] = "lyr_str_float_text",
        // The StringBuilder's bulk copies (measurement point 3): memcpy.
        ["std.core.putBytes"] = "LYR_BYTES_PUT_VIEW",
        ["std.core.copyBytes"] = "lyr_bytes_copy",
        ["std.core.moveElements"] = "LYR_SLICE_MOVE",
        ["std.core.panicRange"] = "lyr_panic_range",
        // StringView (10 S1, M8a S12): a view's bytes compared, copied and decoded at a byte, and
        // read as the 'Slice<uint8>' it is below the checker.
        ["std.core.viewsHoldTheSameBytes"] = "LYR_VIEW_EQ",
        ["std.core.compareViewBytes"] = "LYR_VIEW_CMP",
        ["std.core.viewToString"] = "LYR_VIEW_STR",
        ["std.core.charOfView"] = "LYR_VIEW_CHAR",
        ["std.core.viewBytes"] = "LYR_VIEW_BYTES",
        // Bytes to text (10 S3; M8a S12): checked, copied, decoded with replacement.
        ["std.core.utf8Invalid"] = "LYR_UTF8_INVALID",
        // f-strings into one builder (10 S6; M8a S13): a float's text, an integer's digits in place.
        ["std.core.textOfFloat"] = "lyr_str_from_float",
        ["std.core.textOfFloat32"] = "lyr_str_from_float32",
        ["std.core.putIntDigits"] = "lyr_bytes_put_int",
        ["std.core.putUintDigits"] = "lyr_bytes_put_uint",
        ["std.core.stringOfSlice"] = "LYR_STR_OF_SLICE",
        ["std.core.stringOfUtf8Lossy"] = "LYR_STR_UTF8_LOSSY",
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
