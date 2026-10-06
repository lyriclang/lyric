using System.Security.Cryptography;
using System.Text;

namespace Lyric5.Toolchain;

/// <summary>
/// One C translation unit: its source, the defines and include directories it needs, whether the
/// profile's sanitizers instrument it, and anything else it needs on the command line.
/// </summary>
public sealed record CUnit(
    string Source,
    IReadOnlyList<string> Defines,
    IReadOnlyList<string> IncludeDirs,
    bool Instrument = true,
    IReadOnlyList<string>? ExtraFlags = null);

public sealed class CBuildException(string message) : Exception(message);

/// <summary>
/// Turns C units into objects, objects into an archive, and archives into an executable, for one
/// compiler, target and profile. Objects are cached by content: the key covers the compiler
/// identity, the target, every flag, the source and every header in the unit's include
/// directories — a changed header rebuilds every unit that could see it.
/// </summary>
public sealed class CBuild
{
    private readonly string _cacheDir;

    public CCompiler Compiler { get; }
    public Target Target { get; }
    public BuildProfile Profile { get; }

    public CBuild(CCompiler compiler, Target target, BuildProfile profile, string cacheDir)
    {
        if (profile.RequiresClang && compiler.Kind != CCompilerKind.Clang)
        {
            throw new CBuildException($"the {profile.Name} profile needs clang; {compiler.Name} ships no sanitizer runtime for it");
        }
        if (!target.IsHost && !compiler.CanCrossCompile)
        {
            throw new CBuildException($"{compiler.Name} cannot build for {target} without a sysroot; cross builds use zig cc");
        }
        Compiler = compiler;
        Target = target;
        Profile = profile;
        _cacheDir = Path.Combine(cacheDir, target.Triple, profile.Name);
        Directory.CreateDirectory(_cacheDir);
    }

    /// <summary>The command-line prefix that selects the compiler and the target.</summary>
    private List<string> Driver()
    {
        var driver = new List<string>();
        if (Compiler.Kind == CCompilerKind.Zig)
        {
            driver.Add("cc");
            if (!Target.IsHost) { driver.Add("-target"); driver.Add(Target.Triple); }
        }
        return driver;
    }

    public IReadOnlyList<string> FlagsFor(CUnit unit)
    {
        // No contraction of a * b + c into a fused multiply-add: deterministic IEEE arithmetic is
        // the standard (01 L10), and a fused result differs in the last bit from the two roundings
        // the source spells. Fast-math and contraction come back only by an explicit flag.
        var flags = new List<string> { "-std=c11", "-ffunction-sections", "-fdata-sections", "-ffp-contract=off" };
        // The compilation directory a debugger joins a relative path to is '.', not wherever the
        // build happens to run (11 W2 P6). gcc has no flag for it; it maps the working directory
        // like any other prefix — which puts the directory into its cache keys.
        flags.Add(Compiler.Kind == CCompilerKind.Gcc
            ? $"-fdebug-prefix-map={Path.TrimEndingDirectorySeparator(Environment.CurrentDirectory)}=."
            : "-fdebug-compilation-dir=.");
        // zig cc turns UBSan on by itself at -O0, with zig's own runtime and report format. The
        // sanitizers belong to their profiles (01 C7), which compile with clang; debug is plain.
        if (Compiler.Kind == CCompilerKind.Zig) flags.Add("-fno-sanitize=undefined");
        // LTO is bitcode in the objects; zig 0.16 links Mach-O with a linker of its own that takes
        // none ("LTO requires using LLD"), so a build for macOS goes without (the review's B11).
        flags.AddRange(Target.Os == TargetOs.MacOs ? Profile.Codegen.Where(f => !IsLto(f)) : Profile.Codegen);
        if (unit.Instrument) flags.AddRange(Profile.Instrumentation);
        foreach (var define in unit.Defines) flags.Add("-D" + define);
        foreach (var include in unit.IncludeDirs) { flags.Add("-I"); flags.Add(include); }
        if (unit.ExtraFlags is not null) flags.AddRange(unit.ExtraFlags);
        return flags;
    }

