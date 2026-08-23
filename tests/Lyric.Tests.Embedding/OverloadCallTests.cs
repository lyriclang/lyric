using System.Runtime.CompilerServices;
using Lyric.Core;
using Lyric.Embedding;

namespace Lyric.Tests.Embedding;

/// <summary>
/// Calling an OVERLOADED script function from the host (v3.0).
///
/// <para>The host has runtime values rather than declared types, and it addresses functions by
/// name — so the two things the script uses to tell overloads apart are both missing here. What
/// is left is the argument COUNT, and where that does not separate them the host is told to say
/// which one it means.</para>
/// </summary>
public class OverloadCallTests
{
    private static string RepoRoot([CallerFilePath] string thisFile = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));

    private static ScriptInstance Instance(string source)
    {
        var vm = new LangVm(new HostOptions
        {
            StdlibRoot = Path.Combine(RepoRoot(), "stdlib"),
            Capabilities = Capability.None,
        });
        return vm.Instantiate(vm.Compile(source, "mod"));
    }

    private const string Overloaded = """
        pub fn code(n: int): int { return 1; }
        pub fn code(s: string): int { return 2; }
        pub fn code(a: int, b: int): int { return 3; }
        """;

    [Fact]
    public void The_argument_count_finds_the_one_that_takes_it()
    {
        var instance = Instance(Overloaded);

        // Two arguments: exactly one overload takes them, so the name is enough.
        Assert.Equal(3L, instance.Call<long>("code", 1, 2));
    }

    [Fact]
    public void Two_of_one_count_are_an_ambiguity_the_host_settles()
    {
        var instance = Instance(Overloaded);

        var refused = Assert.Throws<ScriptException>(() => instance.Call<long>("code", 1));
        Assert.Equal("LYR-EMB0008", refused.Code);

        // And the way out is in the message: the full name, as the disassembly shows it.
        Assert.Contains("mod.code(int)", refused.Message, StringComparison.Ordinal);
        Assert.Contains("mod.code(string)", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_full_name_reaches_the_one_meant()
    {
        var instance = Instance(Overloaded);

        Assert.Equal(1L, instance.Call<long>("code(int)", 1));
        Assert.Equal(2L, instance.Call<long>("code(string)", "x"));
    }

    [Fact]
    public void A_name_that_is_not_there_is_still_that_error()
    {
        var instance = Instance(Overloaded);

        var missing = Assert.Throws<ScriptException>(() => instance.Call<long>("nope", 1));
        Assert.Equal("LYR-EMB0006", missing.Code);
    }
}
