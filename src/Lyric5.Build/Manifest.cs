using System.Text.RegularExpressions;

namespace Lyric5.Build;

/// <summary>A package a manifest depends on (design/v5/spec/11 W2 P2, P9; 07 V7): read from a
/// directory or from a git repository — a registry's version is no part of 5.0 (07 P8).</summary>
/// <param name="Path">The package's directory, absolute, for <c>{ path = "…" }</c>.</param>
/// <param name="Git">The repository and its revision, for <c>{ git = "…" }</c>.</param>
/// <param name="Line">The line it is written on, for what is refused about it.</param>
public sealed record Dependency(string Name, string? Path, GitSource? Git, int Line)
{
    /// <summary>Where the package is read from, as a message says it.</summary>
    public string Describe() => Git?.ToString() ?? $"'{Path}'";
}

/// <summary>A git repository and the revision of it a dependency reads (11 W2 P9): a tag, a
/// branch, a commit — or, none named, the branch the repository's <c>HEAD</c> names.</summary>
public sealed record GitSource(string Url, GitRefKind Kind, string? Ref)
{
    public string Revision => Kind switch
    {
        GitRefKind.Tag => $"tag '{Ref}'",
        GitRefKind.Branch => $"branch '{Ref}'",
        GitRefKind.Rev => $"commit '{Ref}'",
        _ => "its default branch",
    };

    public override string ToString() => $"{Url} at {Revision}";
}

public enum GitRefKind { Default, Tag, Branch, Rev }

/// <summary>A program of the package besides its <c>src/main.lyr</c> (design/v5/spec/11 W2 P2):
/// <c>[[bin]] name = "tool" entry = "src/tool.lyr"</c>.</summary>
/// <param name="Entry">The entry module's file, absolute: a module of the package.</param>
public sealed record Binary(string Name, string Entry, int Line);

/// <summary>The least toolchain that builds the package (11 W2 P12; 07 V9).</summary>
public sealed record ToolchainPin(SemVer Minimum, int Line);

/// <summary>
/// A package's manifest, <c>lyric.toml</c> (design/v5/spec/07 V7, 11 W2): read, never run (P1).
/// <c>[package]</c> names the package — the first segment of its module paths (07 M1, M3) —, its
/// version and its edition (07 V9). The other sections come with the slices that give them a
/// meaning; until then a section, like a key, the toolchain does not know is an error, as an
/// unknown option is (11 C3).
/// </summary>
public sealed partial record Manifest(string File, string Name, string Version, string Edition)
{
    /// <summary><c>[dependencies]</c>: the packages this one imports from (P6).</summary>
    public IReadOnlyList<Dependency> Dependencies { get; init; } = [];

    /// <summary><c>[override]</c> (07 P7): a package of the graph read from another directory,
    /// wherever it is asked for — the root manifest's alone count.</summary>
    public IReadOnlyList<Dependency> Overrides { get; init; } = [];

    /// <summary><c>include</c> (11 W2 P8): the files the package is made of, instead of the
    /// default; <c>null</c> where the default holds.</summary>
    public IReadOnlyList<string>? Include { get; init; }

    /// <summary><c>exclude</c> (P8): files taken out of what the package is made of.</summary>
    public IReadOnlyList<string> Exclude { get; init; } = [];

    /// <summary><c>[profile.&lt;name&gt;]</c> (P3): the built-in profiles changed, the manifest's own
    /// named — the root manifest's alone count.</summary>
    public IReadOnlyDictionary<string, ProfileSpec> Profiles { get; init; } = new Dictionary<string, ProfileSpec>();

    /// <summary><c>[[bin]]</c> (P2): the programs besides <c>src/main.lyr</c>.</summary>
    public IReadOnlyList<Binary> Binaries { get; init; } = [];

    /// <summary><c>toolchain</c> (P12): the least toolchain that builds the package; <c>null</c> for any.</summary>
    public ToolchainPin? Toolchain { get; init; }

    /// <summary><c>[native]</c> (07 B5): the native part every target gets.</summary>
    public NativePart Native { get; init; } = NativePart.None;

    /// <summary><c>[native.linux]</c> and its siblings: what one operating system gets on top.</summary>
    public IReadOnlyDictionary<Lyric5.Toolchain.TargetOs, NativePart> NativePerSystem { get; init; } =
        new Dictionary<Lyric5.Toolchain.TargetOs, NativePart>();

    /// <summary>The native part a build for <paramref name="os"/> compiles and links.</summary>
    public NativePart NativeOn(Lyric5.Toolchain.TargetOs os) =>
        NativePerSystem.TryGetValue(os, out var more) ? Native.With(more) : Native;

