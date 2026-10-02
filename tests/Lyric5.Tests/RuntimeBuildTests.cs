using Lyric5.Compiler;
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

    /// <param name="binary">The executable's own name in <c>bin/</c> when another test class
    /// links the same program: two classes run in parallel, and on Windows one linking the path
    /// the other is running fails with "Permission denied" (seen in CI, M4 S1).</param>
    internal static ProcessRunner.Result RunTest(string name, Profile profile, CCompiler? compiler = null, string[]? args = null, string? binary = null) =>
        RunC(Path.Combine(Root, "runtime", "tests", name + ".c"), binary ?? name, profile, compiler, args);

    /// <summary>The emitter's units as a program: written into the cache, built and run like a
    /// runtime test — with zig, or the compiler a sanitizer profile needs.</summary>
    internal static ProcessRunner.Result RunEmitted(IReadOnlyList<CEmitter.Unit> units, string name, Profile profile,
        string[]? args = null, CCompiler? compiler = null) =>
        // Its own name in bin/: 'hello' is also a runtime test program, and two tests linking to
        // one path raced (seen on Windows).
        RunC(WriteUnits(units, name), "emitted-" + name, profile, compiler, args);

    /// <summary>The emitter's units built as <see cref="RunEmitted"/> builds them, and not run: the
    /// program's path, for a test that drives it itself — sends it signals (M6 S7).</summary>
    internal static string BuildEmitted(IReadOnlyList<CEmitter.Unit> units, string name, Profile profile) =>
        BuildC(WriteUnits(units, name), "emitted-" + name, profile, null);

    /// <summary>Where <see cref="RunEmitted"/> put the program: to run it again, timed.</summary>
    internal static string EmittedBinary(string name, Profile profile) =>
        Path.Combine(Cache, "bin", Target.Host.Triple, profile.Name(), "emitted-" + name + Target.Host.ExecutableSuffix);

    /// <summary>One file per unit under <c>emitted/&lt;name&gt;/</c>, the module's first.</summary>
    private static string[] WriteUnits(IReadOnlyList<CEmitter.Unit> units, string name)
    {
        var dir = Path.Combine(Cache, "emitted", name);
        Directory.CreateDirectory(dir);
        var paths = new string[units.Count];
        for (var i = 0; i < units.Count; i++)
        {
            paths[i] = Path.Combine(dir, $"u{i}.c");
            File.WriteAllText(paths[i], units[i].Text);
        }
        return paths;
    }

    /// <summary>
    /// The emitter's C under a test harness instead of <c>lyr_run_main</c>: the program is
    /// compiled with <c>lyr_run_main</c> renamed to <c>lyr_test_run_main</c>, which the harness
    /// (a file under <c>tests/Lyric5.Tests/harness</c>) defines — so a test starts the runtime
    /// its own way, under a heap limit for one, without the emitter knowing.
    /// </summary>
    internal static ProcessRunner.Result RunEmittedUnder(string harness, IReadOnlyList<CEmitter.Unit> units, string name, Profile profile) =>
        RunC(WriteUnits(units, $"{name}-{harness}"), $"emitted-{name}-{harness}", profile, null, null,
            ["lyr_run_main=lyr_test_run_main"], Path.Combine(Root, "tests", "Lyric5.Tests", "harness", harness + ".c"));

    private static ProcessRunner.Result RunC(string source, string name, Profile profile, CCompiler? compiler, string[]? args,
        string[]? defines = null, string? beside = null) =>
        RunC([source], name, profile, compiler, args, defines, beside);

    private static ProcessRunner.Result RunC(string[] sources, string name, Profile profile, CCompiler? compiler, string[]? args,
        string[]? defines = null, string? beside = null) =>
        ProcessRunner.Run(BuildC(sources, name, profile, compiler, defines, beside), args ?? [], TimeSpan.FromMinutes(2));

    private static string BuildC(string[] sources, string name, Profile profile, CCompiler? compiler,
        string[]? defines = null, string? beside = null)
    {
        var build = new CBuild(compiler ?? Zig(), Target.Host, profile, Cache);
        var archive = RuntimeLayout.BuildArchive(build, Root, Path.Combine(Cache, "lib"));
        string[] includes = [RuntimeLayout.IncludeDir(Root), Path.Combine(Root, "runtime", "third_party", "bdwgc", "include")];
        var units = sources.Select(source => new CUnit(source, defines ?? [], includes)).ToList();
        if (beside is not null) units.Add(new CUnit(beside, [], includes));
        var objects = build.Compile(units);
        return build.LinkExecutable([.. objects, archive],
            Path.Combine(Cache, "bin", Target.Host.Triple, profile.Name(), name + Target.Host.ExecutableSuffix));
    }
}
