using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lyric5.Toolchain;

namespace Lyric5.Build;

/// <summary>A header the generator could not read: what the C compiler said, or why it was not asked.</summary>
public sealed class BindgenException(string message) : Exception(message);

/// <summary>A declaration the module leaves out, where it was, and why (spec 15 §5 rule 19).</summary>
public sealed record LeftOut(string Name, string File, int Line, string Why);

/// <summary>What the generator made of a header: the module's text, the files the C compiler read —
/// what the module is made anew for when one changes —, and what it left out.</summary>
public sealed record Bound(string Text, IReadOnlyList<string> Read, IReadOnlyList<LeftOut> Left);

/// <summary>
/// A module of a package from a C header (spec 15 §5.4): the declarations of the header and of the
/// headers beside and below it, as the C compiler reads them for one target — its syntax tree as
/// JSON, which zig cc and clang write, and its macros from the preprocessor — mapped by the type
/// table of 13 §2–§3. What it cannot write is left out and listed at the module's end.
/// </summary>
public static partial class Bindgen
{
    /// <summary>The generator's own version: a module it made is made anew when it changes.</summary>
    public const string Version = "1";

    /// <summary>As long as the build gives one run of the C compiler (<see cref="CBuild.Limit"/>): zig's
    /// first look at another target's headers, on a machine under load, took more than two minutes.</summary>
    private static readonly TimeSpan CompileTime = TimeSpan.FromMinutes(5);

    /// <summary>The C compiler within <see cref="CompileTime"/>; one that does not finish is the
    /// environment's failure, said as the header's finding is — never the toolchain's own.</summary>
    private static ProcessRunner.Result Compiler(CCompiler compiler, IReadOnlyList<string> arguments, string header)
    {
        CBuild.Said(compiler.Path, arguments);
        try { return ProcessRunner.Run(compiler.Path, arguments, CompileTime); }
        catch (TimeoutException)
        {
            throw new BindgenException($"{compiler.Name} did not read {header} within {CompileTime.TotalMinutes:0} minutes");
        }
    }

    /// <summary>The words of Lyric a C name cannot be (spec 01 §2 rule 2); such a name gets a <c>_</c>.</summary>
    private static readonly HashSet<string> Words =
    [
        "import", "as", "pub", "internal", "private", "static", "struct", "class", "enum", "interface", "extend", "fn", "mut",
        "let", "var", "if", "else", "while", "do", "for", "in", "is", "match", "break", "continue", "return", "yield", "defer",
        "try", "catch", "throw", "true", "false", "null", "this", "comptime", "inline", "quote", "macro",
        // and the builtin types' names, which no declaration takes (03 §1.1)
        "int", "int8", "int16", "int32", "int64", "uint", "uint8", "uint16", "uint32", "uint64", "float", "float32", "float64",
        "bool", "char", "string", "void",
    ];

    /// <summary>The module of <paramref name="header"/> for <paramref name="target"/>, its includes found
    /// in <paramref name="include"/>, with <paramref name="defines"/>.</summary>
    /// <param name="shown">The header as the manifest names it, for the module's first line.</param>
    /// <param name="version">The toolchain's version, for the module's first line.</param>
    /// <exception cref="BindgenException">The C compiler writes no syntax tree, or the header does not compile.</exception>
    public static Bound Make(CCompiler compiler, Target target, string header, IReadOnlyList<string> include,
        IReadOnlyList<string> defines, string shown, string version)
    {
        if (compiler.Kind == CCompilerKind.Gcc)
            throw new BindgenException($"{compiler.Name} writes no syntax tree of a header — bindings are made with zig cc or clang (LYRIC_CC)");
        if (!target.IsHost && compiler.Kind != CCompilerKind.Zig)
            throw new BindgenException($"{compiler.Name} cannot read a header for {target.Triple}; bindings for another target are made with zig cc");
        header = Path.GetFullPath(header);
        List<string> flags = compiler.Kind == CCompilerKind.Zig ? ["cc", "-target", target.Triple] : [];
        flags.Add("-std=c11");
        foreach (var define in defines) flags.Add("-D" + define);
        foreach (var directory in include) { flags.Add("-I"); flags.Add(Path.GetFullPath(directory)); }

        var tree = Compiler(compiler, [.. flags, "-Xclang", "-ast-dump=json", "-fsyntax-only", header], header);
        // zig's driver ends a syntax-only run with "FileNotFound" and exit 1 after the tree is out;
        // what else is an error is the header's
        var errors = tree.Stderr.Split('\n').Where(l => l.Contains("error", StringComparison.Ordinal) && !l.Contains("FileNotFound", StringComparison.Ordinal)
                                                           && !l.Contains("generated.", StringComparison.Ordinal)).ToList();
        if (errors.Count > 0 || tree.Stdout.Length == 0)
            throw new BindgenException(errors.Count > 0 ? string.Join("; ", errors.Select(e => e.Trim())) : tree.Stderr.Trim());

        var depends = Path.Combine(Path.GetTempPath(), $"lyric-bindgen-{Environment.ProcessId}-{Guid.NewGuid():N}.d");
        string macros;
        List<string> read;
        try
        {
            var pre = Compiler(compiler, [.. flags, "-E", "-dM", "-MD", "-MF", depends, header], header);
            if (pre.ExitCode != 0) throw new BindgenException(pre.Stderr.Trim());
            macros = pre.Stdout;
            read = File.Exists(depends) ? Dependencies(File.ReadAllText(depends)) : [header];
        }
        finally { if (File.Exists(depends)) File.Delete(depends); }

        var below = Path.GetDirectoryName(header)! + Path.DirectorySeparatorChar;
        bool Ours(string? file) => file is not null && (Path.GetFullPath(file) is var full) && (full == header || full.StartsWith(below, StringComparison.Ordinal));
        using var json = JsonDocument.Parse(tree.Stdout);
        var writer = new Writer(target, below);
        writer.Read(json.RootElement, Ours);
        writer.ReadMacros(macros, read.Where(Ours).Select(Path.GetFullPath).Distinct().ToList());
        return new Bound(writer.Module(shown, target, version), read.Select(Path.GetFullPath).Distinct().ToList(), writer.Left);
    }

