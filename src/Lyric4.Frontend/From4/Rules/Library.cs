using System.Text;
using System.Text.RegularExpressions;
using Lyric4.AST;
using Lyric4.Core;
using Lyric4.Resolver;
using Lyric4.Sema;

namespace Lyric4.From4.Rules;

/// <summary>A line of <c>from4.toml</c>: a function of 4.6's library and its form in Lyric 5.</summary>
internal sealed record LibraryEntry(string Rule, string From, string? Name, string? Form, bool Iterator, bool Lambda, bool Throws, bool Statement, string? Hand, string? Moved, IReadOnlyList<string> Imports, string? Review, string? Advice, string? Optional, bool Walk);

/// <summary>The table of <c>from4.toml</c>, embedded in this assembly: 4.6's library by function.</summary>
internal static class LibraryTable
{
    private static readonly Lazy<Dictionary<string, LibraryEntry>> Entries = new(Read);

    private static Dictionary<string, LibraryEntry> Read()
    {
        using var stream = typeof(LibraryTable).Assembly.GetManifestResourceStream("from4.toml")
            ?? throw new InvalidOperationException("from4.toml is not in the assembly");
        using var reader = new StreamReader(stream);
        var table = Lyric.Core.Toml.Parse(reader.ReadToEnd());
        var entries = new Dictionary<string, LibraryEntry>(StringComparer.Ordinal);
        foreach (var kind in new[] { "free", "method" })
        {
            if (!table.TryGet(kind, out var list)) continue;
            foreach (var row in ((Lyric.Core.TomlArray)list).Cast<Lyric.Core.TomlTable>())
            {
                string? Text(string key) => row.TryGet(key, out var value) ? (string)value : null;
                var from = Text("from")!;
                bool Flag(string key) => row.TryGet(key, out var value) && (bool)value;
                entries.Add(from, new LibraryEntry(Text("rule")!, from, Text("name"), Text("form"),
                    Flag("iterator"), Flag("lambda"), Flag("throws"), Flag("statement"), Text("hand"), Text("moved"),
                    row.TryGet("imports", out var imports) ? [.. ((Lyric.Core.TomlArray)imports).Cast<string>()] : [],
                    Text("review"), Text("advice"), Text("optional"), Flag("walk")));
            }
        }
        return entries;
    }

    public static LibraryEntry? Of(string key) => Entries.Value.GetValueOrDefault(key);

    /// <summary>The table's lines that name no function of 4.6's library: a line with a typing error
    /// would do nothing, silently.</summary>
    public static IReadOnlyList<string> Faults(Func<string, string?> stdlibFile) =>
        [.. Entries.Value.Keys.Where(key =>
        {
            var dot = key.LastIndexOf('.');
            var module = key.StartsWith("string.", StringComparison.Ordinal) ? "std.string" : key[..dot];
            var name = key[(dot + 1)..];
            // a method's line names its type, which stands in some file of the library
            var files = key.StartsWith("std.", StringComparison.Ordinal) || module == "std.string"
                ? new[] { $"{module.Replace('.', '/')}.lyr" }
                : ["std/collections.lyr", "std/result.lyr", "std/string.lyr", "std/time.lyr", "std/json.lyr", "std/random.lyr", "std/io/stream.lyr"];
            return !files.Select(stdlibFile).OfType<string>().Any(text => text.Contains($"fn {name}(", StringComparison.Ordinal)
                || text.Contains($"fn {name}<", StringComparison.Ordinal) || text.Contains($"class {name} ", StringComparison.Ordinal)
                || text.Contains($"struct {name} ", StringComparison.Ordinal) || text.Contains($"type {name} ", StringComparison.Ordinal));
        })];

    /// <summary>Whether the table holds a function of a module: its imports change.</summary>
    public static bool HasModule(string module) => Entries.Value.Keys.Any(key => key.StartsWith(module + ".", StringComparison.Ordinal));

    /// <summary>What an import of a name from a module names in Lyric 5: the name itself, the new
    /// one, or <c>null</c> where the call no longer needs an import.</summary>
    public static string? ImportName(string module, string name) =>
        Of($"{module}.{name}") is { } entry ? entry.Moved is null && entry.Hand is null ? entry.Name : entry.Hand is null ? null : name : name;

    /// <summary>The module an imported name lives in now, where it left the one it came from.</summary>
    public static string? MovedTo(string module, string name) => Of($"{module}.{name}")?.Moved;

