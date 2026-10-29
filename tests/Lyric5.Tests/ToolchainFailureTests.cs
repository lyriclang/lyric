using Lyric5.Build;
using Lyric5.Toolchain;

namespace Lyric5.Tests;

/// <summary>
/// Where the toolchain's failures go: its own (<c>LYR-ICE0001</c>) with a report written beside
/// the message — version, command, directory, machine, place, exception and trace —, and a C
/// compiler that does not finish in time as the environment's (<c>LYR-BLD0001</c>, exit 2) with what
/// it did and the limit, its half-written object gone, not as the toolchain's own.
/// </summary>
public class ToolchainFailureTests
{
    [Fact]
    public void The_report_holds_what_a_bug_report_needs_and_lies_under_out()
    {
        var root = TestDirectories.Fresh("lyric5-ice-");
        var report = IceReport.Write(root, "5.0.0-test", ["build", "-C", "my app"], "the toolchain failed: Boom: no", "lowering 'app.main.main' at src/main.lyr:3:1", "   at Somewhere()");
        Assert.NotNull(report);
        Assert.Equal(Path.Combine(root, "out"), Path.GetDirectoryName(report));
        Assert.Matches(@"^lyric5-ice-\d{8}-\d{6}-\d+\.txt$", Path.GetFileName(report));
        var text = File.ReadAllText(report);
        Assert.StartsWith("lyric5 5.0.0-test failed: this is a bug in lyric5", text);
        Assert.Contains("command:   lyric5 build -C 'my app'\n", text);
        Assert.Contains($"directory: {Environment.CurrentDirectory}\n", text);
        Assert.Contains("while:     lowering 'app.main.main' at src/main.lyr:3:1\n", text);
        Assert.Contains("\nthe toolchain failed: Boom: no\n\n   at Somewhere()\n", text);

        // where out/ cannot be made — a file stands there —, the temporary directory takes it
        var blocked = TestDirectories.Fresh("lyric5-ice-blocked-");
        File.WriteAllText(Path.Combine(blocked, "out"), "");
        var elsewhere = IceReport.Write(blocked, "5.0.0-test", [], "m", null, null);
        Assert.Equal(Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar), Path.GetDirectoryName(elsewhere));
        Assert.Contains("while:     (no declaration was being checked or lowered)", File.ReadAllText(elsewhere!));
        File.Delete(elsewhere!);
    }

    [Fact]
    public void A_C_compiler_that_does_not_finish_is_stopped_and_said_as_the_environment_s()
    {
        // a stand-in compiler that never finishes
        var dir = TestDirectories.Fresh("lyric5-slow-cc-");
        var source = Path.Combine(dir, "slow.c");
        File.WriteAllText(source,
            "#ifdef _WIN32\n#include <windows.h>\n#define PAUSE() Sleep(60000)\n#else\n#include <unistd.h>\n#define PAUSE() sleep(60)\n#endif\n\n"
            + "int main(void) {\n    PAUSE();\n    return 0;\n}\n");
        var compiler = CCompiler.Locate() ?? throw new InvalidOperationException("no C compiler");
        var slow = Path.Combine(dir, "slow-cc" + Target.Host.ExecutableSuffix);
        string[] compile = [source, "-o", slow];
        var built = ProcessRunner.Run(compiler.Path, compiler.Kind == CCompilerKind.Zig ? ["cc", .. compile] : compile, TimeSpan.FromMinutes(10));
        Assert.True(built.ExitCode == 0, built.Stderr);

        var cache = TestDirectories.Fresh("lyric5-slow-cache-");
        var build = new CBuild(new CCompiler(CCompilerKind.Clang, slow, "stand-in"), Target.Host, Profile.Debug, cache) { Limit = TimeSpan.FromSeconds(2) };
        File.WriteAllText(Path.Combine(dir, "a.c"), "int a(void) { return 1; }\n");
        File.WriteAllText(Path.Combine(dir, "b.c"), "int b(void) { return 2; }\n");
        var started = DateTime.UtcNow;
        var refused = Assert.Throws<CBuildException>(() => build.Compile([new CUnit(Path.Combine(dir, "a.c"), [], []), new CUnit(Path.Combine(dir, "b.c"), [], [])]));
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(45), "the compiler was not stopped at its limit");
        Assert.Contains($"compiling {Path.Combine(dir, "a.c")} for {Target.Host} (debug) did not finish within 2 s, and was stopped", refused.Message);
        Assert.Contains($"compiling {Path.Combine(dir, "b.c")}", refused.Message);
        Assert.Empty(Directory.Exists(Path.Combine(cache, "obj")) ? Directory.GetFiles(Path.Combine(cache, "obj")) : []);
    }
}
