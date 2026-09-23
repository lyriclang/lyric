namespace Lyric.Tests.Cli;

/// <summary>
/// <c>-o ""</c> is a refusal with a code, not a stack trace.
///
/// <para>An empty path travelled all the way to <c>File.WriteAllBytes</c> in <c>lyrc</c> and to
/// <c>Path.GetFullPath</c> in <c>lyrpack</c>. Both throw <c>ArgumentException</c>, nothing above
/// caught it, and a successful compile therefore ended in an unhandled .NET exception — the
/// program was fine, the command line was not, and the output said neither.</para>
///
/// <para>Worth two tests rather than one: the hole was reported for <c>lyrc</c> alone, and
/// <c>lyrpack</c> turned out to have it too. Asking the second tool is what found it.</para>
///
/// <para>The shape is not exotic. <c>-o "$OUT"</c> with <c>OUT</c> unset is exactly this, and a
/// shell offers no warning on the way.</para>
/// </summary>
public class EmptyOutputPathTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Lyrc_refuses_an_empty_output_path(string path)
    {
        var result = Toolchain.Lyrc("build", Toolchain.Example("hello.lyr"), "-o", path);

        Assert.Contains("LYR-CLI0002", result.Err, StringComparison.Ordinal);
        Assert.DoesNotContain("Unhandled exception", result.Err, StringComparison.Ordinal);
        Assert.NotEqual(0, result.ExitCode);
    }

    [Fact]
    public void Lyrpack_refuses_an_empty_output_path()
    {
        using var module = Toolchain.Temp(".lyrbc");
        var built = Toolchain.Lyrc("build", Toolchain.Example("hello.lyr"), "-o", module.Path);
        Assert.Equal(0, built.ExitCode);

        var result = Toolchain.Lyrpack(module.Path, "-o", "");

        Assert.Contains("LYR-CLI0002", result.Err, StringComparison.Ordinal);
        Assert.DoesNotContain("Unhandled exception", result.Err, StringComparison.Ordinal);
        Assert.NotEqual(0, result.ExitCode);
    }

    /// <summary>The control: a path that IS a path still writes one.</summary>
    [Fact]
    public void A_real_output_path_still_works()
    {
        using var module = Toolchain.Temp(".lyrbc");
        var result = Toolchain.Lyrc("build", Toolchain.Example("hello.lyr"), "-o", module.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(module.Path), "nothing was written");
    }
}
