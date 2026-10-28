using System.Security.Cryptography;
using System.Text;
using Lyric5.Toolchain;

namespace Lyric5.Build;

/// <summary>
/// The modules the program's packages make from C headers (spec 15 §5.4), for one target:
/// <c>out/cache/bindings/&lt;triple&gt;/&lt;package&gt;/</c>, a module <c>game.rl</c> as <c>rl.lyr</c>
/// there, read as a package's <c>gen/</c> is. A module is made anew where a file the C compiler read,
/// the package's <c>include</c> or <c>defines</c>, the target, the toolchain or the generator
/// changed — its key, beside it, holds them.
/// </summary>
public static class Bindings
{
    /// <summary>Where <paramref name="package"/>'s modules for <paramref name="target"/> are kept.</summary>
    public static string Root(Project project, Target target, string package) =>
        Path.Combine(project.CacheDir, "bindings", target.Triple, package);

    /// <summary>The packages' roots for <paramref name="target"/> that are there, by name — for the
    /// compiler, as <c>gen/</c>'s are; <c>null</c> where there is none.</summary>
    public static IReadOnlyDictionary<string, string>? Roots(Project project, Target? target) =>
        project.Graph is { } graph ? Roots(project.CacheDir, graph, target) : null;

    /// <summary>As <see cref="Roots(Project, Target?)"/>, for a graph read without a program — a
    /// library's, whose cache is its root's <c>out/cache</c>.</summary>
    public static IReadOnlyDictionary<string, string>? Roots(string cacheDir, PackageGraph graph, Target? target)
    {
        var roots = graph.Packages.Keys
            .Select(name => (Name: name, Root: Path.Combine(cacheDir, "bindings", (target ?? Target.Host).Triple, name)))
            .Where(r => Directory.Exists(r.Root))
            .ToDictionary(r => r.Name, r => r.Root, StringComparer.Ordinal);
        return roots.Count > 0 ? roots : null;
    }

    /// <summary>Makes every package's bindings for <paramref name="target"/> that is not made yet;
    /// 0, or the exit code after reporting: a header that does not compile, a module the toolchain
    /// cannot write (<c>LYR-BLD0005</c>), a module that lies under <c>src/</c>, <c>gen/</c> or
    /// <c>tests/</c> too (<c>LYR-RES0016</c>), a source tree that cannot be read.</summary>
    public static int Make(Project project, Target target, CCompiler compiler, string version, TextWriter error)
    {
        if (project.Graph is not { } graph) return 0;
        // the syntax tree comes from zig cc or clang; gcc writes none
        if (compiler.Kind == CCompilerKind.Gcc && CCompiler.Locate(CCompilerKind.Zig) is { } zig) compiler = zig;
        foreach (var manifest in graph.Packages.Values.OrderBy(m => m.Name, StringComparer.Ordinal))
        {
            var part = manifest.NativeFor(target);
            var root = Root(project, target, manifest.Name);
            if (part.Bindings.Count == 0)
            {
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
                continue;
            }
            var made = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                foreach (var binding in part.Bindings)
                {
                    var segments = binding.Module.Split('.');
                    var file = Path.Combine([root, .. segments[..^1], segments[^1] + ".lyr"]);
                    if (!made.Add(file))
                    {
                        Say.Error(error, "LYR-PKG0002", $"{manifest.Name}'s manifest names the module '{binding.Module}' in 'bindings' twice");
                        return 1;
                    }
                    foreach (var (place, shown) in new[] { (manifest.SourceRoot, "src"), (manifest.GenRoot, "gen"), (Path.Combine(manifest.Root, "tests"), "tests") })
                    {
                        var other = Path.Combine([place, .. segments[..^1], segments[^1] + ".lyr"]);
                        if (!File.Exists(other)) continue;
                        Say.Error(error, "LYR-RES0016",
                            $"the module {manifest.Name}.{binding.Module} is made from '{binding.Header}' and lies in {shown}/ too: "
                            + $"'{shown}/{string.Join('/', segments)}.lyr' — rename one of them");
                        return 1;
                    }
                    var header = Full(graph, manifest, binding.Header);
                    var include = Include(graph, manifest, target);
                    if (Fresh(file, Key(version, target, header, include, part.Defines))) continue;
                    if (!File.Exists(header))
                    {
                        Say.Error(error, "LYR-PKG0002", $"{manifest.Name}'s manifest names '{binding.Header}' in 'bindings', and there is no such file");
                        return 1;
                    }
                    Bound bound;
                    try { bound = Bindgen.Make(compiler, target, header, include, part.Defines, binding.Header, version); }
                    catch (BindgenException failed)
                    {
                        Say.Error(error, "LYR-BLD0005", $"{manifest.Name}'s bindings of '{binding.Header}' for {target.Triple} cannot be made: {failed.Message}");
                        return 1;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                    File.WriteAllText(file, bound.Text);
                    File.WriteAllText(file + ".key", Key(version, target, header, include, part.Defines) + "\n"
                        + string.Concat(bound.Read.Order(StringComparer.Ordinal).Select(read => $"{HashOf(read)}  {read}\n")));
                }
            }
            catch (ManifestException refused)
            {
                // a header in a source tree that cannot be read
                Say.Manifest(error, refused);
                return refused.Exit;
            }
            // a module no entry makes any more is gone
            foreach (var stale in Directory.EnumerateFiles(root, "*.lyr", SearchOption.AllDirectories).Where(f => !made.Contains(f)).ToList())
            {
                File.Delete(stale);
                File.Delete(stale + ".key");
            }
        }
        return 0;
    }