    /// <summary>The function a callee or a name stands for, as a key of the table: <c>std.math.absInt</c>
    /// for a free one, <c>string.toChars</c> for a method of a string.</summary>
    public static string? KeyOf(RuleContext context, Expr callee)
    {
        if (Tree.Target(context, callee) is not FunctionSymbol function || !Tree.InStdlib(context, function)) return null;
        var path = context.Model.Sources.GetPath(function.Declaration!.Span.File);
        if (callee is MemberExpr member && Tree.Target(context, member.Target) is not ModuleSymbol)
        {
            var type = context.Model.Types.TypeOf(member.Target) switch
            {
                PrimitiveType { Kind: PrimitiveKind.String } => "string",
                NamedRef { Symbol.Name: var named } => named,
                GenericInstance { Definition.Name: var named } => named,
                // through '?.': the type behind the optional
                Optional { Inner: NamedRef { Symbol.Name: var named } } when member.IsOptional => named,
                Optional { Inner: GenericInstance { Definition.Name: var named } } when member.IsOptional => named,
                // a static one: the type as written, 'List<int>.empty'
                _ when function.IsStatic => context.Model.Sources.GetText(member.Span.File)[member.Target.Span.Start..member.Target.Span.End].Split('<')[0].Split('.')[^1],
                _ => null,
            };
            if (type is not null) return $"{type}.{function.Name}";
        }
        var file = path.Replace('\\', '/');
        var std = file.LastIndexOf("/std/", StringComparison.Ordinal);
        return std < 0 ? null : $"{Path.ChangeExtension(file[(std + 1)..], null).Replace('/', '.')}.{function.Name}";
    }

    /// <summary>The type a written type stands for, as a key of the table: <c>std.io.net.Listener</c>.</summary>
    public static string? KeyOfType(RuleContext context, NamedType named)
    {
        if (Tree.Target(context, named) is not TypeSymbol { Declaration: { } declaration } symbol || !Tree.InStdlib(context, symbol)) return null;
        var file = context.Model.Sources.GetPath(declaration.Span.File).Replace('\\', '/');
        var std = file.LastIndexOf("/std/", StringComparison.Ordinal);
        return std < 0 ? null : $"{Path.ChangeExtension(file[(std + 1)..], null).Replace('/', '.')}.{symbol.Name}";
    }

    /// <summary>What a module's calls of the table need imported beyond what it imports, by module:
    /// a form that names what 4.6's call did not, <c>Instant.now()</c> for <c>nowMillis()</c>.</summary>
    public static Dictionary<string, List<string>> Needed(RuleContext context, Module module)
    {
        var needed = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var call in Tree.Below(module).OfType<CallExpr>())
        {
            if (KeyOf(context, call.Callee) is not { } key || Of(key) is not { Hand: null } entry) continue;
            foreach (var import in entry.Imports)
            {
                var dot = import.LastIndexOf('.');
                var (path, name) = (import[..dot], import[(dot + 1)..]);
                // a module's methods come with any import of it
                var imported = module.Declarations.OfType<ImportDecl>().Any(d => Imports.Target(string.Join('.', d.Path)) == path
                    && (name == "*" || (d.Clause is ImportSelective selective && selective.Names.Contains(name))));
                if (imported) continue;
                if (!needed.TryGetValue(path, out var names)) needed[path] = names = [];
                if (!names.Contains(name)) names.Add(name);
            }
        }
        foreach (var names in needed.Values)
        {
            if (names.Count > 1) names.Remove("*");
            names.Sort(StringComparer.Ordinal);
        }
        return needed;
    }
}

/// <summary>T11, T13, T14, T16: the table's functions. A <c>name</c> is the name written anew where
/// it stands, or a lambda where it was a value; a <c>form</c> replaces what stands between the call's receiver and
/// its arguments, which stay as written. A function with a form used as a value has no form to take:
/// said by hand.</summary>
internal sealed partial class Library : IRule
{
    public string Id => "T11";

    [GeneratedRegex(@"\{(this|\d+)(!|~[a-z0-9]+)?\}")]
    private static partial Regex Placeholder();

