using System.Runtime.CompilerServices;
using Lyric.Core;
using Lyric.Embedding;
using Lyric.Vm;

namespace Lyric.Tests.Embedding;

/// <summary>
/// <c>std.io.stream</c> as a HOST sees it: the capability it demands, and what an
/// <see cref="ExecutionBudget"/> counts while a task waits on a file.
///
/// <para>Both were named as gaps by the first two sweep rounds and left untested, because a probe
/// program runs standalone — where every capability is granted and no budget is set. Only a host
/// fixture can ask these two questions.</para>
/// </summary>
public class StreamHostTests
{
    private static string RepoRoot([CallerFilePath] string thisFile = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));

    private static LangVm Vm(Capability granted) => new(new HostOptions
    {
        StdlibRoot = Path.Combine(RepoRoot(), "stdlib"),
        Capabilities = granted,
    });

    private const string ReadsAFile = """
        import std.io.stream as stream;
        import std.task { Wait, spawn, run };

        fn drain(path: string): Coroutine<Wait> {
            let f = stream.open(path)!;
            while (true) {
                let got = stream.readSome(f, 64)!;
                if (got.length == 0) {
                    break;
                }
            }
            stream.close(f);
        }

        pub fn go(path: string): void {
            spawn(drain(path));
            run();
        }
        """;

    /// <summary>
    /// The module demands <c>fileAccess</c> AND <c>osAccess</c>, and a host granting only the
    /// first is refused before anything runs.
    ///
    /// <para>That second bit is not decoration: waiting is <c>std.task</c>, and a module requires
    /// the union of what it imports. It is the reason the handle is not part of
    /// <c>std.io.file</c> — a script that only reads a config file must not have to pay for a
    /// scheduler it never starts.</para>
    /// </summary>
    [Fact]
    public void The_handle_needs_the_scheduler_bit_as_well_as_the_disk_bit()
    {
        var starved = Vm(Capability.FileAccess);
        var refused = Assert.ThrowsAny<Exception>(() =>
        {
            // Whichever stage refuses it — compile, instantiate or the bind behind them — the
            // script must not reach the natives with the scheduler bit missing.
            var compiled = starved.Compile(ReadsAFile, "mod");
            var running = starved.Instantiate(compiled);
            running.CallVoid("go", "nothing.txt");
        });

        Assert.Contains("osAccess", refused.ToString(), StringComparison.Ordinal);

        // Both bits together compile — the refusal above is about the capability, not the source.
        var vm = Vm(Capability.FileAccess | Capability.OsAccess);
        var module = vm.Compile(ReadsAFile, "mod");
        Assert.NotNull(module);
    }

    /// <summary>
    /// A budget reaches inside a task that waits on a file: the work a resumed chain does is the
    /// work of the call that resumed it, and a suspension neither resets nor forks the count.
    ///
    /// <para>The 4.0 contract said so for chains in general; this pins it for a chain that
    /// SUSPENDS on host I/O rather than on a plain yield, which is the shape 4.2 added. A budget
    /// too small to finish the drain has to stop it — otherwise a script could wait its way out
    /// of the half of the sandbox that stops one which is merely not stopping.</para>
    /// </summary>
    [Fact]
    public void A_budget_counts_the_work_a_task_does_while_it_waits_on_a_file()
    {
        var path = Path.Combine(Path.GetTempPath(),
                $"lyric-budget-stream-{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(path, new byte[64 * 1024]);
        try
        {
            var vm = Vm(Capability.FileAccess | Capability.OsAccess);
            var instance = vm.Instantiate(vm.Compile(ReadsAFile, "mod"));
            var budget = new ExecutionBudget(20_000);

            var stopped = Assert.Throws<ScriptBudgetException>(
                () => instance.CallVoid("go", budget, path));

            Assert.Equal("LYR-CAP0002", stopped.Code);
            Assert.Equal(0, budget.Remaining);
        }
        finally
        {
            // Best effort: the stopped script never reached its `close`, and the handle it
            // opened outlives it — see STATUS §Still open. Deleting would throw here.
            try { File.Delete(path); } catch (IOException) { }
        }
    }

    /// <summary>The same drain UNDER a budget large enough finishes, so the test above is about
    /// the budget rather than about the read being impossible from a host.</summary>
    [Fact]
    public void The_same_drain_finishes_when_the_budget_allows_it()
    {
        var path = Path.Combine(Path.GetTempPath(),
                $"lyric-budget-stream-ok-{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(path, new byte[1024]);
        try
        {
            var vm = Vm(Capability.FileAccess | Capability.OsAccess);
            var instance = vm.Instantiate(vm.Compile(ReadsAFile, "mod"));
            var budget = new ExecutionBudget(50_000_000);

            instance.CallVoid("go", budget, path);

            Assert.True(budget.Remaining > 0, "the drain should not have spent the whole budget");
        }
        finally
        {
            File.Delete(path);
        }
    }
}
