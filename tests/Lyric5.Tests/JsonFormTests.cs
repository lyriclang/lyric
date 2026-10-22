using System.Text.Json;
using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// The JSON form (spec 16 §4): under <c>--json</c>, <c>build</c>, <c>run</c> and <c>test</c>
/// write one object a line on the standard output — every line parses by itself, each has its type and
/// version — and nothing on the standard error; the findings the driver says itself (a usage error,
/// a manifest's refusal) come the same way, a manifest's at its line.
/// </summary>
[Collection("console")]
public class JsonFormTests
{
    private static List<JsonElement> Events(string output)
    {
        var events = new List<JsonElement>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var e = JsonDocument.Parse(line).RootElement;
            Assert.True(e.TryGetProperty("type", out _), $"no type: {line}");
            Assert.Equal(1, e.GetProperty("version").GetInt32());
            events.Add(e);
        }
        return events;
    }

    private static string Type(JsonElement e) => e.GetProperty("type").GetString()!;

    [Fact]
    public void A_finding_is_a_line_with_its_record_and_the_build_closes_with_the_count()
    {
        var root = Package(("lyric.toml", AppManifest), ("src/main.lyr", """
            fn main(): void {
                let x: int = "text";
            }
            """));
        var (exit, output, error) = RunRaw("build", "-C", root, "--json");
        Assert.Equal(1, exit);
        Assert.Equal("", error);
        var events = Events(output);
        Assert.Equal(["diagnostic", "summary"], events.Select(Type));
        var d = events[0];
        Assert.Equal("LYR-SEM0001", d.GetProperty("code").GetString());
        Assert.Equal("error", d.GetProperty("severity").GetString());
        Assert.Equal("cannot assign 'string' to 'int'", d.GetProperty("message").GetString());
        var span = d.GetProperty("span");
        Assert.Equal(Path.Combine("src", "main.lyr"), span.GetProperty("file").GetString());
        Assert.Equal(2, span.GetProperty("start").GetProperty("line").GetInt32());
        Assert.Equal(18, span.GetProperty("start").GetProperty("column").GetInt32());
        Assert.StartsWith("error[LYR-SEM0001]: cannot assign 'string' to 'int'\n --> ", d.GetProperty("rendered").GetString());
        Assert.Equal(1, events[1].GetProperty("errors").GetInt32());
    }

    [Fact]
    public void A_clean_build_names_its_binary()
    {
        var root = Package(("lyric.toml", AppManifest), ("src/main.lyr", "fn main(): void { }\n"));
        var (exit, output, _) = RunRaw("build", "-C", root, "--json");
        Assert.Equal(0, exit);
        var events = Events(output);
        Assert.Equal(["artifact", "summary"], events.Select(Type));
        Assert.Equal("binary", events[0].GetProperty("kind").GetString());
        Assert.Equal("app", events[0].GetProperty("name").GetString());
        Assert.True(File.Exists(events[0].GetProperty("path").GetString()));
        Assert.Equal(0, events[1].GetProperty("errors").GetInt32());
    }

    [Fact]
    public void Each_test_is_a_line_what_it_prints_an_output_line_and_the_run_counts()
    {
        var root = Package(("lyric.toml", AppManifest), ("src/main.lyr", "fn main(): void { }\n"), ("tests/t.lyr", """
            import std.test { assertEq };
            import std.io { println };

            @Test
            fn holds(): void {
                assertEq(1 + 1, 2);
            }

            @Test
            fn breaks(): void {
                println("from the test");
                assertEq(1 + 1, 3);
            }

            @Test { skip = "not today" }
            fn later(): void { }
            """));
        var (exit, output, error) = RunRaw("test", "-C", root, "--json");
        Assert.Equal(1, exit);
        Assert.Equal("", error);
        var events = Events(output);
        var tests = events.Where(e => Type(e) == "test").ToDictionary(e => e.GetProperty("name").GetString()!);
        Assert.Equal("passed", tests["app.tests.t.holds"].GetProperty("outcome").GetString());
        var broken = tests["app.tests.t.breaks"];
        Assert.Equal("failed", broken.GetProperty("outcome").GetString());
        Assert.Equal("LYR-RT0008", broken.GetProperty("panic").GetString());
        Assert.StartsWith("panic [LYR-RT0008]: 1 + 1 = 2, expected 3", broken.GetProperty("message").GetString());
        Assert.Equal("not today", tests["app.tests.t.later"].GetProperty("message").GetString());
        var printed = Assert.Single(events, e => Type(e) == "output");
        Assert.Equal("from the test", printed.GetProperty("text").GetString());
        var count = Assert.Single(events, e => Type(e) == "tests");
        Assert.Equal((1, 1, 1), (count.GetProperty("passed").GetInt32(), count.GetProperty("failed").GetInt32(), count.GetProperty("skipped").GetInt32()));
        Assert.Equal("summary", Type(events[^1]));
    }

    [Fact]
    public void The_drivers_own_findings_come_as_events_a_manifests_at_its_line()
    {
        var (exit, output, error) = RunRaw("build", "--json", "--bogus");
        Assert.Equal(2, exit);
        Assert.Equal("", error);
        var usage = Events(output)[0];
        Assert.Equal("LYR-CLI0003", usage.GetProperty("code").GetString());
        Assert.Equal(JsonValueKind.Null, usage.GetProperty("span").ValueKind);
        Assert.Equal("help", usage.GetProperty("notes")[0].GetProperty("kind").GetString());

        var root = Package(("lyric.toml", "[package]\nname = \"app\"\nversion = 0.1\n"), ("src/main.lyr", "fn main(): void { }\n"));
        var (_, manifest, _) = RunRaw("build", "-C", root, "--json");
        var refused = Events(manifest)[0];
        Assert.Equal("LYR-PKG0001", refused.GetProperty("code").GetString());
        Assert.Equal("lyric.toml", refused.GetProperty("span").GetProperty("file").GetString());
        Assert.Equal(3, refused.GetProperty("span").GetProperty("start").GetProperty("line").GetInt32());
    }

    [Fact]
    public void A_manifests_refusal_as_text_shows_its_line()
    {
        var root = Package(("lyric.toml", "[package]\nname = \"app\"\nversion = 0.1\n"), ("src/main.lyr", "fn main(): void { }\n"));
        var (exit, _, error) = RunRaw("build", "-C", root);
        Assert.Equal(1, exit);
        Assert.Contains("error[LYR-PKG0001]: '0.1': floats, dates and times are not part of a manifest\n --> lyric.toml:3:11\n", error);
        Assert.Contains("3 | version = 0.1\n", error);
    }

    [Fact]
    public void Run_counts_before_its_program_and_metadata_keeps_its_own_json()
    {
        var root = Package(("lyric.toml", AppManifest), ("src/main.lyr", "fn main(): void { }\n"));
        var (exit, output, _) = RunRaw("run", "-C", root, "--json");
        Assert.Equal(0, exit);
        Assert.Equal(["artifact", "summary"], Events(output).Select(Type));
        var (_, metadata, _) = RunRaw("metadata", "-C", root, "--json");
        Assert.StartsWith("{\"version\":1,", metadata);
    }
}
