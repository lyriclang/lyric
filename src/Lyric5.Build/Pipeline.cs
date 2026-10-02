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
        // A package's modules by their paths, a single file with std alone (07 M1, 11 C8).
        var options = new CompilerOptions
        {
            StdlibRoot = StdlibRoot, PackageRoots = project.PackageRoots,
            PackageDependencies = project.DeclaredDependencies,
        };
        var result = SourceCompiler.Lower(ScriptSource.FromDisk(project.Source, project.Module), options);
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
        output.Write(what == "ir" ? IrPrinter.Dump(result.Ir!) : CEmitter.Join(CEmitter.Emit(result.Ir!, result.Sources, StdlibRoot)));
        return 0;
    }

    /// <summary>The binary's path, or the exit code of the failure.</summary>
    public static (int Exit, string? Executable) Build(BuildRequest request, TextWriter error)
    {
        var project = request.Project;
        Directory.CreateDirectory(project.CacheDir);

        // The C, keyed by the sources, the toolchain and the compiler that emits it: the same
        // program emits the same C, so the front end runs only for a program that changed. The
        // module's unit is named by that key; a generic instance's unit by its own content (01 C3),
        // under 'units/', where nothing else changes — so its object survives every edit that
        // leaves the instance alone. The list of units is written last: its presence says the C is
        // complete.
        var key = Key(SourcesKey(project), request.ToolchainVersion, CEmitter.Version, CompilerIdentity);
        var manifest = Path.Combine(project.CacheDir, $"{project.BinaryName}-{key}.units");
        if (!File.Exists(manifest))
        {
            var result = Compile(project, error);
            if (result is null) return (1, null);
            var paths = new List<string>();
            foreach (var unit in CEmitter.Emit(result.Ir!, result.Sources, StdlibRoot))
            {
                var path = unit.Instance is null
                    ? Path.Combine(project.CacheDir, $"{project.BinaryName}-{key}.c")
                    : Path.Combine(project.CacheDir, "units", $"inst-{Key(unit.Text)}.c");
                WriteOnce(path, unit.Text);
                paths.Add(path);
            }
            WriteOnce(manifest, string.Join("\n", paths) + "\n");
        }
        var sources = File.ReadAllLines(manifest).Where(line => line.Length > 0).ToList();

        try
        {
            var archive = RuntimeArchive.For(request.Compiler, request.Target, request.Profile, request.ToolchainVersion);
            var build = new CBuild(request.Compiler, request.Target, request.Profile, project.CacheDir);
            var runtimeRoot = RuntimeArchive.SourceRoot()!;
            var units = sources.Select(path => new CUnit(path, [], [RuntimeLayout.IncludeDir(runtimeRoot)])).ToList();
            var objects = build.Compile(units);
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

    /// <summary>The compiler, by the identity of its assemblies — the front end's and the
    /// emitter's. A deterministic build gives the same bytes the same id, so a compiler that
    /// changed keys new C even where no version string moved — a change to the lowering alone
    /// once reused the old C — and an unchanged one finds what it emitted before.</summary>
    private static readonly string CompilerIdentity = string.Join(",",
        typeof(SourceCompiler).Assembly.ManifestModule.ModuleVersionId,
        typeof(CEmitter).Assembly.ManifestModule.ModuleVersionId);

    /// <summary>What the C depends on besides the compiler: the entry module, every source file of
    /// the package — its manifest too — or the single file, and every file of the standard library,
    /// each by its path and its content. Keyed by the entry file alone, a change to an imported
    /// module — or to the standard library beside a toolchain whose version did not move — reused
    /// the old C.</summary>
    private static string SourcesKey(Project project)
    {
        var parts = new List<string> { project.Module };
        if (project.Graph is { } graph)
        {
            // Every package of the graph: a changed dependency builds anew as a changed module does.
            foreach (var (name, manifest) in graph.Packages.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                parts.Add(name);
                parts.Add(File.ReadAllText(manifest.File));
                AddTree(parts, manifest.SourceRoot);
            }
        }
        else
        {
            parts.Add(File.ReadAllText(project.Source));
        }
        AddTree(parts, StdlibRoot);
        return Key([.. parts]);
    }

    /// <summary>
    /// A library built by itself (design/v5/spec/07 B6): every module under its <c>src/</c> through
    /// the front end and the lowering, one compilation — a check; there is no program to build. Exit
    /// 0 when it holds, 1 after reporting.
    /// </summary>
    public static int CheckLibrary(LibraryException library, TextWriter error)
    {
        var manifest = library.Manifest;
        var roots = Directory.Exists(manifest.SourceRoot)
            ? Directory.EnumerateFiles(manifest.SourceRoot, "*.lyr", SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal)
                .Select(file => ScriptSource.FromDisk(file, Project.ModulePathOf(manifest, file)!))
                .ToList()
            : [];
        var options = new CompilerOptions
        {
            StdlibRoot = StdlibRoot, PackageRoots = library.Graph.SourceRoots,
            PackageDependencies = library.Graph.Declared,
        };
        var result = SourceCompiler.CheckProject(roots, options);
        return result.Render(error) ? 0 : 1;
    }

    private static void AddTree(List<string> parts, string root)
    {
        if (!Directory.Exists(root)) return;
        foreach (var file in Directory.EnumerateFiles(root, "*.lyr", SearchOption.AllDirectories)
                     .Select(file => (Relative: Path.GetRelativePath(root, file).Replace('\\', '/'), Full: file))
                     .OrderBy(file => file.Relative, StringComparer.Ordinal))
        {
            parts.Add(file.Relative);
            parts.Add(File.ReadAllText(file.Full));
        }
    }

    /// <summary>A content-named file is written once: a second writer of the same path writes
    /// the same bytes, and the first to move wins.</summary>
    private static void WriteOnce(string path, string text)
    {
        if (File.Exists(path)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var partial = $"{path}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(partial, text);
        try { File.Move(partial, path, overwrite: false); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException && File.Exists(path)) { File.Delete(partial); }
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
