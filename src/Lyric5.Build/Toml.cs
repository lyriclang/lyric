using System.Globalization;
using System.Text;

namespace Lyric5.Build;

/// <summary>
/// The TOML a manifest is written in (design/v5/spec/11 W2: <c>lyric.toml</c>), the subset it uses:
/// tables, arrays of tables and dotted headers; bare, quoted and dotted keys; basic strings with
/// their escapes, literal strings; integers; booleans; arrays and inline tables; comments. What a
/// manifest never holds — floats, dates and times, multi-line strings — is refused with its
/// position, as is what TOML itself refuses: a key defined twice, a table opened twice, a value
/// where a table is needed.
/// </summary>
public static class Toml
{
    /// <summary>The document as its root table.</summary>
    /// <exception cref="TomlException">The text is not TOML, or not the subset.</exception>
    public static TomlTable Parse(string text) => new Reader(text).Document();

    private sealed class Reader(string text)
    {
        private int _pos;
        private int _line = 1;
        private int _lineStart;

        private int Column => _pos - _lineStart + 1;
        private char Current => _pos < text.Length ? text[_pos] : '\0';
        private bool AtEnd => _pos >= text.Length;

        private TomlException Error(string message) => new(_line, Column, message);

        public TomlTable Document()
        {
            var root = new TomlTable { Line = 1, Explicit = true };
            var current = root;
            while (true)
            {
                SkipBlank();
                if (AtEnd) return root;
                if (Current == '[') current = Header(root);
                else KeyValue(current);
                EndOfLine();
            }
        }

        /// <summary><c>[a.b]</c> or <c>[[a.b]]</c>: the table that the key-values below fill.</summary>
        private TomlTable Header(TomlTable root)
        {
            var line = _line;
            _pos++;
            var array = Current == '[';
            if (array) _pos++;
            SkipSpaces();
            var keys = Key();
            SkipSpaces();
            Expect(']');
            if (array) Expect(']');

            var table = root;
            for (var i = 0; i < keys.Count - 1; i++) table = Descend(table, keys[i], line);
            var last = keys[^1];
            if (array)
            {
                if (!table.TryGet(last, out var existing))
                {
                    existing = new TomlArray { OfTables = true };
                    table.Add(last, existing);
                }
                if (existing is not TomlArray { OfTables: true } tables)
                    throw new TomlException(line, 1, $"'{string.Join('.', keys)}' is not an array of tables");
                var element = new TomlTable { Line = line, Explicit = true };
                tables.Add(element);
                return element;
            }
            if (table.TryGet(last, out var found))
            {
                if (found is not TomlTable { Inline: false } open || open.Explicit)
                    throw new TomlException(line, 1, $"table '{string.Join('.', keys)}' is defined twice");
                open.Explicit = true;
                return open;
            }
            var made = new TomlTable { Line = line, Explicit = true };
            table.Add(last, made);
            return made;
        }

        /// <summary>The table a dotted header or key passes through: made where it is missing, the
        /// last element where it is an array of tables.</summary>
        private static TomlTable Descend(TomlTable table, string key, int line)
        {
            if (!table.TryGet(key, out var next))
            {
                var made = new TomlTable { Line = line };
                table.Add(key, made);
                return made;
            }
            return next switch
            {
                TomlTable { Inline: false } t => t,
                TomlArray { OfTables: true } a when a.Count > 0 => (TomlTable)a[^1],
                _ => throw new TomlException(line, 1, $"'{key}' is a value, not a table"),
            };
        }

        private void KeyValue(TomlTable table)
        {
            var line = _line;
            var keys = Key();
            SkipSpaces();
            Expect('=');
            SkipSpaces();
            var value = Value();
            for (var i = 0; i < keys.Count - 1; i++) table = Descend(table, keys[i], line);
            var last = keys[^1];
            if (table.Contains(last))
                throw new TomlException(line, 1, $"'{string.Join('.', keys)}' is defined twice");
            table.Add(last, value);
        }

