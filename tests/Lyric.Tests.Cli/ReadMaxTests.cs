namespace Lyric.Tests.Cli;

/// <summary>
/// A non-positive <c>max</c> on the <c>readSome</c> family panics rather than reading a byte
/// nobody asked for.
///
/// <para>The natives clamp <c>max</c> into <c>[1, 1MB]</c>, so before 4.2.1 asking for zero
/// bytes returned ONE. A caller computing a remaining-byte count — the length-prefixed frame
/// reader these modules exist for — then over-consumed its stream silently, and on UDP the rest
/// of the datagram was discarded with it. There is no honest value to answer instead: an empty
/// array already means EOF, and <c>null</c> already means a failure with a reason.</para>
///
/// <para>The pin lives here rather than in <c>stdlib-tests</c> because a panic ends the program,
/// which the lyrtest runner cannot survive.</para>
/// </summary>
public sealed class ReadMaxTests
{
    private const int PanicExit = 101;

    private static void PanicsWith(string program, string expected)
    {
        using var source = Toolchain.Temp(".lyr");
        File.WriteAllText(source.Path, program);

        var result = Toolchain.Lyric("run", source.Path);

        Assert.True(result.ExitCode == PanicExit,
            $"expected a panic, got exit {result.ExitCode}:\n{result.Out}\n{result.Err}");
        Assert.Contains(expected, result.Out + result.Err);
    }

    [Fact]
    public void A_file_read_of_zero_bytes_panics()
    {
        PanicsWith("""
            import std.io.stream as stream;
            import std.io.file { tempDir, writeText };
            import std.io.path { joinPath };
            import std.task { Wait, spawn, run };

            fn readNothing(path: string): Coroutine<Wait> {
                let f = stream.open(path)!;
                let got = stream.readSome(f, 0);
                stream.close(f);
            }

            fn main(): int {
                let path = joinPath(tempDir(), "lyric-readmax-file.bin");
                let written = writeText(path, "abcdef");
                spawn(readNothing(path));
                run();
                return 0;
            }
            """, "std.io.stream.readSome: max must be positive");
    }

    [Fact]
    public void A_datagram_read_of_zero_bytes_panics()
    {
        // The worst of the four: a datagram is CUT to max and its remainder dropped, so the
        // clamp turned "read nothing" into "keep one byte, throw the packet away".
        PanicsWith("""
            import std.io.net as net;
            import std.task { Wait, spawn, run };

            fn receiveNothing(): Coroutine<Wait> {
                let s = net.bind("127.0.0.1", 0)!;
                let got = net.receiveFrom(s, 0);
                net.close(s);
            }

            fn main(): int {
                spawn(receiveNothing());
                run();
                return 0;
            }
            """, "std.io.net.receiveFrom: max must be positive");
    }

    [Fact]
    public void A_negative_max_panics_too()
    {
        PanicsWith("""
            import std.io.stream as stream;
            import std.io.file { tempDir, writeText };
            import std.io.path { joinPath };
            import std.task { Wait, spawn, run };

            fn readNegative(path: string): Coroutine<Wait> {
                let f = stream.open(path)!;
                let got = stream.readSome(f, 0 - 5);
                stream.close(f);
            }

            fn main(): int {
                let path = joinPath(tempDir(), "lyric-readmax-negative.bin");
                let written = writeText(path, "abcdef");
                spawn(readNegative(path));
                run();
                return 0;
            }
            """, "std.io.stream.readSome: max must be positive");
    }

    [Fact]
    public void A_max_of_one_still_reads_one_byte()
    {
        // The boundary the guard must NOT move: one is a legitimate request.
        using var source = Toolchain.Temp(".lyr");
        File.WriteAllText(source.Path, """
            import std.io.stream as stream;
            import std.io.file { tempDir, writeText };
            import std.io.path { joinPath };
            import std.io.console { println };
            import std.task { Wait, spawn, run };

            fn readOne(path: string): Coroutine<Wait> {
                let f = stream.open(path)!;
                let got = stream.readSome(f, 1)!;
                println(got.length);
                stream.close(f);
            }

            fn main(): int {
                let path = joinPath(tempDir(), "lyric-readmax-one.bin");
                let written = writeText(path, "abcdef");
                spawn(readOne(path));
                run();
                return 0;
            }
            """);

        var result = Toolchain.Lyric("run", source.Path);

        Assert.True(result.ExitCode == 0, $"{result.Out}\n{result.Err}");
        Assert.Contains("1", result.Out);
    }
}
