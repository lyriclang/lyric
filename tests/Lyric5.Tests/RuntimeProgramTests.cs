using Lyric5.Toolchain;

namespace Lyric5.Tests;

/// <summary>
/// The C programs under <c>runtime/tests</c>, built against <c>liblyr.a</c> and run on the host in
/// the debug and the release profile. Each program states its expectation in its header comment;
/// the expected output and exit code here are the same statement, checked.
/// </summary>
public class RuntimeProgramTests
{
    public static TheoryData<string, Profile, int, string, string[]> Programs()
    {
        var data = new TheoryData<string, Profile, int, string, string[]>();
        foreach (var profile in new[] { Profile.Debug, Profile.Release })
        {
            data.Add("alloc", profile, 0, "alloc ok\n", []);
            data.Add("strings", profile, 0, "strings ok\n", []);
            data.Add("numeric", profile, 0, "numeric ok\n", []);
            data.Add("arrays", profile, 0, "arrays ok\n", []);
            data.Add("config", profile, 0, "config ok\n", []);
            data.Add("hello", profile, 7, "Hello, Lyric!\nargs: 2\n", ["one", "two"]);
            data.Add("roots", profile, 0, "roots ok\n", []);
            data.Add("weak", profile, 0, "weak ok\n", []);
            data.Add("threads", profile, 0, "threads ok\n", []);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Programs))]
    public void A_runtime_program_does_what_its_header_says(string name, Profile profile, int exit, string stdout, string[] args)
    {
        var result = RuntimeBuildTests.RunTest(name, profile, args: args);
        Assert.True(result.ExitCode == exit, $"exit {result.ExitCode}, expected {exit}\nstderr:\n{result.Stderr}");
        Assert.Equal(stdout, result.Stdout.Replace("\r\n", "\n"));
    }
}
