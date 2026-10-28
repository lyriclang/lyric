using System.Collections;
using System.Reflection;
using System.Text;
using Lyric.AST;
using Lyric.Core;
using Lyric.Formatting;

namespace Lyric.Tests.Formatting;

/// <summary>
/// Two promises of the formatter over the Lyric 5 corpus: formatting a formatted file changes
/// nothing, and the formatted file parses into the same tree as the original — every node, name,
/// literal and order alike, only the places in the text differ. The second is what lets the
/// formatter run over a package without anyone reading the diff. With <c>LYRIC_FMT_PROOF_REPORT</c>
/// set, a report over the corpus and the conformance cases under <c>LYRIC_FMT_CASES</c> is written
/// there instead of asserting.
/// </summary>
public class Lyric5ProofTests
{
    [Theory]
    [MemberData(nameof(Lyric5CorpusTests.Entries), MemberType = typeof(Lyric5CorpusTests))]
    public void Formatting_a_formatted_Lyric_5_file_changes_nothing(string name)
    {
        var entry = Lyric5CorpusTests.Corpus().Single(e => e.Name == name);
        Assert.True(Idempotence(entry) is null, $"{name}: {Idempotence(entry)}");
    }

    [Theory]
    [MemberData(nameof(Lyric5CorpusTests.Entries), MemberType = typeof(Lyric5CorpusTests))]
    public void A_formatted_Lyric_5_file_parses_into_the_same_tree(string name)
    {
        var entry = Lyric5CorpusTests.Corpus().Single(e => e.Name == name);
        Assert.True(SameTree(entry) is null, $"{name}: {SameTree(entry)}");
    }

    /// <summary><c>null</c> where a second pass gives the first pass's text, else where they part.</summary>
    public static string? Idempotence(Lyric5CorpusTests.Entry entry)
    {
        if (Format(entry.Text, entry.Name) is not { } once) return null;
        var twice = Format(once, entry.Name);
        if (twice is null) return "the formatted text does not format";
        return once == twice ? null : "second pass differs " + FirstDifference(once, twice);
    }

    /// <summary><c>null</c> where the formatted text's tree is the original's, else the first node
    /// where they part.</summary>
    public static string? SameTree(Lyric5CorpusTests.Entry entry)
    {
        if (Format(entry.Text, entry.Name) is not { } formatted) return null;
        var before = Dump(entry.Text, entry.Name);
        var after = Dump(formatted, entry.Name);
        if (before is null) return null;
        if (after is null) return "the formatted text does not parse";
        return before == after ? null : "trees differ " + FirstDifference(before, after);
    }

    private static string? Format(string text, string name)
    {
        var sm = new SourceManager { Lyric5 = true };
        var id = sm.AddVirtual(name, text);
        return Formatter.Format(sm, id, new DiagnosticEngine(sm));
    }

    /// <summary>The module's tree as text, without spans; <c>null</c> where it does not parse.</summary>
    public static string? Dump(string text, string name)
    {
        var sm = new SourceManager { Lyric5 = true };
        var id = sm.AddVirtual(name, text);
        var de = new DiagnosticEngine(sm);
        var module = new Lyric.Parsing.Parser(sm, id, de).ParseModule();
        if (de.HasErrors) return null;
        var starts = TokenStarts(sm, id);
        var sb = new StringBuilder();
        Write(sb, module, 0, starts);
        return sb.ToString();
    }

    /// <summary>A token of the text: where it begins, its kind and its text.</summary>
    private sealed record Tok(int Start, Lyric.Lexing.TokenKind Kind, string Text);

    /// <summary>The tokens of the text, in order.</summary>
    private static Tok[] TokenStarts(SourceManager sm, FileId id)
    {
        var text = sm.GetText(id);
        var lexer = new Lyric.Lexing.Lexer(sm, id, new DiagnosticEngine(sm));
        var tokens = new List<Tok>();
        Lyric.Lexing.Token token;
        do
        {
            token = lexer.Next();
            tokens.Add(new Tok(token.Span.Start, token.TokenKind, text.Substring(token.Span.Start, token.Span.End - token.Span.Start)));
        } while (token.TokenKind != Lyric.Lexing.TokenKind.Eof);
        return tokens.ToArray();
    }

