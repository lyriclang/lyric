using Lyric5.Build;
using Lyric5.Toolchain;
using Profile = Lyric5.Toolchain.Profile;

namespace Lyric5.Tests;

/// <summary>
/// <c>lyric5 build</c> and <c>run</c> (M2 S4; design/v5/spec/11 C5, C8, P4, P5; 01 L7): a file is
/// an implicit package with <c>out/</c> by it, the binary lands under profile and target, an
/// unchanged program compiles nothing twice, and <c>run</c> passes the program's exit through.
/// Every test works in a directory of its own, so no two share an <c>out/</c>.
/// </summary>
[Collection("console")]
public class BuildTests
{
    private static readonly string Root = RuntimeLayout.FindRoot(AppContext.BaseDirectory);

    private static string Fresh(params string[] programs)
    {
        var dir = TestDirectories.Fresh("lyric5-build-");
        foreach (var program in programs)
            File.Copy(Path.Combine(Root, "tests", "Lyric5.Tests", "programs", program + ".lyr"), Path.Combine(dir, program + ".lyr"));
        return dir;
    }

    private static (int Exit, string Out, string Err) Run(params string[] args)
    {
        var savedOut = Console.Out;
        var savedErr = Console.Error;
        var output = new StringWriter();
        var error = new StringWriter();
        Console.SetOut(output);
        Console.SetError(error);
        try
        {
            var exit = Program.Main(args);
            return (exit, output.ToString().Replace("\r\n", "\n"), error.ToString().Replace("\r\n", "\n"));
        }
        finally
        {
            Console.SetOut(savedOut);
            Console.SetError(savedErr);
        }
    }

    [Fact]
    public void A_file_is_a_package_with_out_beside_it_or_at_the_repository_root()
    {
        var root = TestDirectories.Fresh("lyric5-root-");
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        var src = Directory.CreateDirectory(Path.Combine(root, "src", "deep")).FullName;
        File.WriteAllText(Path.Combine(src, "tool.lyr"), "fn main(): int { return 0; }\n");
        var project = Project.ForFile(Path.Combine(src, "tool.lyr"));
        Assert.Equal("tool", project.Name);
        Assert.Equal(root, project.Root);
        Assert.Equal(Path.Combine(root, "out", "debug", Target.Host.Triple, "tool" + Target.Host.ExecutableSuffix),
            project.Executable(Profile.Debug, Target.Host));

        var alone = Fresh("hello");
        Assert.Equal(alone, Project.ForFile(Path.Combine(alone, "hello.lyr")).Root);
    }

    [Fact]
    public void Build_places_the_binary_under_out_and_it_runs()
    {
        var dir = Fresh("hello");
        var (exit, output, err) = Run("build", Path.Combine(dir, "hello.lyr"));
        Assert.True(exit == 0, err);
        Assert.Equal("", output);
        var exe = Path.Combine(dir, "out", "debug", Target.Host.Triple, "hello" + Target.Host.ExecutableSuffix);
        Assert.True(File.Exists(exe), exe);
        var result = ProcessRunner.Run(exe, [], TimeSpan.FromMinutes(1));
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("Hello, Lyric!\n", result.Stdout.Replace("\r\n", "\n"));
    }

    [Fact]
    public void An_unchanged_program_compiles_nothing_twice()
    {
        var dir = Fresh("fib");
        Assert.Equal(0, Run("build", Path.Combine(dir, "fib.lyr"), "--profile", "release").Exit);
        var cache = Path.Combine(dir, "out", "cache");
        var before = Directory.GetFiles(cache, "*", SearchOption.AllDirectories)
            .ToDictionary(f => f, File.GetLastWriteTimeUtc);
        var exe = Path.Combine(dir, "out", "release", Target.Host.Triple, "fib" + Target.Host.ExecutableSuffix);
        var linked = File.GetLastWriteTimeUtc(exe);
        Assert.Contains(before.Keys, f => f.EndsWith(".c"));
        Assert.Contains(before.Keys, f => f.EndsWith(".o"));

        Assert.Equal(0, Run("build", Path.Combine(dir, "fib.lyr"), "--profile", "release").Exit);
        foreach (var (file, time) in before) Assert.Equal(time, File.GetLastWriteTimeUtc(file));
        Assert.Equal(linked, File.GetLastWriteTimeUtc(exe));

        // A changed program is a new key: the old C stays (the cache is by content), a new one appears.
        File.AppendAllText(Path.Combine(dir, "fib.lyr"), "\n// changed\n");
        Assert.Equal(0, Run("build", Path.Combine(dir, "fib.lyr"), "--profile", "release").Exit);
        Assert.Equal(before.Count(f => f.Key.EndsWith(".c")) + 1, Directory.GetFiles(cache, "*.c").Length);
    }

    [Fact]
    public void Run_passes_the_programs_exit_code_through()
    {
        var dir = Fresh("fib", "overflow");
        Assert.Equal(55, Run("run", Path.Combine(dir, "fib.lyr")).Exit);
        Assert.Equal(101, Run("run", Path.Combine(dir, "overflow.lyr")).Exit);
    }

    [Fact]
    public void A_cross_target_builds_and_does_not_run()
    {
        var dir = Fresh("fib");
        var other = Target.Tier1.First(t => !t.IsHost && t.Os == TargetOs.Linux);
        var (exit, _, err) = Run("build", Path.Combine(dir, "fib.lyr"), "--target", other.Triple);
        Assert.True(exit == 0, err);
        Assert.True(File.Exists(Path.Combine(dir, "out", "debug", other.Triple, "fib" + other.ExecutableSuffix)));
        var run = Run("run", Path.Combine(dir, "fib.lyr"), "--target", other.Triple);
        Assert.Equal(2, run.Exit);
        Assert.Contains("is not this machine", run.Err);
    }

    [Theory]
    [InlineData("--profile", "fast", "unknown profile")]
    [InlineData("--target", "z80-cpm", "unknown target")]
    [InlineData("--emit", "asm", "unknown emission")]
    public void An_unknown_value_is_exit_2(string option, string value, string message)
    {
        var dir = Fresh("hello");
        var (exit, _, err) = Run("build", Path.Combine(dir, "hello.lyr"), option, value);
        Assert.Equal(2, exit);
        Assert.Contains(message, err);
    }

    [Fact]
    public void The_separator_belongs_to_run()
    {
        var dir = Fresh("hello");
        var (exit, _, err) = Run("build", Path.Combine(dir, "hello.lyr"), "--", "x");
        Assert.Equal(2, exit);
        Assert.Contains("only 'run'", err);
    }
}