    /// <summary>The files a make rule names after its colon.</summary>
    private static List<string> Dependencies(string rule)
    {
        var text = rule.Replace("\\\r\n", " ").Replace("\\\n", " ");
        var colon = text.IndexOf(": ", StringComparison.Ordinal);
        var files = new List<string>();
        var current = new StringBuilder();
        for (var i = colon + 2; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\\' && i + 1 < text.Length && text[i + 1] == ' ') { current.Append(' '); i++; }
            else if (char.IsWhiteSpace(c)) { if (current.Length > 0) { files.Add(current.ToString()); current.Clear(); } }
            else current.Append(c);
        }
        if (current.Length > 0) files.Add(current.ToString());
        return files;
    }

    /// <summary>A C type as clang prints it, taken apart.</summary>
    private abstract record CType
    {
        public sealed record Named(string Name, bool Const) : CType;
        public sealed record Pointer(CType To) : CType;
        public sealed record Array(CType Of, long Length) : CType;
        public sealed record Function(CType Result, IReadOnlyList<CType> Parameters, bool Variadic) : CType;
    }

    /// <summary>Where a type stands: what 13 §2–§3 let cross there.</summary>
    private enum Use { Parameter, Result, Field, Pointee, Alias }

    /// <summary>A type mapped, or why it is not.</summary>
    private readonly record struct Mapped(string? Lyric, string? Why, bool Pointer = false)
    {
        public static Mapped No(string why) => new(null, why);
    }

    private static readonly string[] Qualifiers = ["const", "volatile", "restrict", "__restrict", "_Nonnull", "_Nullable", "_Null_unspecified", "__unaligned"];

    /// <summary>The type clang prints, read: a pointer to a function, an array, a pointer, or a name.</summary>
    private static CType? Parse(string text)
    {
        text = text.Trim();
        // R (*)(P) — or (**) — a pointer to a function
        var fp = FunctionPointer().Match(text);
        if (fp.Success)
        {
            if (Function(fp.Groups["result"].Value, fp.Groups["parameters"].Value) is not { } function) return null;
            CType type = function;
            for (var i = 0; i < fp.Groups["stars"].Value.Count(c => c == '*'); i++) type = new CType.Pointer(type);
            return type;
        }
        var array = ArraySuffix().Match(text);
        if (array.Success)
        {
            if (Parse(array.Groups["of"].Value) is not { } element) return null;
            var lengths = Regex.Matches(array.Groups["dims"].Value, @"\[(\d*)\]").Select(m => m.Groups[1].Value).ToList();
            CType type = element;
            for (var i = lengths.Count - 1; i >= 0; i--)
                type = new CType.Array(type, lengths[i].Length == 0 ? -1 : long.Parse(lengths[i], CultureInfo.InvariantCulture));
            return type;
        }
        // a function's own type, 'size_t (const char *, Flags)': its parameters are in the last parentheses
        if (text.EndsWith(')'))
        {
            var depth = 0;
            for (var i = text.Length - 1; i >= 0; i--)
            {
                if (text[i] == ')') depth++;
                else if (text[i] == '(' && --depth == 0)
                    return i == 0 ? null : Function(text[..i], text[(i + 1)..^1]);
            }
            return null;
        }
        // trailing qualifiers belong to the pointer: 'char *const' is a pointer
        var words = text.Replace("*", " * ").Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        var star = words.LastIndexOf("*");
        if (star >= 0)
        {
            if (words.Skip(star + 1).Any(w => !Qualifiers.Contains(w))) return null;
            return Parse(string.Join(' ', words.Take(star))) is { } to ? new CType.Pointer(to) : null;
        }
        var isConst = words.Contains("const");
        var name = string.Join(' ', words.Where(w => !Qualifiers.Contains(w)));
        return name.Length == 0 ? null : new CType.Named(name, isConst);
    }

