using System.Text.RegularExpressions;

namespace Lyric5.Build;

/// <summary>
/// A package's manifest, <c>lyric.toml</c> (design/v5/spec/07 V7, 11 W2): read, never run (P1).
/// <c>[package]</c> names the package — the first segment of its module paths (07 M1, M3) —, its
/// version and its edition (07 V9). The other sections come with the slices that give them a
/// meaning; until then a section, like a key, the toolchain does not know is an error, as an
/// unknown option is (11 C3).
/// </summary>
public sealed partial record Manifest(string File, string Name, string Version, string Edition)
{
    /// <summary>The directory of the manifest: the package's root, where <c>out/</c> lies (P5).</summary>
    public string Root => Path.GetDirectoryName(File)!;

    /// <summary>Where the package's modules are (07 M1): <c>src/</c>.</summary>
    public string SourceRoot => Path.Combine(Root, "src");

    /// <summary>The editions this toolchain knows (07 V9).</summary>
    private static readonly string[] Editions = ["5"];

    /// <summary>The keys of <c>[package]</c> M7 S1 reads, and the ones that describe the package
    /// and change no build — for <c>lyric metadata</c> (P10).</summary>
    private static readonly string[] PackageKeys = ["name", "version", "edition"];

    private static readonly string[] DescriptiveStrings = ["description", "license", "repository"];

    /// <summary>The keys of <c>[package]</c> with a meaning the plan gives them later: a pin that
    /// is accepted and not enforced would be a promise the toolchain does not keep.</summary>
    private static readonly Dictionary<string, string> LaterKeys = new(StringComparer.Ordinal)
    {
        ["toolchain"] = "M7 S6",
        ["include"] = "M7 S5",
        ["exclude"] = "M7 S5",
    };

    /// <summary>The sections the plan gives a meaning to later, with the slice they come with.</summary>
    private static readonly Dictionary<string, string> LaterSections = new(StringComparer.Ordinal)
    {
        ["dependencies"] = "M7 S4",
        ["override"] = "M7 S4",
        ["bin"] = "M7 S6",
        ["profile"] = "M7 S6",
        ["native"] = "M7 S6",
        ["lints"] = "M12",
        ["build-dependencies"] = "M7 S7",
        ["trust"] = "M7 S7",
    };

    /// <summary>Reads and checks <paramref name="file"/>.</summary>
    /// <exception cref="ManifestException">The manifest is not TOML, or not a manifest.</exception>
    public static Manifest Read(string file)
    {
        TomlTable document;
        try
        {
            document = Toml.Parse(System.IO.File.ReadAllText(file));
        }
        catch (TomlException e)
        {
            throw new ManifestException("LYR-PKG0001", file, e.Line, e.Column, e.Message);
        }

        foreach (var key in document.Keys)
        {
            if (key == "package") continue;
            document.TryGet(key, out var section);
            var line = section switch
            {
                TomlTable t => t.Line,
                TomlArray { Count: > 0 } a when a[0] is TomlTable first => first.Line,
                _ => 1,
            };
            throw LaterSections.TryGetValue(key, out var when)
                ? new ManifestException("LYR-PKG0003", file, line, 1, $"[{key}] comes with {when}: this toolchain does not read it yet")
                : new ManifestException("LYR-PKG0003", file, line, 1, $"'{key}' is no part of a manifest");
        }

        if (!document.TryGet("package", out var found) || found is not TomlTable package)
            throw new ManifestException("LYR-PKG0002", file, 1, 1, "a manifest needs a [package] table");
        foreach (var key in package.Keys)
        {
            if (PackageKeys.Contains(key) || DescriptiveStrings.Contains(key) || key == "authors") continue;
            throw LaterKeys.TryGetValue(key, out var when)
                ? new ManifestException("LYR-PKG0003", file, package.Line, 1,
                    $"[package] '{key}' comes with {when}: this toolchain does not read it yet")
                : new ManifestException("LYR-PKG0003", file, package.Line, 1, $"[package] has no key '{key}'");
        }
        foreach (var key in DescriptiveStrings)
            if (package.TryGet(key, out var text) && text is not string)
                throw new ManifestException("LYR-PKG0002", file, package.Line, 1, $"'{key}' is a string, not {Describe(text)}");
        if (package.TryGet("authors", out var authors) && (authors is not TomlArray list || list.Any(a => a is not string)))
            throw new ManifestException("LYR-PKG0002", file, package.Line, 1, "'authors' is an array of strings");

        var name = RequiredString(package, "name", file);
        if (!NamePattern().IsMatch(name))
            throw new ManifestException("LYR-PKG0002", file, package.Line, 1,
                $"'{name}' is no package name: a lowercase letter, then letters, digits and '_' — the first segment of its module paths (07 M1)");
        if (name == "std")
            throw new ManifestException("LYR-PKG0002", file, package.Line, 1, "'std' is the standard library's name (07 M3, D1)");

        var version = RequiredString(package, "version", file);
        if (!SemVerPattern().IsMatch(version))
            throw new ManifestException("LYR-PKG0002", file, package.Line, 1, $"'{version}' is no semantic version (MAJOR.MINOR.PATCH)");

        var edition = "5";
        if (package.TryGet("edition", out var given))
        {
            if (given is not string e || !Editions.Contains(e))
                throw new ManifestException("LYR-PKG0002", file, package.Line, 1,
                    $"edition {Describe(given)} is not one this toolchain knows: {string.Join(", ", Editions.Select(x => $"\"{x}\""))}");
            edition = e;
        }

        return new Manifest(Path.GetFullPath(file), name, version, edition);
    }

    private static string RequiredString(TomlTable table, string key, string file)
    {
        if (!table.TryGet(key, out var value))
            throw new ManifestException("LYR-PKG0002", file, table.Line, 1, $"[package] needs a '{key}'");
        if (value is not string text)
            throw new ManifestException("LYR-PKG0002", file, table.Line, 1, $"'{key}' is a string, not {Describe(value)}");
        return text;
    }

    private static string Describe(object value) => value switch
    {
        string s => $"\"{s}\"",
        long n => $"the number {n}",
        bool b => b ? "true" : "false",
        TomlArray => "an array",
        _ => "a table",
    };

    [GeneratedRegex("^[a-z][a-z0-9_]*$")]
    private static partial Regex NamePattern();

    [GeneratedRegex(@"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z.-]+)?(\+[0-9A-Za-z.-]+)?$")]
    private static partial Regex SemVerPattern();
}

/// <summary>A manifest the toolchain refuses: its code (11 W6), file, line and column, and why.</summary>
public sealed class ManifestException(string code, string file, int line, int column, string message) : Exception(message)
{
    public string Code { get; } = code;
    public string File { get; } = file;
    public int Line { get; } = line;
    public int Column { get; } = column;

    /// <summary>As the driver writes a diagnostic.</summary>
    public string Render() => $"error[{Code}]: {Message}\n  --> {File}:{Line}:{Column}";
}