    public void Apply(RuleContext context)
    {
        foreach (var module in context.Modules)
        {
            var text = context.TextOf(module);
            var nodes = Tree.Below(module).ToList();
            var sources = nodes.OfType<ForInStmt>().Select(f => f.Iterable).ToHashSet<Expr>(ReferenceEqualityComparer.Instance);
            var callees = nodes.OfType<CallExpr>().Select(c => c.Callee).ToHashSet<Expr>(ReferenceEqualityComparer.Instance);
            var statements = nodes.OfType<ExprStmt>().Select(s => s.Expr).ToHashSet<Expr>(ReferenceEqualityComparer.Instance);
            var inserts = new SortedDictionary<int, (string Rule, FileId File, string Text)>();
            foreach (var call in nodes.OfType<CallExpr>())
            {
                if (LibraryTable.KeyOf(context, call.Callee) is not { } key || LibraryTable.Of(key) is not { } entry) continue;
                if (entry.Hand is { } hand)
                {
                    context.Note(entry.Rule, "LYR-MIG0601", NoteKind.Hand, call.Span,
                        $"'{Conformances.Text(text, call)}' has no form in Lyric 5", hand);
                    continue;
                }
                if (entry.Form is not { } form) continue;
                if (call.Callee is MemberExpr { IsOptional: true } && entry.Optional is null)
                {
                    context.Note(entry.Rule, "LYR-MIG0601", NoteKind.Hand, call.Span,
                        $"'{Conformances.Text(text, call)}' has no form through '?.' in Lyric 5",
                        $"take the optional apart first, with 'if (let v = o)', and write the call on 'v': {entry.Form.Replace("{this}", "v")}");
                    continue;
                }
                if (entry.Statement && !statements.Contains(call))
                    context.Note(entry.Rule, "LYR-MIG0402", NoteKind.Hand, call.Span,
                        entry.Throws
                            ? $"'{Conformances.Text(text, call)}' answered whether it worked; in Lyric 5 it throws, and it answers nothing"
                            : $"'{Conformances.Text(text, call)}' answered whether it worked; in Lyric 5 it answers nothing",
                        entry.Throws ? "call it in a 'try' block, and do in its 'catch' what the 'false' did" : "call it as a statement");
                else
                {
                    if (call.Callee is MemberExpr { IsOptional: true }) form = entry.Optional!;
                    Write(context, text, call, entry, form, sources.Contains(call), inserts);
                    if (entry.Review is { } review)
                        context.Note(entry.Rule, "LYR-MIG0602", NoteKind.Review, call.Span, $"'{Conformances.Text(text, call)}' {review}", entry.Advice);
                }
            }
            // several forms open at one place, '(a + b) - c': one edit
            foreach (var (at, insert) in inserts) context.Replace(insert.Rule, insert.File, at, at, insert.Text);
            foreach (var name in nodes.OfType<Expr>().Where(n => n is IdentifierExpr or MemberExpr))
            {
                if (LibraryTable.KeyOf(context, name) is not { } key || LibraryTable.Of(key) is not { } entry) continue;
                if (entry.Hand is not null) continue;
                if (entry is { Name: { } to, Form: null })
                {
                    var written = name is MemberExpr member ? member.Member : ((IdentifierExpr)name).Name;
                    if (entry.Lambda && !callees.Contains(name))
                        context.Replace(entry.Rule, name.Span, Lambda(context, name, to));
                    else
                        context.Replace(entry.Rule, name.Span.File, name.Span.End - written.Length, name.Span.End, to);
                }
                else if (!callees.Contains(name))
                    context.Note(entry.Rule, "LYR-MIG0311", NoteKind.Hand, name.Span,
                        $"'{Conformances.Text(text, name)}' is written as a method in Lyric 5, and no function stands for it",
                        $"pass a lambda that calls it: (it) => {entry.Form!.Replace("{q}", "").Replace("{0}", "it")}");
            }
            foreach (var named in nodes.OfType<NamedType>())
            {
                if (LibraryTable.KeyOfType(context, named) is not { } key || LibraryTable.Of(key) is not { } entry) continue;
                if (entry.Hand is { } hand)
                    context.Note(entry.Rule, "LYR-MIG0601", NoteKind.Hand, named.Span,
                        $"'{Conformances.Text(text, named)}' has no form in Lyric 5", hand);
                else if (entry.Name is { } to)
                    context.Replace(entry.Rule, named.Span.File, named.Span.End - named.Path[^1].Length, named.Span.End, to);
            }
        }
    }

    /// <summary>A renamed function used as a value: the new one is generic and no value in Lyric 5,
    /// so a lambda calls it, with the parameters 4.6's took.</summary>
    private static string Lambda(RuleContext context, Expr name, string to)
    {
        var declaration = (FunctionDecl)Tree.Target(context, name)!.Declaration!;
        var source = context.Model.Sources.GetText(declaration.Span.File);
        var parameters = declaration.Parameters.Select(p => $"{p.Name}: {source[p.Type.Span.Start..p.Type.Span.End]}");
        var arguments = declaration.Parameters.Select(p => p.Name);
        return $"({string.Join(", ", parameters)}) => {to}({string.Join(", ", arguments)})";
    }

