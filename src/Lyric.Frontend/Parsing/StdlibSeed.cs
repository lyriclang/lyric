using Lyric.Core;
using Lyric.Resolver;

namespace Lyric.Parsing;

/// <summary>
/// The standard library parsed once, for compilations that follow each other — an editor's, one
/// per keystroke. A compilation that holds the seed starts from <see cref="Fork"/>, a source
/// manager with the library's files under the ids they were parsed under, and its loader hands out
/// the parsed modules instead of reading and parsing the files again.
///
/// <para>Only the parse is shared. A tree is immutable and every table a compilation keeps about
/// it is its own, keyed by the node; what the resolver and the checker make of a module is made
/// again, because what a program adds — a conformance block on a library type — reaches into the
/// library's own code.</para>
///
/// <para>Immutable once built, so compilations on several threads may share it. A file whose text
/// is no longer the one parsed — the library edited in place — is read and parsed as without a
/// seed, and so is one an editor holds open (the overlay wins).</para>
/// </summary>
public sealed class StdlibSeed
{
    private readonly SourceManager _sources;
    private readonly Dictionary<string, (LoadedModule Module, DateTime Written, long Length)> _modules;

    private StdlibSeed(string root, SourceManager sources,
        Dictionary<string, (LoadedModule, DateTime, long)> modules)
    {
        Root = root;
        _sources = sources;
        _modules = modules;
    }

    /// <summary>The library's directory, as <see cref="CompilerOptions.StdlibRoot"/> names it.</summary>
    public string Root { get; }

    /// <summary>
    /// Parses every module under <paramref name="root"/>. A file that does not parse cleanly is
    /// left out: it is parsed again by each compilation, which reports what is wrong with it.
    /// </summary>
    public static StdlibSeed Parse(string root, bool lyric5 = true)
    {
        var sources = new SourceManager { Lyric5 = lyric5 };
        var modules = new Dictionary<string, (LoadedModule, DateTime, long)>(PathComparer);
        var std = Path.Combine(root, "std");
        if (Directory.Exists(std))
        {
            foreach (var file in Directory.EnumerateFiles(std, "*.lyr", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                var full = Path.GetFullPath(file);
                var info = new FileInfo(full);
                var diagnostics = new DiagnosticEngine(sources);
                var parsed = ParsedModule.Parse(sources, sources.AddFromDisk(full), diagnostics);
                if (diagnostics.SortedSnapshot().Count > 0) continue;
                modules[full] = (new LoadedModule(parsed.Ast, IsNative: true, parsed.Documentation), info.LastWriteTimeUtc, info.Length);
            }
        }
        return new StdlibSeed(Path.GetFullPath(root), sources, modules);
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<StdlibSeed>> Seeds =
        new(PathComparer);

    /// <summary>The seed of <paramref name="root"/> for this process, parsed by the first to ask:
    /// several servers in one process — a test run's — share one parse.</summary>
    public static StdlibSeed Shared(string root) =>
        Seeds.GetOrAdd(Path.GetFullPath(root), full => new Lazy<StdlibSeed>(() => Parse(full))).Value;

    /// <summary>Whether this seed is the library a compilation reads from <paramref name="root"/>.</summary>
    public bool Serves(string root) => PathComparer.Equals(Path.GetFullPath(root), Root);

    /// <summary>A source manager to compile in: the library's files under their ids, and room for
    /// the compilation's own.</summary>
    public SourceManager Fork() => _sources.Fork();

    /// <summary>The parsed module of a library file, while the file is still the one parsed.</summary>
    public LoadedModule? Find(string file)
    {
        var full = Path.GetFullPath(file);
        if (!_modules.TryGetValue(full, out var known)) return null;
        var info = new FileInfo(full);
        return info.Exists && info.LastWriteTimeUtc == known.Written && info.Length == known.Length ? known.Module : null;
    }

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
