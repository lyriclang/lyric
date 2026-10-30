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
    IReadOnlyList<string>? ExtraFlags = null)
{
    /// <summary>What the count of the units compiled calls it: the file's name, unless the build
    /// names it — a program's unit lies in the cache under its key. No part of the key.</summary>
    public string? Shown { get; init; }
}

public sealed class CBuildException(string message) : Exception(message)
{
    /// <summary>For a link that failed: what the linker wrote, which the build reads for the
    /// findings it knows (spec 15 §5.2 rule 12); <c>null</c> for every other failure.</summary>
    public string? LinkerOutput { get; init; }
}

/// <summary>Where a link finds libraries beyond the linker's own directories, and whether the
/// binary searches its own directory for shared libraries first — where files lie beside it. With
/// them, what the link takes after the libraries by name: the flags <c>pkg-config</c> gave that are
/// no directory and no library, and the macOS frameworks.</summary>
public sealed record LinkPaths(IReadOnlyList<string> SearchDirs, bool OwnDirFirst,
    IReadOnlyList<string>? Args = null, IReadOnlyList<string>? Frameworks = null)
{
    public static readonly LinkPaths None = new([], false);
}

/// <summary>
/// Turns C units into objects, objects into an archive, and archives into an executable, for one
/// compiler, target and profile. Objects are cached by content: the key covers the compiler
/// identity, the target, every flag, the source and every header in the unit's include
/// directories — a changed header rebuilds every unit that could see it.
/// </summary>
public sealed partial class CBuild
{
    private readonly string _cacheDir;

    public CCompiler Compiler { get; }
    public Target Target { get; }
    public BuildProfile Profile { get; }

    /// <summary>Whether zig is told the target although it is the host's: for a <paramref name="portable"/>
    /// build — an archive another machine links, which must not carry this machine's CPU features —,
    /// and where zig is built for another architecture than this process and would build for its own.</summary>
    private readonly bool _namesTarget;

