using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// The scheduler without the class <c>Atomic</c> (design/v5/spec/06, the review's M7-5): its
/// words that other threads read and write — a context's state and its cancellation, a spin
/// lock, a pool's counters, the channels' ids — are fields of its own, read and written through
/// the library's atomic operations, which take a PLACE. They were objects of
/// <c>std.sync</c>'s <c>Atomic&lt;T&gt;</c>: two allocations for every task, and the import that
/// kept <c>std.sync</c> below <c>std.task</c>, where the locks cannot move to it.
///
/// <para><c>Atomic&lt;T&gt;</c> itself is built on the same operations (10 Q10: one path for a
/// type), so <c>std.sync</c> imports <c>std.task</c> and not the other way.</para>
///
/// <para>What the tests can see is the program's IR: which types it holds and which of the
/// runtime's operations it calls. That the fields are read and written atomically everywhere is
/// TSan's to say (<c>SanitizerTests</c>, <c>LYRIC5_SANITIZERS=all</c>).</para>
/// </summary>
[Collection("console")]
public class SchedulerAtomicsTests
{
    private static string Ir(string main)
    {
        var dir = Package(("main.lyr", main));
        var (exit, ir, error) = Run("build", Path.Combine(dir, "main.lyr"), "--emit", "ir");
        Assert.True(exit == 0, error);
        return ir;
    }

    /// <summary>The lines of an IR that say "atomic", for a failure's message.</summary>
    private static string Atomics(string ir) =>
        string.Join("\n", ir.Split('\n').Where(line => line.Contains("tomic")).Distinct().Take(12));

    [Fact]
    public void A_program_that_spawns_holds_no_atomic_object()
    {
        var ir = Ir("""
            import std.io { println };
            import std.task { spawn };

            fn main(): void {
                let t = spawn(() => 41 + 1);
                println(f"{try t.await()}");
            }
            """);
        Assert.False(ir.Contains("Atomic<"), Atomics(ir));
        Assert.False(ir.Contains("std.sync."), Atomics(ir));
        // a waker claims a context by one exchange on the context's own field
        Assert.True(ir.Contains("std.task.atomicCompareAndSet"), Atomics(ir));
    }

    [Fact]
    public void An_atomic_of_the_library_is_built_on_the_same_operations()
    {
        var ir = Ir("""
            import std.io { println };
            import std.sync { Atomic };

            fn main(): void {
                let n = Atomic<int>.new(40);
                let flag = Atomic<bool>.new(false);
                let before = n.fetchAndAdd(1);
                let swapped = n.compareAndSet(41, 7);
                let old = n.exchange(9);
                flag.store(true);
                println(f"{before} {swapped} {old} {n.load()} {flag.load()}");
            }
            """);
        Assert.True(ir.Contains("std.task.atomicFetchAndAdd"), Atomics(ir));
        Assert.True(ir.Contains("std.task.atomicCompareAndSet"), Atomics(ir));
        Assert.True(ir.Contains("std.task.atomicExchange"), Atomics(ir));
        Assert.True(ir.Contains("std.task.atomicStore"), Atomics(ir));
        Assert.True(ir.Contains("std.task.atomicLoad"), Atomics(ir));
        Assert.False(ir.Contains("std.sync.atomic"), Atomics(ir));
    }

    /// <summary>And it still does what an atomic does: the values of every operation, for both
    /// types the library allows.</summary>
    [Fact]
    public void An_atomic_of_the_library_gives_what_it_gave()
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", """
            import std.io { println };
            import std.sync { Atomic };

            fn main(): void {
                let n = Atomic<int>.new(40);
                let flag = Atomic<bool>.new(false);
                let before = n.fetchAndAdd(1);
                let after = n.addAndFetch(1);
                let swapped = n.compareAndSet(42, 7);
                let refused = n.compareAndSet(42, 8);
                let old = n.exchange(9);
                let was = flag.exchange(true);
                println(f"{before} {after} {swapped} {refused} {old} {n.load()} {was} {flag.load()}");
            }
            """));
        Assert.Equal("40 42 true false 7 9 false true\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }
}
