using System.Security.Cryptography;
using System.Text;
using Lyric.Compiler;
using Lyric.Ir;
using Lyric5.Compiler;
using Lyric5.Toolchain;
using Profile = Lyric5.Toolchain.Profile;

namespace Lyric5.Build;

/// <summary>What a build is asked for: the project, the profile, the target, the C compiler, and
/// the toolchain version every cache key carries.</summary>
public sealed record BuildRequest(Project Project, Profile Profile, Target Target, CCompiler Compiler, string ToolchainVersion);

/// <summary>
/// From a <c>.lyr</c> to a binary (design/v5/spec/13, M2): the front end, the subset gate, the C
/// emitter, the runtime archive, the C compiler, the linker — each step behind a content key, so
/// an unchanged program compiles nothing twice (01 L7, 11 C9). Diagnostics go to <c>error</c>;
/// the exit codes are the driver's (11 C5): 1 for a program the compiler refuses, 2 for a build
/// that could not run at all.
/// </summary>
public static class Pipeline
{
    /// <summary>Where <c>lyric5</c> finds the seed of the Lyric 5 standard library: beside itself.</summary>
    public static string StdlibRoot => Path.Combine(AppContext.BaseDirectory, "stdlib5");

    /// <summary>The front end and the gate on a project's source: the IR, or <c>null</c> after reporting.</summary>
    public static CompileResult? Compile(Project project, TextWriter error)
    {
        var options = new CompilerOptions { StdlibRoot = StdlibRoot };
        var result = SourceCompiler.Lower(project.Source, options);
        if (!result.Render(error) || result.Ir is null) return null;
        if (SubsetGate.Check(result.Ir, result.Diagnostics)) return result;
        result.Diagnostics.RenderText(error);
        return null;
    }

    /// <summary><c>--emit ir</c> and <c>--emit c</c>: the text on <c>output</c>.</summary>
    public static int Emit(Project project, string what, TextWriter output, TextWriter error)
    {
        var result = Compile(project, error);
        if (result is null) return 1;
        output.Write(what == "ir" ? IrPrinter.Dump(result.Ir!) : CEmitter.Emit(result.Ir!, result.Sources));
        return 0;
    }

    /// <summary>The binary's path, or the exit code of the failure.</summary>
    public static (int Exit, string? Executable) Build(BuildRequest request, TextWriter error)
    {
        var project = request.Project;
        Directory.CreateDirectory(project.CacheDir);

        // The C, keyed by the source, the toolchain and the emitter: the same program emits the
        // same C, so the front end runs only for a program that changed.
        var source = File.ReadAllText(project.Source);
        var key = Key(source, request.ToolchainVersion, CEmitter.Version);
        var cFile = Path.Combine(project.CacheDir, $"{project.Name}-{key}.c");
        if (!File.Exists(cFile))
        {
            var result = Compile(project, error);
            if (result is null) return (1, null);
            var partial = $"{cFile}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
            File.WriteAllText(partial, CEmitter.Emit(result.Ir!, result.Sources));
            try { File.Move(partial, cFile, overwrite: false); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException && File.Exists(cFile)) { File.Delete(partial); }
        }

        try
        {
            var archive = RuntimeArchive.For(request.Compiler, request.Target, request.Profile, request.ToolchainVersion);
            var build = new CBuild(request.Compiler, request.Target, request.Profile, project.CacheDir);
            var runtimeRoot = RuntimeArchive.SourceRoot()!;
            var unit = new CUnit(cFile, [], [RuntimeLayout.IncludeDir(runtimeRoot)]);
            var objects = build.Compile([unit]);
            var executable = project.Executable(request.Profile, request.Target);
            if (!UpToDate(executable, [.. objects, archive])) build.LinkExecutable([.. objects, archive], executable);
            return (0, executable);
        }
        catch (CBuildException failure)
        {
            error.WriteLine($"error[LYR-BLD0001]: {failure.Message}");
            return (2, null);
        }
    }

    /// <summary>A binary newer than every content-keyed input it was linked from is that link.</summary>
    private static bool UpToDate(string executable, IEnumerable<string> inputs)
    {
        if (!File.Exists(executable)) return false;
        var linked = File.GetLastWriteTimeUtc(executable);
        return inputs.All(input => File.GetLastWriteTimeUtc(input) <= linked);
    }

    private static string Key(params string[] parts)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var part in parts)
        {
            sha.AppendData(Encoding.UTF8.GetBytes(part));
            sha.AppendData([0]);
        }
        return System.Convert.ToHexStringLower(sha.GetHashAndReset())[..16];
    }
}