    private static CType.Function? Function(string result, string parameters)
    {
        if (Parse(result) is not { } returns) return null;
        var list = new List<CType>();
        var variadic = false;
        var depth = 0;
        var start = 0;
        var text = parameters.Trim();
        for (var i = 0; i <= text.Length; i++)
        {
            if (i < text.Length && text[i] == '(') depth++;
            else if (i < text.Length && text[i] == ')') depth--;
            else if (i == text.Length || text[i] == ',' && depth == 0)
            {
                var one = text[start..i].Trim();
                start = i + 1;
                if (one == "...") variadic = true;
                else if (one.Length > 0 && one != "void")
                {
                    if (Parse(one) is not { } parameter) return null;
                    list.Add(parameter);
                }
            }
        }
        return new CType.Function(returns, list, variadic);
    }

    /// <summary>
    /// Reads the declarations of the header's files in the order they come, and writes them. A
    /// location in clang's JSON names its file and line only where they change from the location
    /// written before it — anywhere in the tree —, so the tree is walked in its order to know them.
    /// </summary>
    private sealed class Writer(Target target, string below)
    {
        private string? _file;
        private int _line;

        private readonly List<string> _out = [];
        public readonly List<LeftOut> Left = [];
        private readonly HashSet<string> _imports = new(StringComparer.Ordinal);

        /// <summary>Every typedef of the translation unit — the system's too —, by name: its type,
        /// and whether it is one of the header's (it is written as an alias then).</summary>
        private readonly Dictionary<string, (string Type, bool Ours, string? OwnedTag)> _typedefs = new(StringComparer.Ordinal);

        /// <summary>The header's structs by C name: their fields, or none for one C declares without them.</summary>
        private readonly Dictionary<string, Record> _structs = new(StringComparer.Ordinal);

        /// <summary>The header's enums by C name: the integer type of their constants.</summary>
        private readonly Dictionary<string, string> _enums = new(StringComparer.Ordinal);

        /// <summary>The structs left out, by C name: a value of one needs it.</summary>
        private readonly HashSet<string> _missing = new(StringComparer.Ordinal);

        private readonly List<Declaration> _declarations = [];

        private sealed record Record(string Name, string File, int Line, List<(string Name, string Type, bool Bits)>? Fields, bool Union);

        private sealed record Declaration(string Kind, string Name, string File, int Line, JsonElement Node);

        public void Read(JsonElement unit, Func<string?, bool> ours)
        {
            if (!unit.TryGetProperty("inner", out var inner)) return;
            // anonymous structs and enums named by their typedef: the tag's id to the name
            var named = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var node in inner.EnumerateArray())
            {
                var (file, line) = Locate(node);
                var kind = node.GetProperty("kind").GetString()!;
                var name = node.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                if (kind == "TypedefDecl")
                {
                    var owned = OwnedTag(node);
                    _typedefs[name] = (node.GetProperty("type").GetProperty("qualType").GetString()!, ours(file), owned);
                    if (owned is not null && ours(file)) named.TryAdd(owned, name);
                }
                if (!ours(file) || node.TryGetProperty("isImplicit", out var implicitly) && implicitly.GetBoolean()) continue;
                _declarations.Add(new Declaration(kind, name, Display(file!), line, node));
            }
            foreach (var d in _declarations)
            {
                var id = d.Node.GetProperty("id").GetString()!;
                var name = d.Name.Length > 0 ? d.Name : named.GetValueOrDefault(id) ?? "";
                if (d.Kind == "RecordDecl" && name.Length > 0)
                {
                    var union = d.Node.TryGetProperty("tagUsed", out var tag) && tag.GetString() == "union";
                    List<(string, string, bool)>? fields = null;
                    if (d.Node.TryGetProperty("completeDefinition", out var complete) && complete.GetBoolean())
                    {
                        fields = [];
                        foreach (var field in Children(d.Node).Where(c => c.GetProperty("kind").GetString() == "FieldDecl"))
                            fields.Add((field.TryGetProperty("name", out var fn) ? fn.GetString() ?? "" : "",
                                field.GetProperty("type").GetProperty("qualType").GetString()!,
                                field.TryGetProperty("isBitfield", out var bits) && bits.GetBoolean()));
                    }
                    // a declaration without its fields does not undo one with them
                    if (fields is not null || !_structs.ContainsKey(name))
                        _structs[name] = new Record(name, d.File, d.Line, fields, union);
                }
                if (d.Kind == "EnumDecl" && name.Length > 0)
                {
                    var type = d.Node.TryGetProperty("fixedUnderlyingType", out var fixedType)
                        ? fixedType.GetProperty("qualType").GetString()!
                        : Children(d.Node).Select(c => c.GetProperty("type").GetProperty("qualType").GetString()).FirstOrDefault() ?? "int";
                    _enums[name] = type;
                }
            }
            // a struct is written where each of its fields is: a struct of a struct left out is left
            // out too, until nothing changes
            bool changed;
            do
            {
                changed = false;
                foreach (var record in _structs.Values)
                    if (!_missing.Contains(record.Name) && record.Fields is not null && StructWhy(record) is not null)
                        changed = _missing.Add(record.Name);
            } while (changed);
            foreach (var d in _declarations) Write(d, named);
        }

