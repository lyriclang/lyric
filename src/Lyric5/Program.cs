using System.Diagnostics;
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
        Console.Out.WriteLine($"  c compiler  {DescribeCompiler(FindCCompiler())}");
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

    private sealed record CCompiler(string Name, string Path, string Version);

    /// <summary>
    /// The C compiler a build would use (design/v5/spec/01 C8): <c>LYRIC_CC</c> when set, otherwise
    /// the first of <c>zig cc</c>, clang and gcc that answers.
    /// </summary>
    private static CCompiler? FindCCompiler()
    {
        var chosen = Environment.GetEnvironmentVariable("LYRIC_CC");
        if (!string.IsNullOrWhiteSpace(chosen))
        {
            return Probe(chosen, chosen.EndsWith("zig", StringComparison.Ordinal) ? "version" : "--version");
        }

        return Probe("zig", "version") ?? Probe("clang", "--version") ?? Probe("gcc", "--version");
    }

    private static CCompiler? Probe(string command, string versionArgument)
    {
        var path = Locate(command);
        if (path is null) return null;
        try
        {
            using var process = Process.Start(new ProcessStartInfo(path, versionArgument)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });
            if (process is null) return null;
            var first = process.StandardOutput.ReadLine() ?? "";
            if (!process.WaitForExit(5000))
            {
                process.Kill();
                return null;
            }
            return process.ExitCode == 0 ? new CCompiler(command, path, first.Trim()) : null;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }

    private static string? Locate(string command)
    {
        if (Path.IsPathRooted(command)) return File.Exists(command) ? command : null;

        var extensions = OperatingSystem.IsWindows() ? new[] { ".exe", ".cmd", "" } : new[] { "" };
        var directories = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (var directory in directories)
        {
            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(directory, command + extension);
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }

    private static string DescribeCompiler(CCompiler? compiler) =>
        compiler is null
            ? "none found — install zig (recommended), clang or gcc"
            : $"{compiler.Name} {compiler.Version} ({compiler.Path})";
}
