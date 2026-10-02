using System.Runtime.InteropServices;
using Lyric5.Toolchain;

namespace Lyric5.Tests;

/// <summary>
/// The runtime under clang's sanitizers (M1 S5; design/v5/spec/01 C7): ASan with UBSan over every
/// program that ends on its own or by a panic, TSan over the programs that run threads. They run
/// on Linux x86-64, the platform the plan names for them, in the run that asks for them with
/// <c>LYRIC5_SANITIZERS=1</c> — CI's C toolchain job, which also prepares the kernel for TSan.
/// There a missing clang fails the test instead of passing it unexamined; everywhere else the
/// tests have nothing to do (the whole-suite jobs would only repeat them, without that preparation).
///
/// A finding fails the run twice over: the sanitizer's exit code (ASan and UBSan end the program,
/// TSan exits with 66) and its report on stderr, which the test looks for as well — a report on
/// a program that panics anyway would otherwise hide behind the expected 101. The crash programs
/// are not here: they test the fault handlers, which the sanitizers replace with their own.
/// </summary>
public class SanitizerTests
{
    private static string Asked => Environment.GetEnvironmentVariable("LYRIC5_SANITIZERS") ?? "";

    private static bool Applies =>
        Asked is "1" or "all" && OperatingSystem.IsLinux() && RuntimeInformation.OSArchitecture == Architecture.X64;

    private static CCompiler Clang() =>
        CCompiler.Locate(CCompilerKind.Clang) ?? throw new InvalidOperationException("the sanitizer profiles need clang on PATH");

    private static readonly string[] Reports =
        ["ERROR: AddressSanitizer", "runtime error:", "WARNING: ThreadSanitizer", "ERROR: ThreadSanitizer"];

    public static TheoryData<string, string[], int> AsanPrograms() => new()
    {
        { "gc_smoke", [], 0 },
        { "alloc", [], 0 },
        { "strings", [], 0 },
        { "arrays", [], 0 },
        { "config", [], 0 },
        { "hello", ["one", "two"], 7 },
        { "roots", [], 0 },
        { "weak", [], 0 },
        { "threads", [], 0 },
        { "panic_checks", ["ok"], 0 },
        { "panic_checks", ["divmin"], 101 },
        // The number tower's macros compute in unsigned arithmetic exactly so that UBSan has
        // nothing to say about a wrap, a shift into the sign bit or a saturated conversion.
        { "numeric", ["ok"], 0 },
        { "numeric", ["shlneg"], 101 },
        { "panic_index", [], 101 },
        { "panic_hook", [], 101 },
        { "heap_limit", [], 101 },
        // Coroutines (M6): ASan follows every switch between stacks, and a stack released while
        // frames on it never returned is unpoisoned before its next use.
        { "coro_basic", [], 0 },
        { "coro_basic", ["self"], 101 },
        { "coro_gc", [], 0 },
        { "coro_pace", [], 0 },
        { "coro_park", [], 0 },
        { "coro_threads", [], 0 },
        { "poll_basic", [], 0 },
        { "task_main", [], 7 },
        { "coro_panic", [], 0 },
        { "coro_panic", ["main"], 101 },
        { "coro_storm", [], 0 },
    };

    public static TheoryData<string, string[], int> TsanPrograms() => new()
    {
        { "weak", [], 0 },
        { "gc_smoke", [], 0 },
        // A coroutine is a fiber to TSan; the collections between the switches order them.
        { "coro_basic", [], 0 },
        { "coro_park", [], 0 },
        { "task_main", [], 7 },
        { "coro_panic", [], 0 },
        { "coro_gc", [], 0 },
    };

    [Theory]
    [MemberData(nameof(AsanPrograms))]
    public void A_program_runs_clean_under_ASan_and_UBSan(string name, string[] args, int exit) =>
        RunClean(name, Profile.Asan, args, exit);

    [Theory]
    [MemberData(nameof(TsanPrograms))]
    public void A_threaded_program_runs_clean_under_TSan(string name, string[] args, int exit) =>
        RunClean(name, Profile.Tsan, args, exit);

    /// <summary>
    /// The throw paths under ASan and UBSan (13, M5): the emitter's C for every program that throws,
    /// catches, runs a defer or a close on the error path, suppresses, rethrows, or lets an error
    /// leave main — and UBSan's check of '__builtin_unreachable()' holds the blocks the lowering
    /// sealed as unreachable to never being reached. An error escaping main exits 1, a 'try!' on
    /// an error 101. Each under a name of its own, so the plain run of the same program in
    /// <see cref="CEmitterTests"/> never shares its sources.
    /// </summary>
    [Theory]
    [InlineData("errors", 0)]
    [InlineData("error_paths", 0)]
    [InlineData("try_forms", 0)]
    [InlineData("catch_sets", 0)]
    [InlineData("defer_errors", 0)]
    [InlineData("resources", 0)]
    [InlineData("fn_throws", 0)]
    [InlineData("bank", 0)]
    [InlineData("uncaught", 1)]
    [InlineData("uncaught_suppressed", 1)]
    [InlineData("rethrow", 1)]
    [InlineData("deep_error", 1)]
    [InlineData("forced", 101)]
    public void A_program_that_throws_runs_clean_under_ASan_and_UBSan(string name, int exit) => RunEmittedClean(name, exit);

