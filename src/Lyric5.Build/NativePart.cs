using Lyric.Core;
using System.Text.RegularExpressions;
using Lyric5.Toolchain;

namespace Lyric5.Build;

/// <summary>A shared library a package brings: the file a binary loads beside it, and on Windows
/// the import library the link reads instead — zig's linker does not link against a DLL itself.</summary>
/// <param name="File">The <c>.so</c>, <c>.dylib</c> or <c>.dll</c>, a path of the package.</param>
/// <param name="ImportLibrary">Windows' import library, a path of the package; <c>null</c> elsewhere.</param>
public sealed record Dylib(string File, string? ImportLibrary)
{
    /// <summary>What the link reads: the import library on Windows, the library itself elsewhere.</summary>
    public string Linked => ImportLibrary ?? File;
}

/// <summary>
/// The native part of a package: C sources compiled with the program
/// under its profile, the directories their includes are found in, their definitions, and what the
/// program is linked with — <c>[native]</c>, per operating system <c>[native.linux]</c>,
/// <c>[native.windows]</c>, <c>[native.macos]</c>, and per triple <c>[native.x86_64-linux-gnu]</c>,
/// layered in that order: each list the one before with its own entries after.
/// </summary>
/// <param name="Sources">Patterns of the package's files, as the package content takes them: the C sources.</param>
/// <param name="Include">Directories of the package the sources find their includes in.</param>
/// <param name="Libs">Libraries by name, linked as <c>-l&lt;name&gt;</c>.</param>
/// <param name="Archives">Prebuilt static libraries, paths of the package, linked by their path.</param>
/// <param name="Dylibs">Prebuilt shared libraries, linked by their path and put beside the binary.</param>
/// <param name="Defines">The sources' preprocessor definitions, <c>NAME</c> or <c>NAME=value</c>.</param>
public sealed partial record NativePart(
    IReadOnlyList<string> Sources,
    IReadOnlyList<string> Include,
    IReadOnlyList<string> Libs,
    IReadOnlyList<string> Archives,
    IReadOnlyList<Dylib> Dylibs,
    IReadOnlyList<string> Defines)
{
    public static readonly NativePart None = new([], [], [], [], [], []);

    private static readonly Dictionary<string, TargetOs> Systems = new(StringComparer.Ordinal)
    {
        ["linux"] = TargetOs.Linux,
        ["windows"] = TargetOs.Windows,
        ["macos"] = TargetOs.MacOs,
    };

    /// <summary>The triples a table may name: Tier 1 and Tier 2 (11 T4).</summary>
    private static readonly Dictionary<string, Target> Triples =
        Target.Tier1.Concat(Target.Tier2).ToDictionary(t => t.Triple, StringComparer.Ordinal);

    /// <summary>This part with <paramref name="more"/> on top.</summary>
    public NativePart With(NativePart more) =>
        new([.. Sources, .. more.Sources], [.. Include, .. more.Include], [.. Libs, .. more.Libs],
            [.. Archives, .. more.Archives], [.. Dylibs, .. more.Dylibs], [.. Defines, .. more.Defines]);

    /// <summary><c>[native]</c> and its tables per operating system and per triple, checked.</summary>
    /// <exception cref="ManifestException">A key, a table, a pattern, a path, a definition or a
    /// library name is refused.</exception>
    public static (NativePart Common, IReadOnlyDictionary<TargetOs, NativePart> PerSystem,
        IReadOnlyDictionary<string, NativePart> PerTriple) Read(TomlTable document, string file)
    {
        var perSystem = new Dictionary<TargetOs, NativePart>();
        var perTriple = new Dictionary<string, NativePart>(StringComparer.Ordinal);
        if (!document.TryGet("native", out var found)) return (None, perSystem, perTriple);
        if (found is not TomlTable native)
            throw new ManifestException("LYR-PKG0002", file, 1, 1, "[native] is a table: [native] sources = [\"native/*.c\"]");
        foreach (var key in native.Keys)
        {
            if (Systems.TryGetValue(key, out var os))
                perSystem[os] = Part(Table(native, key, file), $"native.{key}", file, os);
            else if (Triples.TryGetValue(key, out var target))
                perTriple[key] = Part(Table(native, key, file), $"native.{key}", file, target.Os);
            else if (native.TryGet(key, out var value) && value is TomlTable)
                throw new ManifestException("LYR-PKG0003", file, native.Line, 1,
                    $"[native.{key}] names no operating system and no target: linux, windows, macos, or one of "
                    + string.Join(", ", Triples.Keys));
        }
        return (Part(native, "native", file, null), perSystem, perTriple);
    }

    private static TomlTable Table(TomlTable native, string key, string file) =>
        native.TryGet(key, out var value) && value is TomlTable table
            ? table
            : throw new ManifestException("LYR-PKG0002", file, native.Line, 1, $"[native.{key}] is a table");

    /// <summary>One table's keys. <paramref name="os"/> is the system the table holds for —
    /// <c>null</c> for <c>[native]</c>, which holds for every one and so names no shared library.</summary>
    private static NativePart Part(TomlTable table, string where, string file, TargetOs? os)
    {
        foreach (var key in table.Keys)
        {
            if (key is "sources" or "include" or "libs" or "archives" or "defines") continue;
            if (key == "dylibs" && os is not null) continue;
            if (os is null && (Systems.ContainsKey(key) || Triples.ContainsKey(key))) continue;
            if (os is null && table.TryGet(key, out var value) && value is TomlTable) continue; // refused by Read, with its name
            throw new ManifestException("LYR-PKG0003", file, table.Line, 1, key == "dylibs"
                ? $"[{where}] has no key 'dylibs': a shared library is built for one target — name it in [native.linux], [native.windows], [native.macos] or a triple's table"
                : $"[{where}] has no key '{key}'");
        }
        List<string> Strings(string key, Func<string, string?> refusal, string many)
        {
            if (!table.TryGet(key, out var value)) return [];
            if (value is not TomlArray list || list.Any(v => v is not string))
                throw new ManifestException("LYR-PKG0002", file, table.Line, 1, $"'{key}' of [{where}] is an array of {many}");
            var items = list.Cast<string>().ToList();
            foreach (var item in items)
                if (refusal(item) is { } why)
                    throw new ManifestException("LYR-PKG0002", file, table.Line, 1, $"'{item}' in '{key}' of [{where}] {why}");
            return items;
        }
        return new NativePart(
            Strings("sources", s => PackageContent.IsPattern(s) ? null : "is no pattern of the package's files", "patterns of the package's files"),
            Strings("include", d => PackageContent.IsPattern(d) && d.IndexOfAny(['*', '?']) < 0 ? null : "is no directory of the package",
                "directories of the package"),
            Strings("libs", l => LibraryPattern().IsMatch(l) ? null : "is no library name", "library names"),
            Strings("archives", ArchiveRefusal, "paths of the package's static libraries"),
            os is { } system ? ReadDylibs(table, where, file, system) : [],
            Strings("defines", d => DefinePattern().IsMatch(d) ? null : "is no definition: NAME or NAME=value, NAME a C identifier",
                "definitions"));
    }

    /// <summary>Why an entry of <c>archives</c> is refused, or <c>null</c>: a path of the package,
    /// and a static library — a shared one belongs in <c>dylibs</c>.</summary>
    private static string? ArchiveRefusal(string path)
    {
        if (!IsPath(path)) return "is no path of the package";
        if (IsShared(path)) return "is a shared library: name it in 'dylibs' of its system's or its triple's table";
        return null;
    }

    private static List<Dylib> ReadDylibs(TomlTable table, string where, string file, TargetOs os)
    {
        if (!table.TryGet("dylibs", out var value)) return [];
        var form = os switch
        {
            TargetOs.Windows => "tables { dll = \"native/foo.dll\", lib = \"native/libfoo.dll.a\" } — the DLL and its import library",
            TargetOs.MacOs => "paths of the package's .dylib files",
            _ => "paths of the package's .so files",
        };
        if (value is not TomlArray list)
            throw new ManifestException("LYR-PKG0002", file, table.Line, 1, $"'dylibs' of [{where}] is an array of {form}");
        var dylibs = new List<Dylib>();
        foreach (var entry in list)
        {
            Dylib? dylib = (os, entry) switch
            {
                (TargetOs.Windows, TomlTable pair) when pair.Keys.All(k => k is "dll" or "lib")
                    && pair.TryGet("dll", out var dll) && dll is string dllPath && IsPath(dllPath)
                    && dllPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                    && pair.TryGet("lib", out var lib) && lib is string libPath && IsPath(libPath)
                    && !IsShared(libPath) => new Dylib(dllPath, libPath),
                (TargetOs.Linux, string path) when IsPath(path) && SoPattern().IsMatch(path) => new Dylib(path, null),
                (TargetOs.MacOs, string path) when IsPath(path) && path.EndsWith(".dylib", StringComparison.Ordinal) => new Dylib(path, null),
                _ => null,
            };
            dylibs.Add(dylib ?? throw new ManifestException("LYR-PKG0002", file, table.Line, 1,
                $"an entry of 'dylibs' in [{where}] is not one of {form}"));
        }
        return dylibs;
    }

    /// <summary>A path a manifest names one file by: a pattern of the package without wildcards.</summary>
    private static bool IsPath(string path) => PackageContent.IsPattern(path) && path.IndexOfAny(['*', '?']) < 0 && !path.EndsWith('/');

    private static bool IsShared(string path) =>
        SoPattern().IsMatch(path) || path.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);

    /// <summary>The C sources this part names under <paramref name="root"/>, sorted.</summary>
    public IReadOnlyList<string> SourceFiles(string root)
    {
        if (Sources.Count == 0 || !Directory.Exists(root)) return [];
        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(file => (Relative: Path.GetRelativePath(root, file).Replace('\\', '/'), Full: file))
            .Where(file => !file.Relative.StartsWith("out/", StringComparison.Ordinal) && PackageContent.Matches(Sources, file.Relative))
            .OrderBy(file => file.Relative, StringComparer.Ordinal)
            .Select(file => file.Full)
            .ToList();
    }

    [GeneratedRegex("^[A-Za-z0-9_][A-Za-z0-9_.+-]*$")]
    private static partial Regex LibraryPattern();

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*(=.*)?$", RegexOptions.Singleline)]
    private static partial Regex DefinePattern();

    /// <summary>A shared library of ELF: <c>libfoo.so</c>, or with its version, <c>libfoo.so.1.2</c>.</summary>
    [GeneratedRegex(@"\.so(\.[0-9]+)*$")]
    private static partial Regex SoPattern();
}