    /// <summary>A path of the native part: the package's, or a tree's (<c>@name/…</c>).</summary>
    public static string Full(PackageGraph graph, Manifest manifest, string path) =>
        NativePart.TreePath(path) is { } tree
            ? Path.Combine(graph.Trees!.Directory(manifest, tree.Name), tree.Within)
            : Path.Combine(manifest.Root, path);

    /// <summary>What the header is compiled with: the package's <c>include</c> and the <c>include</c>
    /// of each package it declares, as its sources see them (spec 15 §5 rule 6).</summary>
    public static List<string> Include(PackageGraph graph, Manifest manifest, Target target)
    {
        List<string> include = [.. manifest.NativeFor(target).Include.Select(d => Full(graph, manifest, d))];
        foreach (var dependency in manifest.Dependencies.Select(d => d.Name).Distinct().Order(StringComparer.Ordinal))
            if (graph.Packages.TryGetValue(dependency, out var declared))
                include.AddRange(declared.NativeFor(target).Include.Select(d => Full(graph, declared, d)));
        return include;
    }

    /// <summary>What a module is made from besides the files it read.</summary>
    private static string Key(string version, Target target, string header, IReadOnlyList<string> include, IReadOnlyList<string> defines) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n",
            [$"lyric {version}", $"bindgen {Bindgen.Version}", target.Triple, Path.GetFullPath(header),
             .. include.Select(Path.GetFullPath).Select(i => "-I" + i), .. defines.Select(d => "-D" + d)]))));

    /// <summary>Whether the module is there and made from what it would be made of now.</summary>
    private static bool Fresh(string file, string key)
    {
        if (!File.Exists(file) || !File.Exists(file + ".key")) return false;
        var lines = File.ReadAllLines(file + ".key");
        if (lines.Length == 0 || lines[0] != key) return false;
        foreach (var line in lines.Skip(1))
        {
            var space = line.IndexOf("  ", StringComparison.Ordinal);
            if (space < 0) return false;
            var read = line[(space + 2)..];
            if (!File.Exists(read) || HashOf(read) != line[..space]) return false;
        }
        return true;
    }

    private static string HashOf(string file) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)));
}
