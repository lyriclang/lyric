using System.Runtime.CompilerServices;
using Lyric.AST;
using Lyric.Compiler;
using Lyric.Core;
using Lyric.Lexing;
using Lyric.Lsp.Analysis;
using Lyric.Parsing;
using Range = Lyric.Lsp.Protocol.Range;

namespace Lyric.Tests.Lsp;

/// <summary>
/// Every provider over the Lyric 5 packages of the repository — std's own tests and the examples —
/// at the names they write: what each says must hold for any program, not only for the fixtures of
/// the other tests. A provider that throws, a definition or a reference outside a written file, a
/// definition that is not a name, an outline or a token stream that breaks the protocol's rules, is
/// found here first.
/// </summary>
public sealed class ProviderCorpusTests
{
    private static string Root([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));

    public static TheoryData<string> Packages() =>
        ["tests/std", "examples/macros", "examples/jsonserver", "examples/logfilter"];

    /// <summary>The package's compilation, as the editor makes it.</summary>
    private static (CompileResult Result, PackageUnit Unit) Analyse(string package)
    {
        var root = Path.Combine(Root(), package);
        var unit = PackageUnit.Find(Path.Combine(root, "lyric.toml"), out var refused)
                   ?? throw new InvalidOperationException(refused ?? $"no package at {root}");
        var result = SourceCompiler.CheckProject(
            unit.Files.Select(f => ScriptSource.FromDisk(f.File, f.Module)).ToList(),
            unit.Options with { Lower = false, StdlibSeed = StdlibSeed.Shared(Lyric5.Build.Pipeline.StdlibRoot) });
        Assert.NotNull(result.Model);
        return (result, unit);
    }

    /// <summary>The names a file writes, by the lexer — every <paramref name="every"/>th of them.</summary>
    private static List<Span> Names(SourceManager sources, FileId file, int every)
    {
        var lexer = new Lexer(sources, file, new DiagnosticEngine(sources));
        var names = new List<Span>();
        var seen = 0;
        for (var token = lexer.Next(); token.TokenKind != TokenKind.Eof; token = lexer.Next())
            if (token.TokenKind == TokenKind.Identifier && seen++ % every == 0)
                names.Add(token.Span);
        return names;
    }

    private static Module RootOf(SemanticModel model, FileId file) =>
        model.Compilation.Modules.Select(m => model.Compilation.AstOf(m)).First(ast => ast.Span.File == file);

    private static bool Written(CompileResult result, FileId file) =>
        file.IsValid && !result.Model!.Compilation.IsSynthesized(file) && File.Exists(result.Sources.GetPath(file));

    [Theory]
    [MemberData(nameof(Packages))]
    public void Every_provider_answers_at_the_names_a_package_writes(string package)
    {
        var (result, unit) = Analyse(package);
        var model = result.Model!;
        var failures = new List<string>();

        foreach (var (path, _) in unit.Files)
        {
            var file = DiagnosticMapper.FindFile(result.Sources, path);
            if (!file.IsValid) continue;
            var root = RootOf(model, file);
            var text = result.Sources.GetText(file);
            string Where(int offset) => $"{Path.GetRelativePath(Root(), path)}:{result.Sources.Locate(file, offset)}";

            void Try(string what, int offset, Action action)
            {
                try { action(); }
                catch (Exception e) { failures.Add($"{Where(offset)} {what}: {e.GetType().Name}: {e.Message}"); }
            }

            var names = Names(result.Sources, file, every: 1);
            for (var i = 0; i < names.Count; i++)
            {
                var name = names[i];
                var offset = name.Start + name.Length / 2;
                var written = text.Substring(name.Start, name.Length);

                Try("hover", offset, () => HoverProvider.At(model, root, file, offset));
                Try("definition", offset, () =>
                {
                    if (DefinitionProvider.At(model, root, file, offset) is not { } target) return;
                    if (!Written(result, target.File))
                        failures.Add($"{Where(offset)} definition of '{written}' in no written file: {result.Sources.GetPath(target.File)}");
                    else if (target.NameSpan.Length > 0
                             && result.Sources.GetText(target.File).Substring(target.NameSpan.Start, target.NameSpan.Length) is var at
                             && at != written && !written.EndsWith(at, StringComparison.Ordinal))
                        failures.Add($"{Where(offset)} definition of '{written}' names '{at}'");
                });
                if (i % 5 == 0)
                    Try("references", offset, () =>
                    {
                        foreach (var site in ReferenceProvider.At(model, root, file, offset, includeDeclaration: true) ?? [])
                            if (!Written(result, site.File))
                                failures.Add($"{Where(offset)} a reference of '{written}' in no written file: {result.Sources.GetPath(site.File)}");
                    });
            }

            Try("outline", 0, () => DocumentSymbolProvider.Of(result.Sources, root));
            Try("folding", 0, () => FoldingProvider.Of(root, file, result.Sources));
            Try("inlay hints", 0, () => InlayHintProvider.Of(model, root, file, result.Sources,
                new Range { Start = new() { Line = 0, Character = 0 }, End = new() { Line = result.Sources.LineCount(file), Character = 0 } }));
            Try("semantic tokens", 0, () =>
            {
                var data = SemanticTokensProvider.Of(model, root, file, result.Sources);
                int line = 0, character = 0, end = -1, lastLine = -1;
                for (var t = 0; t + 4 < data.Count; t += 5)
                {
                    line += data[t];
                    character = data[t] == 0 ? character + data[t + 1] : data[t + 1];
                    if (line == lastLine && character < end)
                        failures.Add($"{Where(0)} semantic tokens overlap at {line + 1}:{character + 1}");
                    lastLine = line;
                    end = character + data[t + 2];
                }
            });
        }

        // one of each kind first, then the rest: a kind that fails everywhere hides no other
        var kinds = failures.GroupBy(f => f[(f.IndexOf(' ') + 1)..].Split('\'')[0]).Select(g => $"{g.Count()}× {g.First()}");
        Assert.True(failures.Count == 0, $"{failures.Count} failures:\n" + string.Join('\n', kinds.Take(40)));
    }
}
