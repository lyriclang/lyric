using System.Collections.Concurrent;
using Lyric.AST;
using Lyric.Core;
using Lyric.Ir.Interp;
using Lyric.Lexing;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Compiler;

/// <summary>
/// One module of a <see cref="CheckedStdlib"/>: its resolved symbol, its post-synthesis and
/// loaded syntax trees, the extension blocks declared in it (excluding synthesized inline-array
/// blocks, which are keyed by array length), the inline-array lengths it mentions, and whether it
/// contains any coroutine.
/// </summary>
public sealed record CachedStdModule(
    ModuleSymbol Symbol,
    Module Ast,
    Module LoadedAst,
    IReadOnlyList<ExtensionBlock> Extensions,
    IReadOnlyList<int> InlineArrayLengths,
    bool UsesCoroutines);

/// <summary>
/// The standard library parsed, resolved, and typechecked once, kept across compilations in a
/// long-lived server so edits to a package only re-check the package's own modules.
///
/// <para>Immutable once built and safe to share across threads. Compilations fork its source
/// manager, share its builtin scope, chain their binding/type/documentation tables onto its
/// read-only base tables, and pull only the standard-library modules their imports reach.</para>
///
/// <para>A snapshot is invalidated whenever the standard library directory, any <c>.lyr</c> file
/// under <c>std/</c> (timestamp, length, or file count), the toolchain version, the build facts,
/// or overflow-check settings change, or when an open editor overlay covers a file inside the
/// standard library.</para>
/// </summary>
public sealed class CheckedStdlib
{
    private readonly record struct FileStamp(string FullPath, DateTime LastWriteUtc, long Length);

    private readonly record struct CacheKey(
        string Root, string ToolchainVersion, BuildFacts Build, bool OverflowChecks);

    private readonly SourceManager _sources;
    private readonly FileStamp[] _stamps;
    private readonly string _stdDir;
    private readonly Dictionary<string, CachedStdModule> _modules;
    private readonly Dictionary<int, IReadOnlyList<ExtensionBlock>> _inlineArrayExtensions;

    private CheckedStdlib(
        string root,
        string toolchainVersion,
        BuildFacts build,
        bool overflowChecks,
        FileStamp[] stamps,
        SourceManager sources,
        SymbolTable builtins,
        DocumentationTable documentation,
        BindingResult binding,
        TypeResult types,
        Dictionary<string, CachedStdModule> modules,
        Dictionary<int, IReadOnlyList<ExtensionBlock>> inlineArrayExtensions,
        IReadOnlyList<(AssociatedTypeSymbol Member, ExtensionBlock Block, LyrType Answer)> blockAnswers,
        IReadOnlyDictionary<GlobalSymbol, LyrType> globals,
        IReadOnlyDictionary<GlobalSymbol, Expr> foldedConstants,
        IReadOnlySet<FunctionDecl> metaDefaults,
        IReadOnlyDictionary<FileId, List<Token>> tokens,
        bool hasWhenOrDeprecated)
    {
        Root = root;
        _stdDir = Path.Combine(root, "std");
        ToolchainVersion = toolchainVersion;
        Build = build;
        OverflowChecks = overflowChecks;
        _stamps = stamps;
        _sources = sources;
        Builtins = builtins;
        Documentation = documentation;
        Binding = binding;
        Types = types;
        _modules = modules;
        _inlineArrayExtensions = inlineArrayExtensions;
        BlockAnswers = blockAnswers;
        Globals = globals;
        FoldedConstants = foldedConstants;
        MetaDefaults = metaDefaults;
        Tokens = tokens;
        HasWhenOrDeprecated = hasWhenOrDeprecated;
    }

    public string Root { get; }
    public string ToolchainVersion { get; }
    public BuildFacts Build { get; }
    public bool OverflowChecks { get; }

    public SymbolTable Builtins { get; }
    public DocumentationTable Documentation { get; }
    public BindingResult Binding { get; }
    public TypeResult Types { get; }
    internal IReadOnlyList<(AssociatedTypeSymbol Member, ExtensionBlock Block, LyrType Answer)> BlockAnswers { get; }
    internal IReadOnlyDictionary<GlobalSymbol, LyrType> Globals { get; }
    internal IReadOnlyDictionary<GlobalSymbol, Expr> FoldedConstants { get; }
    internal IReadOnlySet<FunctionDecl> MetaDefaults { get; }
    internal IReadOnlyDictionary<FileId, List<Token>> Tokens { get; }
    public bool HasWhenOrDeprecated { get; }

    /// <summary>A source manager holding all standard-library files (written and synthesized) under
    /// their cached file ids, ready for a compilation to append its own files to.</summary>
    public SourceManager ForkSources() => _sources.Fork();

    /// <summary>Looks up a cached standard-library module by its full dotted name.</summary>
    public CachedStdModule? FindModule(string fullName) =>
        _modules.TryGetValue(fullName, out var cached) ? cached : null;

