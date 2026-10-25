using System.Text;
using System.Text.Json;
using Lyric.Core;
using Lyric.Lexing;

namespace Lyric5.Doc.Api;

/// <summary>
/// One line of a package's public surface: an element, its qualified path, and its signature with
/// the path in the place of its name, in one spelling whatever the source's layout.
/// </summary>
/// <param name="Path">The element's qualified name: <c>geo.Point.x</c>; a conformance's is its
/// type's.</param>
/// <param name="Kind">What it is: <c>fn</c>, <c>struct</c>, <c>field</c>, <c>conformance</c> …</param>
/// <param name="Signature">The declaration's head with comments and layout taken out.</param>
/// <param name="Line">The text form: the signature with the path for the name.</param>
/// <param name="Owner">For a member, the kind of the type it belongs to.</param>
/// <param name="Default">Whether a member has a default a caller or conformer may lean on: a field's
/// initial value, an interface method's body, an associated type's.</param>
public sealed record ApiEntry(string Module, string Path, string Kind, string Signature, string Line, string? Owner, bool Default);

/// <summary>
/// A package's public surface as <c>lyric api</c> prints it: one line per element, sorted, the same
/// for the same declarations however they are laid out — a comment, a line break or a space does
/// not change a line; a parameter's name does, since a call may pass its argument by name.
/// </summary>
public static class ApiSurface
{
    /// <summary>The entries of <paramref name="surface"/>, sorted by their text form.</summary>
    public static IReadOnlyList<ApiEntry> Entries(Surface surface)
    {
        var entries = new List<ApiEntry>();
        foreach (var module in surface.Modules)
        foreach (var item in module.Items)
        {
            var owner = item.Kind == ItemKind.Extension ? $"{module.Path}.{Normalize(item.Target ?? item.Name)}" : $"{module.Path}.{item.Name}";
            if (item.Kind != ItemKind.Extension) entries.Add(Entry(module.Path, owner, item, null));
            foreach (var member in item.Members)
                entries.Add(Entry(module.Path, $"{owner}.{member.Name}", member, item.Kind == ItemKind.Extension ? null : KindOf(item.Kind)));
            foreach (var iface in item.Conformances.Select(Normalize).Distinct(StringComparer.Ordinal))
                entries.Add(new ApiEntry(module.Path, owner, "conformance", iface, $"{owner} :: {iface}", null, false));
        }
        return [.. entries.OrderBy(e => e.Path, StringComparer.Ordinal)
            .ThenBy(e => e.Kind == "conformance" ? 1 : 0)
            .ThenBy(e => e.Line, StringComparer.Ordinal)];
    }

    private static ApiEntry Entry(string module, string path, Item item, string? owner)
    {
        // A type's conformances are lines of their own, whether its head or a block names them.
        var written = item.Kind is ItemKind.Struct or ItemKind.Class or ItemKind.Enum or ItemKind.Interface
            ? WithoutConformances(item.Signature)
            : item.Signature;
        var signature = Normalize(written);
        var line = Qualified(written, item.Name, path);
        if (item.Kind == ItemKind.Variant) line = "variant " + line;
        var hasDefault = item.Kind == ItemKind.Method ? item.Default : HasValue(signature);
        // Whether a conformer may leave an interface's method out is part of the interface.
        if (owner == "interface" && item.Kind == ItemKind.Method && hasDefault) line += " { … }";
        return new ApiEntry(module, path, KindOf(item.Kind), signature, line, owner, hasDefault);
    }

    private static string KindOf(ItemKind kind) => kind switch
    {
        ItemKind.Function => "fn",
        ItemKind.Struct => "struct",
        ItemKind.Class => "class",
        ItemKind.Enum => "enum",
        ItemKind.Interface => "interface",
        ItemKind.Alias => "type",
        ItemKind.Constant => "const",
        ItemKind.Field => "field",
        ItemKind.Method => "method",
        ItemKind.Variant => "variant",
        ItemKind.AssociatedType => "assoc",
        _ => "extension",
    };

    /// <summary>Whether a field's or an associated type's signature gives it a value: an <c>=</c> of
    /// its own, not one of an argument's default.</summary>
    private static bool HasValue(string signature)
    {
        var depth = 0;
        foreach (var token in Tokens(signature))
        {
            if (token is "(" or "[" or "<") depth++;
            else if (token is ")" or "]" or ">") depth--;
            else if (token == "=" && depth == 0) return true;
        }
        return false;
    }

