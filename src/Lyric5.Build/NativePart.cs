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

/// <summary>A source tree of <c>[native.vendor]</c>: C the package does not hold and that is no
/// package — a revision of a repository, or a file at a URL pinned by its hash. The native part's
/// paths name it <c>@name/…</c>.</summary>
/// <param name="Git">The repository and its revision; <c>null</c> for a URL's tree.</param>
/// <param name="Url">The file's URL; <c>null</c> for a tree from git.</param>
/// <param name="Hash">The file's SHA-256, in lowercase hexadecimal; <c>null</c> where the manifest
/// gives none — refused once the build has loaded the file and can name it.</param>
/// <param name="Line">The line it is written on, for what is refused about it.</param>
public sealed record VendorTree(string Name, GitSource? Git, string? Url, string? Hash, int Line)
{
    /// <summary>Where the tree is read from, as a message says it.</summary>
    public string Describe() => Git?.ToString() ?? Url!;
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

    /// <summary>The triples a table may name: Tier 1 and Tier 2.</summary>
    private static readonly Dictionary<string, Target> Triples =
        Target.Tier1.Concat(Target.Tier2).ToDictionary(t => t.Triple, StringComparer.Ordinal);

    /// <summary>This part with <paramref name="more"/> on top.</summary>
    public NativePart With(NativePart more) =>
        new([.. Sources, .. more.Sources], [.. Include, .. more.Include], [.. Libs, .. more.Libs],
            [.. Archives, .. more.Archives], [.. Dylibs, .. more.Dylibs], [.. Defines, .. more.Defines]);

    /// <summary><c>[native]</c>, its tables per operating system and per triple, and its source
    /// trees, checked: a path <c>@name/…</c> names a tree <c>[native.vendor]</c> holds.</summary>
    /// <exception cref="ManifestException">A key, a table, a pattern, a path, a definition, a
    /// library name or a tree is refused.</exception>
    public static (NativePart Common, IReadOnlyDictionary<TargetOs, NativePart> PerSystem,
        IReadOnlyDictionary<string, NativePart> PerTriple, IReadOnlyList<VendorTree> Vendor) Read(TomlTable document, string file)
    {
        var perSystem = new Dictionary<TargetOs, NativePart>();
        var perTriple = new Dictionary<string, NativePart>(StringComparer.Ordinal);
        if (!document.TryGet("native", out var found)) return (None, perSystem, perTriple, []);
        if (found is not TomlTable native)
            throw new ManifestException("LYR-PKG0002", file, 1, 1, "[native] is a table: [native] sources = [\"native/*.c\"]");
        List<VendorTree> vendor = [];
        foreach (var key in native.Keys)
        {
            if (Systems.TryGetValue(key, out var os))
                perSystem[os] = Part(Table(native, key, file), $"native.{key}", file, os);
            else if (Triples.TryGetValue(key, out var target))
                perTriple[key] = Part(Table(native, key, file), $"native.{key}", file, target.Os);
            else if (key == "vendor")
                vendor = ReadVendor(Table(native, key, file), file);
            else if (native.TryGet(key, out var value) && value is TomlTable)
                throw new ManifestException("LYR-PKG0003", file, native.Line, 1,
                    $"[native.{key}] names no operating system and no target: linux, windows, macos, or one of "
                    + string.Join(", ", Triples.Keys) + " — and [native.vendor] the source trees");
        }
        var common = Part(native, "native", file, null);
        var trees = vendor.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        var tables = perSystem.Select(p => (Where: $"native.{Systems.First(s => s.Value == p.Key).Key}", Part: p.Value))
            .Concat(perTriple.Select(p => (Where: $"native.{p.Key}", Part: p.Value)))
            .Prepend((Where: "native", Part: common));
        foreach (var (where, part) in tables)
            foreach (var (key, path) in part.Paths())
                if (TreePath(path) is { } tree && !trees.Contains(tree.Name))
                    throw new ManifestException("LYR-PKG0002", file, native.Line, 1,
                        $"'{path}' in '{key}' of [{where}] names the source tree '{tree.Name}', which [native.vendor] does not hold"
                        + (trees.Count > 0 ? $": {string.Join(", ", trees.Order(StringComparer.Ordinal))}" : ""));
        return (common, perSystem, perTriple, vendor);
    }

