using System.Text.RegularExpressions;
using Lyric5.Toolchain;

namespace Lyric5.Build;

/// <summary>
/// The native part of a package (design/v5/spec/07 B5; 11 W2): C sources compiled with the program
/// under its profile, the directories their includes are found in, the libraries the program is
/// linked with — <c>[native]</c>, and per operating system <c>[native.linux]</c>,
/// <c>[native.windows]</c>, <c>[native.macos]</c>, whose entries come on top.
/// </summary>
/// <param name="Sources">Patterns of the package's files (11 W2 P8's form): the C sources.</param>
/// <param name="Include">Directories of the package the sources find their includes in.</param>
/// <param name="Libs">Libraries by name, linked as <c>-l&lt;name&gt;</c>.</param>
public sealed partial record NativePart(IReadOnlyList<string> Sources, IReadOnlyList<string> Include, IReadOnlyList<string> Libs)
{
    public static readonly NativePart None = new([], [], []);

    private static readonly Dictionary<string, TargetOs> Systems = new(StringComparer.Ordinal)
    {
        ["linux"] = TargetOs.Linux,
        ["windows"] = TargetOs.Windows,
        ["macos"] = TargetOs.MacOs,
    };

    /// <summary>This part with <paramref name="more"/> on top.</summary>
    public NativePart With(NativePart more) =>
        new([.. Sources, .. more.Sources], [.. Include, .. more.Include], [.. Libs, .. more.Libs]);

    /// <summary><c>[native]</c> and its tables per operating system, checked.</summary>
    /// <exception cref="ManifestException">A key, a pattern, a directory or a library name is refused.</exception>
    public static (NativePart Common, IReadOnlyDictionary<TargetOs, NativePart> PerSystem) Read(TomlTable document, string file)
    {
        var perSystem = new Dictionary<TargetOs, NativePart>();
        if (!document.TryGet("native", out var found)) return (None, perSystem);
        if (found is not TomlTable native)
            throw new ManifestException("LYR-PKG0002", file, 1, 1, "[native] is a table: [native] sources = [\"native/*.c\"]");
        foreach (var (name, os) in Systems)
        {
            if (!native.TryGet(name, out var table)) continue;
            if (table is not TomlTable part)
                throw new ManifestException("LYR-PKG0002", file, native.Line, 1, $"[native.{name}] is a table");
            perSystem[os] = Part(part, $"native.{name}", file, allowSystems: false);
        }
        return (Part(native, "native", file, allowSystems: true), perSystem);
    }

    private static NativePart Part(TomlTable table, string where, string file, bool allowSystems)
    {
        foreach (var key in table.Keys)
            if (key is not ("sources" or "include" or "libs") && !(allowSystems && Systems.ContainsKey(key)))
                throw new ManifestException("LYR-PKG0003", file, table.Line, 1, $"[{where}] has no key '{key}'");
        List<string> Strings(string key, Func<string, bool> valid, string many, string one)
        {
            if (!table.TryGet(key, out var value)) return [];
            if (value is not TomlArray list || list.Any(v => v is not string))
                throw new ManifestException("LYR-PKG0002", file, table.Line, 1, $"'{key}' of [{where}] is an array of {many}");
            var items = list.Cast<string>().ToList();
            foreach (var item in items)
                if (!valid(item))
                    throw new ManifestException("LYR-PKG0002", file, table.Line, 1, $"'{item}' in '{key}' of [{where}] is no {one}");
            return items;
        }
        return new NativePart(
            Strings("sources", PackageContent.IsPattern, "patterns of the package's files", "pattern of the package's files"),
            Strings("include", d => PackageContent.IsPattern(d) && d.IndexOfAny(['*', '?']) < 0,
                "directories of the package", "directory of the package"),
            Strings("libs", l => LibraryPattern().IsMatch(l), "library names", "library name"));
    }

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
}