    /// <summary>A region of the text as the tokens it covers, without the separators the one form
    /// adds or leaves out (commas, semicolons, an empty <c>()</c> before a block): what a region
    /// holds does not move when the layout around it does.</summary>
    private static string Covered(Tok[] tokens, int from, int end)
    {
        var inside = tokens.Where(t => t.Start >= from && t.Start < end && t.Kind is not
            (Lyric.Lexing.TokenKind.Comma or Lyric.Lexing.TokenKind.Semicolon or Lyric.Lexing.TokenKind.Eof)).ToList();
        var words = new List<string>();
        for (var i = 0; i < inside.Count; i++)
        {
            if (inside[i].Text == "(" && i + 2 < inside.Count && inside[i + 1].Text == ")" && inside[i + 2].Text == "{") { i++; continue; }
            words.AddRange(inside[i].Text == ">>" ? [">", ">"] : [inside[i].Text]);
        }
        return string.Join(" ", words);
    }

    private static readonly Dictionary<Type, PropertyInfo[]> Properties = [];

    private static void Write(StringBuilder sb, object? value, int depth, Tok[] starts)
    {
        if (depth > 400) throw new InvalidOperationException("the tree is deeper than a module can be");
        switch (value)
        {
            case null:
                sb.Append("null");
                return;
            case string s:
                sb.Append('"').Append(s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n")).Append('"');
                return;
            case byte[] bytes:
                sb.Append("bytes[").Append(Convert.ToHexString(bytes)).Append(']');
                return;
            case Span:
                return;
            case ExternBlock block:
                // what a block's functions share: they are the module's declarations already, and
                // each points back here
                sb.Append("ExternBlock(Visibility=").Append(block.Visibility).Append(" Members=").Append(block.Members.Count).Append(')');
                return;
            case TryReach reach:
                // the region a 'try' covers, as tokens: the text's offsets move with its layout
                sb.Append("TryReach(").Append(Covered(starts, reach.From, reach.End)).Append(')');
                return;
            case Enum or bool or char:
                sb.Append(value);
                return;
            case IFormattable f when value.GetType().IsPrimitive || value is decimal:
                sb.Append(f.ToString(null, System.Globalization.CultureInfo.InvariantCulture));
                return;
            case IEnumerable items:
                sb.Append('[');
                foreach (var item in items)
                {
                    Write(sb, item, depth + 1, starts);
                    sb.Append(", ");
                }
                sb.Append(']');
                return;
        }

        var type = value.GetType();
        sb.Append(type.Name).Append('(');
        foreach (var property in PropertiesOf(type))
        {
            sb.Append(property.Name).Append('=');
            Write(sb, property.GetValue(value), depth + 1, starts);
            sb.Append(' ');
        }
        sb.Append(')');
    }

    /// <summary>A node's properties in a fixed order, without its span and the record's own.</summary>
    private static PropertyInfo[] PropertiesOf(Type type)
    {
        lock (Properties)
        {
            if (Properties.TryGetValue(type, out var known)) return known;
            var found = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetIndexParameters().Length == 0 && p.Name != "EqualityContract" && p.PropertyType != typeof(Span))
                .OrderBy(p => p.Name, StringComparer.Ordinal)
                .ToArray();
            Properties[type] = found;
            return found;
        }
    }

    private static string FirstDifference(string a, string b)
    {
        var i = 0;
        while (i < a.Length && i < b.Length && a[i] == b[i]) i++;
        string Around(string s) => s.Substring(Math.Max(0, i - 60), Math.Min(120, s.Length - Math.Max(0, i - 60))).Replace("\n", "⏎");
        return $"at {i}: «{Around(a)}» against «{Around(b)}»";
    }

    [Fact]
    public void Report()
    {
        var path = Environment.GetEnvironmentVariable("LYRIC_FMT_PROOF_REPORT");
        if (string.IsNullOrEmpty(path)) return;
        var rows = Lyric5CorpusTests.Corpus(Environment.GetEnvironmentVariable("LYRIC_FMT_CASES"))
            .Select(e => (Entry: e, Twice: Idempotence(e), Tree: SameTree(e)))
            .ToList();
        var sb = new StringBuilder();
        sb.AppendLine("| corpus | files | idempotent | same tree |");
        sb.AppendLine("|---|---|---|---|");
        foreach (var g in rows.GroupBy(r => r.Entry.Corpus))
            sb.AppendLine($"| {g.Key} | {g.Count()} | {g.Count(r => r.Twice is null)} | {g.Count(r => r.Tree is null)} |");
        sb.AppendLine();
        foreach (var r in rows.Where(r => r.Twice is not null)) sb.AppendLine($"- {r.Entry.Name}: {r.Twice}");
        foreach (var r in rows.Where(r => r.Tree is not null)) sb.AppendLine($"- {r.Entry.Name}: {r.Tree}");
        File.WriteAllText(path, sb.ToString());
    }
}
