using Lyric.Compiler;
using Lyric.Lsp.Analysis;

namespace Lyric.Tests.Lsp;

/// <summary>
/// A file outside every package, checked the way the server checks one: alone, with the standard
/// library beside the binary. The providers are tested on what this compile answers, so a test of
/// a provider is a test of what the editor shows.
/// </summary>
internal static class Single
{
    public static CompilerOptions Options(string path) => PackageUnit.SingleFileOptions(path);

    public static CompileResult Check(string path, string text) =>
        SourceCompiler.Check(ScriptSource.FromBuffer(path, text), Options(path));
}