        /// <summary>A key: bare or quoted parts joined by dots.</summary>
        private List<string> Key()
        {
            var parts = new List<string>();
            while (true)
            {
                SkipSpaces();
                if (Current == '"') parts.Add(BasicString());
                else if (Current == '\'') parts.Add(LiteralString());
                else
                {
                    var start = _pos;
                    while (!AtEnd && (char.IsAsciiLetterOrDigit(Current) || Current is '_' or '-')) _pos++;
                    if (_pos == start) throw Error("expected a key");
                    parts.Add(text[start.._pos]);
                }
                SkipSpaces();
                if (Current != '.') return parts;
                _pos++;
            }
        }

        private object Value()
        {
            switch (Current)
            {
                case '"':
                    if (text.AsSpan(_pos).StartsWith("\"\"\"")) throw Error("multi-line strings are not part of a manifest");
                    return BasicString();
                case '\'':
                    if (text.AsSpan(_pos).StartsWith("'''")) throw Error("multi-line strings are not part of a manifest");
                    return LiteralString();
                case '[': return Array();
                case '{': return InlineTable();
            }
            if (text.AsSpan(_pos).StartsWith("true") && !IsBareChar(4)) { _pos += 4; return true; }
            if (text.AsSpan(_pos).StartsWith("false") && !IsBareChar(5)) { _pos += 5; return false; }
            return Integer();
        }

        private bool IsBareChar(int offset) =>
            _pos + offset < text.Length && (char.IsAsciiLetterOrDigit(text[_pos + offset]) || text[_pos + offset] is '_' or '-');

        /// <summary>An integer — decimal with an optional sign, or <c>0x</c>/<c>0o</c>/<c>0b</c> without
        /// one; underscores only between digits.</summary>
        private long Integer()
        {
            var start = _pos;
            var column = Column;
            while (!AtEnd && (char.IsAsciiLetterOrDigit(Current) || Current is '_' or '+' or '-' or '.' or ':')) _pos++;
            var raw = text[start.._pos];
            if (raw.Length == 0) throw new TomlException(_line, column, "expected a value");

            var sign = raw[0] is '+' or '-' ? raw[..1] : "";
            var body = raw[sign.Length..];
            var radix = body.Length > 2 && body[0] == '0' && body[1] is 'x' or 'o' or 'b'
                ? body[1] switch { 'x' => 16, 'o' => 8, _ => 2 }
                : 10;
            var digits = radix == 10 ? body : body[2..];
            if (radix == 10 && (raw.Contains('.') || raw.Contains(':') || body.Contains('-')
                                || digits.Contains('e') || digits.Contains('E') || body is "inf" or "nan"))
                throw new TomlException(_line, column, $"'{raw}': floats, dates and times are not part of a manifest");

            var wellFormed = digits.Length > 0 && digits[0] != '_' && digits[^1] != '_' && !digits.Contains("__")
                             && (radix == 10 || sign.Length == 0)
                             && (radix != 10 || digits.Length == 1 || digits[0] != '0');
            var clean = digits.Replace("_", "");
            try
            {
                if (!wellFormed) throw new FormatException();
                var value = radix == 10
                    ? long.Parse(sign + clean, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture)
                    : System.Convert.ToInt64(clean, radix);
                return value;
            }
            catch (Exception e) when (e is FormatException or OverflowException or ArgumentException)
            {
                throw new TomlException(_line, column, $"'{raw}' is not a value");
            }
        }

        private string BasicString()
        {
            _pos++;
            var result = new StringBuilder();
            while (true)
            {
                if (AtEnd || Current == '\n') throw Error("a string is not closed");
                var c = Current;
                _pos++;
                if (c == '"') return result.ToString();
                if (c != '\\') { result.Append(c); continue; }
                var escape = Current;
                _pos++;
                switch (escape)
                {
                    case '"': result.Append('"'); break;
                    case '\\': result.Append('\\'); break;
                    case 'n': result.Append('\n'); break;
                    case 't': result.Append('\t'); break;
                    case 'r': result.Append('\r'); break;
                    case 'b': result.Append('\b'); break;
                    case 'f': result.Append('\f'); break;
                    case 'u': result.Append(Unicode(4)); break;
                    case 'U': result.Append(Unicode(8)); break;
                    default: throw Error($"'\\{escape}' is no escape");
                }
            }
        }