    /// <summary>The directory of the manifest: the package's root, where <c>out/</c> lies (P5).</summary>
    public string Root => Path.GetDirectoryName(File)!;

    /// <summary>Where the package's modules are (07 M1): <c>src/</c>.</summary>
    public string SourceRoot => Path.Combine(Root, "src");

    /// <summary>The editions this toolchain knows (07 V9).</summary>
    private static readonly string[] Editions = ["5"];

    /// <summary>The keys of <c>[package]</c> the toolchain reads, and the ones that describe the
    /// package and change no build — for <c>lyric metadata</c> (P10).</summary>
    private static readonly string[] PackageKeys = ["name", "version", "edition", "include", "exclude", "toolchain"];

    private static readonly string[] DescriptiveStrings = ["description", "license", "repository"];

    /// <summary>The sections the plan gives a meaning to later, with the slice they come with.</summary>
    private static readonly Dictionary<string, string> LaterSections = new(StringComparer.Ordinal)
    {
        ["lints"] = "M12",
        ["build-dependencies"] = "M7 S7",
        ["trust"] = "M7 S7",
    };

    /// <summary>Reads and checks <paramref name="file"/>.</summary>
    /// <exception cref="ManifestException">The manifest is not TOML, or not a manifest.</exception>
    public static Manifest Read(string file) => Parse(System.IO.File.ReadAllText(file), file);

    /// <summary>Checks <paramref name="source"/> as the manifest at <paramref name="file"/> — where
    /// its paths are relative to and its diagnostics point.</summary>
    /// <exception cref="ManifestException">The manifest is not TOML, or not a manifest.</exception>
    public static Manifest Parse(string source, string file)
    {
        TomlTable document;
        try
        {
            document = Toml.Parse(source);
        }
        catch (TomlException e)
        {
            throw new ManifestException("LYR-PKG0001", file, e.Line, e.Column, e.Message);
        }

        foreach (var key in document.Keys)
        {
            if (key is "package" or "dependencies" or "override" or "profile" or "bin" or "native") continue;
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
            throw new ManifestException("LYR-PKG0003", file, package.Line, 1, $"[package] has no key '{key}'");
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

        var (native, nativePerSystem) = NativePart.Read(document, file);
        return new Manifest(Path.GetFullPath(file), name, version, edition)
        {
            Native = native,
            NativePerSystem = nativePerSystem,
            Dependencies = ReadDependencies(document, "dependencies", file),
            Overrides = ReadDependencies(document, "override", file),
            Include = Patterns(package, "include", file),
            Exclude = Patterns(package, "exclude", file) ?? [],
            Profiles = global::Lyric5.Build.Profiles.Read(document, file),
            Binaries = ReadBinaries(document, file, name),
            Toolchain = ReadToolchain(package, file),
        };
    }

    /// <summary><c>toolchain = "5.1"</c> or <c>">=5.1"</c>: the least toolchain that builds the
    /// package (11 W2 P12; 07 V9). A pin the toolchain did not check would be a promise it did not keep.</summary>
    private static ToolchainPin? ReadToolchain(TomlTable package, string file)
    {
        if (!package.TryGet("toolchain", out var given)) return null;
        if (given is not string required || ToolchainPattern().Match(required) is not { Success: true } match)
            throw new ManifestException("LYR-PKG0002", file, package.Line, 1,
                $"toolchain {Describe(given)} is no toolchain requirement: \"5.1\" or \">=5.1\", the least toolchain that builds the package");
        var patch = match.Groups[3].Success ? long.Parse(match.Groups[3].Value) : 0;
        return new ToolchainPin(new SemVer(long.Parse(match.Groups[1].Value), long.Parse(match.Groups[2].Value), patch, null), package.Line);
    }

    /// <summary><c>[[bin]]</c>: each program a name and an entry — a module of the package, a
    /// <c>.lyr</c> file under <c>src/</c> (11 W2 P2). One name per program: the package's own,
    /// for <c>src/main.lyr</c>, included.</summary>
    private static List<Binary> ReadBinaries(TomlTable document, string file, string package)
    {
        var binaries = new List<Binary>();
        if (!document.TryGet("bin", out var found)) return binaries;
        if (found is not TomlArray { OfTables: true } tables)
            throw new ManifestException("LYR-PKG0002", file, 1, 1, "[[bin]] is an array of tables: [[bin]] name = \"tool\" entry = \"src/tool.lyr\"");
        var root = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(file))!;
        var source = System.IO.Path.Combine(root, "src");
        foreach (TomlTable table in tables)
        {
            foreach (var key in table.Keys)
                if (key is not ("name" or "entry"))
                    throw new ManifestException("LYR-PKG0003", file, table.Line, 1, $"[[bin]] has no key '{key}'");
            if (!table.TryGet("name", out var given) || given is not string name || !BinaryPattern().IsMatch(name))
                throw new ManifestException("LYR-PKG0002", file, table.Line, 1,
                    "[[bin]] needs a name: a lowercase letter, then letters, digits, '_' and '-'");
            if (!table.TryGet("entry", out var at) || at is not string entry)
                throw new ManifestException("LYR-PKG0002", file, table.Line, 1, $"[[bin]] '{name}' needs an entry: entry = \"src/{name}.lyr\"");
            var full = System.IO.Path.GetFullPath(entry, root);
            var relative = System.IO.Path.GetRelativePath(source, full);
            if (relative.StartsWith("..", StringComparison.Ordinal) || System.IO.Path.IsPathRooted(relative)
                || !full.EndsWith(".lyr", StringComparison.Ordinal))
                throw new ManifestException("LYR-PKG0002", file, table.Line, 1,
                    $"[[bin]] '{name}' enters at '{entry}', which is no module of the package: a .lyr file under src/");
            if (binaries.Any(b => b.Name == name)
                || (name == package && System.IO.File.Exists(System.IO.Path.Combine(source, "main.lyr"))))
                throw new ManifestException("LYR-PKG0002", file, table.Line, 1,
                    $"two programs are named '{name}'" + (name == package ? $" — src/main.lyr is '{package}'s" : ""));
            binaries.Add(new Binary(name, full, table.Line));
        }
        return binaries;
    }