    /// <summary>A call in its form: an edit for each stretch between what stays, so that an argument
    /// stays open to other rules.</summary>
    private static void Write(RuleContext context, string text, CallExpr call, LibraryEntry entry, string form, bool isSource,
        SortedDictionary<int, (string Rule, FileId File, string Text)> inserts)
    {
        // the module the call named stays named
        var qualifier = call.Callee is MemberExpr { Target: var module } ? Conformances.Text(text, module) + "." : "";
        form = form.Replace("{q}", qualifier);
        var position = call.Span.Start;
        var pending = new StringBuilder();
        if (isSource && entry.Walk) pending.Append("try ");
        var at = 0;
        foreach (Match match in Placeholder().Matches(form))
        {
            pending.Append(form, at, match.Index - at);
            at = match.Index + match.Length;
            var what = match.Groups[1].Value == "this"
                ? ((MemberExpr)call.Callee).Target
                : call.Arguments[int.Parse(match.Groups[1].Value)];
            if (match.Groups[2].Value == "!")
            {
                if (Tree.Below(what).Prepend(what).Any(n => n is CallExpr))
                    context.Note(entry.Rule, "LYR-MIG0313", NoteKind.Review, what.Span,
                        $"'{Conformances.Text(text, what)}' is written twice and runs twice: 'substring' takes an end in Lyric 5, not a count",
                        "where it does more than compute, keep its value in a 'let' first");
                pending.Append(Conformances.Text(text, what));
                continue;
            }
            // a type a literal takes by itself, and anything else is cast to
            var cast = match.Groups[2].Value.StartsWith('~') && what is not IntLiteralExpr ? match.Groups[2].Value[1..] : null;
            // a postfix binds to all of it only where it is written so
            var parens = ((at < form.Length && form[at] == '.') || cast is not null) && !Tree.TakesPostfix(what);
            if (parens) pending.Append('(');
            Stretch(context, text, entry, call, position, what.Span.Start, pending, inserts);
            position = what.Span.End;
            if (parens) pending.Append(')');
            if (cast is not null) pending.Append(" as ").Append(cast);
        }
        pending.Append(form, at, form.Length - at);
        if (entry.Iterator) pending.Append(isSource ? ".map((part) => part.toString())" : ".map((part) => part.toString()).toArray()");
        Stretch(context, text, entry, call, position, call.Span.End, pending, inserts);
    }

    private static void Stretch(RuleContext context, string text, LibraryEntry entry, CallExpr call, int start, int end, StringBuilder written,
        SortedDictionary<int, (string Rule, FileId File, string Text)> inserts)
    {
        if (start == end && written.Length > 0)
            inserts[start] = (entry.Rule, call.Span.File, (inserts.TryGetValue(start, out var open) ? open.Text : "") + written);
        else if (text[start..end] != written.ToString()) context.Replace(entry.Rule, call.Span.File, start, end, written.ToString());
        written.Clear();
    }
}

/// <summary>B10: three variants of <c>JsonValue</c> have their whole names in Lyric 5, in an
/// expression and in a pattern.</summary>
internal sealed class JsonVariants : IRule
{
    public string Id => "B10";

    private static readonly Dictionary<string, string> Names = new(StringComparer.Ordinal)
    {
        ["Str"] = "String",
        ["Arr"] = "Array",
        ["Obj"] = "Object",
    };

    public void Apply(RuleContext context)
    {
        foreach (var module in context.Modules)
        {
            var text = context.TextOf(module);
            foreach (var node in Tree.Below(module))
            {
                var (symbol, name, end) = node switch
                {
                    VariantPattern pattern => (context.Model.Types.RefOf(pattern), pattern.Path[^1], text.IndexOf(pattern.Path[^1], pattern.Span.Start, StringComparison.Ordinal) + pattern.Path[^1].Length),
                    MemberExpr member => (Tree.Target(context, member), member.Member, member.Span.End),
                    _ => (null, "", 0),
                };
                if (symbol is not EnumVariantSymbol || !Tree.InStdlib(context, symbol) || !Names.TryGetValue(name, out var to)) continue;
                if (!context.Model.Sources.GetPath(symbol.Declaration!.Span.File).Replace('\\', '/').EndsWith("/std/json.lyr", StringComparison.Ordinal)) continue;
                context.Replace(Id, node.Span.File, end - name.Length, end, to);
            }
        }
    }
}