    /// <summary>
    /// Generators under ASan and UBSan (M6 S2a): every pull switches stacks, a yield hands the
    /// puller an address in the suspended frame, a result and an error cross from the coroutine's
    /// stack to the puller's, and the dropped coroutines go with their stacks.
    /// </summary>
    [Theory]
    [InlineData("generators", 0)]
    [InlineData("generators_close", 0)]
    [InlineData("generator_lambdas", 0)]
    [InlineData("dynamic_yields", 0)]
    [InlineData("tasks", 0)]
    [InlineData("spawn", 0)]
    [InlineData("cancel", 0)]
    [InlineData("scopes", 0)]
    [InlineData("timeout", 0)]
    [InlineData("channels", 0)]
    [InlineData("select", 0)]
    [InlineData("task_status", 101)]
    [InlineData("scope_panic", 101)]
    [InlineData("detached_panic", 101)]
    [InlineData("task_panic", 101)]
    [InlineData("yield_in_task", 101)]
    [InlineData("yield_mismatch", 101)]
    [InlineData("close_yield", 101)]
    public void A_coroutine_program_runs_clean_under_ASan_and_UBSan(string name, int exit) => RunEmittedClean(name, exit);

    private static void RunEmittedClean(string name, int exit)
    {
        if (!Applies) return;
        var result = RuntimeBuildTests.RunEmitted(CEmitterTests.EmitC(name), name + "-asan", Profile.Asan, compiler: Clang());
        Assert.True(result.ExitCode == exit, $"exit {result.ExitCode}, expected {exit}\nstderr:\n{result.Stderr}");
        foreach (var report in Reports) Assert.DoesNotContain(report, result.Stderr);
    }

    /// <summary>
    /// The threads program under TSan, on request only (<c>LYRIC5_SANITIZERS=all</c>): on GitHub's
    /// Ubuntu runner (clang 18) the collector's stop-the-world signals now and then reach a thread
    /// only after its retry limit and it aborts ("Signals delivery fails constantly") — in 3 of 5
    /// runs, with and without the kernel preparation. Locally (clang 22) 96 stressed runs passed.
    /// The mechanism is not found; it goes with the signal-based stop of stage 1 (M11 stops at
    /// safepoints). Until then this run is local: STATUS keeps the thread open.
    /// </summary>
    [Fact]
    public void The_threads_program_runs_clean_under_TSan_where_asked_for()
    {
        if (Asked != "all") return;
        RunClean("threads", Profile.Tsan, [], 0);
        RunClean("coro_threads", Profile.Tsan, [], 0);
        RunClean("coro_storm", Profile.Tsan, [], 0);
        RunClean("poll_basic", Profile.Tsan, [], 0);
    }

    /// <summary>
    /// The controls for the ASan profile: with the runtime's options and the collector linked in,
    /// a heap overflow, a signed overflow and a reached '__builtin_unreachable()' still end the
    /// program with their report — the last is what the throw-path run relies on.
    /// </summary>
    [Theory]
    [InlineData("overflow", "ERROR: AddressSanitizer: heap-buffer-overflow")]
    [InlineData("signed", "runtime error: signed integer overflow")]
    [InlineData("unreachable", "runtime error: execution reached an unreachable program point")]
    public void A_fault_under_ASan_is_reported_and_ends_the_program(string which, string report)
    {
        if (!Applies) return;
        var result = RuntimeBuildTests.RunTest("asan_faults", Profile.Asan, Clang(), [which]);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(report, result.Stderr);
        Assert.DoesNotContain("not reached", result.Stdout);
    }

    /// <summary>
    /// The control for the model the runtime gives TSan (a collection orders the threads): two
    /// threads that allocate and then race on a global, with no collection between, are reported.
    /// </summary>
    [Fact]
    public void A_race_the_collector_does_not_order_is_still_reported()
    {
        if (!Applies) return;
        var result = RuntimeBuildTests.RunTest("tsan_race", Profile.Tsan, Clang());
        Assert.Equal(66, result.ExitCode);
        Assert.Contains("WARNING: ThreadSanitizer: data race", result.Stderr);
        Assert.Contains("tsan_race.c", result.Stderr);
    }

    private static void RunClean(string name, Profile profile, string[] args, int exit)
    {
        if (!Applies) return;
        var result = RuntimeBuildTests.RunTest(name, profile, Clang(), args);
        Assert.True(result.ExitCode == exit, $"exit {result.ExitCode}, expected {exit}\nstderr:\n{result.Stderr}");
        foreach (var report in Reports) Assert.DoesNotContain(report, result.Stderr);
    }
}
