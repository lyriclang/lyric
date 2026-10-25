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
            LldbInit = Path.Combine(AppContext.BaseDirectory, "lyric.lldbinit"),
        });
        return adapter.RunAsync().GetAwaiter().GetResult();
    }

    /// <summary>The program a launch names — a package's directory, or a file — built by this
    /// binary in the debug profile, its findings as the build says them; the standard output is
    /// the protocol's, so the build runs as a process of its own.</summary>
    private static (string? Executable, string Said) BuildForDebug(string program)
    {
        var full = Path.GetFullPath(program);
        var self = Environment.ProcessPath!;
        var start = new ProcessStartInfo(self) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        // under 'dotnet lyric5.dll' the binary is the host: the program is the assembly
        if (Path.GetFileNameWithoutExtension(self) == "dotnet") start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "lyric5.dll"));
        start.ArgumentList.Add("build");
        start.ArgumentList.Add("--profile");
        start.ArgumentList.Add("debug");
        if (Directory.Exists(full)) { start.ArgumentList.Add("-C"); start.ArgumentList.Add(full); }
        else start.ArgumentList.Add(full);
        using var build = Process.Start(start)!;
        build.StandardInput.Close();
        var output = build.StandardOutput.ReadToEndAsync();
        var said = build.StandardError.ReadToEnd();
        build.WaitForExit();
        said = output.Result + said;
        if (build.ExitCode != 0) return (null, said);

        var profile = BuildProfile.Of(Profile.Debug);
        var project = Directory.Exists(full) ? Project.ProgramsOf(full)?.FirstOrDefault() : Project.ForFile(full);
        return (project?.Executable(profile, Target.Host), said);
    }
}
