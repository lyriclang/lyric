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
    public Profile Profile { get; }

    public CBuild(CCompiler compiler, Target target, Profile profile, string cacheDir)
    {
        if (profile.RequiresClang() && compiler.Kind != CCompilerKind.Clang)
        {
            throw new CBuildException($"the {profile.Name()} profile needs clang; {compiler.Name} ships no sanitizer runtime for it");
        }
        if (!target.IsHost && !compiler.CanCrossCompile)
        {
            throw new CBuildException($"{compiler.Name} cannot build for {target} without a sysroot; cross builds use zig cc");
        }
        Compiler = compiler;
        Target = target;
        Profile = profile;
        _cacheDir = Path.Combine(cacheDir, target.Triple, profile.Name());
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
        var flags = new List<string> { "-std=c11", "-ffunction-sections", "-fdata-sections" };
        flags.AddRange(Profile.Codegen());
        if (unit.Instrument) flags.AddRange(Profile.Instrumentation());
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
        Add(Profile.Name());
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
        var partial = target + $".{Environment.ProcessId}.tmp";
        var arguments = Driver();
        arguments.AddRange(FlagsFor(unit));
        arguments.AddRange(["-c", unit.Source, "-o", partial]);
        var result = ProcessRunner.Run(Compiler.Path, arguments, TimeSpan.FromMinutes(5));
        if (result.ExitCode != 0)
        {
            File.Delete(partial);
            throw new CBuildException($"compiling {unit.Source} for {Target} ({Profile.Name()}) failed:\n{Command(arguments)}\n{result.Stderr}{result.Stdout}");
        }
        File.Move(partial, target, overwrite: true);
        return target;
    }

    public string Archive(IReadOnlyList<string> objects, string output)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        if (File.Exists(output)) File.Delete(output);

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
        arguments.Add(output);
        arguments.AddRange(objects);
        var result = ProcessRunner.Run(tool, arguments, TimeSpan.FromMinutes(2));
        if (result.ExitCode != 0) throw new CBuildException($"archiving {output} failed:\n{result.Stderr}");
        return output;
    }

    /// <summary>
    /// Links objects and archives into an executable. Sections nobody references are dropped
    /// (design/v5/spec/01 L11); the sanitizer runtime comes along in the sanitizer profiles.
    /// </summary>
    public string LinkExecutable(IReadOnlyList<string> inputs, string output)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        var arguments = Driver();
        arguments.AddRange(Profile.Codegen());
        arguments.AddRange(Profile.Instrumentation());
        arguments.AddRange(inputs);
        arguments.AddRange(["-o", output]);
        arguments.AddRange(Target.Os switch
        {
            TargetOs.MacOs => ["-Wl,-dead_strip"],
            TargetOs.Windows => ["-Wl,--gc-sections"],
            _ => ["-Wl,--gc-sections", "-lpthread", "-lm"],
        });
        var result = ProcessRunner.Run(Compiler.Path, arguments, TimeSpan.FromMinutes(5));
        if (result.ExitCode != 0)
        {
            throw new CBuildException($"linking {output} for {Target} ({Profile.Name()}) failed:\n{Command(arguments)}\n{result.Stderr}{result.Stdout}");
        }
        return output;
    }

    private string Command(IEnumerable<string> arguments) =>
        Compiler.Path + " " + string.Join(' ', arguments.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
}
