using Lyric.Compiler;
using Lyric5.Doc;
using Lyric5.Doc.Api;

namespace Lyric5.Build;

/// <summary>
/// <c>lyric doc</c>: a package's public surface, read from its check, written as a reference apart
/// from the site — the package's name in the header, its pages under its name, the theme beside
/// them, and an <c>index.html</c> at the top that leads there.
/// </summary>
public static class DocRun
{
    /// <summary>The surface of a package's own modules: every module under its <c>src/</c>,
    /// checked as a library is; <c>null</c> after reporting where the check fails.</summary>
    public static Surface? PackageSurface(Manifest manifest, PackageGraph graph, TextWriter error)
    {
        var result = Pipeline.CheckPackage(manifest, graph);
        if (!result.Render(error) || CompilationOf(result) is not { } compilation) return null;
        var root = Path.GetFullPath(manifest.SourceRoot) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var ours = compilation.Modules.Where(m =>
            Path.GetFullPath(compilation.Sources.GetPath(compilation.LoadedAstOf(m).Span.File)).StartsWith(root, comparison));
        return SurfaceReader.Read(compilation, ours, manifest.Name, manifest.Version);
    }

    /// <summary>The surface of the standard library beside the toolchain: every module of
    /// <c>stdlib5/std</c>.</summary>
    public static Surface? StdSurface(string version, TextWriter error, string? stdlibRoot = null)
    {
        var library = stdlibRoot ?? Pipeline.StdlibRoot;
        var std = Path.Combine(library, "std");
        var modules = Directory.EnumerateFiles(std, "*.lyr", SearchOption.AllDirectories)
            .Select(f => "std." + Path.GetRelativePath(std, f)[..^4].Replace(Path.DirectorySeparatorChar, '.').Replace('/', '.'))
            .Order(StringComparer.Ordinal)
            .ToList();
        var root = ScriptSource.FromText("docs", string.Concat(modules.Select(m => $"import {m};\n")));
        var result = SourceCompiler.CheckProject([root], new CompilerOptions
        {
            StdlibRoot = library,
            PackageRoots = new Dictionary<string, string>(StringComparer.Ordinal),
        });
        // The root that imports every module is the run's own: what it warns about is no one's.
        if (!result.Ok)
        {
            result.Diagnostics.RenderText(error);
            return null;
        }
        if (CompilationOf(result) is not { } compilation) return null;
        var std5 = compilation.Modules.Where(m => m.FullName.StartsWith("std.", StringComparison.Ordinal));
        return SurfaceReader.Read(compilation, std5, "std", version);
    }

    private static Lyric.Resolver.Compilation? CompilationOf(CompileResult result) => result.Model?.Compilation;

    /// <summary>The reference's pages apart from the site, with the <c>index.html</c> at the top.</summary>
    public static IReadOnlyList<Page> Pages(Surface surface)
    {
        var paths = DocPages.PathsOf(surface);
        var site = new SiteContext(surface.Version, [.. paths, "index.html"], [], surface.Package);
        var target = surface.Package + "/";
        var top = new Page("index.html",
            "<!doctype html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\" />\n"
            + $"<meta http-equiv=\"refresh\" content=\"0; url={Html.Attr(target)}\" />\n"
            + $"<title>{Html.Text(surface.Package)}</title>\n</head>\n<body>\n"
            + $"<p><a href=\"{Html.Attr(target)}\">{Html.Text(surface.Package)}</a></p>\n</body>\n</html>\n");
        return [top, .. DocPages.Build(surface, site)];
    }

    /// <summary>Writes the reference into <paramref name="outDir"/>, which is emptied first, and
    /// says where it is. Exit 0.</summary>
    public static int Write(Surface surface, string outDir, TextWriter output)
    {
        var pages = Pages(surface);
        SiteWriter.Write(outDir, pages);
        var types = surface.Modules.Sum(m => m.Items.Count(i => i.Kind is ItemKind.Struct or ItemKind.Class or ItemKind.Enum or ItemKind.Interface));
        output.WriteLine($"documented {surface.Package} {surface.Version}: {surface.Modules.Count} modules, {types} types, "
                         + $"{pages.Count} pages in {outDir}");
        return 0;
    }
}
