using System.Text.RegularExpressions;
using Lyric5.Toolchain;

namespace Lyric5.Tests;

/// <summary>
/// Panics and crashes (M1 S4; design/v5/spec/05 E8, 10 Q9, 01 S3): the programs under
/// <c>runtime/tests</c> that end badly on purpose, in the debug and the release profile. The panic
/// golden is exact — code, message, and the program's own frames with their lines, nothing of the
/// runtime above them and nothing of the C start-up below. Paths are reduced to file names: the
/// debug information holds absolute ones.
/// </summary>
public partial class PanicTests
{
    public static TheoryData<Profile> Profiles() => new() { Profile.Debug, Profile.Release };

    [GeneratedRegex(@"\((?:[^()\n]*[\\/])?([^\\/()\n:]+):(\d+)\)")]
    private static partial Regex FramePosition();

    private static string Normalize(string text) => FramePosition().Replace(text.Replace("\r\n", "\n"), "($1:$2)");

    private static string[] Lines(string text) => Normalize(text).Split('\n', StringSplitOptions.RemoveEmptyEntries);

    [Theory]
    [MemberData(nameof(Profiles))]
    public void A_panic_prints_code_message_and_the_programs_frames(Profile profile)
    {
        var result = RuntimeBuildTests.RunTest("panic_index", profile);
        Assert.True(result.ExitCode == 101, $"exit {result.ExitCode}\nstderr:\n{result.Stderr}");
        Assert.Equal("""
            panic [LYR-RT0003]: index 7 out of bounds for length 3
                at fail_at (panic_index.c:12)
                at middle (panic_index.c:17)
                at program (panic_index.c:21)

            """, Normalize(result.Stderr));
        Assert.Equal("", result.Stdout);
    }

