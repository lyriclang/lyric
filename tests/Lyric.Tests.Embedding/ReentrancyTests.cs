using System.Runtime.CompilerServices;
using Lyric.Embedding;

namespace Lyric.Tests.Embedding;

/// <summary>
/// Script to host to script: a host function that calls back into the instance that called it.
///
/// <para>THE LIMIT USED TO COUNT THE WRONG THING. <c>MaxCallDepth</c> counted the frames of ONE
/// run, and every re-entry starts that stack at zero — so the count never reached 1024 while the
/// CLR stack kept growing, and the process died of a stack overflow with no panic, no backtrace
/// and no code. Pure Lyric recursion was caught the whole time, which is exactly why it went
/// unnoticed: the obvious test passed.</para>
///
/// <para>Every test here carries its control. A limit that refuses everything would satisfy the
/// first test on its own.</para>
/// </summary>
public class ReentrancyTests
{
    private static string RepoRoot([CallerFilePath] string thisFile = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));

    private static LangVm Vm() => new(new HostOptions
    {
        StdlibRoot = Path.Combine(RepoRoot(), "stdlib"),
    });

    /// <summary>The control: recursion that never leaves the interpreter was always caught.</summary>
    [Fact]
    public void Runaway_recursion_inside_the_script_is_a_panic()
    {
        var vm = Vm();
        var instance = vm.Instantiate(vm.Compile("""
            pub fn los(n: int): int { return los(n) + 1; }
            """, "mod"));

        var ex = Assert.ThrowsAny<Exception>(() => instance.Call<long>("los", 1));
        Assert.Contains("call depth", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The finding: unbounded re-entry ends as a panic rather than as a dead process.
    ///
    /// <para>Without the nesting limit this test does not fail — it takes the test host down with
    /// it, which is why it is worth having.</para>
    /// </summary>
    [Fact]
    public void Runaway_reentry_through_the_host_is_a_panic_and_not_a_dead_process()
    {
        ScriptInstance? instance = null;
        var rounds = 0;

        var vm = Vm();
        vm.RegisterFunction("wieder", (long n) =>
        {
            rounds++;
            return instance!.Call<long>("los", n);
        });

        instance = vm.Instantiate(vm.Compile("""
            import host { wieder };
            pub fn los(n: int): int { return wieder(n) + 1; }
            """, "mod"));

        var ex = Assert.ThrowsAny<Exception>(() => instance.Call<long>("los", 1));
        Assert.True(ex.Message.Contains("re-entry nested", StringComparison.Ordinal),
            $"after {rounds} rounds got {ex.GetType().Name}: {ex.Message}");
    }

    /// <summary>
    /// The control for the other direction: re-entry that ENDS still works, and works repeatedly.
    ///
    /// <para>The tally the limit reads is raised by a run and lowered again when it leaves. Miss
    /// the lowering and this test goes red on the second call while the first one passes — which
    /// is the failure a limit-only test cannot see.</para>
    /// </summary>
    [Fact]
    public void Bounded_reentry_works_and_leaves_nothing_behind()
    {
        ScriptInstance? instance = null;

        var vm = Vm();
        vm.RegisterFunction("runter", (long n) =>
            n <= 0 ? 0 : instance!.Call<long>("los", n - 1));

        instance = vm.Instantiate(vm.Compile("""
            import host { runter };
            pub fn los(n: int): int { return runter(n) + 1; }
            """, "mod"));

        Assert.Equal(6, instance.Call<long>("los", 5));
        Assert.Equal(6, instance.Call<long>("los", 5));
        Assert.Equal(1, instance.Call<long>("los", 0));
    }

    /// <summary>
    /// A deep run inside the script leaves the frame tally where it found it.
    ///
    /// <para>The tally is thread-static, so a run that forgot to put it back would make every
    /// later call on that thread start closer to the limit — a program that works once and fails
    /// afterwards, which is the worst shape a limit can have.</para>
    /// </summary>
    [Fact]
    public void A_deep_run_does_not_poison_the_next_one()
    {
        var vm = Vm();
        var instance = vm.Instantiate(vm.Compile("""
            pub fn tief(n: int): int { if (n <= 0) { return 0; } return tief(n - 1) + 1; }
            pub fn flach(): int { return 42; }
            """, "mod"));

        Assert.Equal(500, instance.Call<long>("tief", 500));
        Assert.Equal(42, instance.Call<long>("flach"));
        Assert.Equal(500, instance.Call<long>("tief", 500));
    }

    /// <summary>
    /// A run that PANICKED leaves the tally where it found it too.
    ///
    /// <para>The restore lives in a <c>finally</c> for this case alone: a native that throws
    /// leaves the tally raised on its way out, and only unwinding through the run that raised it
    /// puts it back.</para>
    /// </summary>
    [Fact]
    public void A_run_that_panicked_does_not_poison_the_next_one()
    {
        var vm = Vm();
        var instance = vm.Instantiate(vm.Compile("""
            pub fn los(n: int): int { return los(n) + 1; }
            pub fn flach(): int { return 42; }
            """, "mod"));

        Assert.ThrowsAny<Exception>(() => instance.Call<long>("los", 1));
        Assert.Equal(42, instance.Call<long>("flach"));
        Assert.ThrowsAny<Exception>(() => instance.Call<long>("los", 1));
        Assert.Equal(42, instance.Call<long>("flach"));
    }
}
