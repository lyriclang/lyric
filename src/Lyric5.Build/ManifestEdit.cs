using System.Text;
using System.Text.RegularExpressions;

namespace Lyric5.Build;

/// <summary>
/// <c>lyric add</c> and <c>lyric remove</c>: one line of
/// <c>[dependencies]</c> — or, for a source tree, of <c>[native.vendor]</c> — written or taken out as text — every other line of the manifest, its
/// comments, its order and its line ends stay as they are. A dependency written otherwise — a
/// table of its own, dotted keys — is the person's to edit, not this.
/// </summary>
public static partial class ManifestEdit
{
    /// <summary><paramref name="text"/> with <c>name = value</c> in <c>[dependencies]</c>: the line
    /// that names it replaced, else a new line after the table's last, else a new table at the end.</summary>
    /// <exception cref="InvalidOperationException">The dependency is written in another form.</exception>
    /// <param name="section">The table: <c>dependencies</c>, or <c>native.vendor</c> for a source tree.</param>
    public static string Add(string text, string name, string value, string section = "dependencies")
    {
        var (lines, newline) = Split(text);
        var entry = $"{name} = {value}";
        if (Table(lines, section) is not { } table)
        {
            Refuse(lines, name, section);
            while (lines.Count > 0 && lines[^1].Trim().Length == 0) lines.RemoveAt(lines.Count - 1);
            lines.AddRange(["", $"[{section}]", entry]);
            return Join(lines, newline);
        }
        Refuse(lines, name, section);
        if (Line(lines, table, name) is { } at) lines[at] = entry;
        else
        {
            var last = table.Header;
            for (var i = table.Header + 1; i < table.End; i++)
                if (lines[i].Trim().Length > 0) last = i;
            lines.Insert(last + 1, entry);
        }
        return Join(lines, newline);
    }

    /// <summary><paramref name="text"/> without the line of <c>name</c> in <c>[dependencies]</c>;
    /// <c>null</c> where there is none.</summary>
    /// <exception cref="InvalidOperationException">The dependency is written in another form.</exception>
    public static string? Remove(string text, string name, string section = "dependencies")
    {
        var (lines, newline) = Split(text);
        Refuse(lines, name, section);
        if (Table(lines, section) is not { } table || Line(lines, table, name) is not { } at) return null;
        lines.RemoveAt(at);
        return Join(lines, newline);
    }

    /// <summary><paramref name="text"/> with the tag on <c>name</c>'s line in <c>[dependencies]</c>
    /// set to <paramref name="tag"/>, the rest of the line and of the file as it was
    /// (<c>lyric update</c> raises a version). <c>null</c> where the line names no tag.</summary>
    /// <exception cref="InvalidOperationException">The dependency is written in another form.</exception>
    public static string? SetTag(string text, string name, string tag)
    {
        var (lines, newline) = Split(text);
        Refuse(lines, name, "dependencies");
        if (Table(lines, "dependencies") is not { } table || Line(lines, table, name) is not { } at) return null;
        var value = TagValue().Match(lines[at]).Groups["value"];
        if (!value.Success) return null;
        lines[at] = lines[at][..value.Index] + Quote(tag) + lines[at][(value.Index + value.Length)..];
        return Join(lines, newline);
    }

    /// <summary>A string as TOML writes it.</summary>
    public static string Quote(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private readonly record struct Span(int Header, int End);

    /// <summary>The table <paramref name="section"/>: its header's line and the line after its last.</summary>
    private static Span? Table(List<string> lines, string section)
    {
        var pattern = new Regex($@"^\s*\[\s*{Dotted(section)}\s*\]\s*(#.*)?$");
        var header = lines.FindIndex(pattern.IsMatch);
        if (header < 0) return null;
        var end = lines.FindIndex(header + 1, l => AnyHeader().IsMatch(l));
        return new Span(header, end < 0 ? lines.Count : end);
    }

    /// <summary>The line in the table whose key is <paramref name="name"/>, bare or quoted.</summary>
    private static int? Line(List<string> lines, Span table, string name)
    {
        for (var i = table.Header + 1; i < table.End; i++)
        {
            var match = Entry().Match(lines[i]);
            if (match.Success && Unquote(match.Groups["key"].Value) == name) return i;
        }
        return null;
    }

    /// <summary>A dependency written as a table of its own, or with dotted keys, is not one line.</summary>
    private static void Refuse(List<string> lines, string name, string section)
    {
        var tableOf = new Regex($@"^\s*\[\s*{Dotted(section)}\s*\.\s*(?<key>[A-Za-z0-9_-]+|""[^""]*""|'[^']*')\s*\]");
        foreach (var line in lines)
        {
            var header = tableOf.Match(line);
            var dotted = DottedEntry().Match(line);
            if ((header.Success && Unquote(header.Groups["key"].Value) == name)
                || (dotted.Success && Unquote(dotted.Groups["key"].Value) == name))
                throw new InvalidOperationException($"'{name}' is written as a table in lyric.toml, not as one line: edit it by hand");
        }
    }

    /// <summary>A table's dotted name as a pattern: blanks allowed around each dot.</summary>
    private static string Dotted(string section) => string.Join(@"\s*\.\s*", section.Split('.').Select(Regex.Escape));

    private static string Unquote(string key) => key.Length >= 2 && key[0] is '"' or '\'' ? key[1..^1] : key;

    private static (List<string> Lines, string Newline) Split(string text)
    {
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = text.Split(newline).ToList();
        // The text's last line end: the split leaves an empty last element for it.
        if (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        return (lines, newline);
    }

    private static string Join(List<string> lines, string newline)
    {
        var text = new StringBuilder();
        foreach (var line in lines) text.Append(line).Append(newline);
        return text.ToString();
    }

    /// <summary>The <c>tag = "…"</c> of an inline table, its string as written.</summary>
    [GeneratedRegex(@"\btag\s*=\s*(?<value>""(?:[^""\\]|\\.)*"")")]
    private static partial Regex TagValue();

    [GeneratedRegex(@"^\s*\[")]
    private static partial Regex AnyHeader();

    [GeneratedRegex(@"^\s*(?<key>[A-Za-z0-9_-]+|""[^""]*""|'[^']*')\s*=")]
    private static partial Regex Entry();

    [GeneratedRegex(@"^\s*(?<key>[A-Za-z0-9_-]+|""[^""]*""|'[^']*')\s*\.\s*[A-Za-z0-9_""'-]+\s*=")]
    private static partial Regex DottedEntry();
}