    /// <summary>Cached inline-array extension blocks for <paramref name="length"/>, if synthesized
    /// when the standard library was checked.</summary>
    public IReadOnlyList<ExtensionBlock>? InlineArrayExtensionsOf(int length) =>
        _inlineArrayExtensions.TryGetValue(length, out var blocks) ? blocks : null;

    /// <summary>Whether every <c>.lyr</c> file under <c>std/</c> still matches the snapshot on disk
    /// (same file count, last-write timestamps, and lengths).</summary>
    public bool IsFreshOnDisk()
    {
        if (!Directory.Exists(_stdDir)) return _stamps.Length == 0;
        var count = 0;
        foreach (var _ in Directory.EnumerateFiles(_stdDir, "*.lyr", SearchOption.AllDirectories))
            count++;
        if (count != _stamps.Length) return false;
        foreach (var stamp in _stamps)
        {
            var info = new FileInfo(stamp.FullPath);
            if (!info.Exists || info.LastWriteTimeUtc != stamp.LastWriteUtc || info.Length != stamp.Length)
                return false;
        }
        return true;
    }

    /// <summary>Whether this cached snapshot can serve <paramref name="options"/>.</summary>
    public bool IsValidFor(CompilerOptions options)
    {
        if (options.WarnDeps) return false;
        var root = Path.GetFullPath(options.StdlibRoot ?? StdlibLoader.DefaultRoot());
        if (!PathComparer.Equals(root, Root)) return false;
        var version = options.ToolchainVersion ?? Core.ToolchainVersion.Value;
        if (!string.Equals(version, ToolchainVersion, StringComparison.Ordinal)) return false;
        if (options.Build != Build || options.OverflowChecks != OverflowChecks) return false;

        if (options.SourceOverlay is { Count: > 0 } overlay)
        {
            var prefix = Path.TrimEndingDirectorySeparator(Root) + Path.DirectorySeparatorChar;
            foreach (var path in overlay.Keys)
                if (Path.GetFullPath(path).StartsWith(prefix, PathComparison))
                    return false;
        }

        return IsFreshOnDisk();
    }

