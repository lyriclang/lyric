using System.Runtime.CompilerServices;

namespace Lyric5.Tests;

/// <summary>The environment every test runs in: the machine's own toolchains are none of the tests'
/// business. A command a test runs in this process, in a package that asks for a newer toolchain,
/// would otherwise be handed to one installed under <c>~/.lyric</c> — and on Unix, replace the test
/// host with it.</summary>
internal static class TestEnvironment
{
    [ModuleInitializer]
    internal static void Isolate()
    {
        Environment.SetEnvironmentVariable("LYRIC_HOME", Path.Combine(Path.GetTempPath(), $"lyric5-tests-home-{Environment.ProcessId}"));
        Environment.SetEnvironmentVariable("LYRIC_TOOLCHAIN", null);
        Environment.SetEnvironmentVariable("LYRIC_TOOLCHAIN_FROM", null);
    }
}
