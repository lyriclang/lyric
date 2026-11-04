using System.Text;
using System.Text.RegularExpressions;
using Lyric4.AST;
using Lyric4.Resolver;
using Lyric4.Sema;

namespace Lyric4.From4.Rules;

/// <summary>A line of <c>from4.toml</c>: a function of 4.6's library and its form in Lyric 5.</summary>
internal sealed record LibraryEntry(string Rule, string From, string? Name, string? Form, bool Iterator);

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
                entries.Add(from, new LibraryEntry(Text("rule")!, from, Text("name"), Text("form"),
                    row.TryGet("iterator", out var iterator) && (bool)iterator));
            }
        }
        return entries;
    }

    public static LibraryEntry? Of(string key) => Entries.Value.GetValueOrDefault(key);

    /// <summary>Whether the table holds a function of a module: its imports change.</summary>
    public static bool HasModule(string module) => Entries.Value.Keys.Any(key => key.StartsWith(module + ".", StringComparison.Ordinal));

    /// <summary>What an import of a name from a module names in Lyric 5: the name itself, the new
    /// one, or <c>null</c> where the call no longer needs an import.</summary>
    public static string? ImportName(string module, string name) =>
        Of($"{module}.{name}") is { } entry ? entry.Name : name;

    /// <summary>The function a callee or a name stands for, as a key of the table: <c>std.math.absInt</c>
    /// for a free one, <c>string.toChars</c> for a method of a string.</summary>
    public static string? KeyOf(RuleContext context, Expr callee)
    {
        if (Tree.Target(context, callee) is not FunctionSymbol { Declaration: { } declaration } function) return null;
        var path = context.Model.Sources.GetPath(declaration.Span.File);
        if (!From4.IsStdlib(path)) return null;
        if (callee is MemberExpr member && context.Model.Types.TypeOf(member.Target) is PrimitiveType { Kind: PrimitiveKind.String })
            return $"string.{function.Name}";
        var file = path.Replace('\\', '/');
        var std = file.LastIndexOf("/std/", StringComparison.Ordinal);
        return std < 0 ? null : $"{Path.ChangeExtension(file[(std + 1)..], null).Replace('/', '.')}.{function.Name}";
    }
}

/// <summary>T11, T13, T14, T16: the table's functions. A <c>name</c> is the name written anew where
/// it stands, or a lambda where it was a value; a <c>form</c> replaces what stands between the call's receiver and
/// its arguments, which stay as written. A function with a form used as a value has no form to take:
/// said by hand.</summary>
internal sealed partial class Library : IRule
{
    public string Id => "T11";

    [GeneratedRegex(@"\{(this|\d+)(!?)\}")]
    private static partial Regex Placeholder();

    public void Apply(RuleContext context)
    {
        foreach (var module in context.Modules)
        {
            var text = context.TextOf(module);
            var nodes = Tree.Below(module).ToList();
            var sources = nodes.OfType<ForInStmt>().Select(f => f.Iterable).ToHashSet<Expr>(ReferenceEqualityComparer.Instance);
            var callees = nodes.OfType<CallExpr>().Select(c => c.Callee).ToHashSet<Expr>(ReferenceEqualityComparer.Instance);
            foreach (var call in nodes.OfType<CallExpr>())
                if (LibraryTable.KeyOf(context, call.Callee) is { } key && LibraryTable.Of(key) is { Form: { } form } entry)
                    Write(context, text, call, entry, form, sources.Contains(call));
            foreach (var name in nodes.OfType<Expr>().Where(n => n is IdentifierExpr or MemberExpr))
            {
                if (LibraryTable.KeyOf(context, name) is not { } key || LibraryTable.Of(key) is not { } entry) continue;
                if (entry.Name is { } to)
                {
                    var written = name is MemberExpr member ? member.Member : ((IdentifierExpr)name).Name;
                    if (callees.Contains(name))
                        context.Replace(entry.Rule, name.Span.File, name.Span.End - written.Length, name.Span.End, to);
                    else
                        context.Replace(entry.Rule, name.Span, Lambda(context, name, to));
                }
                else if (!callees.Contains(name))
                    context.Note(entry.Rule, "LYR-MIG0311", NoteKind.Hand, name.Span,
                        $"'{Conformances.Text(text, name)}' is written as a method in Lyric 5, and no function stands for it",
                        $"pass a lambda that calls it: (it) => {entry.Form!.Replace("{0}", "it")}");
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
    private static void Write(RuleContext context, string text, CallExpr call, LibraryEntry entry, string form, bool isSource)
    {
        var position = call.Span.Start;
        var pending = new StringBuilder();
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
            // a postfix binds to all of it only where it is written so
            var parens = at < form.Length && form[at] == '.' && !Tree.TakesPostfix(what);
            if (parens) pending.Append('(');
            Stretch(context, text, entry, call, position, what.Span.Start, pending);
            position = what.Span.End;
            if (parens) pending.Append(')');
        }
        pending.Append(form, at, form.Length - at);
        if (entry.Iterator) pending.Append(isSource ? ".map((part) => part.toString())" : ".map((part) => part.toString()).toArray()");
        Stretch(context, text, entry, call, position, call.Span.End, pending);
    }

    private static void Stretch(RuleContext context, string text, LibraryEntry entry, CallExpr call, int start, int end, StringBuilder written)
    {
        if (text[start..end] != written.ToString()) context.Replace(entry.Rule, call.Span.File, start, end, written.ToString());
        written.Clear();
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
                if (!StringCalls.Is(context, binary.Left, "indexOf", out var call, out var member)
                    || Found(binary.Operator, binary.Right) is not { } found) continue;
                tested.Add(call);
                if (!found) context.Replace(Id, binary.Span.File, binary.Span.Start, binary.Span.Start, "!");
                context.Replace(Id, member.Span.File, member.Span.End - "indexOf".Length, member.Span.End, "contains");
                context.Replace(Id, binary.Span.File, call.Span.End, binary.Span.End, "");
            }
            foreach (var node in nodes)
            {
                if (!StringCalls.Is(context, node, "indexOf", out var call, out var member) || tested.Contains(call)) continue;
                context.Replace(Id, member.Span.File, member.Span.End - "indexOf".Length, member.Span.End, "find");
                context.Note(Id, "LYR-MIG0315", NoteKind.Review, call.Span,
                    $"'{Conformances.Text(text, call)}' gave a character's index or -1; 'find' gives a byte's index or 'null'",
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
