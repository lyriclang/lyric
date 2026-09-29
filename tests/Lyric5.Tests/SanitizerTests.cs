using System.Runtime.InteropServices;
using Lyric5.Toolchain;

namespace Lyric5.Tests;

/// <summary>
/// The runtime under clang's sanitizers (M1 S5; design/v5/spec/01 C7): ASan with UBSan over every
/// program that ends on its own or by a panic, TSan over the programs that run threads. They run
/// on Linux x86-64, the platform the plan names for them; there a missing clang fails the test
/// instead of passing it unexamined. Elsewhere the tests have nothing to do.
///
/// A finding fails the run twice over: the sanitizer's exit code (ASan and UBSan end the program,
/// TSan exits with 66) and its report on stderr, which the test looks for as well — a report on
/// a program that panics anyway would otherwise hide behind the expected 101. The crash programs
/// are not here: they test the fault handlers, which the sanitizers replace with their own.
/// </summary>
public class SanitizerTests
{
    private static bool Applies => OperatingSystem.IsLinux() && RuntimeInformation.OSArchitecture == Architecture.X64;

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
        { "panic_index", [], 101 },
        { "panic_hook", [], 101 },
        { "heap_limit", [], 101 },
    };

    public static TheoryData<string, string[], int> TsanPrograms() => new()
    {
        { "threads", [], 0 },
        { "weak", [], 0 },
        { "gc_smoke", [], 0 },
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
    /// The controls for the ASan profile: with the runtime's options and the collector linked in,
    /// a heap overflow and a signed overflow still end the program with their report.
    /// </summary>
    [Theory]
    [InlineData("overflow", "ERROR: AddressSanitizer: heap-buffer-overflow")]
    [InlineData("signed", "runtime error: signed integer overflow")]
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
