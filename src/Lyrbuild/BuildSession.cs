using Lyric.Compiler;
using Lyric.Core;
using Lyric.Embedding;

namespace Lyric.Cli.Build;

/// <summary>One artifact as the script handed it over, its paths made absolute against the
/// project directory. <see cref="Kind"/> is one of the three names <c>std.build</c> spells its
/// enum with, which is the seam between the model in Lyric and the compiles here.</summary>
public sealed record Declared(string Kind, string Name, string Entry, string Output,
    bool Optimize, bool SourceMap, bool DebugInfo, bool DenyWarnings)
{
    public const string Executable = "executable";
    public const string Library = "library";
    public const string Packed = "packed";
}

/// <summary>An option the script asked about, for <c>--help</c>.</summary>
public sealed record DeclaredOption(string Name, string Help, bool IsFlag);

/// <summary>
/// The toolchain's half of <c>std.build</c>: the natives the script's questions are bound to,
/// the artifacts it hands over once <c>build</c> has returned, and the compiles.
///
/// <para>The MODEL is not here. Which artifacts exist, what their defaults are, where one lands
/// without an explicit output, what a profile changes on it — that is Lyric, in
/// <c>stdlib/std/build.lyr</c>, where the standard library's own tests can reach it. What is
/// here is what only the toolchain can answer: the profile table, the command line, the
/// compiler, the packer.</para>
///
/// <para>NOTHING IS COMPILED WHILE THE SCRIPT'S <c>build</c> RUNS. The entry the runner writes
/// around the script calls <c>build()</c> first and <c>finish()</c> after, and <c>finish</c>
/// is what hands the artifacts over and calls <see cref="CompileAll"/> — so an option set on
/// the line after the declaration still applies, and a file the script generates is finished
/// before anything reads it.</para>
/// </summary>
internal sealed class BuildSession(
    string directory, ProjectFile? project, string? stdlibRoot, Profile profile,
    IReadOnlyDictionary<string, string> defines, IReadOnlyList<string> only,
    TextWriter output, TextWriter error)
{
    private readonly List<Declared> _artifacts = [];
    private readonly List<DeclaredOption> _options = [];

    /// <summary>What the script asked about, in the order it asked. First declaration wins for
    /// a name asked twice.</summary>
    public IReadOnlyList<DeclaredOption> Options => _options;

    /// <summary>Set when the COMMAND LINE was wrong rather than the script or its sources: a
    /// <c>-D</c> no option answers, an <c>--only</c> no artifact carries. The exit code is then
    /// the usage one.</summary>
    public bool UsageError { get; private set; }

    /// <summary>Binds the natives <c>std.build</c> declares, under the names its declarations
    /// produce. A build script's VM is the only place they are ever bound.</summary>
    public void Register(LangVm vm)
    {
        vm.RegisterNative("std.build.selectedProfile", () => profile.Name);

        vm.RegisterNative("std.build.profileField",
            (string name, string field) => ProfileField(name, field));

        vm.RegisterNative("std.build.declareOption", (string name, string help, bool isFlag) =>
        {
            if (_options.All(o => o.Name != name)) _options.Add(new DeclaredOption(name, help, isFlag));
        });

        vm.RegisterNative("std.build.hasOption", (string name) => defines.ContainsKey(name));

        vm.RegisterNative("std.build.optionValue",
            (string name) => defines.TryGetValue(name, out var value) ? value : "");

        // A relative path means the same thing everywhere in the script: against the project
        // directory, never against where the build was started from.
        vm.RegisterNative("std.build.declare", (string kind, string name, string entry,
            string artifactOutput, bool optimize, bool sourceMap, bool debugInfo, bool denyWarnings) =>
        {
            _artifacts.Add(new Declared(kind, name,
                Path.GetFullPath(Path.Combine(directory, entry)),
                artifactOutput.Length == 0 ? "" : Path.GetFullPath(Path.Combine(directory, artifactOutput)),
                optimize, sourceMap, debugInfo, denyWarnings));
        });

        vm.RegisterNative("std.build.compileAll", () => (long)CompileAll());
    }

    /// <summary>The profile table, one field at a time: the script's <c>Profile</c> is a struct
    /// of scalars, and scalars are what cross the boundary.</summary>
    private static bool ProfileField(string name, string field)
    {
        var named = Profile.Named(name)
                    ?? throw new InvalidOperationException(
                        $"unknown profile '{name}' (debug or release)");

        return field switch
        {
            "optimize" => named.Optimize,
            "sourceMap" => named.SourceMap,
            "debugInfo" => named.DebugInfo,
            "denyWarnings" => named.DenyWarnings,
            _ => throw new InvalidOperationException($"unknown profile field '{field}'"),
        };
    }

    /// <summary>
    /// Compiles what the script handed over and returns how many artifacts failed.
    ///
    /// <para>The command line is checked against the script FIRST, here, because only now is
    /// the script's word complete: a <c>-D</c> nobody asked about is a typo that would
    /// otherwise do nothing in silence, and an <c>--only</c> nobody declared would build
    /// nothing and report success.</para>
    /// </summary>
    public int CompileAll()
    {
        foreach (var define in defines.Keys)
        {
            if (_options.Any(o => o.Name == define)) continue;
            UsageError = true;
            Fail(CliDiagnostics.UnknownCommand,
                $"-D {define}: {Program.FileName} declares no such option{Listing(_options.Select(o => o.Name))}");
        }

        foreach (var name in only)
        {
            if (_artifacts.Any(a => a.Name == name)) continue;
            UsageError = true;
            Fail(CliDiagnostics.UnknownArtifact,
                $"--only {name}: {Program.FileName} declares no artifact of that name{Listing(_artifacts.Select(a => a.Name).Distinct())}");
        }

        if (UsageError) return 1;

        if (_artifacts.Count == 0)
        {
            // Silence would look like success and leave nothing behind, which is the one
            // outcome a build must never have.
            Fail(CliDiagnostics.BuildScriptFailed,
                $"{Program.FileName}: 'build' declared nothing to compile");
            return 1;
        }

        var selected = only.Count == 0
            ? _artifacts
            : _artifacts.Where(a => only.Contains(a.Name)).ToList();

        var failed = 0;
        foreach (var artifact in selected)
            if (!Build(artifact)) failed++;
        return failed;
    }

    private static string Listing(IEnumerable<string> names)
    {
        var list = names.ToList();
        return list.Count == 0 ? "" : $" — it declares {string.Join(", ", list)}";
    }

    /// <summary>One artifact, whole: a library is checked, a program is compiled and written,
    /// a packed program is compiled and packed. Diagnostics are rendered as they come; the
    /// answer is whether the artifact is there.</summary>
    public bool Build(Declared artifact)
    {
        if (artifact.Kind == Declared.Library) return CheckLibrary(artifact);

        if (!File.Exists(artifact.Entry))
        {
            Fail(CliDiagnostics.FileUnreadable, $"{artifact.Entry}: no such file");
            return false;
        }

        var result = SourceCompiler.Compile(artifact.Entry, OptionsFor(artifact));
        result.Diagnostics.RenderText(error);
        if (!result.Ok || result.Bytes is null) return false;
        if (!WarningsAllowed(artifact, result)) return false;

        return artifact.Kind == Declared.Packed
            ? Pack(artifact, result.Bytes)
            : Write(artifact.Output, result.Bytes);
    }

    /// <summary>Every module under the root as one compilation, the way the language server
    /// checks a project: nothing is written, because a library has no consumer for a module of
    /// its own — it is source somebody imports.</summary>
    private bool CheckLibrary(Declared artifact)
    {
        var root = artifact.Entry;
        if (!Directory.Exists(root))
        {
            Fail(CliDiagnostics.FileUnreadable, $"{root}: no such directory");
            return false;
        }

        var files = Directory.GetFiles(root, "*.lyr", SearchOption.AllDirectories);
        Array.Sort(files, StringComparer.Ordinal);
        if (files.Length == 0)
        {
            Fail(CliDiagnostics.FileUnreadable, $"{artifact.Name}: no .lyr file under {root}");
            return false;
        }

        var roots = files
            .Select(file => ScriptSource.FromDisk(file, ScriptSource.ModuleNameUnder(root, file)))
            .ToList();

        var result = SourceCompiler.CheckProject(roots, OptionsFor(artifact) with { SourceRoot = root });
        result.Diagnostics.RenderText(error);
        if (!result.Ok) return false;
        if (!WarningsAllowed(artifact, result)) return false;

        output.WriteLine($"{artifact.Name}: {files.Length} {(files.Length == 1 ? "module" : "modules")} checked");
        return true;
    }

    /// <summary>The <c>denyWarnings</c> gate, AFTER the render: the warnings keep their severity
    /// in the output, and this is what carries the artifact's policy into the answer.</summary>
    private bool WarningsAllowed(Declared artifact, CompileResult result)
    {
        var warnings = result.Diagnostics.WarningCount;
        if (warnings == 0 || !artifact.DenyWarnings) return true;

        Fail(CliDiagnostics.WarningsDenied, warnings == 1
            ? $"{artifact.Name}: 1 warning denied — the artifact asks for none"
            : $"{artifact.Name}: {warnings} warnings denied — the artifact asks for none");
        return false;
    }

    private CompilerOptions OptionsFor(Declared artifact) => new()
    {
        StdlibRoot = stdlibRoot,
        SourceRoot = project?.SourceRoot,
        NativeRoots = project?.NativeRoots,
        DependencyRoots = project?.Dependencies,
        Optimize = artifact.Optimize,
        SourceMap = artifact.SourceMap,
        DebugInfo = artifact.DebugInfo,
    };

    private bool Write(string path, byte[] bytes)
    {
        try
        {
            var parent = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            File.WriteAllBytes(path, bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Fail(CliDiagnostics.OutputUnwritable, $"{path}: {ex.Message}");
            return false;
        }

        output.WriteLine($"{path}: {bytes.Length} bytes");
        return true;
    }

    /// <summary>
    /// Through the <c>lyrpack</c> PROCESS, as the driver packs: one packer, not a second copy of
    /// it in this binary. The module goes through a temporary file, because the packer takes
    /// files and nobody's deliverable is a module whose only purpose was to be packed.
    /// </summary>
    private bool Pack(Declared artifact, byte[] bytes)
    {
        var packer = Tool.Packer.Resolve(null);
        if (!File.Exists(packer))
        {
            Fail(CliDiagnostics.VmNotFound,
                $"{Tool.Packer.Name} not found: {packer} (set {Tool.Packer.EnvironmentVariable})");
            return false;
        }

        // The platform's suffix on an output that names none: the script derived
        // 'out/<profile>/<name>' without knowing where it runs, and only a script that spells
        // an extension itself has decided against one.
        var executable = artifact.Output;
        if (OperatingSystem.IsWindows() && Path.GetExtension(executable).Length == 0)
            executable += ".exe";

        var module = Path.Combine(Path.GetTempPath(), $"{artifact.Name}-{Guid.NewGuid():N}.lyrbc");
        try
        {
            File.WriteAllBytes(module, bytes);
            var parent = Path.GetDirectoryName(executable);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

            return Tool.Run(packer, [module, "-o", executable], error) == ExitCodes.Success;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Fail(CliDiagnostics.OutputUnwritable, $"{executable}: {ex.Message}");
            return false;
        }
        finally
        {
            try { File.Delete(module); } catch (IOException) { /* nothing further to do */ }
        }
    }

    private void Fail(string code, string message) =>
        CliDiagnostics.Fail(error, code, message, ExitCodes.Failure);
}
