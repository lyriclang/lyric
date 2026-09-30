using Lyric5.Toolchain;
using System.Reflection;

namespace Lyric5;

/// <summary>
/// The Lyric 5 driver. At this stage it answers one question — what toolchain is this and what
/// will it build with — because every later verb needs the same answer first.
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "--version" or "-V" or "version")
        {
            PrintVersion();
            return 0;
        }

        if (args[0] is "--help" or "-h" or "help")
        {
            Console.Out.WriteLine("usage: lyric5 [--version | --help]");
            Console.Out.WriteLine("The Lyric 5 command line, in development. Only the self-report exists yet.");
            return 0;
        }

        Console.Error.WriteLine($"error[LYR-CLI0003]: unknown argument '{args[0]}'");
        Console.Error.WriteLine("  = help: lyric5 --help lists what exists");
        return 2;
    }

    private static void PrintVersion()
    {
        Console.Out.WriteLine($"lyric {DisplayVersion()}");
        Console.Out.WriteLine("  edition     5");
        Console.Out.WriteLine($"  c compiler  {DescribeCompiler(CCompiler.Locate())}");
    }

    /// <summary>
    /// The version a person reads: the informational version, which a dev build stamps as
    /// <c>5.0.0-dev.&lt;date&gt;+&lt;sha&gt;</c>. A local build carries the SDK's full source revision
    /// after the '+'; it is shortened to the seven characters a commit is usually named by.
    /// </summary>
    private static string DisplayVersion()
    {
        var stamped = typeof(Program).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "5.0.0";
        var plus = stamped.IndexOf('+');
        if (plus < 0) return stamped;
        var revision = stamped[(plus + 1)..];
        return revision.Length > 7 && revision.All(char.IsAsciiHexDigit)
            ? stamped[..(plus + 1)] + revision[..7]
            : stamped;
    }

    private static string DescribeCompiler(CCompiler? compiler) =>
        compiler?.ToString() ?? "none found — install zig (recommended), clang or gcc";
}
