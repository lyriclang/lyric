namespace Lyric5.Toolchain;

/// <summary>
/// What the Lyric 5 runtime is made of, per target: the vendored collector (stage 1 of
/// design/v5/spec/01 L1), and the runtime's own sources under <c>runtime/src</c>.
/// </summary>
public static class RuntimeLayout
{
    /// <summary>The repository root: the nearest directory upward that holds <c>runtime/include/lyr</c>.</summary>
    public static string FindRoot(string start)
    {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "runtime", "include", "lyr"))) return dir.FullName;
        }
        throw new DirectoryNotFoundException($"no runtime/include/lyr above {start}");
    }

    public static string IncludeDir(string root) => Path.Combine(root, "runtime", "include");

    private static string BdwgcDir(string root) => Path.Combine(root, "runtime", "third_party", "bdwgc");

    /// <summary>
    /// The collector's configuration. Threads from contract 0 (L6); C11 atomics instead of
    /// libatomic_ops; every interior pointer keeps its object alive, because views point into
    /// strings and arrays (V9, V10); no executable heap.
    /// </summary>
    public static IReadOnlyList<string> CollectorDefines(Target target)
    {
        var defines = new List<string>
        {
            "GC_THREADS",
            "GC_BUILTIN_ATOMIC",
            "ALL_INTERIOR_POINTERS",
            "NO_EXECUTE_PERMISSION",
            "GC_NO_THREAD_REDIRECTS",
        };
        if (target.Os == TargetOs.Windows) defines.Add("GC_NOT_DLL");
        if (target.Os == TargetOs.Linux) defines.Add("_GNU_SOURCE");
        return defines;
    }

    public static IReadOnlyList<CUnit> Units(string root, Target target)
    {
        var bdwgc = BdwgcDir(root);
        var bdwgcInclude = Path.Combine(bdwgc, "include");
        var units = new List<CUnit>
        {
            // The amalgamation compiles the whole collector as one unit. Third-party code: its own
            // warnings are not ours to fix, and it is never instrumented (it reads memory it does
            // not own, on purpose — that is what a conservative scan is).
            new(Path.Combine(bdwgc, "extra", "gc.c"), CollectorDefines(target), [bdwgcInclude],
                Instrument: false, ExtraFlags: ["-w", "-std=gnu11"]),
        };

        var own = Path.Combine(root, "runtime", "src");
        var runtimeDefines = CollectorDefines(target).Where(d => d is "GC_THREADS" or "GC_NOT_DLL" or "GC_NO_THREAD_REDIRECTS").ToList();
        foreach (var source in Directory.EnumerateFiles(own, "*.c").Order(StringComparer.Ordinal))
        {
            units.Add(new CUnit(source, runtimeDefines, [IncludeDir(root), bdwgcInclude],
                ExtraFlags: ["-Wall", "-Wextra"]));
        }
        return units;
    }

    /// <summary>Builds <c>liblyr.a</c> for the build's target and profile and returns its path.</summary>
    public static string BuildArchive(CBuild build, string root, string outputDir)
    {
        var objects = build.Compile(Units(root, build.Target));
        return build.Archive(objects, Path.Combine(outputDir, build.Target.Triple, build.Profile.Name(), "liblyr.a"));
    }
}