    /// <summary>A type's head up to its <c>::</c> list: <c>pub struct Point&lt;T&gt;</c>.</summary>
    private static string WithoutConformances(string head)
    {
        var tokens = Tokens(head).ToList();
        var depth = 0;
        for (var i = 0; i < tokens.Count; i++)
        {
            if (tokens[i] is "(" or "[" or "<") depth++;
            else if (tokens[i] is ")" or "]" or ">") depth--;
            else if (tokens[i] == ">>") depth -= 2;
            else if (tokens[i] == "::" && depth == 0) return Join(tokens.Take(i));
        }
        return head;
    }

    /// <summary>The text form: one line per entry.</summary>
    public static string Text(IReadOnlyList<ApiEntry> entries)
    {
        var sb = new StringBuilder();
        foreach (var e in entries) sb.Append(e.Line).Append('\n');
        return sb.ToString();
    }

    /// <summary>The entries as one JSON document: <c>{"package", "version", "elements": [...]}</c>,
    /// each element its module, path, kind, signature and line, in the text form's order.</summary>
    public static string Json(string package, string version, IReadOnlyList<ApiEntry> entries)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteString("package", package);
            w.WriteString("version", version);
            w.WriteStartArray("elements");
            foreach (var e in entries)
            {
                w.WriteStartObject();
                w.WriteString("module", e.Module);
                w.WriteString("path", e.Path);
                w.WriteString("kind", e.Kind);
                w.WriteString("signature", e.Signature);
                w.WriteString("line", e.Line);
                if (e.Owner is { } owner) w.WriteString("owner", owner);
                if (e.Default) w.WriteBoolean("default", true);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }

    /// <summary>The signature in one spelling: its tokens, without comments, a space only where
    /// the language reads one.</summary>
    public static string Normalize(string written) => Join(Tokens(written));

    /// <summary>As <see cref="Normalize"/>, with the first token that is the element's name replaced by
    /// its qualified path.</summary>
    private static string Qualified(string written, string name, string path)
    {
        var tokens = Tokens(written).ToList();
        var at = tokens.IndexOf(name);
        if (at >= 0) tokens[at] = path;
        return Join(tokens);
    }

    private static IEnumerable<string> Tokens(string written)
    {
        var sources = new SourceManager { Lyric5 = true };
        var file = sources.AddVirtual("signature.lyr", written);
        var lexer = new Lexer(sources, file, new DiagnosticEngine(sources));
        var tokens = new List<string>();
        while (true)
        {
            var token = lexer.Next();
            if (token.TokenKind == TokenKind.Eof) break;
            tokens.Add(written.Substring(token.Span.Start, token.Span.End - token.Span.Start));
        }
        return tokens;
    }

    private static bool IsWord(string token) =>
        token.Length > 0 && (char.IsLetterOrDigit(token[0]) || token[0] is '_' or '"' or '\'' or '`');

    private static string Join(IEnumerable<string> written)
    {
        var tokens = written.ToList();
        var sb = new StringBuilder();
        string? previous = null;
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            // A trailing comma is layout: a list laid out one per line ends with one.
            if (token == "," && i + 1 < tokens.Count && tokens[i + 1] is ")" or "]" or ">" or ">>" or "}") continue;
            if (previous is not null && SpaceBetween(previous, token)) sb.Append(' ');
            sb.Append(token);
            previous = token;
        }
        return sb.ToString();
    }

    /// <summary>Whether the one spelling puts a space between two tokens: between two words, after a
    /// comma or a colon, around <c>::</c>, <c>=</c>, <c>-&gt;</c> and <c>=&gt;</c>, and before a word
    /// that follows a closing bracket — <c>(x: int): T throws E</c>, <c>List&lt;T&gt;</c>.</summary>
    private static bool SpaceBetween(string left, string right)
    {
        if (right is "," or ")" or "]" or "." or ";") return false;
        if (left is "(" or "[" or "." or "<" or "@") return false;
        if (left is "," or ":" or "::" or "=" or "->" or "=>" or "|") return true;
        if (right is "::" or "=" or "->" or "=>" or "|") return true;
        if (right is ":" or "<" or ">" or ">>" or "(" or "[" or "!") return false;
        if (left is ")" or "]" or ">" or ">>") return IsWord(right) || right == "?";
        return IsWord(left) && IsWord(right);
    }
}
