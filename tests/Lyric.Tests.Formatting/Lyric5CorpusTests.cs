using System.Runtime.CompilerServices;
using System.Text;
using Lyric.Core;
using Lyric.Formatting;
using Lyric.Lexing;
using Lyric5.Doc;

namespace Lyric.Tests.Formatting;

/// <summary>
/// The formatter against the Lyric 5 corpus — the standard library, its tests, the examples and
/// the Book's snippets: each file formats, the output parses, and its tokens and comments are the
/// input's, whitespace aside. With <c>LYRIC_FMT_REPORT</c> set, a report over the corpus (and the
/// conformance cases under <c>LYRIC_FMT_CASES</c>) is written there instead of asserting.
/// </summary>
public class Lyric5CorpusTests
{
    private static string RepoRoot([CallerFilePath] string thisFile = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));

    /// <summary>One corpus entry: its name, the corpus it belongs to and its text.</summary>
    public sealed record Entry(string Corpus, string Name, string Text);

    public static IEnumerable<Entry> Corpus(string? cases = null)
    {
        var root = RepoRoot();
        foreach (var (corpus, dir) in new[] { ("stdlib5", "stdlib5"), ("tests/std", "tests/std"), ("examples", "examples") })
        foreach (var file in Directory.GetFiles(Path.Combine(root, dir), "*.lyr", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (relative.Split('/').Any(s => s is "bin" or "obj" or "out")) continue;
            yield return new Entry(corpus, relative, File.ReadAllText(file));
        }

        var book = Path.Combine(root, "docs", "book");
        foreach (var file in Directory.GetFiles(book, "*.md").Order(StringComparer.Ordinal))
        foreach (var snippet in Snippet.Extract(Path.GetFileName(file), File.ReadAllText(file)))
            yield return new Entry("book", snippet.ToString(), snippet.Program());

        if (cases is null) yield break;
        foreach (var file in Directory.GetFiles(cases, "*.lyr", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            yield return new Entry("conformance", Path.GetRelativePath(cases, file).Replace('\\', '/'), File.ReadAllText(file));
    }

    /// <summary>What became of one file: <c>null</c> where it is as it should be, else the reason.</summary>
    public static (string Category, string Detail)? Check(Entry entry)
    {
        var input = Parse(entry.Text, entry.Name);
        if (!input.Parses) return ("input does not parse", "");

        string? formatted;
        try { formatted = Format(entry.Text, entry.Name); }
        catch (Exception e) { return ("formatter throws", e.Message); }
        if (formatted is null) return ("formatter refused", "");

        var output = Parse(formatted, entry.Name + " (formatted)");
        if (!output.Parses) return ("output does not parse", FirstError(formatted, entry.Name));

        foreach (var comment in input.Comments)
            if (!output.Comments.Contains(comment)) return ("comment lost", comment.Trim());

        if (!input.Tokens.SequenceEqual(output.Tokens))
            return ("tokens differ", FirstDifference(input.Tokens, output.Tokens));
        if (!input.Comments.SequenceEqual(output.Comments))
            return ("comments moved", "");
        return null;
    }

    private sealed record Lexed(bool Parses, List<string> Tokens, List<string> Comments);

    private static Lexed Parse(string text, string name)
    {
        var sm = new SourceManager { Lyric5 = true };
        var id = sm.AddVirtual(name, text);
        var de = new DiagnosticEngine(sm);
        new Lyric.Parsing.Parser(sm, id, de).ParseModule();
        var parses = !de.HasErrors;

        var lexer = new Lexer(sm, id, new DiagnosticEngine(sm), collectTrivia: true);
        var tokens = new List<string>();
        Token token;
        do
        {
            token = lexer.Next();
            if (token.TokenKind != TokenKind.Eof)
                tokens.Add($"{token.TokenKind}:{text.Substring(token.Span.Start, token.Span.End - token.Span.Start)}");
        } while (token.TokenKind != TokenKind.Eof);
        tokens = Normalized(tokens);
        var comments = lexer.CollectedTrivia
            .Select(t => text.Substring(t.Span.Start, t.Span.End - t.Span.Start).Replace("\r\n", "\n").TrimEnd())
            .ToList();
        return new Lexed(parses, tokens, comments);
    }

    /// <summary>The tokens without what the one form decides and the file does not say: a trailing
    /// comma before a closing bracket, a ',' or ';' the grammar takes or leaves after a closing
    /// brace, the empty '()' before a trailing block, and '>>' closing two type argument lists.</summary>
    private static List<string> Normalized(List<string> tokens)
    {
        var split = tokens.SelectMany(t => t == "Shr:>>" ? ["Greater:>", "Greater:>"] : new[] { t }).ToList();
        var kept = new List<string>(split.Count);
        for (var i = 0; i < split.Count; i++)
        {
            var t = split[i];
            if (t == "Comma:," && i + 1 < split.Count && IsClosing(split[i + 1])) continue;
            if (t is "Comma:," or "Semicolon:;" && kept.Count > 0 && kept[^1] == "RBrace:}") continue;
            if (t == "LParen:(" && i + 2 < split.Count && split[i + 1] == "RParen:)" && split[i + 2] == "LBrace:{") { i++; continue; }
            kept.Add(t);
        }
        return kept;
    }

    private static bool IsClosing(string token) => token is "RParen:)" or "RBracket:]" or "RBrace:}" or "Greater:>";

    private static string? Format(string text, string name)
    {
        var sm = new SourceManager { Lyric5 = true };
        var id = sm.AddVirtual(name, text);
        return Formatter.Format(sm, id, new DiagnosticEngine(sm));
    }

    private static string FirstError(string text, string name)
    {
        var sm = new SourceManager { Lyric5 = true };
        var id = sm.AddVirtual(name, text);
        var de = new DiagnosticEngine(sm);
        new Lyric.Parsing.Parser(sm, id, de).ParseModule();
        if (de.Count == 0) return "";
        var first = de.Diagnostics[0];
        var line = text[..Math.Min(first.Span.Start, text.Length)].Count(c => c == '\n');
        var lines = text.Split('\n');
        return $"{first.Code}: {first.Message} — line {line + 1}: {(line < lines.Length ? lines[line].Trim() : "")}";
    }

    private static string FirstDifference(List<string> a, List<string> b)
    {
        var i = 0;
        while (i < a.Count && i < b.Count && a[i] == b[i]) i++;
        string Around(List<string> t) => string.Join(" ", t.Skip(Math.Max(0, i - 4)).Take(8).Select(s => s[(s.IndexOf(':') + 1)..]));
        return $"at token {i}: in «{Around(a)}» out «{Around(b)}»";
    }

    public static TheoryData<string> Entries()
    {
        var data = new TheoryData<string>();
        foreach (var e in Corpus()) data.Add(e.Name);
        return data;
    }

    [Theory]
    [MemberData(nameof(Entries))]
    public void A_Lyric_5_file_formats_and_keeps_its_tokens_and_comments(string name)
    {
        var entry = Corpus().Single(e => e.Name == name);
        var outcome = Check(entry);
        Assert.True(outcome is null, $"{name}: {outcome?.Category} {outcome?.Detail}");
    }

    [Fact]
    public void Report()
    {
        var path = Environment.GetEnvironmentVariable("LYRIC_FMT_REPORT");
        if (string.IsNullOrEmpty(path)) return;
        var results = Corpus(Environment.GetEnvironmentVariable("LYRIC_FMT_CASES"))
            .Select(e => (Entry: e, Outcome: Check(e))).ToList();

        var sb = new StringBuilder();
        sb.AppendLine("| corpus | files | good | " + string.Join(" | ", Categories) + " |");
        sb.AppendLine("|---|---|---|" + string.Concat(Categories.Select(_ => "---|")));
        foreach (var group in results.GroupBy(r => r.Entry.Corpus))
        {
            sb.Append($"| {group.Key} | {group.Count()} | {group.Count(r => r.Outcome is null)} |");
            foreach (var c in Categories) sb.Append($" {group.Count(r => r.Outcome?.Category == c)} |");
            sb.AppendLine();
        }
        sb.AppendLine();
        foreach (var r in results.Where(r => r.Outcome is not null && r.Outcome.Value.Category != "input does not parse"))
            sb.AppendLine($"- {r.Entry.Corpus} {r.Entry.Name}: {r.Outcome!.Value.Category} {r.Outcome.Value.Detail}");
        sb.AppendLine();
        sb.AppendLine("Inputs that do not parse (each expecting a lexer or parser error?):");
        foreach (var r in results.Where(r => r.Outcome?.Category == "input does not parse"))
        {
            var expects = System.Text.RegularExpressions.Regex.IsMatch(r.Entry.Text, @"LYR-(PAR|LEX)\d{4}");
            sb.AppendLine($"- {r.Entry.Name}: {(expects ? "names a PAR/LEX code" : "NAMES NONE")}");
        }
        File.WriteAllText(path, sb.ToString());
    }

    private static readonly string[] Categories =
        ["input does not parse", "formatter throws", "formatter refused", "output does not parse", "comment lost", "tokens differ", "comments moved"];
}
