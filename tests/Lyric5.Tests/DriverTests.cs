using Lyric5.Toolchain;

namespace Lyric5.Tests;

/// <summary>
/// The verbs of <c>lyric5</c> (design/v5/spec/11 C1–C5), run in-process through <c>Main</c>:
/// what it does not know is exit 2, what the program did wrong is exit 1, and <c>build --emit ir</c>
/// prints the IR of a program the subset gate lets through. The tests share the console, so they
/// run one at a time.
/// </summary>
[Collection("console")]
public class DriverTests
{
    private static readonly string Root = RuntimeLayout.FindRoot(AppContext.BaseDirectory);

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

    private static string Program_(string name) => Path.Combine(Root, "tests", "Lyric5.Tests", "programs", name);

    [Fact]
    public void An_unknown_verb_is_exit_2()
    {
        var (exit, _, err) = Run("frobnicate");
        Assert.Equal(2, exit);
        Assert.Contains("LYR-CLI0003", err);
    }

    [Fact]
    public void An_unknown_option_is_exit_2()
    {
        var (exit, _, err) = Run("build", Program_("hello.lyr"), "--emit", "ir", "--fast");
        Assert.Equal(2, exit);
        Assert.Contains("'--fast'", err);
    }

    [Fact]
    public void A_missing_file_is_exit_2()
    {
        var (exit, _, err) = Run("build", Program_("nowhere.lyr"), "--emit", "ir");
        Assert.Equal(2, exit);
        Assert.Contains("LYR-CLI0001", err);
    }

    [Fact]
    public void Hello_emits_its_ir()
    {
        var (exit, output, err) = Run("build", Program_("hello.lyr"), "--emit", "ir");
        Assert.True(exit == 0, err);
        Assert.Contains("fn main.main -> i64", output);
        Assert.Contains("callimport std.io.println", output);
        Assert.Contains("callimport std.string.concat", output);
    }

    [Fact]
    public void Fib_emits_its_c()
    {
        var (exit, output, err) = Run("build", Program_("fib.lyr"), "--emit", "c");
        Assert.True(exit == 0, err);
        Assert.Contains("static int64_t lyr_main_fib(int64_t l0_n)", output);
        Assert.Contains("int main(int argc, char **argv) { return lyr_run_main(argc, argv, lyr_main_main); }", output);
    }

    /// <summary>Strings come with S3: until then the emitter says so through the gate's code, and
    /// the driver ends with 1 rather than a stack trace.</summary>
    [Fact]
    public void Hello_as_c_waits_for_s3()
    {
        var (exit, output, err) = Run("build", Program_("hello.lyr"), "--emit", "c");
        Assert.Equal(1, exit);
        Assert.Equal("", output);
        Assert.Contains("LYR-CG0001", err);
        Assert.Contains("strings (M2 S3)", err);
    }

    [Fact]
    public void A_program_outside_the_core_is_exit_1_with_the_milestone()
    {
        var (exit, output, err) = Run("build", Program_("not_yet.lyr"), "--emit", "ir");
        Assert.Equal(1, exit);
        Assert.Equal("", output);
        Assert.Contains("LYR-CG0001", err);
        Assert.Contains("(M6)", err);
    }

    [Fact]
    public void A_program_with_an_error_is_exit_1()
    {
        var (exit, _, err) = Run("build", Program_("broken.lyr"), "--emit", "ir");
        Assert.Equal(1, exit);
        Assert.Contains("error[", err);
        Assert.DoesNotContain("LYR-CG0001", err);
    }

    [Fact]
    public void Version_names_the_edition()
    {
        var (exit, output, _) = Run("version");
        Assert.Equal(0, exit);
        Assert.StartsWith("lyric 5.0.0", output);
        Assert.Contains("edition     5", output);
    }
}
