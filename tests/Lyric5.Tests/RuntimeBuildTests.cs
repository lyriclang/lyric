using Lyric5.Toolchain;

namespace Lyric5.Tests;

/// <summary>
/// The runtime as the plan's M1 artifact states it: <c>liblyr.a</c> builds for every Tier 1
/// target, and its test programs link and run on the host. These tests need zig — the toolchain
/// requires a C compiler, so a missing one is a failure here, not a skip.
/// </summary>
public class RuntimeBuildTests
{
    private static readonly string Root = RuntimeLayout.FindRoot(AppContext.BaseDirectory);

    /// <summary>Stable across runs, so the collector is compiled once per machine, not per test run.</summary>
    private static readonly string Cache = Path.Combine(Path.GetTempPath(), "lyric5-test-cache");

    private static CCompiler Zig() =>
        CCompiler.Locate(CCompilerKind.Zig)
        ?? throw new InvalidOperationException("zig is required to build the Lyric 5 runtime (tooling/zig-version names the version)");

    [Fact]
    public void The_runtime_archive_builds_for_every_tier_1_target()
    {
        var zig = Zig();
        foreach (var target in Target.Tier1)
        {
            var build = new CBuild(zig, target, Profile.Release, Cache);
            var archive = RuntimeLayout.BuildArchive(build, Root, Path.Combine(Cache, "lib"));
            Assert.True(new FileInfo(archive).Length > 0, $"{archive} is empty");

            // And a program links against it for that target (run only where the target is the host).
            var test = new CUnit(Path.Combine(Root, "runtime", "tests", "gc_smoke.c"), [],
                [RuntimeLayout.IncludeDir(Root), Path.Combine(Root, "runtime", "third_party", "bdwgc", "include")]);
            var objects = build.Compile([test]);
            var exe = build.LinkExecutable([.. objects, archive],
                Path.Combine(Cache, "bin", target.Triple, "gc_smoke" + target.ExecutableSuffix));
            Assert.True(File.Exists(exe), $"no executable for {target}");
        }
    }

    [Theory]
    [InlineData(Profile.Debug)]
    [InlineData(Profile.Release)]
    public void The_collector_smoke_runs_on_the_host(Profile profile)
    {
        var result = RunTest("gc_smoke", profile);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("gc ok\n", result.Stdout.Replace("\r\n", "\n"));
    }

    internal static ProcessRunner.Result RunTest(string name, Profile profile, CCompiler? compiler = null, string[]? args = null) =>
        RunC(Path.Combine(Root, "runtime", "tests", name + ".c"), name, profile, compiler, args);

    /// <summary>C text (the emitter's) as a program: written into the cache, built and run like a
    /// runtime test.</summary>
    internal static ProcessRunner.Result RunEmitted(string cText, string name, Profile profile, string[]? args = null)
    {
        var dir = Path.Combine(Cache, "emitted");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name + ".c");
        File.WriteAllText(path, cText);
        // Its own name in bin/: 'hello' is also a runtime test program, and two tests linking to
        // one path raced (seen on Windows).
        return RunC(path, "emitted-" + name, profile, null, args);
    }

    private static ProcessRunner.Result RunC(string source, string name, Profile profile, CCompiler? compiler, string[]? args)
    {
        var build = new CBuild(compiler ?? Zig(), Target.Host, profile, Cache);
        var archive = RuntimeLayout.BuildArchive(build, Root, Path.Combine(Cache, "lib"));
        var unit = new CUnit(source, [],
            [RuntimeLayout.IncludeDir(Root), Path.Combine(Root, "runtime", "third_party", "bdwgc", "include")]);
        var objects = build.Compile([unit]);
        var exe = build.LinkExecutable([.. objects, archive],
            Path.Combine(Cache, "bin", Target.Host.Triple, profile.Name(), name + Target.Host.ExecutableSuffix));
        return ProcessRunner.Run(exe, args ?? [], TimeSpan.FromMinutes(2));
    }
}
