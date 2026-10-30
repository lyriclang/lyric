using Lyric4.AST;
using Lyric4.Core;
using Lyric4.Parsing;
using Lyric4.Resolver;
using Lyric4.Sema;

namespace Lyric4.From4;

/// <summary>A Lyric 4 program as 4.6 read it: its sources, what 4.6 said of it, and — where it
/// resolved — every module, binding and type, which the migration rules read.</summary>
public sealed record Model(
    SourceManager Sources,
    DiagnosticEngine Diagnostics,
    Compilation Compilation,
    BindingResult Binding,
    TypeResult Types,
    IReadOnlyList<Module> Roots)
{
    /// <summary>Whether 4.6 refused the program: such a program is not migrated.</summary>
    public bool HasErrors => Diagnostics.HasErrors;
}

/// <summary>
/// Reads a Lyric 4 program with Lyric 4.6's own front end, frozen in this assembly: the parser,
/// the resolver and the checker of release/v4.6.0, and 4.6's standard library from this
/// assembly's resources — what 4.6's <c>lyric check</c> did, without lowering. A program that 4.6
/// accepted is accepted here, and one it refused is refused, with 4.6's diagnostics.
/// </summary>
public static class From4
{
    /// <summary>Where the standard library seems to lie: its files are served from the resources
    /// by path under this root, which nothing on disk has to hold.</summary>
    private static readonly string StdlibRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "lyric4-stdlib"));

    private static readonly Lazy<IReadOnlyDictionary<string, string>> Stdlib = new(ReadStdlib);

    /// <summary>One file, a program of its own, as <c>lyric check file.lyr</c> read it: its
    /// imports beside it, <c>std</c> from 4.6's library.</summary>
    public static Model CheckFile(string path)
    {
        var full = Path.GetFullPath(path);
        return Check([full], Path.GetDirectoryName(full)!, singleProgram: true, project: null);
    }

    /// <summary>A project as its <c>lyric.json</c> names it: every <c>.lyr</c> under its source root a
    /// module named by its path there, its dependencies and native roots as 4.6 loaded them.</summary>
    /// <exception cref="ProjectFileException">The <c>lyric.json</c> is not one 4.6 read.</exception>
    public static Model CheckProject(string lyricJson)
    {
        var project = ProjectFile.Read(Path.GetFullPath(lyricJson));
        var files = Directory.EnumerateFiles(project.SourceRoot, "*.lyr", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .ToList();
        return Check(files, project.SourceRoot, singleProgram: false, project);
    }

    private static Model Check(IReadOnlyList<string> files, string sourceRoot, bool singleProgram, ProjectFile? project)
    {
        var sources = new SourceManager();
        var diagnostics = new DiagnosticEngine(sources);
        var compilation = new Compilation(sources, diagnostics) { ModuleLoader = Loader(sources, diagnostics, sourceRoot, project) };

        var roots = new List<Module>();
        foreach (var file in files)
        {
            FileId id;
            try { id = sources.AddFromDisk(file); }
            catch (IOException) { continue; }
            var parsed = ParsedModule.Parse(sources, id, diagnostics);
            // a single file is the module 'main' to 4.6; a project's are named by their path, unless a header says otherwise
            var name = singleProgram || parsed.Ast.Header is not null ? null : ModuleNameUnder(sourceRoot, file);
            compilation.AddModule(parsed.Ast, name, documentation: parsed.Documentation);
            roots.Add(parsed.Ast);
        }

        var binding = compilation.Resolve();
        var types = Semantics.Analyze(compilation, binding, diagnostics, singleProgram);
        return new Model(sources, diagnostics, compilation, binding, types, roots);
    }

    /// <summary>4.6's loader: <c>std</c> from the library, a native root's segment from it, a
    /// dependency's from its project, everything else from the program's own root.</summary>
    private static Func<string[], LoadedModule?> Loader(SourceManager sources, DiagnosticEngine diagnostics, string sourceRoot, ProjectFile? project)
    {
        var fromStdlib = StdlibLoader.ForRoot(StdlibRoot, sources, diagnostics, Stdlib.Value);
        var fromProject = StdlibLoader.ForProject(sourceRoot, sources, diagnostics);
        var native = project?.NativeRoots.ToDictionary(e => e.Key, e => StdlibLoader.ForRoot(e.Value, sources, diagnostics), StringComparer.Ordinal);
        var dependencies = project?.Dependencies.ToDictionary(e => e.Key, e => StdlibLoader.ForProject(e.Value, sources, diagnostics), StringComparer.Ordinal);
        return path =>
        {
            if (path is ["std", ..]) return fromStdlib(path);
            if (path.Length > 0 && native is not null && native.TryGetValue(path[0], out var nativeRoot)) return nativeRoot(path);
            if (path.Length > 0 && dependencies is not null && dependencies.TryGetValue(path[0], out var dependency)) return dependency(path);
            return fromProject(path);
        };
    }

    private static string ModuleNameUnder(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        var stem = relative.EndsWith(".lyr", StringComparison.OrdinalIgnoreCase) ? relative[..^".lyr".Length] : relative;
        return string.Join('.', stem.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    }

    /// <summary>4.6's standard library from the resources, by the path the loader asks under
    /// <see cref="StdlibRoot"/>.</summary>
    private static IReadOnlyDictionary<string, string> ReadStdlib()
    {
        var assembly = typeof(From4).Assembly;
        var files = new Dictionary<string, string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var name in assembly.GetManifestResourceNames())
        {
            if (!name.StartsWith("stdlib/", StringComparison.Ordinal)) continue;
            using var stream = assembly.GetManifestResourceStream(name)!;
            var relative = name["stdlib/".Length..].Replace('/', Path.DirectorySeparatorChar);
            files[Path.GetFullPath(Path.Combine(StdlibRoot, relative))] = new StreamReader(stream).ReadToEnd();
        }
        return files;
    }
}
