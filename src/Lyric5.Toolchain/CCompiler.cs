using System.Diagnostics;

namespace Lyric5.Toolchain;

public enum CCompilerKind { Zig, Clang, Gcc }

/// <summary>
/// A C compiler the toolchain can drive. <see cref="Locate"/> applies the order of
/// design/v5/spec/01 C8: <c>LYRIC_CC</c> when set, otherwise the first of <c>zig</c>, <c>clang</c>
/// and <c>gcc</c> that answers for its version.
/// </summary>
public sealed record CCompiler(CCompilerKind Kind, string Path, string Version)
{
    public string Name => Kind switch
    {
        CCompilerKind.Zig => "zig",
        CCompilerKind.Clang => "clang",
        _ => "gcc",
    };

    /// <summary>Only zig cc cross-compiles without a sysroot; clang and gcc build for the host.</summary>
    public bool CanCrossCompile => Kind == CCompilerKind.Zig;

    /// <summary>What goes into every cache key: a different compiler binary is a different build.</summary>
    public string Identity => $"{Kind}|{Path}|{Version}";

    public static CCompiler? Locate() => Locate(Environment.GetEnvironmentVariable("LYRIC_CC"));

    public static CCompiler? Locate(string? chosen)
    {
        if (!string.IsNullOrWhiteSpace(chosen)) return Probe(chosen, KindOf(chosen));
        return Probe("zig", CCompilerKind.Zig) ?? Probe("clang", CCompilerKind.Clang) ?? Probe("gcc", CCompilerKind.Gcc);
    }

    /// <summary>A specific kind, for profiles that require one (the sanitizer profiles need clang).</summary>
    public static CCompiler? Locate(CCompilerKind kind) => kind switch
    {
        CCompilerKind.Zig => Probe("zig", kind),
        CCompilerKind.Clang => Probe("clang", kind),
        _ => Probe("gcc", kind),
    };

    private static CCompilerKind KindOf(string command)
    {
        var name = System.IO.Path.GetFileNameWithoutExtension(command);
        if (name.StartsWith("zig", StringComparison.Ordinal)) return CCompilerKind.Zig;
        if (name.StartsWith("clang", StringComparison.Ordinal)) return CCompilerKind.Clang;
        return CCompilerKind.Gcc;
    }

    private static CCompiler? Probe(string command, CCompilerKind kind)
    {
        var path = FindOnPath(command);
        if (path is null) return null;
        var version = kind == CCompilerKind.Zig ? "version" : "--version";
        var result = ProcessRunner.TryRun(path, [version], TimeSpan.FromSeconds(10));
        if (result is null || result.ExitCode != 0) return null;
        var first = result.Stdout.Split('\n', 2)[0].Trim();
        return new CCompiler(kind, path, first);
    }

    /// <summary>Resolves a command against PATH, the way a shell would.</summary>
    public static string? FindOnPath(string command)
    {
        if (System.IO.Path.IsPathRooted(command)) return File.Exists(command) ? command : null;

        string[] extensions = OperatingSystem.IsWindows() ? [".exe", ".cmd", ""] : [""];
        var directories = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(System.IO.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (var directory in directories)
        {
            foreach (var extension in extensions)
            {
                var candidate = System.IO.Path.Combine(directory, command + extension);
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }

    public override string ToString() => $"{Name} {Version} ({Path})";
}

/// <summary>Runs a process to completion and captures both streams.</summary>
public static class ProcessRunner
{
    public sealed record Result(int ExitCode, string Stdout, string Stderr);

    public static Result Run(string file, IEnumerable<string> arguments, TimeSpan timeout, string? workingDirectory = null,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var info = new ProcessStartInfo(file)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        if (environment is not null)
        {
            foreach (var (key, value) in environment) info.Environment[key] = value;
        }

        using var process = Process.Start(info) ?? throw new InvalidOperationException($"could not start {file}");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeout))
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw new TimeoutException($"{file} did not finish within {timeout.TotalSeconds:0} s");
        }
        process.WaitForExit();
        return new Result(process.ExitCode, stdout.Result, stderr.Result);
    }

    public static Result? TryRun(string file, IEnumerable<string> arguments, TimeSpan timeout)
    {
        try { return Run(file, arguments, timeout); }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or TimeoutException)
        {
            return null;
        }
    }
}
