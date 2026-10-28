using System.Diagnostics;

namespace Lyric5.Toolchain;

public enum CCompilerKind { Zig, Clang, Gcc }

/// <summary>
/// A C compiler the toolchain can drive. <see cref="Locate()"/> takes
/// <c>LYRIC_CC</c> when set, otherwise the zig <c>lyric toolchain install zig</c>
/// put under the home, otherwise the first of <c>zig</c>, <c>clang</c> and
/// <c>gcc</c> on the PATH that answers for its version.
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

    /// <summary>The architecture the compiler's binary is built for, as a triple names it —
    /// <c>x86_64</c>, <c>aarch64</c> —; <c>null</c> where its header says neither, for a script, or
    /// for a binary that carries both. zig cc without <c>-target</c> builds for its own
    /// architecture, which is not this process's under Rosetta or Windows' emulation.</summary>
    public string? Architecture => ExecutableArchitecture(Path);

    public static CCompiler? Locate() => Locate(Environment.GetEnvironmentVariable("LYRIC_CC"));

    /// <summary>The architecture in an executable's header: ELF's machine, Mach-O's CPU type, or
    /// PE's machine.</summary>
    public static string? ExecutableArchitecture(string path)
    {
        try
        {
            using var file = File.OpenRead(path);
            Span<byte> head = stackalloc byte[64];
            if (file.ReadAtLeast(head, 64, throwOnEndOfStream: false) < 64) return null;
            var magic = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(head);
            if (magic == 0x464c457f)
                return System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(head[18..]) switch { 0x3e => "x86_64", 0xb7 => "aarch64", _ => null };
            if (magic == 0xfeedfacf)
                return System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(head[4..]) switch { 0x01000007 => "x86_64", 0x0100000c => "aarch64", _ => null };
            if (head[0] == 'M' && head[1] == 'Z')
            {
                file.Position = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(head[60..]);
                Span<byte> pe = stackalloc byte[6];
                if (file.ReadAtLeast(pe, 6, throwOnEndOfStream: false) < 6 || pe[0] != 'P' || pe[1] != 'E') return null;
                return System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(pe[4..]) switch { 0x8664 => "x86_64", 0xaa64 => "aarch64", _ => null };
            }
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static CCompiler? Locate(string? chosen) => Locate(chosen, LyricHome.Root);

    /// <summary>The compiler <paramref name="chosen"/> names; else the zig installed under the home
    /// <paramref name="home"/> in the version this toolchain pins; else the first of <c>zig</c>,
    /// <c>clang</c> and <c>gcc</c> on the PATH.</summary>
    public static CCompiler? Locate(string? chosen, string home)
    {
        if (!string.IsNullOrWhiteSpace(chosen)) return Probe(chosen, KindOf(chosen));
        return InstalledZig(home) ?? Probe("zig", CCompilerKind.Zig) ?? Probe("clang", CCompilerKind.Clang) ?? Probe("gcc", CCompilerKind.Gcc);
    }

    /// <summary>A specific kind, for profiles that require one (the sanitizer profiles need clang).</summary>
    public static CCompiler? Locate(CCompilerKind kind) => kind switch
    {
        CCompilerKind.Zig => InstalledZig(LyricHome.Root) ?? Probe("zig", kind),
        CCompilerKind.Clang => Probe("clang", kind),
        _ => Probe("gcc", kind),
    };

    /// <summary>The pinned zig under the home <paramref name="home"/>, where it is installed and answers.</summary>
    public static CCompiler? InstalledZig(string home) =>
        LyricHome.InstalledZig(home) is { } binary ? Probe(binary, CCompilerKind.Zig) : null;

    private static CCompilerKind KindOf(string command)
    {
        var name = System.IO.Path.GetFileNameWithoutExtension(command);
        if (name.StartsWith("zig", StringComparison.Ordinal)) return CCompilerKind.Zig;
        if (name.StartsWith("clang", StringComparison.Ordinal)) return CCompilerKind.Clang;
        return CCompilerKind.Gcc;
    }

    /// <summary>The compiler's own version line, asked once per installed binary: the answer is
    /// memoized under the binary's path, size and time, because starting `zig version` costs
    /// 25 ms — half of what a warm build may cost in all.</summary>
    private static CCompiler? Probe(string command, CCompilerKind kind)
    {
        var path = FindOnPath(command);
        if (path is null) return null;
        var version = kind == CCompilerKind.Zig ? "version" : "--version";
        var first = UserCache.Memo("compiler-version", UserCache.FileStamp(path), () =>
        {
            var result = ProcessRunner.TryRun(path, [version], TimeSpan.FromSeconds(10));
            return result is null || result.ExitCode != 0 ? "" : result.Stdout.Split('\n', 2)[0].Trim();
        });
        return first.Length == 0 ? null : new CCompiler(kind, path, first);
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

    /// <summary>What a captured stream is read as: UTF-8, what a Lyric program writes and
    /// the C toolchain too. Left to the default, Windows read it in the console's code page, and
    /// a program's 'é' came back as two characters of CP437.</summary>
    private static readonly System.Text.Encoding Utf8 = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Runs <paramref name="file"/> with an empty standard input — what it reads ends at
    /// once, so nothing waits on the caller's — and answers its exit code and both streams. A
    /// process that outlives <paramref name="timeout"/> is killed, and the exception carries the
    /// end of what it wrote.</summary>
    public static Result Run(string file, IEnumerable<string> arguments, TimeSpan timeout, string? workingDirectory = null,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var info = new ProcessStartInfo(file)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Utf8,
            StandardErrorEncoding = Utf8,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        if (environment is not null)
        {
            foreach (var (key, value) in environment) info.Environment[key] = value;
        }

        using var process = Process.Start(info) ?? throw new InvalidOperationException($"could not start {file}");
        process.StandardInput.Close();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeout))
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            process.WaitForExit(TimeSpan.FromSeconds(15));
            throw new TimeoutException($"{file} did not finish within {timeout.TotalSeconds:0} s"
                + Tail("stdout", stdout) + Tail("stderr", stderr));
        }
        process.WaitForExit();
        return new Result(process.ExitCode, stdout.Result, stderr.Result);
    }

    /// <summary>The last lines a killed process wrote to one stream, where the stream has closed.</summary>
    private static string Tail(string name, Task<string> stream)
    {
        if (!stream.Wait(TimeSpan.FromSeconds(15)) || stream.Result.Length == 0) return "";
        var lines = stream.Result.TrimEnd().Split('\n');
        return $"\n--- {name}, its last lines:\n" + string.Join('\n', lines[Math.Max(0, lines.Length - 20)..]);
    }

    /// <summary>Runs a program on the caller's own console — its output is the user's, not the
    /// toolchain's — and answers its exit code. <c>lyric run</c>.</summary>
    public static int RunInherited(string file, IEnumerable<string> arguments, string? workingDirectory = null)
    {
        var info = new ProcessStartInfo(file)
        {
            UseShellExecute = false,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new InvalidOperationException($"could not start {file}");
        process.WaitForExit();
        return process.ExitCode;
    }

    /// <summary>Runs a program and passes on what it writes as it writes it, line by line —
    /// to writers rather than to the console, so a caller that redirects the console gets it —;
    /// answers its exit code. <c>lyric test</c>'s program.</summary>
    public static int Stream(string file, IEnumerable<string> arguments, TextWriter output, TextWriter error,
        string? workingDirectory = null) =>
        Stream(file, arguments, output.WriteLine, error.WriteLine, workingDirectory);

    /// <summary>As the other, each line to a callback of its own — one at a time, never two at once —
    /// and with variables added to the program's environment (what <c>lyric test --json</c> passes).</summary>
    public static int Stream(string file, IEnumerable<string> arguments, Action<string> output, Action<string> error,
        string? workingDirectory = null, IReadOnlyDictionary<string, string>? environment = null)
    {
        var info = new ProcessStartInfo(file)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Utf8,
            StandardErrorEncoding = Utf8,
            UseShellExecute = false,
        };
        if (workingDirectory is not null) info.WorkingDirectory = workingDirectory;
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        foreach (var (name, value) in environment ?? new Dictionary<string, string>()) info.Environment[name] = value;
        using var process = new Process { StartInfo = info };
        var gate = new object();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (gate) output(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (gate) error(e.Data); };
        if (!process.Start()) throw new InvalidOperationException($"could not start {file}");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();
        return process.ExitCode;
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
