using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Lyric5.Build;
using Lyric5.Toolchain;

namespace Lyric5;

public static partial class Program
{
    // --- the switch to the toolchain a command asks for -----------------------------------------

    /// <summary>The verbs that never hand a command on: they read no package, or manage the
    /// toolchains themselves.</summary>
    private static readonly HashSet<string> Unswitched = ["toolchain", "help", "explain", "completions", "new", "init", "-h", "--help"];

    /// <summary>
    /// Before the verb: the toolchain <see cref="ToolchainSwitch"/> decides on runs the command —
    /// this one, by returning <c>null</c>, or an installed one, whose exit is returned. Where the
    /// package asks for a toolchain none is, a terminal is offered the least stable one that is;
    /// elsewhere the verb runs and refuses the package (<c>LYR-PKG0009</c>). The verb is read from
    /// <paramref name="args"/>, the options every verb takes already taken out; an installed toolchain
    /// is given <paramref name="given"/>, the command line as it came.
    /// </summary>
    private static int? Switch(string[] args, string[] given)
    {
        if (args.Length == 0 || Unswitched.Contains(args[0])) return null;
        var home = ToolchainHome.ForUser();
        try
        {
            switch (ToolchainSwitch.Decide(DisplayVersion(), Environment.GetEnvironmentVariable, home, ManifestOf(args)))
            {
                case SwitchDecision.Installed installed:
                    return HandOn(home.BinaryOf(installed.Version), given);
                case SwitchDecision.Missing missing when args[0] is not ("version" or "--version" or "-V")
                                                        && !Console.IsInputRedirected && !Console.IsErrorRedirected:
                    return ToolchainSwitch.Offer(Fetcher(), home, missing, DisplayVersion(), Console.Error, Console.ReadLine) is { } version
                        ? HandOn(home.BinaryOf(version), given)
                        : null;
                default:
                    return null;
            }
        }
        catch (ToolchainException refused)
        {
            Say.Error(Console.Error, refused.Code, refused.Message, refused.Help);
            return 2;
        }
    }

    /// <summary>The <c>lyric.toml</c> of the package the command runs in: from the directory
    /// <c>-C</c> names before a <c>--</c>, else from the working directory.</summary>
    private static string? ManifestOf(string[] args)
    {
        var dash = Array.IndexOf(args, "--");
        var options = dash < 0 ? args : args[..dash];
        var at = Array.LastIndexOf(options, "-C");
        var dir = at >= 0 && at + 1 < options.Length ? Path.GetFullPath(options[at + 1]) : Directory.GetCurrentDirectory();
        return Directory.Exists(dir) ? Project.FindManifest(dir) : null;
    }

    /// <summary>
    /// The command run by <paramref name="binary"/>, with <c>LYRIC_TOOLCHAIN_FROM</c> set so that it
    /// does not hand it on again: in place of this process on Unix (<c>execve</c>), on Windows as a
    /// child that inherits the console, whose exit is this one's — a Ctrl+C reaches the child and
    /// leaves the parent waiting for it.
    /// </summary>
    private static int HandOn(string binary, string[] args)
    {
        Console.Out.Flush();
        Console.Error.Flush();
        var from = DisplayVersion();
        if (OperatingSystem.IsWindows())
        {
            var start = new ProcessStartInfo(binary) { UseShellExecute = false };
            foreach (var arg in args) start.ArgumentList.Add(arg);
            start.Environment[ToolchainSwitch.FromVariable] = from;
            ConsoleCancelEventHandler wait = (_, e) => e.Cancel = true;
            Console.CancelKeyPress += wait;
            try
            {
                using var child = Process.Start(start)!;
                child.WaitForExit();
                return child.ExitCode;
            }
            catch (Win32Exception e)
            {
                Say.Error(Console.Error, "LYR-CLI0025", $"{binary} cannot be run: {e.Message}");
                return 2;
            }
            finally
            {
                Console.CancelKeyPress -= wait;
            }
        }

        var argv = new IntPtr[args.Length + 2];
        var envp = new List<IntPtr>();
        try
        {
            argv[0] = Marshal.StringToCoTaskMemUTF8(binary);
            for (var i = 0; i < args.Length; i++) argv[i + 1] = Marshal.StringToCoTaskMemUTF8(args[i]);
            foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
                if ((string)entry.Key != ToolchainSwitch.FromVariable)
                    envp.Add(Marshal.StringToCoTaskMemUTF8($"{entry.Key}={entry.Value}"));
            envp.Add(Marshal.StringToCoTaskMemUTF8($"{ToolchainSwitch.FromVariable}={from}"));
            envp.Add(IntPtr.Zero);
            Native.execve(binary, argv, [.. envp]);
            // execve returns only where it failed
            var error = Marshal.GetLastPInvokeError();
            Say.Error(Console.Error, "LYR-CLI0025", $"{binary} cannot be run: {Marshal.GetPInvokeErrorMessage(error)}");
            return 2;
        }
        finally
        {
            foreach (var p in argv) Marshal.FreeCoTaskMem(p);
            foreach (var p in envp) Marshal.FreeCoTaskMem(p);
        }
    }

    private static partial class Native
    {
        [LibraryImport("libc", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
        internal static partial int execve(string path, IntPtr[] argv, IntPtr[] envp);
    }
}