        private static IEnumerable<JsonElement> Children(JsonElement node) =>
            node.TryGetProperty("inner", out var inner) ? inner.EnumerateArray() : [];

        private static string? OwnedTag(JsonElement typedef) =>
            Children(typedef).Where(c => c.TryGetProperty("ownedTagDecl", out _))
                .Select(c => c.GetProperty("ownedTagDecl").GetProperty("id").GetString()).FirstOrDefault();

        private string Display(string file) =>
            Path.GetFullPath(file) is var full && full.StartsWith(below, StringComparison.Ordinal) ? full[below.Length..].Replace('\\', '/') : Path.GetFileName(file);

        /// <summary>The node's own place, after the places written before it; the rest of the node is
        /// walked so that the next one is read right.</summary>
        private (string? File, int Line) Locate(JsonElement node)
        {
            (string?, int) at = (_file, _line);
            if (node.TryGetProperty("loc", out var loc)) at = Place(loc);
            foreach (var property in node.EnumerateObject())
                if (property.Name != "loc") Walk(property.Value);
            return at;
        }

        /// <summary>A location: a macro's has where it is spelled, then where it is expanded — the
        /// declaration is where it is expanded.</summary>
        private (string? File, int Line) Place(JsonElement loc)
        {
            if (loc.TryGetProperty("spellingLoc", out var spelling)) Place(spelling);
            if (loc.TryGetProperty("expansionLoc", out var expansion)) return Place(expansion);
            if (loc.TryGetProperty("file", out var file)) _file = file.GetString();
            if (loc.TryGetProperty("line", out var line)) _line = line.GetInt32();
            return (_file, _line);
        }

