using Lyric.AST;
using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;

namespace Lyric.Tests.Sema;

/// <summary>The real standard library, for a test whose program names what the prelude gives.</summary>
internal static class Library
{
    private static readonly string Root = FindRoot();

    public static Func<string[], LoadedModule?> Loader(SourceManager sm, DiagnosticEngine de) =>
        StdlibLoader.ForRoot(Root, sm, de);

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "stdlib5")))
            dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new DirectoryNotFoundException("stdlib5"), "stdlib5");
    }
}
