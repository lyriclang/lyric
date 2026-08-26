namespace Lyric.Tests.Cli;

/// <summary>
/// Standard input as a PIPE — the shape the CLI goal names.
///
/// <para><c>readAll</c> has existed since M8b, but nothing ever ran it against an actual pipe:
/// the v4 basket listed it as missing because no test said otherwise. These tests are that
/// answer — a filter program run through <c>lyric run</c> with its input piped in, which is
/// the only way the console natives meet a stream that is not a terminal.</para>
/// </summary>
public sealed class StdinTests
{
    private static string RunPiped(string program, string input)
    {
        using var source = Toolchain.Temp(".lyr");
        File.WriteAllText(source.Path, program);

        var result = Toolchain.RunWithInput(Toolchain.LyricPath, ["run", source.Path], input);

        Assert.True(result.ExitCode == 0, $"the filter failed:\n{result.Out}\n{result.Err}");
        return result.Out.ReplaceLineEndings("\n");
    }

    [Fact]
    public void ReadAll_hands_a_filter_the_whole_pipe()
    {
        // Markers around the echo, so a swallowed trailing line (no final newline here on
        // purpose) or an added one would show.
        var output = RunPiped("""
            import std.io.console as console;

            fn main(): int {
                console.print("[");
                console.print(console.readAll());
                console.println("]");
                return 0;
            }
            """, "one\ntwo\nthree");

        Assert.Equal("[one\ntwo\nthree]\n", output);
    }

    [Fact]
    public void An_empty_pipe_reads_as_the_empty_string()
    {
        // The documented contract: nothing and empty mean the same to readAll — EOF is not an
        // error and not a null, unlike readLine, where it is a state.
        var output = RunPiped("""
            import std.io.console as console;

            fn main(): int {
                console.print("[");
                console.print(console.readAll());
                console.println("]");
                return 0;
            }
            """, "");

        Assert.Equal("[]\n", output);
    }

    [Fact]
    public void Lines_walk_a_pipe_like_a_filter()
    {
        // The idiomatic filter shape over a real pipe: the last line counts even without a
        // final newline.
        var output = RunPiped("""
            import std.io.console as console;

            fn main(): int {
                var count = 0;
                for (line in console.lines()) {
                    count = count + 1;
                }
                console.println(count);
                return 0;
            }
            """, "one\ntwo\nthree");

        Assert.Equal("3\n", output);
    }
}