        private void Walk(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Array)
                foreach (var item in value.EnumerateArray()) Walk(item);
            else if (value.ValueKind == JsonValueKind.Object)
            {
                if (value.TryGetProperty("offset", out _) || value.TryGetProperty("spellingLoc", out _)) { Place(value); return; }
                foreach (var property in value.EnumerateObject()) Walk(property.Value);
            }
        }

        private static string Name(string c) => Words.Contains(c) ? c + "_" : c;

        private string? StructWhy(Record record)
        {
            if (record.Union) return "a union does not cross";
            foreach (var (name, type, bits) in record.Fields!)
            {
                if (bits) return $"its field '{name}' is a bit field, which does not cross";
                if (name.Length == 0) return "a field of it has no name — a union or a struct within it";
                if (Map(type, Use.Field) is { Lyric: null } no) return $"its field '{name}' is {no.Why}";
            }
            return null;
        }

        private void Write(Declaration d, Dictionary<string, string> named)
        {
            var id = d.Node.GetProperty("id").GetString()!;
            var name = d.Name.Length > 0 ? d.Name : named.GetValueOrDefault(id) ?? "";
            switch (d.Kind)
            {
                case "FunctionDecl":
                    WriteFunction(d);
                    break;
                // written where its fields are declared, once
                case "RecordDecl" when name.Length > 0 && _structs[name] is { Fields: not null } record
                                       && record.File == d.File && record.Line == d.Line:
                    if (_written.Add("struct " + name)) WriteStruct(record);
                    break;
                case "EnumDecl":
                    WriteEnum(d, name);
                    break;
                case "TypedefDecl":
                    WriteTypedef(d);
                    break;
                case "VarDecl":
                    Left.Add(new LeftOut(d.Name, d.File, d.Line, "a variable does not cross"));
                    break;
            }
        }

        private readonly HashSet<string> _written = new(StringComparer.Ordinal);

        private void WriteStruct(Record record)
        {
            if (_missing.Contains(record.Name))
            {
                Left.Add(new LeftOut(record.Union ? $"union {record.Name}" : $"struct {record.Name}", record.File, record.Line,
                    StructWhy(record) ?? "a struct it holds is left out"));
                return;
            }
            var text = new StringBuilder($"@Layout(C)\npub struct {Name(record.Name)} {{\n");
            foreach (var (field, type, _) in record.Fields!)
                text.Append($"    pub {Name(field)}: {Map(type, Use.Field).Lyric},\n");
            _out.Add(text.Append('}').ToString());
        }

        private void WriteEnum(Declaration d, string name)
        {
            var type = Map(name.Length > 0 && _enums.TryGetValue(name, out var t) ? t : "int", Use.Field).Lyric ?? "int32";
            if (name.Length > 0 && _written.Add("enum " + name)) _out.Add($"pub type {Name(name)} = {type};");
            BigInteger next = 0;
            var lines = new List<string>();
            foreach (var constant in Children(d.Node).Where(c => c.GetProperty("kind").GetString() == "EnumConstantDecl"))
            {
                var value = ValueOf(constant) ?? next;
                next = value + 1;
                var own = Map(constant.GetProperty("type").GetProperty("qualType").GetString()!, Use.Field).Lyric ?? type;
                lines.Add($"pub let {Name(constant.GetProperty("name").GetString()!)}: {(name.Length > 0 ? Name(name) : own)} = {value};");
            }
            _out.Add(string.Join("\n", lines));
        }

        /// <summary>The value clang computed for an enum's constant, where it has an initializer.</summary>
        private static BigInteger? ValueOf(JsonElement constant)
        {
            foreach (var child in Children(constant))
                if (child.TryGetProperty("value", out var value) && BigInteger.TryParse(value.GetString(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number))
                    return number;
            return null;
        }

        private void WriteTypedef(Declaration d)
        {
            var (type, _, owned) = _typedefs[d.Name];
            // a struct or an enum it names under the same name is written under that name already
            if (Parse(type) is CType.Named { Name: var tag } && (tag == $"struct {d.Name}" || tag == $"enum {d.Name}" || tag == $"union {d.Name}"))
            {
                if (tag.StartsWith("union ", StringComparison.Ordinal) && _written.Add("union " + d.Name))
                    Left.Add(new LeftOut(tag, d.File, d.Line, "a union does not cross"));
                return;
            }
            if (Parse(type) is CType.Named { Name: var opaque } && opaque.StartsWith("struct ", StringComparison.Ordinal)
                && _structs.GetValueOrDefault(opaque[7..]) is { Fields: null } or null) return;   // a pointer names it as Ptr<void>
            var mapped = Map(type, Use.Alias);
            if (mapped.Lyric is null) Left.Add(new LeftOut(d.Name, d.File, d.Line, $"it names {mapped.Why}"));
            else _out.Add($"pub type {Name(d.Name)} = {mapped.Lyric};");
        }

        private void WriteFunction(Declaration d)
        {
            if (!_written.Add("fn " + d.Name)) return;
            if (d.Node.TryGetProperty("inline", out var inline) && inline.GetBoolean()
                || d.Node.TryGetProperty("storageClass", out var storage) && storage.GetString() == "static")
            {
                Left.Add(new LeftOut(d.Name, d.File, d.Line, "a static or inline function has no symbol to link"));
                return;
            }
            var type = Parse(d.Node.GetProperty("type").GetProperty("qualType").GetString()!) as CType.Function;
            var parameters = Children(d.Node).Where(c => c.GetProperty("kind").GetString() == "ParmVarDecl").ToList();
            var written = new List<string>();
            for (var i = 0; i < parameters.Count; i++)
            {
                var name = parameters[i].TryGetProperty("name", out var n) && n.GetString() is { Length: > 0 } given ? Name(given) : $"p{i}";
                var mapped = Map(parameters[i].GetProperty("type").GetProperty("qualType").GetString()!, Use.Parameter);
                if (mapped.Lyric is null)
                {
                    Left.Add(new LeftOut(d.Name, d.File, d.Line, $"its parameter '{name}' is {mapped.Why}"));
                    return;
                }
                written.Add($"{name}: {mapped.Lyric}");
            }
            if (type is null)
            {
                Left.Add(new LeftOut(d.Name, d.File, d.Line, "its type is one the generator does not read"));
                return;
            }
            var result = Map(type.Result, Use.Result);
            if (result.Lyric is null)
            {
                Left.Add(new LeftOut(d.Name, d.File, d.Line, $"its result is {result.Why}"));
                return;
            }
            if (type.Variadic)
            {
                if (written.Count == 0)
                {
                    Left.Add(new LeftOut(d.Name, d.File, d.Line, "it takes variadic arguments and no parameter before them"));
                    return;
                }
                written.Add("...");
            }
            _out.Add($"pub extern \"C\" fn {Name(d.Name)}({string.Join(", ", written)}): {result.Lyric};");
        }

        private Mapped Map(string text, Use use) =>
            Parse(text) is { } type ? Map(type, use) : Mapped.No($"a type the generator does not read ('{text}')");

        private Mapped Map(CType type, Use use)
        {
            switch (type)
            {
                case CType.Function:
                    return Mapped.No("a function type without its pointer");
                case CType.Array array when use is Use.Field or Use.Alias:
                {
                    // an array of arrays is one array of all their elements, laid out alike
                    var length = 1L;
                    CType element = array;
                    while (element is CType.Array inner)
                    {
                        if (inner.Length < 0) return Mapped.No("an array without its length");
                        length *= inner.Length;
                        element = inner.Of;
                    }
                    var of = Map(element, Use.Field);
                    return of.Lyric is null ? of : new Mapped($"{of.Lyric}[{length}]", null);
                }
                case CType.Array array:
                    return Map(new CType.Pointer(array.Of), use);
                case CType.Pointer pointer:
                    return MapPointer(pointer.To, use);
                case CType.Named named:
                    return MapNamed(named, use);
            }
            return Mapped.No("a type the generator does not read");
        }

        private Mapped MapPointer(CType to, Use use)
        {
            var optional = use is Use.Parameter or Use.Result ? "?" : "";
            if (to is CType.Named { Name: "char", Const: true })
            {
                _imports.Add("CStr");
                return new Mapped($"{optional}CStr", null, Pointer: true);
            }
            if (to is CType.Function function)
            {
                var parameters = new List<string>();
                foreach (var parameter in function.Parameters)
                {
                    var mapped = Map(parameter, Use.Parameter);
                    if (mapped.Lyric is null) return Mapped.No($"a pointer to a function whose parameter is {mapped.Why}");
                    parameters.Add(mapped.Lyric);
                }
                if (function.Variadic) return Mapped.No("a pointer to a function with variadic arguments");
                var result = Map(function.Result, Use.Result);
                if (result.Lyric is null) return Mapped.No($"a pointer to a function whose result is {result.Why}");
                return new Mapped($"{optional}extern \"C\" fn({string.Join(", ", parameters)}) -> {result.Lyric}", null, Pointer: true);
            }
            _imports.Add("Ptr");
            // a pointer is an address: what it points at, where it cannot be written, is void
            var pointee = to is CType.Named { Name: "void" } ? "void" : Map(to, Use.Pointee).Lyric ?? "void";
            return new Mapped($"{optional}Ptr<{pointee}>", null, Pointer: true);
        }

        private Mapped MapNamed(CType.Named named, Use use)
        {
            var name = named.Name;
            if (name == "void") return use is Use.Result ? new Mapped("void", null) : Mapped.No("void");
            if (Scalar(name) is { } scalar) return new Mapped(scalar, null);
            if (name.StartsWith("struct ", StringComparison.Ordinal))
            {
                var tag = name[7..];
                if (!_structs.TryGetValue(tag, out var record) || record.Fields is null)
                    return Mapped.No($"a struct whose fields C does not declare here ('{tag}')");
                return _missing.Contains(tag) ? Mapped.No($"the struct '{tag}', which is left out") : new Mapped(Name(tag), null);
            }
            if (name.StartsWith("union ", StringComparison.Ordinal)) return Mapped.No($"a union ('{name[6..]}')");
            if (name.StartsWith("enum ", StringComparison.Ordinal))
            {
                var tag = name[5..];
                return _enums.ContainsKey(tag) ? new Mapped(Name(tag), null) : new Mapped("int32", null);
            }
            if (Fixed(name) is { } known) return new Mapped(known, null);
            if (name is "va_list" or "__builtin_va_list" or "__gnuc_va_list") return Mapped.No("a va_list, which does not cross");
            if (!_typedefs.TryGetValue(name, out var typedef)) return Mapped.No($"a type the generator does not know ('{name}')");
            // the header's typedef is written as its alias, by its name — as a pointer, optional where C may give null
            if (typedef.Ours && Parse(typedef.Type) is { } underlying)
            {
                if (underlying is CType.Named { Name: var tag } && (tag == $"struct {name}" || tag == $"enum {name}"))
                    return MapNamed(new CType.Named(tag, named.Const), use);
                var alias = Map(underlying, Use.Alias);
                if (alias.Lyric is null) return alias;
                if (underlying is CType.Named { Name: var opaque } && opaque.StartsWith("struct ", StringComparison.Ordinal)) return alias;
                return new Mapped((alias.Pointer && use is Use.Parameter or Use.Result ? "?" : "") + Name(name), null, alias.Pointer);
            }
            return Map(typedef.Type, use);
        }

        /// <summary>C's integers and floats by their width on the target (13 §2).</summary>
        private string? Scalar(string name)
        {
            var windows = target.Os == TargetOs.Windows;
            // char is unsigned where the ABI says so: Arm's, but not Apple's
            var unsignedChar = target.Arch == "aarch64" && target.Os != TargetOs.MacOs && !windows;
            return name switch
            {
                "char" => unsignedChar ? "uint8" : "int8",
                "signed char" => "int8",
                "unsigned char" => "uint8",
                "short" or "short int" or "signed short" or "signed short int" => "int16",
                "unsigned short" or "unsigned short int" => "uint16",
                "int" or "signed" or "signed int" => "int32",
                "unsigned" or "unsigned int" => "uint32",
                "long" or "long int" or "signed long" or "signed long int" => windows ? "int32" : "int64",
                "unsigned long" or "unsigned long int" => windows ? "uint32" : "uint64",
                "long long" or "long long int" or "signed long long" or "signed long long int" => "int64",
                "unsigned long long" or "unsigned long long int" => "uint64",
                "float" => "float32",
                "double" => "float",
                "_Bool" or "bool" => "bool",
                _ => null,
            };
        }

        /// <summary>The standard's typedefs whose type is a promise rather than a width (13 §2).</summary>
        private static string? Fixed(string name) => name switch
        {
            "int8_t" => "int8", "int16_t" => "int16", "int32_t" => "int32", "int64_t" => "int64",
            "uint8_t" => "uint8", "uint16_t" => "uint16", "uint32_t" => "uint32", "uint64_t" => "uint64",
            "size_t" or "uintptr_t" => "uint",
            "ssize_t" or "ptrdiff_t" or "intptr_t" => "int",
            _ => null,
        };

        /// <summary>The header's macros, from what the preprocessor ended with: a <c>#define</c> of one
        /// integer or one string literal as a constant, an empty one passed over, the rest listed.</summary>
        public void ReadMacros(string defined, IReadOnlyList<string> files)
        {
            var bodies = new Dictionary<string, (bool Function, string Body)>(StringComparer.Ordinal);
            foreach (var line in defined.Split('\n'))
                if (MacroLine().Match(line.TrimEnd('\r')) is { Success: true } m)
                    bodies[m.Groups["name"].Value] = (m.Groups["args"].Success, m.Groups["body"].Value.Trim());
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var file in files)
            {
                var lines = File.ReadAllLines(file);
                for (var i = 0; i < lines.Length; i++)
                {
                    if (DefineLine().Match(lines[i]) is not { Success: true } m || !seen.Add(m.Groups["name"].Value)) continue;
                    var name = m.Groups["name"].Value;
                    if (!bodies.TryGetValue(name, out var macro) || _written.Contains("fn " + name)) continue;
                    var where = Display(file);
                    if (macro.Function) Left.Add(new LeftOut(name, where, i + 1, "a function-like macro is no declaration"));
                    else if (macro.Body.Length == 0) continue;
                    else if (Integer(macro.Body) is ({ } value, var type))
                        _out.Add($"pub let {Name(name)}: {type} = {value};");
                    else if (LyricString(macro.Body) is { } text) _out.Add($"pub let {Name(name)}: string = {text};");
                    else Left.Add(new LeftOut(name, where, i + 1, "its body is no integer and no string literal"));
                }
            }
        }

        /// <summary>A C integer literal — decimal, hexadecimal, octal or binary, with its suffix, in
        /// parentheses or with a minus —, and the type C gives it: the first of <c>int32</c> and
        /// <c>int64</c> it fits, unsigned with a <c>u</c> (a hexadecimal one may be unsigned too), at
        /// least 64 bits with an <c>l</c>; or <c>null</c>.</summary>
        private static (BigInteger Value, string Type)? Integer(string body)
        {
            var m = IntegerLiteral().Match(body);
            if (!m.Success) return null;
            var digits = m.Groups["digits"].Value;
            BigInteger value;
            if (digits.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                value = BigInteger.Parse("0" + digits[2..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
            else if (digits.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
                value = digits[2..].Aggregate(BigInteger.Zero, (v, c) => v * 2 + (c - '0'));
            else if (digits.Length > 1 && digits[0] == '0')
                value = digits[1..].Aggregate(BigInteger.Zero, (v, c) => v * 8 + (c - '0'));
            else value = BigInteger.Parse(digits, CultureInfo.InvariantCulture);
            var suffix = m.Groups["suffix"].Value.ToLowerInvariant();
            var unsigned = suffix.Contains('u');
            var wide = suffix.Contains('l');
            var hexadecimal = !digits.All(char.IsAsciiDigit) || digits.Length > 1 && digits[0] == '0';
            string? type = null;
            if (!unsigned && !wide && value <= int.MaxValue) type = "int32";
            else if ((unsigned || hexadecimal) && !wide && value <= uint.MaxValue) type = "uint32";
            else if (!unsigned && value <= long.MaxValue) type = "int64";
            else if ((unsigned || hexadecimal) && value <= ulong.MaxValue) type = "uint64";
            if (type is null) return null;
            if (m.Groups["minus"].Success)
            {
                if (type.StartsWith('u')) return null;
                value = -value;
            }
            return (value, type);
        }

        /// <summary>A C string literal written as Lyric writes it, or <c>null</c> where it is none.</summary>
        private static string? LyricString(string body)
        {
            if (!StringLiteral().IsMatch(body)) return null;
            var text = new StringBuilder("\"");
            for (var i = 1; i < body.Length - 1; i++)
            {
                var c = body[i];
                if (c != '\\') { text.Append(c); continue; }
                var e = body[++i];
                switch (e)
                {
                    case 'n': text.Append("\\n"); break;
                    case 't': text.Append("\\t"); break;
                    case 'r': text.Append("\\r"); break;
                    case '\\': text.Append("\\\\"); break;
                    case '"': text.Append("\\\""); break;
                    case '\'': text.Append("\\'"); break;
                    case '0' when i + 1 >= body.Length - 1 || !char.IsAsciiDigit(body[i + 1]): text.Append("\\0"); break;
                    case 'x' when i + 2 < body.Length - 1 && char.IsAsciiHexDigit(body[i + 1]) && char.IsAsciiHexDigit(body[i + 2])
                                  && Convert.ToInt32(body.Substring(i + 1, 2), 16) < 0x80:
                        text.Append($"\\u{{{body.Substring(i + 1, 2)}}}");
                        i += 2;
                        break;
                    default: return null;
                }
            }
            return text.Append('"').ToString();
        }

        /// <summary>The module: what it imports, its declarations in the order they came, and the list
        /// of what it left out.</summary>
        public string Module(string shown, Target target, string version)
        {
            var text = new StringBuilder();
            text.Append($"// The bindings of {shown} for {target.Triple}, made by lyric {version} (bindgen {Version}).\n");
            text.Append("// Written anew by every build that needs them: edit the header, not this.\n");
            if (_imports.Count > 0) text.Append($"\nimport std.ffi {{ {string.Join(", ", _imports.Order(StringComparer.Ordinal))} }};\n");
            foreach (var declaration in _out.Where(o => o.Length > 0)) text.Append('\n').Append(declaration).Append('\n');
            if (Left.Count > 0)
            {
                text.Append("\n// Left out:\n");
                foreach (var left in Left) text.Append($"// - {left.Name} ({left.File}:{left.Line}): {left.Why}\n");
            }
            return text.ToString();
        }
    }

    [GeneratedRegex(@"^(?<result>.*?)\s*\((?<stars>\*+)\s*(const\s*)?\)\s*\((?<parameters>.*)\)$")]
    private static partial Regex FunctionPointer();

    [GeneratedRegex(@"^(?<of>[^\[]+?)\s*(?<dims>(\[\d*\])+)$")]
    private static partial Regex ArraySuffix();

    [GeneratedRegex(@"^#define (?<name>[A-Za-z_][A-Za-z0-9_]*)(?<args>\([^)]*\))?(?: (?<body>.*))?$")]
    private static partial Regex MacroLine();

    [GeneratedRegex(@"^\s*#\s*define\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)")]
    private static partial Regex DefineLine();

    [GeneratedRegex(@"^\(?\s*(?<minus>-)?\s*(?<digits>0[xX][0-9a-fA-F]+|0[bB][01]+|[0-9]+)(?<suffix>[uU]?[lL]{0,2}|[lL]{1,2}[uU])\s*\)?$")]
    private static partial Regex IntegerLiteral();

    [GeneratedRegex(@"^""(?:[^""\\]|\\.)*""$")]
    private static partial Regex StringLiteral();
}
