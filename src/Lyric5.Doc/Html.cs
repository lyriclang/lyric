using System.Text;

namespace Lyric5.Doc;

/// <summary>Escaping for text and attribute values in the HTML the page forms write.</summary>
public static class Html
{
    /// <summary>The text with <c>&amp;</c>, <c>&lt;</c> and <c>&gt;</c> escaped, for element content.</summary>
    public static string Text(string text)
    {
        if (text.AsSpan().IndexOfAny("&<>") < 0) return text;
        var sb = new StringBuilder(text.Length + 16);
        AppendText(sb, text);
        return sb.ToString();
    }

    /// <summary>Appends the text escaped as element content.</summary>
    public static void AppendText(StringBuilder sb, ReadOnlySpan<char> text)
    {
        foreach (var c in text)
        {
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                default: sb.Append(c); break;
            }
        }
    }

    /// <summary>The value escaped for a double-quoted attribute: the content escapes and <c>"</c>.</summary>
    public static string Attr(string value) => Text(value).Replace("\"", "&quot;");
}