    /// <summary>Every path this part names, with its key — the patterns of <c>sources</c> among them.</summary>
    public IEnumerable<(string Key, string Path)> Paths() =>
        Sources.Select(s => ("sources", s)).Concat(Include.Select(i => ("include", i))).Concat(Archives.Select(a => ("archives", a)))
            .Concat(Dylibs.SelectMany(d => d.ImportLibrary is null ? [d.File] : new[] { d.File, d.ImportLibrary }).Select(d => ("dylibs", d)));

    /// <summary>A path of a source tree, <c>@name/rest</c>: the tree's name and the path within it —
    /// empty for the tree's root; <c>null</c> for a path of the package.</summary>
    public static (string Name, string Within)? TreePath(string path)
    {
        if (!path.StartsWith('@')) return null;
        var slash = path.IndexOf('/');
        return slash < 0 ? (path[1..], "") : (path[1..slash], path[(slash + 1)..]);
    }

    /// <summary><c>name = { git = "…", tag | branch | rev = "…" }</c> or <c>name = { url = "…",
    /// hash = "sha256:…" }</c>.</summary>
    private static List<VendorTree> ReadVendor(TomlTable table, string file)
    {
        var trees = new List<VendorTree>();
        foreach (var name in table.Keys)
        {
            table.TryGet(name, out var value);
            var line = value is TomlTable at ? at.Line : table.Line;
            if (!TreeNamePattern().IsMatch(name))
                throw new ManifestException("LYR-PKG0002", file, line, 1,
                    $"'{name}' is no name of a source tree: a lowercase letter, then letters, digits, '_' and '-'");
            if (value is not TomlTable spec || spec.Keys.Any(k => !spec.TryGet(k, out var v) || v is not string))
                throw new ManifestException("LYR-PKG0002", file, line, 1,
                    $"the source tree '{name}' is written {name} = {{ git = \"…\", tag = \"…\" }} or {name} = {{ url = \"…\", hash = \"sha256:…\" }}");
            if (spec.TryGet("git", out var repository))
            {
                if (spec.Keys.FirstOrDefault(k => k is not ("git" or "tag" or "branch" or "rev")) is { } other)
                    throw new ManifestException("LYR-PKG0002", file, line, 1,
                        $"'{other}' is no key of the source tree '{name}', which is read from git: git, and tag, branch or rev");
                var revisions = new[] { "tag", "branch", "rev" }.Where(spec.Contains).ToList();
                trees.Add(new VendorTree(name, Manifest.GitForm(name, (string)repository, spec, revisions, file, line), null, null, line));
                continue;
            }
            if (!spec.TryGet("url", out var given))
                throw new ManifestException("LYR-PKG0002", file, line, 1,
                    $"the source tree '{name}' needs a repository or a URL: {name} = {{ git = \"…\" }} or {name} = {{ url = \"…\", hash = \"sha256:…\" }}");
            if (spec.Keys.FirstOrDefault(k => k is not ("url" or "hash")) is { } extra)
                throw new ManifestException("LYR-PKG0002", file, line, 1,
                    $"'{extra}' is no key of the source tree '{name}', which is loaded from a URL: url and hash");
            var url = (string)given;
            if (!TreeUrlPattern().IsMatch(url))
                throw new ManifestException("LYR-PKG0002", file, line, 1, $"'{url}' is no URL a tree is loaded from: https://, http:// or file://");
            string? hash = null;
            if (spec.TryGet("hash", out var pinned))
            {
                var match = TreeHashPattern().Match((string)pinned);
                if (!match.Success)
                    throw new ManifestException("LYR-PKG0002", file, line, 1, $"'{pinned}' is no hash of a file: sha256: and 64 hexadecimal digits");
                hash = match.Groups["hex"].Value.ToLowerInvariant();
            }
            trees.Add(new VendorTree(name, null, url, hash, line));
        }
        return trees;
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
            Strings("sources", s => Within(s, PackageContent.IsPattern) ? null : "is no pattern of the package's files or a tree's",
                "patterns of the package's files"),
            Strings("include", d => Within(d, rest => PackageContent.IsPattern(rest) && rest.IndexOfAny(['*', '?']) < 0, root: true)
                ? null : "is no directory of the package or a tree's", "directories of the package"),
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

    /// <summary>A path a manifest names one file by: a pattern of the package or of a tree without
    /// wildcards.</summary>
    private static bool IsPath(string path) =>
        Within(path, rest => PackageContent.IsPattern(rest) && rest.IndexOfAny(['*', '?']) < 0 && !rest.EndsWith('/'));

    /// <summary>Whether <paramref name="path"/> is of the form <paramref name="form"/> — in the
    /// package, or after <c>@name/</c> in a tree, whose root itself a <paramref name="root"/> path
    /// may name.</summary>
    private static bool Within(string path, Func<string, bool> form, bool root = false) => TreePath(path) is { } tree
        ? TreeNamePattern().IsMatch(tree.Name) && (root && tree.Within.Length == 0 || form(tree.Within))
        : form(path);

    private static bool IsShared(string path) =>
        SoPattern().IsMatch(path) || path.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);

