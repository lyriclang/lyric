using System.Diagnostics;
using Lyric.Lsp.Dap;
using Lyric5.Build;
using Lyric5.Toolchain;

namespace Lyric5;

public static partial class Program
{
    /// <summary><c>dap</c>: the debug adapter, speaking DAP on the standard input and output and
    /// lldb-dap behind it; a launch builds its program in the debug profile first.</summary>
    private static int Dap(string[] args)
    {
        if (Verbs.Read(Verbs.Find("dap")!, args, Unknown, out var unread) is not { } parsed) return unread;
        if (parsed.Words.Count > 0) return Unknown($"'dap' takes no file, got '{parsed.Words[0]}'");
        if (parsed.Rest is not null) return Unknown("unknown option '--' for 'dap'");

        var adapter = new DapAdapter(Console.OpenStandardInput(), Console.OpenStandardOutput(), new DapAdapterOptions(BuildForDebug)
        {
            LldbScript = Path.Combine(AppContext.BaseDirectory, "lldb", "lyric_lldb.py"),
        });
        return adapter.RunAsync().GetAwaiter().GetResult();
    }

    /// <summary>The program a launch names — a package's directory, or a file — built by this
    /// binary in the debug profile, its findings as the build says them; with a test's name, the
    /// package's tests whose names hold it, as <c>test --no-run</c> builds them. The standard output
    /// is the protocol's, so the build runs as a process of its own.</summary>
    private static DebugBuild BuildForDebug(string program, string? test)
    {
        var full = Path.GetFullPath(program);
        var self = Environment.ProcessPath!;
        var start = new ProcessStartInfo(self) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        // under 'dotnet lyric5.dll' the binary is the host: the program is the assembly
        if (Path.GetFileNameWithoutExtension(self) == "dotnet") start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "lyric5.dll"));
        start.ArgumentList.Add(test is null ? "build" : "test");
        start.ArgumentList.Add("--profile");
        start.ArgumentList.Add("debug");
        if (test is not null)
        {
            start.ArgumentList.Add("--no-run");
            start.ArgumentList.Add("-f");
            start.ArgumentList.Add(test);
        }
        if (Directory.Exists(full) || test is not null)
        {
            start.ArgumentList.Add("-C");
            start.ArgumentList.Add(Directory.Exists(full) ? full : Path.GetDirectoryName(full)!);
        }
        else start.ArgumentList.Add(full);
        using var build = Process.Start(start)!;
        build.StandardInput.Close();
        var output = build.StandardOutput.ReadToEndAsync();
        var said = build.StandardError.ReadToEnd();
        build.WaitForExit();
        if (test is not null)
        {
            // the last line is the binary's path, what comes before it the build's findings
            var lines = output.Result.TrimEnd('\n', '\r').Split('\n');
            var binary = lines[^1].Trim();
            said = string.Join('\n', lines[..^1]) + said;
            if (build.ExitCode != 0 || !File.Exists(binary)) return new DebugBuild(null, output.Result + said, []);
            var tested = Project.TestsOf(Directory.Exists(full) ? full : Path.GetDirectoryName(full)!, write: false);
            return new DebugBuild(binary, said, tested is null ? [] : Pipeline.SourcePlaces(tested));
        }
        said = output.Result + said;
        if (build.ExitCode != 0) return new DebugBuild(null, said, []);

        var profile = BuildProfile.Of(Profile.Debug);
        var project = Directory.Exists(full) ? Project.ProgramsOf(full)?.FirstOrDefault() : Project.ForFile(full);
        return new DebugBuild(project?.Executable(profile, Target.Host), said, project is null ? [] : Pipeline.SourcePlaces(project));
    }
}
