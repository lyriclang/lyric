using Lyric5.Toolchain;
using Profile = Lyric5.Toolchain.Profile;

namespace Lyric5.Tests;

/// <summary>
/// The program and its system (design/v5/spec/10 Q9; 13 M8b S9): a program run with arguments —
/// one of them not ASCII, which Windows hands over as UTF-16 — and a variable in its environment;
/// the variable set again; the platform the test runs on; the end through <c>os.exit</c>, the
/// console flushed first.
/// </summary>
public class SystemTests
{
    [Fact]
    public void A_program_hears_its_arguments_and_its_environment_and_ends_with_its_code()
    {
        var program = RuntimeBuildTests.BuildEmitted(CEmitterTests.EmitC("system"), "system", Profile.Debug);
        var result = ProcessRunner.Run(program, ["one", "zwei é"], TimeSpan.FromSeconds(30),
            environment: new Dictionary<string, string> { ["LYRIC_TEST_SYSTEM"] = "set" });
        var platform = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux";
        var arch = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture
            == System.Runtime.InteropServices.Architecture.Arm64 ? "aarch64" : "x86_64";
        Assert.Equal(3, result.ExitCode);
        Assert.Equal($"2 one|zwei é\nset <none> after\n{platform} {arch}\nbefore exit\n", result.Stdout.Replace("\r\n", "\n"));
        Assert.Equal("", result.Stderr);
    }
}
