using System.Diagnostics;
using Lyric5.Toolchain;
using Profile = Lyric5.Toolchain.Profile;

namespace Lyric5.Tests;

/// <summary>
/// Measurement point 1 (design/v5/spec/13, M2; 01 L11; 11 C9) as ratchets: a hello-world binary
/// under 2 MB that starts in under 5 ms, and a warm <c>lyric5 run</c> that costs at most 50 ms on
/// top of the program's own start. Times are the minimum of several runs — the number a machine
/// can reach, not the noise on top of it — and the start is measured against a plain C hello
/// built the same way, so what is counted is the runtime, not the operating system's process
/// start. They run on Linux, the platform the plan's numbers come from: on Windows a process
/// start costs 10–30 ms of its own, and measured through the .NET host and its JIT the
/// differences drowned (measured, M2 S5); the Windows figures come from CI's NativeAOT job, which
/// prints them.
///
/// <para>Measurement point 2 (13, M3) the same way: the three programs of <c>bench/</c> —
/// integer loops, floating-point arithmetic, an array of structs — against their C twins,
/// built by the same compiler with the release profile's flags, within 3×. The bound against Go
/// (1.5×) is <c>bench/run.py</c>'s, which needs a Go toolchain the tests do not assume.</para>
/// </summary>
[Collection("console")]
public class MeasurementTests
{
    private static readonly string Root = RuntimeLayout.FindRoot(AppContext.BaseDirectory);

    private static string Fresh()
    {
        var dir = Directory.CreateTempSubdirectory("lyric5-measure").FullName;
        File.Copy(Path.Combine(Root, "tests", "Lyric5.Tests", "programs", "hello.lyr"), Path.Combine(dir, "hello.lyr"));
        return dir;
    }

    private static double MinMilliseconds(int runs, Action action)
    {
        var best = double.MaxValue;
        for (var i = 0; i < runs; i++)
        {
            var watch = Stopwatch.StartNew();
            action();
            best = Math.Min(best, watch.Elapsed.TotalMilliseconds);
        }
        return best;
    }

    private static int Quiet(params string[] args)
    {
        var savedOut = Console.Out;
        var savedErr = Console.Error;
        Console.SetOut(TextWriter.Null);
        Console.SetError(TextWriter.Null);
        try { return Program.Main(args); }
        finally { Console.SetOut(savedOut); Console.SetError(savedErr); }
    }

    [Fact]
    public void Hello_is_small_and_starts_fast()
    {
        if (!OperatingSystem.IsLinux()) return;
        var dir = Fresh();
        Assert.Equal(0, Quiet("build", Path.Combine(dir, "hello.lyr"), "--profile", "release"));
        var hello = Path.Combine(dir, "out", "release", Target.Host.Triple, "hello" + Target.Host.ExecutableSuffix);

        // Size, debug information included: the bound is on what is shipped.
        var size = new FileInfo(hello).Length;
        Assert.True(size < 2 * 1024 * 1024, $"hello is {size} bytes; the bound is 2 MB (01 L11)");

        // A plain C hello, built by the same compiler with the same flags, as the floor.
        var plainSource = Path.Combine(dir, "plain.c");
        File.WriteAllText(plainSource, "#include <stdio.h>\nint main(void) { puts(\"Hello, C!\"); return 0; }\n");
        var build = new CBuild(CCompiler.Locate()!, Target.Host, Profile.Release, Path.Combine(dir, "plain-cache"));
        var plain = build.LinkExecutable(build.Compile([new CUnit(plainSource, [], [])]), Path.Combine(dir, "plain" + Target.Host.ExecutableSuffix));

        var lyric = MinMilliseconds(20, () => ProcessRunner.Run(hello, [], TimeSpan.FromSeconds(10)));
        var floor = MinMilliseconds(20, () => ProcessRunner.Run(plain, [], TimeSpan.FromSeconds(10)));
        Assert.True(lyric - floor < 4.0, $"hello starts {lyric:0.0} ms, plain C {floor:0.0} ms: the runtime adds {lyric - floor:0.0} ms, the bound is 4 ms");
        Assert.True(lyric < 5.0, $"hello starts in {lyric:0.0} ms; the bound is 5 ms (01 L11)");
    }

    [Theory]
    [InlineData("loops")]
    [InlineData("arith")]
    [InlineData("structs")]
    public void A_bench_program_runs_within_3x_of_its_C_twin(string name)
    {
        if (!OperatingSystem.IsLinux()) return;
        var dir = Directory.CreateTempSubdirectory("lyric5-bench").FullName;
        var bench = Path.Combine(Root, "bench", name);
        File.Copy(Path.Combine(bench, name + ".lyr"), Path.Combine(dir, name + ".lyr"));
        Assert.Equal(0, Quiet("build", Path.Combine(dir, name + ".lyr"), "--profile", "release"));
        var lyric = Path.Combine(dir, "out", "release", Target.Host.Triple, name + Target.Host.ExecutableSuffix);

        var build = new CBuild(CCompiler.Locate()!, Target.Host, Profile.Release, Path.Combine(dir, "c-cache"));
        var twin = build.LinkExecutable(build.Compile([new CUnit(Path.Combine(bench, name + ".c"), [], [])]),
            Path.Combine(dir, name + "-c" + Target.Host.ExecutableSuffix));

        // The same answer first: a faster program that computes something else measures nothing.
        Assert.Equal(ProcessRunner.Run(twin, [], TimeSpan.FromMinutes(1)).Stdout, ProcessRunner.Run(lyric, [], TimeSpan.FromMinutes(1)).Stdout);

        var ours = MinMilliseconds(3, () => ProcessRunner.RunInherited(lyric, []));
        var theirs = MinMilliseconds(3, () => ProcessRunner.RunInherited(twin, []));
        Assert.True(ours <= 3.0 * theirs,
            $"{name}: Lyric {ours:0} ms, C {theirs:0} ms — {ours / theirs:0.00}×, the bound is 3× (13, measurement point 2)");
    }

    [Fact]
    public void A_warm_run_costs_at_most_50_ms_over_the_program()
    {
        if (!OperatingSystem.IsLinux()) return;
        var dir = Fresh();
        var file = Path.Combine(dir, "hello.lyr");
        Assert.Equal(0, Quiet("run", file, "--profile", "release"));
        var hello = Path.Combine(dir, "out", "release", Target.Host.Triple, "hello" + Target.Host.ExecutableSuffix);

        var program = MinMilliseconds(10, () => ProcessRunner.RunInherited(hello, []));
        var warm = MinMilliseconds(5, () => Assert.Equal(0, Quiet("run", file, "--profile", "release")));
        Assert.True(warm - program < 50.0,
            $"a warm run takes {warm:0.0} ms, the program alone {program:0.0} ms: {warm - program:0.0} ms over it, the bound is 50 ms (11 C9)");
    }
}
