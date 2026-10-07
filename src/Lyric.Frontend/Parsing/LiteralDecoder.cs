using Lyric.AST;
using Lyric.Core;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Lyric.Parsing
{
    public static class LiteralDecoder
    {
        private static Dictionary<string, IntSuffix?> _intSuffixes = new Dictionary<string, IntSuffix?>() 
        { 
            { "i8", IntSuffix.I8 },{ "u8", IntSuffix.U8 },
            { "i16", IntSuffix.I16 },{ "u16", IntSuffix.U16 },
            { "i32", IntSuffix.I32 },{ "u32", IntSuffix.U32 },
            { "i64", IntSuffix.I64 },{ "u64", IntSuffix.U64 }
        };

        private static Dictionary<string, FloatSuffix?> _floatSuffixes = new Dictionary<string, FloatSuffix?>()
        {
            {"f32", FloatSuffix.F32 }, { "f64", FloatSuffix.F64 }
        };

        private static bool isValidDigit(char digit, ulong inBase) //inBase is only needed for 2, 8, 10 and 16
            => inBase switch
            {
                2 => digit is '0' or '1',
                8 => digit is '0' or '1' or '2' or '3' or '4' or '5' or '6' or '7',
                10 => digit is '0' or '1' or '2' or '3' or '4' or '5' or '6' or '7' or '8' or '9',
                16 => digit is '0' or '1' or '2' or '3' or '4' or '5' or '6' or '7' or '8' or '9' or (>= 'a' and <= 'f') or (>= 'A' and <= 'F'),
                _ => false
            };
        private static int digitValue(char digit) => digit switch
        {
            >= '0' and <= '9' => digit - '0',
            >= 'A' and <= 'F' => 10 + (digit - 'A'),
            >= 'a' and <= 'f' => 10 + (digit - 'a'),
            _ => 0
        };

        // The resolution lives in Lyric.Core: the f-string lowering needs it too, and Lyric.Ir
        // must not reference Lyric.Parsing.
        private static string ResolveEscapes(string content) => Escapes.Resolve(content);

        private static string StripQuotes(string text) //From lexer at least -> "\"..." (maybe unterminated)
        {
            if (String.IsNullOrEmpty(text)) return "";
            var end = text.Length >= 2 && text[0].Equals(text[^1]) ? text.Length-1 : text.Length;
            return text[1..end];
        }

        public static (ulong, IntSuffix?) DecodeInt(ReadOnlySpan<char> lexme, Span span, DiagnosticEngine de)
        {
            IntSuffix? suffix = null;
            ulong value = 0;
            var start = 0;
            ulong numBase = 10;

            if (lexme.Length > 2) //look for prefix
            {
                if (lexme[0] == '0')
                {
                    if (lexme[1] is 'x' or 'X' ) { start = 2; numBase = 16; }
                    if (lexme[1] is 'o' or 'O') { start = 2; numBase = 8; }
                    if (lexme[1] is 'b' or 'B') { start = 2; numBase = 2; }
                }
            }

            var suffixStart = start;
            while (suffixStart < lexme.Length && (isValidDigit(lexme[suffixStart], numBase) || lexme[suffixStart] == '_'))
            {
                suffixStart++;
            }
            if (suffixStart < lexme.Length)
            {
                var sufText = lexme[suffixStart..lexme.Length].ToString();
                _intSuffixes.TryGetValue(sufText, out suffix);
                if (suffix == null) de.Report("LYR-PAR0006", Severity.Error, span, $"invalid integer suffix: '{sufText}'");
            }

            foreach (char c in lexme[start..suffixStart])
            {
                if (c == '_') continue;
                ulong d = (ulong)digitValue(c);
                if (value > (ulong.MaxValue - d) / numBase)
                {
                    de.Report("LYR-PAR0007", Severity.Error, span, "integer literal too large");
                    return (0, suffix);
                }
                value = value * numBase + d;
            }
            return (value, suffix);
        }

        public static (double, FloatSuffix?) DecodeFloat(ReadOnlySpan<char> lexme, Span span, DiagnosticEngine de)
        {
            var ls = lexme.ToString().Replace("_", ""); //strip seperator
            var fidx = ls.IndexOf('f');
            FloatSuffix? suffix = null;
            if (fidx > 0)
            {
                _floatSuffixes.TryGetValue(ls[fidx..], out suffix);
                if (suffix == null) de.Report("LYR-PAR0006", Severity.Error, span, $"invalid float suffix: '{ls[fidx..]}'");
                ls = ls[0..fidx];
            }
            double.TryParse(ls, CultureInfo.InvariantCulture, out var value);
            if (double.IsInfinity(value))
            {
                de.Report("LYR-PAR0007", Severity.Error, span, "float literal too large");
                return (0.0d, suffix);
            }
            return (value, suffix);
        }

        public static string DecodeString(ReadOnlySpan<char> lexme, Span span, DiagnosticEngine de)
        {
            var text = lexme.ToString();
            // 'r"…"', 'r#"…"#', 'r"""…"""' (08 Y7 L5, L6): the text as written, no escape.
            if (text.StartsWith('r'))
            {
                var hashes = 0;
                while (1 + hashes < text.Length && text[1 + hashes] == '#') hashes++;
                var body = text[(1 + hashes)..];
                if (hashes == 0 && body.StartsWith("\"\"\"", StringComparison.Ordinal))
                    return Dedent(Between(body, 3, 0), span, de);
                return Between(body, 1, hashes);
            }
            // '"""…"""' (L6): the lines first, then the escapes — an escaped '\n' is no line.
            if (text.StartsWith("\"\"\"", StringComparison.Ordinal))
                return ResolveEscapes(Dedent(Between(text, 3, 0), span, de));
            return ResolveEscapes(StripQuotes(text));
        }

        /// <summary>The text between the opening quotes and the closing ones with their hashes — what
        /// of them stands: an unterminated literal was reported by the lexer.</summary>
        private static string Between(string text, int quotes, int hashes)
        {
            var start = Math.Min(quotes, text.Length);
            var closing = new string('"', quotes) + new string('#', hashes);
            var end = text.Length - start >= closing.Length && text.EndsWith(closing, StringComparison.Ordinal)
                ? text.Length - closing.Length : text.Length;
            return text[start..Math.Max(start, end)];
        }

        /// <summary>
        /// The lines of a multi-line string (08 Y7 L6, Swift's rule): the text begins on the line after
        /// the opening <c>"""</c> and ends on the line before the closing one, which stands on a line of
        /// its own; its indentation is taken off every line, and a line indented less is refused. A
        /// blank line may be indented less. The line breaks are <c>\n</c>, whatever the file has.
        /// </summary>
        public static string Dedent(string content, Span span, DiagnosticEngine de)
        {
            content = content.Replace("\r\n", "\n", StringComparison.Ordinal);
            if (!content.StartsWith('\n'))
            {
                de.Report("LYR-LEX0014", Severity.Error, span,
                    "a multi-line string begins on the line after its opening '\"\"\"', which ends its own line");
                return content;
            }
            content = content[1..];
            var last = content.LastIndexOf('\n');
            var indent = last < 0 ? content : content[(last + 1)..];
            if (indent.Any(c => c is not (' ' or '\t')))
            {
                de.Report("LYR-LEX0014", Severity.Error, span,
                    "a multi-line string ends with its closing '\"\"\"' on a line of its own");
                return content;
            }
            if (last < 0) return "";
            var lines = content[..last].Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].All(c => c is ' ' or '\t')) { lines[i] = ""; continue; }
                if (lines[i].StartsWith(indent, StringComparison.Ordinal)) { lines[i] = lines[i][indent.Length..]; continue; }
                de.Report("LYR-LEX0015", Severity.Error, span,
                    $"line {i + 1} of a multi-line string is indented less than its closing '\"\"\"' — "
                    + "every line takes the closing line's indentation, which is cut off");
                return content;
            }
            return string.Join('\n', lines);
        }

        /// <summary><c>b"…"</c> (08 Y7 L7): ASCII text and the escapes, <c>\xNN</c> any byte. What
        /// is no byte was reported by the lexer and is left out.</summary>
        public static byte[] DecodeBytes(ReadOnlySpan<char> lexme, Span span, DiagnosticEngine de)
        {
            var body = StripQuotes(lexme.ToString()[1..]);
            var bytes = new List<byte>(body.Length);
            for (var i = 0; i < body.Length; i++)
            {
                var c = body[i];
                if (c != '\\')
                {
                    if (c <= (char)0x7F) bytes.Add((byte)c);
                    continue;
                }
                if (++i >= body.Length) break;
                switch (body[i])
                {
                    case 'n': bytes.Add(0x0A); break;
                    case 'r': bytes.Add(0x0D); break;
                    case 't': bytes.Add(0x09); break;
                    case '0': bytes.Add(0x00); break;
                    case '\\': bytes.Add(0x5C); break;
                    case '"': bytes.Add(0x22); break;
                    case '\'': bytes.Add(0x27); break;
                    case 'x':
                        // The two digits after it, which the lexer checked.
                        if (i + 2 < body.Length && byte.TryParse(body.AsSpan(i + 1, 2), NumberStyles.AllowHexSpecifier, null, out var b))
                        {
                            bytes.Add(b);
                            i += 2;
                        }
                        break;
                }
            }
            return bytes.ToArray();
        }

        public static int DecodeChar(ReadOnlySpan<char> lexme, Span span, DiagnosticEngine de)
        {
            var s = ResolveEscapes(StripQuotes(lexme.ToString()));
            return string.IsNullOrEmpty(s) ? 0 : Char.ConvertToUtf32(s, 0);
        }
    }
}