    /// <summary>Parses, resolves, and typechecks the entire standard library under
    /// <paramref name="root"/>. Returns <c>null</c> if the library directory is missing or has
    /// diagnostics so callers fall back cleanly to fresh compilation.</summary>
    public static CheckedStdlib? BuildSnapshot(
        string root,
        BuildFacts? build = null,
        string? toolchainVersion = null,
        bool overflowChecks = true)
    {
        var fullRoot = Path.GetFullPath(root);
        var stdDir = Path.Combine(fullRoot, "std");
        if (!Directory.Exists(stdDir)) return null;

        var files = Directory.EnumerateFiles(stdDir, "*.lyr", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .ToList();
        if (files.Count == 0) return null;

        var sources = new SourceManager();
        var diagnostics = new DiagnosticEngine(sources);
        var stamps = new FileStamp[files.Count];
        var compilation = new Compilation(sources, diagnostics) { WarnNative = false };

        for (var i = 0; i < files.Count; i++)
        {
            var full = Path.GetFullPath(files[i]);
            var info = new FileInfo(full);
            stamps[i] = new FileStamp(full, info.LastWriteTimeUtc, info.Length);

            var id = sources.AddFromDisk(full);
            var parsed = ParsedModule.Parse(sources, id, diagnostics);
            if (diagnostics.HasErrors) return null;

            var rel = Path.GetRelativePath(fullRoot, full);
            var moduleName = Path.ChangeExtension(rel, null)!
                .Replace(Path.DirectorySeparatorChar, '.')
                .Replace(Path.AltDirectorySeparatorChar, '.');
            compilation.AddModule(parsed.Ast, moduleName, isNative: true, documentation: parsed.Documentation);
        }

        CompilerPosition.Begin(sources);
        var binding = compilation.Resolve();
        if (diagnostics.HasErrors) return null;

        var checker = new TypeChecker(compilation, binding, diagnostics);
        var types = checker.Check();
        if (types.NestingExceeded || diagnostics.HasErrors) return null;

        // Run the remaining read-only semantic passes to ensure the cached library is clean.
        new SemaRules(compilation, binding, types, diagnostics, singleProgram: false).Run();
        new ExceptionAnalyzer(compilation, types, diagnostics, checker.ThrownCoveredBy, checker.ErrorRoot, checker.CancelledType).Run();
        if (diagnostics.HasErrors) return null;

        // Force-populate all lazy extension block metadata so blocks are immutable when shared.
        checker.SealExtensions();

        var inlineArrayExtensions = new Dictionary<int, List<ExtensionBlock>>();
        var byModule = new Dictionary<ModuleSymbol, List<ExtensionBlock>>(ReferenceEqualityComparer.Instance);
        foreach (var block in compilation.Extensions.Blocks)
        {
            if (compilation.InlineArrayBlocks.Contains(block.Decl) && block.Decl.Target is ArrayType { Length: { } len })
            {
                if (!inlineArrayExtensions.TryGetValue(len, out var list))
                    inlineArrayExtensions[len] = list = [];
                list.Add(block);
                continue;
            }
            if (!byModule.TryGetValue(block.Module, out var modBlocks))
                byModule[block.Module] = modBlocks = [];
            modBlocks.Add(block);
        }

        var modules = new Dictionary<string, CachedStdModule>(StringComparer.Ordinal);
        foreach (var module in compilation.Modules)
        {
            var ast = compilation.AstOf(module);
            var loaded = compilation.LoadedAstOf(module);
            var lengths = CollectInlineArrayLengths(ast);
            var coroutines = Compilation.ModuleUsesCoroutines(ast);
            var exts = byModule.TryGetValue(module, out var list) ? (IReadOnlyList<ExtensionBlock>)list : [];
            modules[module.FullName] = new CachedStdModule(module, ast, loaded, exts, lengths, coroutines);
        }

        var frozenInline = inlineArrayExtensions.ToDictionary(
            kv => kv.Key, kv => (IReadOnlyList<ExtensionBlock>)kv.Value);

        var hasWhenOrDeprecated = DetectWhenOrDeprecated(compilation, types);

        return new CheckedStdlib(
            fullRoot,
            toolchainVersion ?? Core.ToolchainVersion.Value,
            build ?? BuildFacts.Host,
            overflowChecks,
            stamps,
            sources,
            compilation.Builtins,
            compilation.Documentation,
            binding,
            types,
            modules,
            frozenInline,
            checker.BlockAnswers.ToArray(),
            new Dictionary<GlobalSymbol, LyrType>(checker.Globals, ReferenceEqualityComparer.Instance),
            new Dictionary<GlobalSymbol, Expr>(checker.FoldedConstants, ReferenceEqualityComparer.Instance),
            new HashSet<FunctionDecl>(checker.MetaDefaults, ReferenceEqualityComparer.Instance),
            new Dictionary<FileId, List<Token>>(checker.Tokens),
            hasWhenOrDeprecated);
    }

    private static readonly ConcurrentDictionary<CacheKey, CheckedStdlib?> SharedCache = new();
    private static readonly ConcurrentDictionary<CacheKey, object> CacheLocks = new();

    /// <summary>Returns a cached checked standard library for the given key, building or
    /// refreshing it when absent or stale on disk.</summary>
    public static CheckedStdlib? Shared(
        string root,
        BuildFacts? build = null,
        string? toolchainVersion = null,
        bool overflowChecks = true)
    {
        var key = new CacheKey(
            NormalizePath(root),
            toolchainVersion ?? Core.ToolchainVersion.Value,
            build ?? BuildFacts.Host,
            overflowChecks);

        if (SharedCache.TryGetValue(key, out var existing) && existing is not null && existing.IsFreshOnDisk())
            return existing;

        var gate = CacheLocks.GetOrAdd(key, _ => new object());
        lock (gate)
        {
            if (SharedCache.TryGetValue(key, out existing) && existing is not null && existing.IsFreshOnDisk())
                return existing;
            var built = BuildSnapshot(key.Root, key.Build, key.ToolchainVersion, key.OverflowChecks);
            SharedCache[key] = built;
            return built;
        }
    }

    private static int[] CollectInlineArrayLengths(Module ast)
    {
        var lengths = new HashSet<int>();
        var pending = new Stack<Node>();
        pending.Push(ast);
        while (pending.Count > 0)
        {
            var node = pending.Pop();
            if (node is ArrayType { Length: { } n }) lengths.Add(n);
            foreach (var child in AstChildren.Of(node)) pending.Push(child);
        }
        if (lengths.Count == 0) return [];
        var arr = lengths.ToArray();
        Array.Sort(arr);
        return arr;
    }

    private static bool DetectWhenOrDeprecated(Compilation compilation, TypeResult types)
    {
        var core = compilation.FindModule(["std", "core"])?.Members;
        var when = core?.LookupLocal("When") as TypeSymbol;
        var deprecated = core?.LookupLocal("Deprecated") as TypeSymbol;
        if (when is null && deprecated is null) return false;

        bool Matches(AttributeNode[] attrs)
        {
            foreach (var a in attrs)
            {
                var r = types.RefOf(a);
                if (ReferenceEquals(r, when) || ReferenceEquals(r, deprecated)) return true;
            }
            return false;
        }

        foreach (var module in compilation.Modules)
        {
            var ast = compilation.AstOf(module);
            if (Matches(ast.Attributes)) return true;
            foreach (var decl in ast.Declarations)
            {
                var attrs = decl switch
                {
                    FunctionDecl f => f.Attributes,
                    StructDecl s => s.Attributes,
                    ClassDecl c => c.Attributes,
                    EnumDecl e => e.Attributes,
                    InterfaceDecl i => i.Attributes,
                    TypeAliasDecl a => a.Attributes,
                    GlobalBindingDecl g => g.Attributes,
                    ExtendDecl x => x.Attributes,
                    _ => [],
                };
                if (Matches(attrs)) return true;
            }
        }
        return false;
    }

    private static string NormalizePath(string path)
    {
        var full = Path.GetFullPath(path);
        return OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? full.ToLowerInvariant() : full;
    }

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