    /// <summary>The C sources this part names: the package's under <paramref name="root"/>, sorted,
    /// then each tree's, in the order the trees are first named, under the directory
    /// <paramref name="tree"/> gives for it.</summary>
    public IReadOnlyList<string> SourceFiles(string root, Func<string, string> tree)
    {
        var own = Sources.Where(s => TreePath(s) is null).ToList();
        var files = Matching(root, own, skipOut: true).ToList();
        foreach (var name in Sources.Select(s => TreePath(s)?.Name).OfType<string>().Distinct())
            files.AddRange(Matching(tree(name), [.. Sources.Select(TreePath).Where(t => t?.Name == name).Select(t => t!.Value.Within)], skipOut: false));
        return files;
    }

    private static IEnumerable<string> Matching(string root, IReadOnlyList<string> patterns, bool skipOut)
    {
        if (patterns.Count == 0 || !Directory.Exists(root)) return [];
        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(file => (Relative: Path.GetRelativePath(root, file).Replace('\\', '/'), Full: file))
            .Where(file => !(skipOut && file.Relative.StartsWith("out/", StringComparison.Ordinal)) && PackageContent.Matches(patterns, file.Relative))
            .OrderBy(file => file.Relative, StringComparer.Ordinal)
            .Select(file => file.Full);
    }

    [GeneratedRegex("^[A-Za-z0-9_][A-Za-z0-9_.+-]*$")]
    private static partial Regex LibraryPattern();

    [GeneratedRegex("^[a-z][A-Za-z0-9_-]*$")]
    private static partial Regex TreeNamePattern();

    [GeneratedRegex("^(https?|file)://.+", RegexOptions.IgnoreCase)]
    private static partial Regex TreeUrlPattern();

    [GeneratedRegex("^sha256:(?<hex>[0-9A-Fa-f]{64})$")]
    private static partial Regex TreeHashPattern();

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*(=.*)?$", RegexOptions.Singleline)]
    private static partial Regex DefinePattern();

    /// <summary>A shared library of ELF: <c>libfoo.so</c>, or with its version, <c>libfoo.so.1.2</c>.</summary>
    [GeneratedRegex(@"\.so(\.[0-9]+)*$")]
    private static partial Regex SoPattern();
}