/// <summary>A call of a string's method of 4.6's own library, by name.</summary>
internal static class StringCalls
{
    public static bool Is(RuleContext context, Node node, string method, out CallExpr call, out MemberExpr member)
    {
        call = null!;
        member = null!;
        if (node is not CallExpr { Callee: MemberExpr callee } found
            || LibraryTable.KeyOf(context, callee) != $"string.{method}") return false;
        call = found;
        member = callee;
        return true;
    }
}

/// <summary>T12: a string's <c>length()</c> counted code points in 4.6, in O(n); in Lyric 5 it is its
/// bytes, in O(1), and <c>charCount()</c> counts what 4.6 counted. Written so, the program counts as
/// it did; said for a proposal, once a module, because bytes may be what was meant.</summary>
internal sealed class StringLength : IRule
{
    public string Id => "T12";

    public void Apply(RuleContext context)
    {
        foreach (var module in context.Modules)
        {
            var said = false;
            foreach (var node in Tree.Below(module))
            {
                if (!StringCalls.Is(context, node, "length", out _, out var member)) continue;
                context.Replace(Id, member.Span.File, member.Span.End - "length".Length, member.Span.End, "charCount");
                if (said) continue;
                said = true;
                context.Note(Id, "LYR-MIG0312", NoteKind.Review, member.Span,
                    "a string's 'length()' counted characters in Lyric 4 and counts bytes in Lyric 5; the migration writes 'charCount()', which counts what it counted, in O(n)",
                    "where bytes are what is meant, as beside a slice or a 'find', write 'length()'");
            }
        }
    }
}

/// <summary>T15: <c>s.indexOf(n)</c> gave a character's index or -1; Lyric 5's <c>find</c> gives a
/// byte's or <c>null</c>. A test of the -1 is exactly <c>contains</c>, and written so; any other
/// call is <c>find</c>, said for a proposal, because what it gives has changed twice.</summary>
internal sealed class StringIndexOf : IRule
{
    public string Id => "T15";

    public void Apply(RuleContext context)
    {
        foreach (var module in context.Modules)
        {
            var text = context.TextOf(module);
            var nodes = Tree.Below(module).ToList();
            var tested = new HashSet<CallExpr>(ReferenceEqualityComparer.Instance);
            foreach (var binary in nodes.OfType<BinaryExpr>())
            {
                if (!(StringCalls.Is(context, binary.Left, "indexOf", out var call, out var member) && member.Member == "indexOf")
                    && !StringCalls.Is(context, binary.Left, "lastIndexOf", out call, out member)) continue;
                if (Found(binary.Operator, binary.Right) is not { } found) continue;
                tested.Add(call);
                if (!found) context.Replace(Id, binary.Span.File, binary.Span.Start, binary.Span.Start, "!");
                context.Replace(Id, member.Span.File, member.Span.End - member.Member.Length, member.Span.End, "contains");
                context.Replace(Id, binary.Span.File, call.Span.End, binary.Span.End, "");
            }
            foreach (var node in nodes)
                foreach (var (from, to) in new[] { ("indexOf", "find"), ("lastIndexOf", "rfind"), ("indexOfFrom", "findFrom") })
                {
                    if (!StringCalls.Is(context, node, from, out var call, out var member) || tested.Contains(call)) continue;
                    context.Replace(Id, member.Span.File, member.Span.End - from.Length, member.Span.End, to);
                    context.Note(Id, "LYR-MIG0315", NoteKind.Review, call.Span,
                        $"'{Conformances.Text(text, call)}' gave a character's index or -1; '{to}' gives a byte's index or 'null'",
                        "take the 'null' with '??' or 'if', and 'charIndex(byte)' gives the character's index");
                }
        }
    }

    /// <summary>What a comparison of the index says: <c>true</c> it is there, <c>false</c> it is not,
    /// <c>null</c> something else.</summary>
    private static bool? Found(BinaryOp op, Expr right)
    {
        var value = right switch
        {
            IntLiteralExpr { Value: 0 } => 0,
            UnaryExpr { Operator: UnaryOp.Neg, Operand: IntLiteralExpr { Value: 1 } } => -1,
            _ => (int?)null,
        };
        return (op, value) switch
        {
            (BinaryOp.Ge, 0) or (BinaryOp.Gt, -1) or (BinaryOp.Ne, -1) => true,
            (BinaryOp.Lt, 0) or (BinaryOp.Le, -1) or (BinaryOp.Eq, -1) => false,
            _ => null,
        };
    }
}
