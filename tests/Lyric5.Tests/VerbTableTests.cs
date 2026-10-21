using System.Text.Json;
using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// The verbs as one table (spec 14 §5): what <c>help</c> prints, what each verb is read by, what the
/// spec's table is printed from — and <c>check</c> and <c>env</c>, the verbs it brought (§1.1, §6), and
/// the warnings an up-to-date build says again (§1.2).
/// </summary>
[Collection("console")]
public class VerbTableTests
{
    private const string Warns = "fn main(): void {\n    let unused = 1;\n}\n";

    public static TheoryData<string> Verbs() => [.. new[]
    {
        "build", "run", "check", "expand", "test", "bench", "fix", "env", "update", "add", "remove", "clean",
        "metadata", "explain", "new", "init", "completions", "version", "help",
    }];

    [Theory]
    [MemberData(nameof(Verbs))]
    public void Every_verb_is_in_the_help_and_refuses_an_option_it_does_not_have(string verb)
    {
        var (_, usage, _) = RunRaw("help");
        Assert.Contains($"\n  {verb} ", usage);
        var (exit, _, error) = RunRaw(verb, "--no-such-option");
        Assert.Equal(2, exit);
        Assert.Contains($"unknown option '--no-such-option' for '{verb}'", error);
    }

    [Fact]
    public void A_verbs_help_lists_its_options_and_the_table_prints_as_the_spec_has_it()
    {
        var (exit, help, _) = RunRaw("build", "--help");
        Assert.Equal(0, exit);
        Assert.StartsWith("usage: lyric5 build [<file.lyr>] [options]\n", help);
        foreach (var option in new[] { "--profile <name>", "--[no-]lto", "--emit <ir|c>", "-C <dir>", "--locked" })
            Assert.Contains(option, help);
        Assert.Equal(help, RunRaw("help", "build").Out);

        var (_, table, _) = RunRaw("help", "--markdown");
        Assert.StartsWith("| verb | words | options | lints | `--json` |\n|---|---|---|---|---|\n", table);
        Assert.Contains("| `check` | `[<file.lyr>]` |", table);
        Assert.Contains("| `test` | — | `--profile <name>`", table);
    }

    [Fact]
    public void A_flag_of_a_field_takes_no_value_and_a_value_option_needs_one()
    {
        var root = Package(("lyric.toml", AppManifest), ("src/main.lyr", Warns));
        Assert.Contains("'--lto' takes no value", RunRaw("build", "-C", root, "--lto=yes").Err);
        Assert.Contains("'--profile' needs a value", RunRaw("build", "-C", root, "--profile").Err);
    }

    [Fact]
    public void Check_says_what_build_says_and_makes_nothing()
    {
        var root = Package(("lyric.toml", AppManifest), ("src/main.lyr", Warns));
        var (exit, _, error) = RunRaw("check", "-C", root, "--color", "never");
        Assert.Equal(0, exit);
        Assert.Contains("warning[LYR-SEM0071]: 'unused' is never used", error);
        Assert.False(Directory.Exists(Path.Combine(root, "out", "debug")), "check made a binary");
        Assert.Equal(1, RunRaw("check", "-C", root, "-D", "unused-variable").Exit);

        var broken = Package(("lyric.toml", AppManifest), ("src/main.lyr", "fn main(): void {\n    let x: int = \"text\";\n}\n"));
        Assert.Equal(1, RunRaw("check", "-C", broken).Exit);
    }

    [Fact]
    public void Check_checks_a_library()
    {
        var root = Package(("lyric.toml", AppManifest), ("src/lib.lyr", "pub fn twice(x: int): int {\n    return x * 2;\n}\n"));
        Assert.Equal(0, RunRaw("check", "-C", root).Exit);
    }

    [Fact]
    public void An_up_to_date_build_says_its_warnings_again()
    {
        var root = Package(("lyric.toml", AppManifest), ("src/main.lyr", Warns));
        var first = RunRaw("build", "-C", root, "--color", "never");
        var second = RunRaw("build", "-C", root, "--color", "never");
        Assert.Equal(0, second.Exit);
        Assert.Contains("warning[LYR-SEM0071]: 'unused' is never used", first.Err);
        Assert.Equal(first.Err, second.Err);
        var (_, json, _) = RunRaw("build", "-C", root, "--json");
        Assert.Contains("{\"type\":\"summary\",\"version\":1,\"errors\":0,\"warnings\":1}", json);
    }

    [Fact]
    public void Env_says_each_setting_and_where_it_comes_from()
    {
        var root = Package(("lyric.toml", AppManifest + "\n[profile.release]\nlto = true\n"), ("src/main.lyr", Warns));
        var (exit, output, _) = RunRaw("env", "-C", root, "--profile", "release", "--opt", "1", "--json");
        Assert.True(exit == 0, output);
        var settings = JsonDocument.Parse(output).RootElement.GetProperty("settings").EnumerateArray()
            .ToDictionary(s => s.GetProperty("name").GetString()!, s => (s.GetProperty("value").GetString(), s.GetProperty("origin").GetString()));
        Assert.Equal(("release", "command line"), settings["profile"]);
        Assert.Equal(("1", "command line"), settings["profile.opt"]);
        Assert.Equal(("true", "lyric.toml"), settings["profile.lto"]);
        Assert.Equal("default", settings["profile.debugInfo"].Item2);
        Assert.Equal("default", settings["toolchain"].Item2);

        var (_, text, _) = RunRaw("env", "-C", root);
        Assert.Contains("profile.opt", text);
        Assert.Contains("(default)", text);
    }
}