    /// <summary><c>include</c> or <c>exclude</c>: patterns of the package's files (11 W2 P8).</summary>
    private static List<string>? Patterns(TomlTable package, string key, string file)
    {
        if (!package.TryGet(key, out var value)) return null;
        if (value is not TomlArray list || list.Any(p => p is not string))
            throw new ManifestException("LYR-PKG0002", file, package.Line, 1, $"'{key}' is an array of patterns");
        var patterns = list.Cast<string>().ToList();
        foreach (var pattern in patterns)
            if (!PackageContent.IsPattern(pattern))
                throw new ManifestException("LYR-PKG0002", file, package.Line, 1,
                    $"'{pattern}' is no pattern of the package's files: relative to its root, '/' between directories, no '.' or '..'");
        return patterns;
    }

    /// <summary>The keys of a dependency's table: where it is read from, and the revision of a
    /// repository — one of them.</summary>
    private static readonly string[] DependencyKeys = ["path", "git", "tag", "branch", "rev"];

    private static readonly string[] Revisions = ["tag", "branch", "rev"];

    /// <summary><c>name = { path = "…" }</c>, the path relative to the manifest's directory, or
    /// <c>name = { git = "…", tag | branch | rev = "…" }</c>. An override reads a directory (07 P7).</summary>
    private static List<Dependency> ReadDependencies(TomlTable document, string section, string file)
    {
        var dependencies = new List<Dependency>();
        if (!document.TryGet(section, out var found)) return dependencies;
        if (found is not TomlTable table)
            throw new ManifestException("LYR-PKG0002", file, 1, 1, $"[{section}] is a table of packages");
        var root = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(file))!;
        foreach (var name in table.Keys)
        {
            table.TryGet(name, out var value);
            var line = value is TomlTable at ? at.Line : table.Line;
            if (!NamePattern().IsMatch(name))
                throw new ManifestException("LYR-PKG0002", file, line, 1, $"'{name}' is no package name");
            if (value is string)
                throw new ManifestException("LYR-PKG0003", file, line, 1,
                    $"'{name}' asks a registry for a version, and there is none in 5.0 (07 P8) — give its directory or its repository: {name} = {{ path = \"…\" }}");
            if (value is not TomlTable spec)
                throw new ManifestException("LYR-PKG0002", file, line, 1, $"'{name}' is written {name} = {{ path = \"…\" }} or {name} = {{ git = \"…\" }}");
            foreach (var key in spec.Keys)
            {
                if (!DependencyKeys.Contains(key))
                    throw new ManifestException("LYR-PKG0003", file, line, 1, $"'{name}' has no key '{key}'");
                spec.TryGet(key, out var given);
                if (given is not string)
                    throw new ManifestException("LYR-PKG0002", file, line, 1, $"'{key}' of '{name}' is a string, not {Describe(given)}");
            }
            var hasPath = spec.TryGet("path", out var path);
            var hasGit = spec.TryGet("git", out var url);
            var revisions = Revisions.Where(spec.Contains).ToList();
            if (hasPath && hasGit)
                throw new ManifestException("LYR-PKG0002", file, line, 1, $"'{name}' names a directory and a repository: one of them");
            if (hasPath)
            {
                if (revisions.Count > 0)
                    throw new ManifestException("LYR-PKG0002", file, line, 1, $"'{revisions[0]}' of '{name}' names a revision of a repository, and '{name}' is a directory");
                dependencies.Add(new Dependency(name, System.IO.Path.GetFullPath((string)path, root), null, line));
                continue;
            }
            if (!hasGit)
                throw new ManifestException("LYR-PKG0002", file, line, 1, $"'{name}' needs a path or a git repository: {name} = {{ path = \"…\" }}");
            if (section == "override")
                throw new ManifestException("LYR-PKG0003", file, line, 1, $"an override reads a directory (07 P7): {name} = {{ path = \"…\" }}");
            dependencies.Add(new Dependency(name, null, GitForm(name, (string)url, spec, revisions, file, line), line));
        }
        return dependencies;
    }

    /// <summary>The git form: a URL git reads, and at most one revision of the repository.</summary>
    private static GitSource GitForm(string name, string url, TomlTable spec, List<string> revisions, string file, int line)
    {
        if (!GitUrlPattern().IsMatch(url))
            throw new ManifestException("LYR-PKG0002", file, line, 1,
                $"'{url}' is no git repository: https://, http://, ssh://, git://, file:// or user@host:path");
        if (revisions.Count > 1)
            throw new ManifestException("LYR-PKG0002", file, line, 1, $"'{name}' names a {revisions[0]} and a {revisions[1]}: one revision");
        if (revisions.Count == 0) return new GitSource(url, GitRefKind.Default, null);
        spec.TryGet(revisions[0], out var given);
        var revision = (string)given;
        var (kind, valid) = revisions[0] switch
        {
            "tag" => (GitRefKind.Tag, RefNamePattern().IsMatch(revision)),
            "branch" => (GitRefKind.Branch, RefNamePattern().IsMatch(revision)),
            _ => (GitRefKind.Rev, CommitPattern().IsMatch(revision)),
        };
        if (!valid)
            throw new ManifestException("LYR-PKG0002", file, line, 1, kind == GitRefKind.Rev
                ? $"'{revision}' is no commit: 7 to 64 hexadecimal digits"
                : $"'{revision}' is no {revisions[0]} name");
        return new GitSource(url, kind, revision);
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

    /// <summary>What git reads as a repository's address — a URL of its transports, or the scp
    /// form; never an option ('-…') or a transport that runs a command.</summary>
    [GeneratedRegex(@"^(?:(?:https?|ssh|git|file)://[^\s]+|[A-Za-z0-9._-]+@[A-Za-z0-9.-]+:[^\s]+)$")]
    private static partial Regex GitUrlPattern();

    /// <summary>A tag's or a branch's name as a manifest may write it: no option, no space, no
    /// revision syntax.</summary>
    [GeneratedRegex(@"^(?!.*\.\.)[A-Za-z0-9_][A-Za-z0-9._/+-]*$")]
    private static partial Regex RefNamePattern();

    [GeneratedRegex("^[0-9a-f]{7,64}$")]
    private static partial Regex CommitPattern();

    [GeneratedRegex("^[a-z][a-z0-9_-]*$")]
    private static partial Regex BinaryPattern();

    [GeneratedRegex(@"^(?:>=\s*)?(0|[1-9][0-9]{0,8})\.(0|[1-9][0-9]{0,8})(?:\.(0|[1-9][0-9]{0,8}))?$")]
    private static partial Regex ToolchainPattern();
}

/// <summary>A manifest the toolchain refuses: its code (11 W6), file, line and column, and why —
/// exit 1; a package from git that cannot be reached is the environment's failure, exit 2 (11 C5).</summary>
public sealed class ManifestException(string code, string file, int line, int column, string message, int exit = 1)
    : Exception(message)
{
    public string Code { get; } = code;
    public int Exit { get; } = exit;
    public string File { get; } = file;
    public int Line { get; } = line;
    public int Column { get; } = column;

    /// <summary>As the driver writes a diagnostic.</summary>
    public string Render() => $"error[{Code}]: {Message}\n  --> {File}:{Line}:{Column}";
}