    public CBuild(CCompiler compiler, Target target, BuildProfile profile, string cacheDir, bool portable = false)
    {
        _namesTarget = !target.IsHost || compiler.Kind == CCompilerKind.Zig
            && (portable || compiler.Architecture is { } arch && arch != target.Arch);
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
            if (_namesTarget) { driver.Add("-target"); driver.Add(Target.Triple); }
        }
        return driver;
    }

    public IReadOnlyList<string> FlagsFor(CUnit unit)
    {
        // No contraction of a * b + c into a fused multiply-add: deterministic IEEE arithmetic is
        // the standard, and a fused result differs in the last bit from the two roundings
        // the source spells. Fast-math and contraction come back only by an explicit flag.
        var flags = new List<string> { "-std=c11", "-ffunction-sections", "-fdata-sections", "-ffp-contract=off" };
        // The compilation directory a debugger joins a relative path to is '.', not wherever the
        // build happens to run. gcc has no flag for it; it maps the working directory
        // like any other prefix — which puts the directory into its cache keys.
        flags.Add(Compiler.Kind == CCompilerKind.Gcc
            ? $"-fdebug-prefix-map={Path.TrimEndingDirectorySeparator(Environment.CurrentDirectory)}=."
            : "-fdebug-compilation-dir=.");
        // zig cc turns UBSan on by itself at -O0, with zig's own runtime and report format. The
        // sanitizers belong to their profiles, which compile with clang; debug is plain.
        if (Compiler.Kind == CCompilerKind.Zig) flags.Add("-fno-sanitize=undefined");
        // LTO is bitcode in the objects; zig 0.16 links Mach-O with a linker of its own that takes
        // none ("LTO requires using LLD"), so a build for macOS goes without.
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

    /// <summary>Compiles every unit (in parallel), reusing cached objects; returns their paths in
    /// order. With <paramref name="of"/>, the units it compiles are a status line — "3 of 14 C units
    /// of app" — and none where every object is cached; the count is drawn as they finish.</summary>
    public IReadOnlyList<string> Compile(IReadOnlyList<CUnit> units, string? of = null)
    {
        var objects = units.Select(ObjectOf).ToArray();
        var stale = Enumerable.Range(0, units.Count).Where(i => !File.Exists(objects[i])).ToList();
        if (of is not null && stale.Count > 0)
            BuildStatus.Say("Compiling", $"{stale.Count} of {units.Count} C unit{(units.Count == 1 ? "" : "s")} of {of}");
        var errors = new System.Collections.Concurrent.ConcurrentBag<string>();
        var done = 0;
        Parallel.ForEach(stale, i =>
        {
            try { CompileTo(units[i], objects[i]); }
            catch (CBuildException exception) { errors.Add(exception.Message); }
            BuildStatus.Count(Interlocked.Increment(ref done), stale.Count, units[i].Shown ?? Path.GetFileName(units[i].Source));
        });
        BuildStatus.Done();
        if (!errors.IsEmpty) throw new CBuildException(string.Join("\n\n", errors));
        return objects;
    }

    public string CompileOne(CUnit unit) => CompileTo(unit, ObjectOf(unit));

    /// <summary>Where a unit's object lies in the cache: named by the unit and its key.</summary>
    private string ObjectOf(CUnit unit) =>
        Path.Combine(_cacheDir, "obj", $"{Path.GetFileNameWithoutExtension(unit.Source)}-{CacheKey(unit)}.o");

    private string CompileTo(CUnit unit, string target)
    {
        if (File.Exists(target)) return target;

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        // Unique per compilation, not per process: builds in one process (parallel tests, a
        // compiler building two targets) compile the same unit at the same time.
        var partial = $"{target}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        var arguments = Driver();
        arguments.AddRange(FlagsFor(unit));
        // a COFF object names the file it was written to in its CodeView record; written to a
        // temporary name, the bytes would differ on every build
        if (Target.Os == TargetOs.Windows) arguments.AddRange(["-Xclang", "-object-file-name=" + Path.GetFileName(target)]);
        arguments.AddRange(["-c", unit.Source, "-o", partial]);
        var result = Run(Compiler.Path, arguments, $"compiling {unit.Source} for {Target} ({Profile.Name})", partial);
        if (result.ExitCode != 0)
        {
            File.Delete(partial);
            // a header nothing holds: where it was looked for (the toolchain's own directories aside)
            var searched = MissingHeader().IsMatch(result.Stderr) && unit.IncludeDirs.Count > 0
                ? "\nthe directories searched for headers: " + string.Join(", ", unit.IncludeDirs)
                : "";
            throw new CBuildException($"compiling {unit.Source} for {Target} ({Profile.Name}) failed:\n{Command(arguments)}\n{result.Stderr}{result.Stdout}{searched}");
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
    /// <param name="bundled">Archives whose members go into this one as they are — a library's
    /// packages' prebuilt archives, so that C links one file. They are named by their bytes in the key:
    /// their names say nothing of what is in them.</param>
    public string Archive(IReadOnlyList<string> objects, string output, IReadOnlyList<string>? bundled = null)
    {
        bundled ??= [];
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        var key = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join('\n', objects.Select(Path.GetFileName)
                .Concat(bundled.Select(archive => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(archive)))))))));
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
            if (bundled.Count > 0 && !Path.GetFileNameWithoutExtension(tool).StartsWith("llvm-ar", StringComparison.Ordinal))
                throw new CBuildException($"putting the packages' archives into {output} needs llvm-ar, which takes an archive's members in (L); {tool} does not");
        }
        // llvm-ar takes an archive's members in only on a quick append ('L' after 'q'); the
        // partial file is new, so appending to it is writing it
        arguments.Add(bundled.Count > 0 ? "qcsL" : "rcs");
        arguments.Add(partial);
        arguments.AddRange(objects);
        arguments.AddRange(bundled);
        var result = Run(tool, arguments, $"archiving {keyed}", partial);
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
    /// from the image; the sanitizer runtime comes along in the sanitizer profiles. The
    /// runtime's backtraces need an unwinder — zig cc links a C program without one, so its own
    /// libunwind comes along on Linux; clang and gcc bring libgcc's; macOS has one in libSystem —
    /// and on Windows DbgHelp, loaded by the runtime at the first trace, not imported. The same
    /// inputs give the same image: a PE image's time stamps and PDB GUID come from its
    /// own bytes, and a Mach-O image carries no debug map — on a macOS host its .dSYM does, beside
    /// it, which is where libbacktrace reads line tables from (<see cref="Reproducible"/>).
    /// </summary>
    /// <param name="libraries">Libraries by name (<c>-l</c>), after every input that needs them.</param>
    /// <param name="paths">Where the libraries are searched, and whether the binary looks for shared
    /// libraries in its own directory first.</param>
    public string LinkExecutable(IReadOnlyList<string> inputs, string output, IReadOnlyList<string>? libraries = null,
        LinkPaths? paths = null)
    {
        paths ??= LinkPaths.None;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        Link(inputs, output, libraries, paths, debugMap: false);
        if (Target.Os == TargetOs.Windows)
        {
            try { Reproducible.NormalizePe(output, Path.ChangeExtension(output, ".pdb")); }
            catch (InvalidDataException failure) { throw new CBuildException($"{output}: {failure.Message}"); }
        }
        if (Target.Os == TargetOs.MacOs && OperatingSystem.IsMacOS()) GatherDebugInfo(inputs, output, libraries, paths);
        return output;
    }

    /// <summary>
    /// Links objects and archives into a shared library — <c>lib&lt;name&gt;.so</c>, a
    /// <c>.dylib</c> that the loader finds by <c>@rpath</c>, or a <c>.dll</c> with its import
    /// library beside it (<c>&lt;name&gt;.lib</c>) —, the program's
    /// exports and the runtime's start and stop its only symbols (<c>LYRIC_API</c>).
    /// </summary>
    public string LinkShared(IReadOnlyList<string> inputs, string output, IReadOnlyList<string>? libraries = null,
        LinkPaths? paths = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        Link(inputs, output, libraries, paths ?? LinkPaths.None, debugMap: false, shared: true);
        return output;
    }

    /// <summary>The system's libraries the runtime calls, by name. Windows: shell32 for
    /// CommandLineToArgvW, the arguments as UTF-16 (runtime/src/os.c), and ws2_32 for Winsock
    /// (runtime/src/net.c) — ntdll's AFD calls are looked up at run time (runtime/src/poll.c), as
    /// wepoll does. Linux: libdl for std.ffi's Library, where the C library keeps dlopen apart (glibc
    /// before 2.34). macOS has them all in libSystem.</summary>
    private IReadOnlyList<string> SystemLibraries => Target.Os switch
    {
        TargetOs.MacOs => [],
        TargetOs.Windows => ["-lshell32", "-lws2_32"],
        _ => ["-lpthread", "-lm", "-ldl"],
    };

    /// <summary>What a C program links beside an archive of Lyric code and the runtime, after it
    /// (spec 13 §4 rule 9): the system's libraries the runtime calls, and on Linux with zig cc the
    /// unwinder its backtraces need, which zig cc does not link by itself.</summary>
    public IReadOnlyList<string> RuntimeLinkArgs =>
        Target.Os == TargetOs.Linux && Compiler.Kind == CCompilerKind.Zig ? [.. SystemLibraries, "-lunwind"] : SystemLibraries;

    /// <summary>One link. A Mach-O image keeps its debug map only when asked: <c>-S</c> leaves out
    /// what would name every object by its path and time.</summary>
    private void Link(IReadOnlyList<string> inputs, string output, IReadOnlyList<string>? libraries, LinkPaths paths, bool debugMap,
        bool shared = false)
    {
        var arguments = Driver();
        // No LTO flag at the link: lld runs LTO over bitcode inputs by itself, and with the flag
        // zig builds its own C library as bitcode too — for Windows its mingw part, whose own
        // symbols then went missing (frexpf, wmemcpy, __DENORM …).
        arguments.AddRange(Profile.Codegen.Where(f => !IsLto(f)));
        arguments.AddRange(Profile.Instrumentation);
        if (shared) arguments.Add("-shared");
        arguments.AddRange(inputs);
        // The directories named are searched before the linker's own, in their order. zig writes
        // each -L that gave it a shared library into an ELF or Mach-O binary as a run path — the
        // build's own directories, in every binary — so for zig the toolchain finds the library
        // itself and hands it over by its path, which zig keeps out of the binary.
        if (Compiler.Kind != CCompilerKind.Zig || Target.Os == TargetOs.Windows)
            foreach (var dir in paths.SearchDirs) arguments.Add("-L" + dir);
        foreach (var library in libraries ?? []) arguments.Add(Found(library, paths.SearchDirs) ?? "-l" + library);
        arguments.AddRange(paths.Args ?? []);
        foreach (var framework in paths.Frameworks ?? []) arguments.AddRange(["-framework", framework]);
        // Windows' loader looks beside the image first by itself; ELF and Mach-O are told to
        if (paths.OwnDirFirst && Target.Os == TargetOs.Linux) arguments.Add("-Wl,-rpath,$ORIGIN");
        if (paths.OwnDirFirst && Target.Os == TargetOs.MacOs) arguments.Add("-Wl,-rpath,@executable_path");
        arguments.AddRange(["-o", output]);
        arguments.AddRange(Target.Os switch
        {
            TargetOs.MacOs => ["-Wl,-dead_strip"],
            // A library's thread is its host's, with the host's stack.
            TargetOs.Windows when shared => ["-Wl,--gc-sections", .. SystemLibraries],
            // The thread's stack as large as the main task's (runtime/include/lyr/task.h,
            // LYR_MAIN_TASK_STACK): what main has as a task it has on the thread. zig's
            // default for a Windows image is 16 MiB (MSVC's 1 MiB); a POSIX thread's comes from
            // the system's limit, 8 MiB as a rule.
            TargetOs.Windows => ["-Wl,--gc-sections", "-Wl,--stack," + MainThreadStack, .. SystemLibraries],
            _ => ["-Wl,--gc-sections", .. SystemLibraries],
        });
        // the name the loader records in what links the library, not the path it was built at
        if (shared && Target.Os == TargetOs.MacOs) arguments.Add("-Wl,-install_name,@rpath/" + Path.GetFileName(output));
        if (shared && Target.Os == TargetOs.Linux) arguments.Add("-Wl,-soname," + Path.GetFileName(output));
        // named after the library, not after the first object, as zig would
        if (shared && Target.Os == TargetOs.Windows) arguments.Add("-Wl,--out-implib," + Path.ChangeExtension(output, ".lib"));
        if (Target.Os == TargetOs.MacOs && !debugMap) arguments.Add("-Wl,-S");
        if (Compiler.Kind == CCompilerKind.Zig) arguments.Add("-fno-sanitize=undefined");
        if (Target.Os == TargetOs.Linux && Compiler.Kind == CCompilerKind.Zig) arguments.Add("-lunwind");
        var result = RunLong(arguments, Path.GetDirectoryName(Path.GetFullPath(output))!, $"linking {output}");
        if (result.ExitCode != 0)
        {
            throw new CBuildException($"linking {output} for {Target} ({Profile.Name}) failed:\n{Command(arguments)}\n{result.Stderr}{result.Stdout}")
            {
                LinkerOutput = result.Stderr + result.Stdout,
            };
        }
    }

    /// <summary>
    /// <c>-l<paramref name="library"/></c> found in the named directories by zig's link, by its path;
    /// <c>null</c> where the linker is to look itself — clang and gcc, or no directory holds it.
    /// <list type="bullet">
    /// <item>ELF and Mach-O: the first directory with <c>libX.so</c>, <c>libX.dylib</c>,
    /// <c>libX.tbd</c> or <c>libX.a</c>, as the linker would search — by its path, no run path is
    /// written.</item>
    /// <item>Windows: zig's linker looks for <c>X.dll</c>, <c>X.lib</c> and <c>libX.a</c> itself
    /// (under <c>-L</c>), and an SDK built with MinGW — SDL's among them — ships the import library
    /// <c>libX.dll.a</c>, which GNU ld and clang find by the name and zig does not.</item>
    /// </list>
    /// </summary>
    private string? Found(string library, IReadOnlyList<string> searchDirs)
    {
        if (Compiler.Kind != CCompilerKind.Zig) return null;
        string[] own = Target.Os switch
        {
            TargetOs.Windows => [$"{library}.dll", $"{library}.lib", $"lib{library}.a"],
            TargetOs.MacOs => [$"lib{library}.dylib", $"lib{library}.tbd", $"lib{library}.a"],
            _ => [$"lib{library}.so", $"lib{library}.a"],
        };
        foreach (var dir in searchDirs)
        {
            if (own.Select(n => Path.Combine(dir, n)).FirstOrDefault(File.Exists) is { } file)
                return Target.Os == TargetOs.Windows ? null : file;
            if (Target.Os == TargetOs.Windows
                && new[] { $"lib{library}.dll.a", $"{library}.dll.a" }.Select(n => Path.Combine(dir, n)).FirstOrDefault(File.Exists) is { } import)
                return import;
        }
        return null;
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
    private ProcessRunner.Result RunLong(List<string> arguments, string directory, string what)
    {
        if (Command(arguments).Length <= LongestCommandLine)
            return Run(Compiler.Path, arguments, what, null);
        var driver = Driver().Count;
        var responseFile = Path.Combine(directory, $".link-{Guid.NewGuid():N}.rsp");
        File.WriteAllLines(responseFile, arguments.Skip(driver)
            .Select(a => "\"" + a.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""));
        try
        {
            return Run(Compiler.Path, [.. arguments.Take(driver), "@" + responseFile], what, null);
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
    private void GatherDebugInfo(IReadOnlyList<string> inputs, string executable, IReadOnlyList<string>? libraries, LinkPaths paths)
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
            Link(inputs, mapped, libraries, paths, debugMap: true);
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

    /// <summary>How long one run of the C compiler, the archiver or the linker may take before it
    /// is stopped.</summary>
    public TimeSpan Limit { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Runs <paramref name="tool"/> within <see cref="Limit"/>: one that does not finish is
    /// the environment's failure (a machine under load, a unit too large), said with what it did and
    /// the limit — never the toolchain's own. Its half-written <paramref name="partial"/> goes.</summary>
    /// <summary>Where <c>lyric build -v</c> has every command of the C compiler, the archiver and the
    /// linker said as it starts (spec 14 §1.1); <c>null</c>: nowhere. The driver's, for one command:
    /// set before the build and cleared after it.</summary>
    public static TextWriter? Echo { get; set; }

    private static readonly object EchoGate = new();

    /// <summary>A command as <c>-v</c> says it — units compile side by side, so one line at a time.</summary>
    public static void Said(string tool, IEnumerable<string> arguments)
    {
        if (Echo is not { } echo) return;
        var line = "running: " + tool + " " + string.Join(' ', arguments.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
        lock (EchoGate) echo.WriteLine(line);
    }

    /// <summary>clang's and gcc's word for an <c>#include</c> no directory holds.</summary>
    [System.Text.RegularExpressions.GeneratedRegex(@"fatal error: ('[^']+' file not found|[^:\n]+: No such file or directory)")]
    private static partial System.Text.RegularExpressions.Regex MissingHeader();

    private ProcessRunner.Result Run(string tool, IEnumerable<string> arguments, string what, string? partial)
    {
        try
        {
            Said(tool, arguments);
            return ProcessRunner.Run(tool, arguments, Limit);
        }
        catch (TimeoutException)
        {
            if (partial is not null) File.Delete(partial);
            throw new CBuildException($"{what} did not finish within {Limit.TotalSeconds:0} s, and was stopped:\n{tool} {string.Join(' ', arguments)}");
        }
    }

    private string Command(IEnumerable<string> arguments) =>
        Compiler.Path + " " + string.Join(' ', arguments.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
}