    public static TheoryData<string, Profile, string> Checks()
    {
        var data = new TheoryData<string, Profile, string>();
        foreach (var profile in new[] { Profile.Debug, Profile.Release })
        {
            data.Add("add", profile, "panic [LYR-RT0002]: arithmetic overflow in '+'");
            data.Add("sub", profile, "panic [LYR-RT0002]: arithmetic overflow in '-'");
            data.Add("mul", profile, "panic [LYR-RT0002]: arithmetic overflow in '*'");
            data.Add("narrow", profile, "panic [LYR-RT0002]: arithmetic overflow in '+'");
            data.Add("under", profile, "panic [LYR-RT0002]: arithmetic overflow in '-'");
            data.Add("div0", profile, "panic [LYR-RT0001]: division by zero");
            data.Add("divmin", profile, "panic [LYR-RT0002]: arithmetic overflow in '/'");
            data.Add("rem0", profile, "panic [LYR-RT0001]: division by zero");
            data.Add("remmin", profile, "panic [LYR-RT0002]: arithmetic overflow in '%'");
            data.Add("unwrap", profile, "panic [LYR-RT0004]: unwrapped a null value");
            data.Add("index", profile, "panic [LYR-RT0003]: index -1 out of bounds for length 4");
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Checks))]
    public void A_failed_check_panics_with_its_code(string which, Profile profile, string firstLine)
    {
        var result = RuntimeBuildTests.RunTest("panic_checks", profile, args: [which]);
        Assert.True(result.ExitCode == 101, $"exit {result.ExitCode}\nstderr:\n{result.Stderr}");
        var lines = Lines(result.Stderr);
        Assert.Equal(firstLine, lines[0]);
        Assert.StartsWith("    at program (panic_checks.c:", lines[1]);
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void A_check_that_holds_computes_the_right_value(Profile profile)
    {
        var result = RuntimeBuildTests.RunTest("panic_checks", profile, args: ["ok"]);
        Assert.True(result.ExitCode == 0, $"exit {result.ExitCode}\nstderr:\n{result.Stderr}");
        Assert.Equal("checks ok\n", result.Stdout.Replace("\r\n", "\n"));
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void A_host_gets_the_report_through_its_writer_and_then_the_hook(Profile profile)
    {
        var result = RuntimeBuildTests.RunTest("panic_hook", profile);
        Assert.True(result.ExitCode == 101, $"exit {result.ExitCode}\nstderr:\n{result.Stderr}");
        Assert.Equal("", result.Stderr);
        Assert.Equal("""
            captured: panic [LYR-RT0007]: the host asked for it
            hook: LYR-RT0007 | the host asked for it | host_work in the trace

            """, result.Stdout.Replace("\r\n", "\n"));
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void A_panic_inside_the_hook_ends_the_process_at_once(Profile profile)
    {
        var result = RuntimeBuildTests.RunTest("panic_hook", profile, args: ["nested"]);
        Assert.True(result.ExitCode == 101, $"exit {result.ExitCode}\nstderr:\n{result.Stderr}");
        Assert.Equal("hook runs\n", result.Stdout.Replace("\r\n", "\n"));
        Assert.Equal("panic while panicking [LYR-RT0001]: division by zero", Lines(result.Stderr)[^1]);
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void A_stack_overflow_is_a_panic_with_a_trace(Profile profile)
    {
        var result = RuntimeBuildTests.RunTest("stack_overflow", profile);
        Assert.True(result.ExitCode == 101, $"exit {result.ExitCode}\nstderr:\n{result.Stderr}");
        var lines = Lines(result.Stderr);
        Assert.Equal("panic [LYR-RT0006]: stack overflow", lines[0]);
        Assert.True(lines[1].StartsWith("    at recurse (stack_overflow.c:"), $"stderr:\n{result.Stderr}");
        // The recursion shows its frame once, then a count — not 256 equal lines.
        Assert.True(lines.Length <= 6, $"stderr:\n{result.Stderr}");
        Assert.Matches(@"^    \.\.\. the frame above repeats \d+ more times$", lines[^2]);
        Assert.Equal("    ... deeper frames not shown", lines[^1]);
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void A_fault_is_a_crash_that_names_the_faulting_frame(Profile profile)
    {
        var result = RuntimeBuildTests.RunTest("crash", profile, args: ["segv"]);
        var lines = Lines(result.Stderr);
        Assert.True(lines.Length >= 2, $"exit {result.ExitCode}\nstderr:\n{result.Stderr}");
        if (OperatingSystem.IsWindows())
        {
            Assert.Equal(unchecked((int)0xC0000005), result.ExitCode);
            Assert.Equal("crash: access violation (invalid memory access at 0x0)", lines[0]);
        }
        else
        {
            Assert.Equal(128 + 11, result.ExitCode);  // killed by SIGSEGV
            Assert.Equal("crash: SIGSEGV (invalid memory access at 0x0)", lines[0]);
        }
        Assert.True(lines[1] == "    at fault_here (crash.c:17)", $"stderr:\n{result.Stderr}");
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void Without_handlers_a_fault_belongs_to_the_operating_system(Profile profile)
    {
        var result = RuntimeBuildTests.RunTest("crash", profile, args: ["plain"]);
        Assert.DoesNotContain("crash:", result.Stderr);
        Assert.Equal(OperatingSystem.IsWindows() ? unchecked((int)0xC0000005) : 128 + 11, result.ExitCode);
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void Allocation_beyond_the_heap_limit_panics(Profile profile)
    {
        var result = RuntimeBuildTests.RunTest("heap_limit", profile);
        Assert.True(result.ExitCode == 101, $"exit {result.ExitCode}\nstderr:\n{result.Stderr}");
        var lines = Lines(result.Stderr);
        Assert.StartsWith("panic [LYR-RT0005]: out of memory allocating 1016 bytes (heap ", lines[0]);
        Assert.All(lines.Skip(1), line => Assert.StartsWith("    ", line));
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void An_abort_is_a_crash_with_a_trace(Profile profile)
    {
        var result = RuntimeBuildTests.RunTest("crash", profile, args: ["abort"]);
        var lines = Lines(result.Stderr);
        Assert.True(lines.Length >= 2, $"exit {result.ExitCode}\nstderr:\n{result.Stderr}");
        Assert.NotEqual(0, result.ExitCode);
        Assert.NotEqual(101, result.ExitCode);
        if (!OperatingSystem.IsWindows()) Assert.Equal(128 + 6, result.ExitCode);  // killed by SIGABRT
        Assert.Equal(OperatingSystem.IsWindows() ? "crash: abort" : "crash: SIGABRT (abort)", lines[0]);
        Assert.Contains("    at abort_here (crash.c:22)", lines);
    }
}