        private string Unicode(int length)
        {
            if (_pos + length > text.Length) throw Error("a unicode escape is cut short");
            var hex = text.Substring(_pos, length);
            if (!int.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var code)
                || code > 0x10FFFF || code is >= 0xD800 and <= 0xDFFF)
                throw Error($"'{hex}' is no unicode scalar");
            _pos += length;
            return char.ConvertFromUtf32(code);
        }

        private string LiteralString()
        {
            _pos++;
            var start = _pos;
            while (!AtEnd && Current != '\'' && Current != '\n') _pos++;
            if (Current != '\'') throw Error("a string is not closed");
            var value = text[start.._pos];
            _pos++;
            return value;
        }

        private TomlArray Array()
        {
            _pos++;
            var array = new TomlArray();
            while (true)
            {
                SkipBlank();
                if (Current == ']') { _pos++; return array; }
                array.Add(Value());
                SkipBlank();
                if (Current == ',') { _pos++; continue; }
                if (Current == ']') { _pos++; return array; }
                throw Error("expected ',' or ']' in an array");
            }
        }

        private TomlTable InlineTable()
        {
            var line = _line;
            _pos++;
            var table = new TomlTable { Line = line, Explicit = true, Inline = true };
            SkipSpaces();
            if (Current == '}') { _pos++; return table; }
            while (true)
            {
                SkipSpaces();
                var keys = Key();
                SkipSpaces();
                Expect('=');
                SkipSpaces();
                var value = Value();
                var target = table;
                for (var i = 0; i < keys.Count - 1; i++) target = Descend(target, keys[i], line);
                if (target.Contains(keys[^1]))
                    throw new TomlException(line, 1, $"'{string.Join('.', keys)}' is defined twice");
                target.Add(keys[^1], value);
                SkipSpaces();
                if (Current == ',') { _pos++; continue; }
                if (Current == '}') { _pos++; return table; }
                throw Error("expected ',' or '}' in an inline table");
            }
        }

        private void Expect(char c)
        {
            if (Current != c) throw Error($"expected '{c}'");
            _pos++;
        }

        private void SkipSpaces()
        {
            while (Current is ' ' or '\t') _pos++;
        }

        /// <summary>Spaces, comments and line ends: between lines and inside an array.</summary>
        private void SkipBlank()
        {
            while (!AtEnd)
            {
                if (Current is ' ' or '\t' or '\r') { _pos++; continue; }
                if (Current == '\n') { NewLine(); continue; }
                if (Current == '#') { while (!AtEnd && Current != '\n') _pos++; continue; }
                return;
            }
        }

        private void EndOfLine()
        {
            SkipSpaces();
            if (Current == '#') while (!AtEnd && Current != '\n') _pos++;
            if (Current == '\r') _pos++;
            if (AtEnd) return;
            if (Current != '\n') throw Error("expected the end of the line");
            NewLine();
        }

        private void NewLine()
        {
            _pos++;
            _line++;
            _lineStart = _pos;
        }
    }
}

/// <summary>A TOML table: its keys in the order they were written, each a <see cref="string"/>, a
/// <see cref="long"/>, a <see cref="bool"/>, a <see cref="TomlArray"/> or a table.</summary>
public sealed class TomlTable
{
    private readonly List<string> _order = new();
    private readonly Dictionary<string, object> _values = new(StringComparer.Ordinal);

    public IReadOnlyList<string> Keys => _order;

    /// <summary>The line the table was opened on, for a diagnostic about it.</summary>
    public int Line { get; init; }

    /// <summary>Opened by a header or written inline, rather than implied by a dotted key.</summary>
    internal bool Explicit { get; set; }

    internal bool Inline { get; init; }

    public bool TryGet(string key, out object value) => _values.TryGetValue(key, out value!);

    internal bool Contains(string key) => _values.ContainsKey(key);

    internal void Add(string key, object value)
    {
        _order.Add(key);
        _values[key] = value;
    }
}

/// <summary>A TOML array; <see cref="OfTables"/> when <c>[[…]]</c> headers made it.</summary>
public sealed class TomlArray : List<object>
{
    internal bool OfTables { get; init; }
}

/// <summary>Text that is not TOML — or not the subset a manifest uses — at a line and column.</summary>
public sealed class TomlException(int line, int column, string message) : Exception(message)
{
    public int Line { get; } = line;
    public int Column { get; } = column;
}
