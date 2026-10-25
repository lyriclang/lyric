using System.Text.RegularExpressions;

namespace Lyric5.Doc;

/// <summary>One Lyric block of a documentation comment, run as a test: its name, the file and
/// line it stands at, the snippet, and the program it is built as.</summary>
/// <param name="Name">How <c>lyric test</c> names it: <c>doc: geo.shapes.Point.scale (shapes.lyr:12)</c>.</param>
/// <param name="Program">The snippet as a program: with the documented element imported where
/// the snippet does not import it, and wrapped as the Book wraps a snippet.</param>
public sealed record DocTest(string Name, string File, int Line, Snippet Snippet, string Program);

/// <summary>
/// The Lyric blocks of a module's documentation comments, as tests. A block is a run of <c>///</c>
/// or <c>//!</c> lines; its fenced snippets are the Book's (<c>lyric</c>, <c>lyric,no_run</c>,
/// <c>lyric,compile_fail=CODE</c>, with an <c>output</c> or <c>diagnostic</c> block below). A
/// <c>///</c> block documents the declaration below it — a member where that stands inside a type —,
/// and its snippets import that declaration, or its type for a member, where it is <c>pub</c>; a
/// <c>//!</c> block documents the module and imports nothing.
/// </summary>
public static partial class DocTests
{
    [GeneratedRegex(@"^(?<indent>\s*)(?<pub>pub\s+)?(?:static\s+|mut\s+|inline\s+|extern\s+""C""\s+)*(?:fn|struct|class|enum|interface|type|let|var|opaque\s+type)\s+(?<name>[A-Za-z_]\w*)")]
    private static partial Regex Declaration();

    [GeneratedRegex(@"^(?<indent>\s*)(?<pub>pub\s+)?(?<name>[A-Za-z_]\w*)\s*[:(,{=]")]
    private static partial Regex FieldOrVariant();

    [GeneratedRegex(@"^(?:pub\s+)?(?:struct|class|enum|interface)\s+(?<name>[A-Za-z_]\w*)|^extend\s+(?<name>[A-Za-z_]\w*)")]
    private static partial Regex Container();

    /// <summary>The doc-tests of the module <paramref name="module"/>, whose source is
    /// <paramref name="text"/>; <paramref name="file"/> is the name its tests show.</summary>
    /// <exception cref="InvalidDataException">A block holds a snippet the Book's form refuses.</exception>
    public static IReadOnlyList<DocTest> Extract(string file, string module, string text)
    {
        var lines = text.ReplaceLineEndings("\n").Split('\n');
        var tests = new List<DocTest>();
        string? container = null;
        var containerPublic = false;
        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (lines[i].Length > 0 && !char.IsWhiteSpace(lines[i][0]) && Container().Match(lines[i]) is { Success: true } c)
            {
                container = c.Groups["name"].Value;
                containerPublic = lines[i].StartsWith("pub ", StringComparison.Ordinal);
            }
            var marker = trimmed.StartsWith("//!", StringComparison.Ordinal) ? "//!"
                : trimmed.StartsWith("///", StringComparison.Ordinal) ? "///" : null;
            if (marker is null) continue;

            var start = i;
            var block = new List<string>();
            while (i < lines.Length && lines[i].TrimStart().StartsWith(marker, StringComparison.Ordinal))
            {
                var body = lines[i].TrimStart()[3..];
                block.Add(body.StartsWith(' ') ? body[1..] : body);
                i++;
            }
            var snippets = Snippet.Extract(file, string.Join('\n', block));
            if (snippets.Count == 0)
            {
                i--;
                continue;
            }

            var (element, import) = marker == "//!" ? (null, null) : Documented(lines, i, container, containerPublic);
            foreach (var snippet in snippets)
            {
                var line = start + snippet.Line;
                var at = snippet with { File = file, Line = line };
                var what = element is null ? module : $"{module}.{element}";
                tests.Add(new DocTest($"doc: {what} ({file}:{line})", file, line, at, ProgramOf(at, module, import)));
            }
            i--;
        }
        return tests;
    }

    /// <summary>The element the declaration at line <paramref name="at"/> declares — a member is
    /// <c>Type.member</c> —, and the name a snippet imports for it, where that is <c>pub</c>.</summary>
    private static (string? Element, string? Import) Documented(string[] lines, int at, string? container, bool containerPublic)
    {
        while (at < lines.Length && (lines[at].Trim().Length == 0 || lines[at].TrimStart().StartsWith('@'))) at++;
        if (at >= lines.Length) return (null, null);
        var line = lines[at];
        var m = Declaration().Match(line);
        if (!m.Success) m = FieldOrVariant().Match(line);
        if (!m.Success) return (null, null);
        var name = m.Groups["name"].Value;
        if (m.Groups["indent"].Length == 0)
            return (name, m.Groups["pub"].Success ? name : null);
        return container is null ? (name, null) : ($"{container}.{name}", containerPublic ? container : null);
    }

    /// <summary>The snippet as a program: <c>import module { Name };</c> added where the snippet
    /// writes no import of the module, then wrapped as <see cref="Snippet.Program"/> wraps it. A
    /// snippet the program keeps as written — one with <c>fn main</c>, or declarations — takes the
    /// import on its first line, so the compiler's lines are the snippet's.</summary>
    private static string ProgramOf(Snippet snippet, string module, string? import)
    {
        var plain = snippet.Program();
        if (import is null || snippet.Code.Contains($"import {module}", StringComparison.Ordinal)) return plain;
        var line = $"import {module} {{ {import} }};";
        return plain.StartsWith(snippet.Code, StringComparison.Ordinal)
            ? $"{line} {plain}"
            : (snippet with { Code = line + "\n" + snippet.Code }).Program();
    }
}