    public string CacheKey(CUnit unit)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Add(string text) { sha.AppendData(Encoding.UTF8.GetBytes(text)); sha.AppendData([0]); }

        Add(Compiler.Identity);
        Add(Target.Triple);
        Add(Profile.Name);
        foreach (var flag in FlagsFor(unit)) Add(flag);
        Add(Path.GetFileName(unit.Source));
        sha.AppendData(File.ReadAllBytes(unit.Source));
        foreach (var dir in unit.IncludeDirs.Append(Path.GetDirectoryName(unit.Source)!).Distinct())
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var header in Directory.EnumerateFiles(dir, "*.h", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                Add(Path.GetRelativePath(dir, header).Replace('\\', '/'));
                sha.AppendData(File.ReadAllBytes(header));
            }
        }
        return Convert.ToHexStringLower(sha.GetHashAndReset())[..32];
    }

    /// <summary>Compiles every unit (in parallel), reusing cached objects; returns their paths in order.</summary>
    public IReadOnlyList<string> Compile(IReadOnlyList<CUnit> units)
    {
        var objects = new string[units.Count];
        var errors = new System.Collections.Concurrent.ConcurrentBag<string>();
        Parallel.For(0, units.Count, i =>
        {
            try { objects[i] = CompileOne(units[i]); }
            catch (CBuildException exception) { errors.Add(exception.Message); }
        });
        if (!errors.IsEmpty) throw new CBuildException(string.Join("\n\n", errors));
        return objects;
    }

    public string CompileOne(CUnit unit)
    {
        var name = Path.GetFileNameWithoutExtension(unit.Source);
        var target = Path.Combine(_cacheDir, "obj", $"{name}-{CacheKey(unit)}.o");
        if (File.Exists(target)) return target;

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        // Unique per compilation, not per process: builds in one process (parallel tests, a
        // compiler building two targets) compile the same unit at the same time.
        var partial = $"{target}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        var arguments = Driver();
        arguments.AddRange(FlagsFor(unit));
        arguments.AddRange(["-c", unit.Source, "-o", partial]);
        var result = ProcessRunner.Run(Compiler.Path, arguments, TimeSpan.FromMinutes(5));
        if (result.ExitCode != 0)
        {
            File.Delete(partial);
            throw new CBuildException($"compiling {unit.Source} for {Target} ({Profile.Name}) failed:\n{Command(arguments)}\n{result.Stderr}{result.Stdout}");
        }
        Publish(partial, target);
        return target;
    }

    /// <summary>
    /// Moves a finished file to its content-keyed name. A file under that name has the same
    /// content, so it is never replaced: when another builder got there first, whichever file won
    /// stays — and nobody has to replace a file a linker or a compiler may have open, which
    /// Windows refuses ("access denied", "used by another process").
    /// </summary>
    private static void Publish(string partial, string target)
    {
        try
        {
            File.Move(partial, target, overwrite: false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException && File.Exists(target))
        {
            File.Delete(partial);
        }
    }

    /// <summary>
    /// Archives objects. The objects are content-addressed, so their names are the archive's key,
    /// and the archive is named by that key beside <paramref name="output"/>
    /// (<c>liblyr-&lt;key&gt;.a</c>): an archive of exactly these objects is reused, a new one is
    /// written under a temporary name and published — a builder never replaces an archive another
    /// one may be linking against. Answers the archive's path.
    /// </summary>
    public string Archive(IReadOnlyList<string> objects, string output)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        var key = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join('\n', objects.Select(Path.GetFileName)))));
        var keyed = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!,
            $"{Path.GetFileNameWithoutExtension(output)}-{key[..16]}{Path.GetExtension(output)}");
        if (File.Exists(keyed)) return keyed;

        var partial = $"{keyed}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        string tool;
        var arguments = new List<string>();
        if (Compiler.Kind == CCompilerKind.Zig)
        {
            tool = Compiler.Path;
            arguments.Add("ar");
        }
        else
        {
            tool = CCompiler.FindOnPath("llvm-ar") ?? CCompiler.FindOnPath("ar")
                ?? throw new CBuildException("no archiver found (llvm-ar or ar)");
        }
        arguments.Add("rcs");
        arguments.Add(partial);
        arguments.AddRange(objects);
        var result = ProcessRunner.Run(tool, arguments, TimeSpan.FromMinutes(2));
        if (result.ExitCode != 0)
        {
            File.Delete(partial);
            throw new CBuildException($"archiving {keyed} failed:\n{result.Stderr}");
        }
        Publish(partial, keyed);
        return keyed;
    }

    /// <summary>
    /// Links objects and archives into an executable. Sections nobody references are dropped
    /// (design/v5/spec/01 L11); the sanitizer runtime comes along in the sanitizer profiles. The
    /// runtime's backtraces need an unwinder — zig cc links a C program without one, so its own
    /// libunwind comes along on Linux; clang and gcc bring libgcc's; macOS has one in libSystem —
    /// and on Windows DbgHelp, loaded by the runtime at the first trace, not imported. The same
    /// inputs give the same image (11 W2 P6): a PE image's time stamps and PDB GUID come from its
    /// own bytes, and a Mach-O image carries no debug map — on a macOS host its .dSYM does, beside
    /// it, which is where libbacktrace reads line tables from (<see cref="Reproducible"/>).
    /// </summary>
    /// <param name="libraries">Libraries by name (<c>-l</c>), after every input that needs them.</param>
    public string LinkExecutable(IReadOnlyList<string> inputs, string output, IReadOnlyList<string>? libraries = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        Link(inputs, output, libraries, debugMap: false);
        if (Target.Os == TargetOs.Windows)
        {
            try { Reproducible.NormalizePe(output, Path.ChangeExtension(output, ".pdb")); }
            catch (InvalidDataException failure) { throw new CBuildException($"{output}: {failure.Message}"); }
        }
        if (Target.Os == TargetOs.MacOs && OperatingSystem.IsMacOS()) GatherDebugInfo(inputs, output, libraries);
        return output;
    }

    /// <summary>One link. A Mach-O image keeps its debug map only when asked: <c>-S</c> leaves out
    /// what would name every object by its path and time.</summary>
    private void Link(IReadOnlyList<string> inputs, string output, IReadOnlyList<string>? libraries, bool debugMap)
    {
        var arguments = Driver();
        // No LTO flag at the link: lld runs LTO over bitcode inputs by itself, and with the flag
        // zig builds its own C library as bitcode too — for Windows its mingw part, whose own
        // symbols then went missing (frexpf, wmemcpy, __DENORM …; the review's B11).
        arguments.AddRange(Profile.Codegen.Where(f => !IsLto(f)));
        arguments.AddRange(Profile.Instrumentation);
        arguments.AddRange(inputs);
        foreach (var library in libraries ?? []) arguments.Add("-l" + library);
        arguments.AddRange(["-o", output]);
        arguments.AddRange(Target.Os switch
        {
            TargetOs.MacOs => ["-Wl,-dead_strip"],
            // The thread's stack as large as the main task's (runtime/include/lyr/task.h,
            // LYR_MAIN_TASK_STACK): what main has as a task it has on the thread (M6-26). zig's
            // default for a Windows image is 16 MiB (MSVC's 1 MiB); a POSIX thread's comes from
            // the system's limit, 8 MiB as a rule.
            TargetOs.Windows => ["-Wl,--gc-sections", "-Wl,--stack," + MainThreadStack],
            _ => ["-Wl,--gc-sections", "-lpthread", "-lm"],
        });
        if (Target.Os == TargetOs.MacOs && !debugMap) arguments.Add("-Wl,-S");
        if (Compiler.Kind == CCompilerKind.Zig) arguments.Add("-fno-sanitize=undefined");
        if (Target.Os == TargetOs.Linux && Compiler.Kind == CCompilerKind.Zig) arguments.Add("-lunwind");
        var result = RunLong(arguments, Path.GetDirectoryName(Path.GetFullPath(output))!);
        if (result.ExitCode != 0)
        {
            throw new CBuildException($"linking {output} for {Target} ({Profile.Name}) failed:\n{Command(arguments)}\n{result.Stderr}{result.Stdout}");
        }
    }

    /// <summary>The main task's stack, in bytes (<c>LYR_MAIN_TASK_STACK</c>), asked of the linker for
    /// the thread's stack where the linker sets it.</summary>
    private const string MainThreadStack = "8388608";

    private static bool IsLto(string flag) => flag.StartsWith("-flto", StringComparison.Ordinal);

    /// <summary>The longest command line passed as it is. Windows takes 32767 characters at most, and
    /// a program of many modules links more object files than that holds ("The filename or
    /// extension is too long").</summary>
    private const int LongestCommandLine = 8000;

    /// <summary>
    /// Runs the compiler with <paramref name="arguments"/> — past <see cref="LongestCommandLine"/>
    /// characters through a response file in <paramref name="directory"/>, which zig cc, clang and
    /// gcc read: the driver's own arguments stay on the command line, the rest go into the file, each
    /// quoted, a backslash doubled — the GNU reading takes it for an escape and gives one back; the
    /// Windows reading keeps two, a separator twice, which Windows takes as one.
    /// </summary>
    private ProcessRunner.Result RunLong(List<string> arguments, string directory)
    {
        if (Command(arguments).Length <= LongestCommandLine)
            return ProcessRunner.Run(Compiler.Path, arguments, TimeSpan.FromMinutes(5));
        var driver = Driver().Count;
        var responseFile = Path.Combine(directory, $".link-{Guid.NewGuid():N}.rsp");
        File.WriteAllLines(responseFile, arguments.Skip(driver)
            .Select(a => "\"" + a.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""));
        try
        {
            return ProcessRunner.Run(Compiler.Path, [.. arguments.Take(driver), "@" + responseFile], TimeSpan.FromMinutes(5));
        }
        finally
        {
            File.Delete(responseFile);
        }
    }

    /// <summary>
    /// The .dSYM beside a macOS image, best effort: without dsymutil a backtrace still names
    /// functions, without lines. dsymutil follows a debug map, which the image does not carry: a
    /// second link has one, under the same name in a directory of its own, and its dSYM describes
    /// the image when the two hold the same bytes at the same addresses. It then takes the image's
    /// UUID, by which libbacktrace pairs the two.
    /// </summary>
    private void GatherDebugInfo(IReadOnlyList<string> inputs, string executable, IReadOnlyList<string>? libraries)
    {
        var dsymutil = CCompiler.FindOnPath("dsymutil");
        if (dsymutil is null) return;
        var dsym = executable + ".dSYM";
        if (Directory.Exists(dsym)) Directory.Delete(dsym, recursive: true);
        var scratch = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(executable))!, $".debugmap-{Guid.NewGuid():N}");
        var mapped = Path.Combine(scratch, Path.GetFileName(executable));
        try
        {
            Directory.CreateDirectory(scratch);
            Link(inputs, mapped, libraries, debugMap: true);
            if (!Reproducible.SameSegments(mapped, executable)) return;
            ProcessRunner.Run(dsymutil, [mapped, "-o", dsym], TimeSpan.FromMinutes(2));
            var dwarf = Path.Combine(dsym, "Contents", "Resources", "DWARF", Path.GetFileName(executable));
            if (File.Exists(dwarf) && Reproducible.MachOUuid(executable) is { } uuid) Reproducible.SetMachOUuid(dwarf, uuid);
        }
        catch (Exception failure) when (failure is CBuildException or TimeoutException or InvalidDataException)
        {
            // The image stands without a dSYM.
        }
        finally
        {
            if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true);
        }
    }

    private string Command(IEnumerable<string> arguments) =>
        Compiler.Path + " " + string.Join(' ', arguments.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
}
