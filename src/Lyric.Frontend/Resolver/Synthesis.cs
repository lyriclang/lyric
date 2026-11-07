using System.Text;
using Lyric.AST;
using Lyric.Core;

namespace Lyric.Resolver;

/// <summary>
/// Conformance synthesis in the compiler:
/// a conformance written without a body — <c>struct P :: [Equatable]</c>
/// with no <c>equals</c> — is implemented from the fields, in an <c>extend</c> block of the type's
/// own module; a generic type conditionally, with the derive-bound on every parameter
/// (<c>extend&lt;T :: [Equatable]&gt; Pair&lt;T&gt; :: [Equatable]</c>). <c>Debug</c> is given to every type that
/// writes none. The block is written as SOURCE and parsed: one grammar, one checker, one lowering,
/// and nothing to keep in step with them.
///
/// <para>The text is laid out one field per line on purpose: a diagnostic from inside the block
/// is re-pointed at the declaration (<see cref="DiagnosticEngine"/>) with the line it stood on,
/// and that line then names the field the rule asks to be named.</para>
/// </summary>
internal static class Synthesis
{
    private static readonly string[] Names = ["Equatable", "Hashable", "Ordered", "TotalOrder", "Clone", "Default", "Display", "Debug", "Identity"];

    // 'Identity' has no member of its own: what it gives are its parents'.
    private static string MethodOf(string iface) => iface switch
    {
        "Equatable" => "equals", "Hashable" => "hash", "Ordered" => "compare", "TotalOrder" => "totalCompare",
        "Clone" => "clone", "Default" => "default", "Display" => "show", "Identity" => "", _ => "debug",
    };

    /// <summary>The parents an interface of the family implies (<c>Ordered :: [Equatable]</c>):
    /// a type listing the child alone needs them too, and gets them the same way.</summary>
    private static string[] ParentsOf(string iface) => iface switch
    {
        "Hashable" or "Ordered" => ["Equatable"],
        "TotalOrder" => ["Ordered", "Equatable"],
        "Identity" => ["Equatable", "Hashable"],
        _ => [],
    };

    /// <summary>The constraint a parameter needs for the synthesized member to compile.</summary>
    private static string BoundOf(string iface) => iface == "Display" ? "Debug" : iface;

    /// <param name="ByIdentity">An <c>Equatable</c> or <c>Hashable</c> a class's <c>Identity</c>
    /// answers: by the object, not by its fields.</param>
    /// <param name="Contradicted">For <c>Identity</c>: the member of those two the type writes
    /// itself, a second answer to the question <c>Identity</c> answers.</param>
    public readonly record struct Request(string Interface, TypeNode? Node, bool ByIdentity = false, string? Contradicted = null);

    /// <summary>What a type asks to have synthesized: the listed interfaces of the family without
    /// the member written, the parents those imply when neither listed nor written, and
    /// <c>Debug</c> unasked. A class listing <c>Identity</c>
    /// has its <c>Equatable</c> and <c>Hashable</c> by identity, listed beside it or not.</summary>
    public static List<Request> RequestsOf(TypeNode[] interfaces, IEnumerable<FunctionDecl> methods, bool isClass)
    {
        var written = new HashSet<string>(methods.Select(m => m.Name), StringComparer.Ordinal);
        var listed = new HashSet<string>(StringComparer.Ordinal);
        var requests = new List<Request>();
        var identity = isClass
            && interfaces.Any(n => n is NamedType { Path: ["Identity"], TypeArguments.Length: 0 });
        foreach (var node in interfaces)
            if (node is NamedType { Path: [var name], TypeArguments.Length: 0 } && Names.Contains(name)
                && listed.Add(name) && !written.Contains(MethodOf(name)))
                requests.Add(new Request(name, node, identity && name is "Equatable" or "Hashable",
                    name == "Identity" ? new[] { "equals", "hash" }.FirstOrDefault(written.Contains) : null));
        foreach (var request in requests.ToArray())
            foreach (var parent in ParentsOf(request.Interface))
                if (!listed.Contains(parent) && !written.Contains(MethodOf(parent)) && requests.All(r => r.Interface != parent)
                    && (request.Interface != "Identity" || identity && request.Contradicted is null))
                    requests.Add(new Request(parent, request.Node, identity && parent is "Equatable" or "Hashable"));
        if (!written.Contains("debug") && !listed.Contains("Debug"))
            requests.Add(new Request("Debug", null));
        return requests;
    }

    /// <summary>The members of the family the compiler writes — <c>Default</c>, <c>Display</c> —
    /// that an interface of the program implies for a type listing it: <c>struct Start :: [Args]</c>
    /// under <c>interface Args :: [Default]</c> (spec 05 §14 rule 5), as
    /// requests where the type neither lists the interface nor writes the member. The std.core
    /// defaults a parent brings need none: a parent's defaults are inherited (05 §1 rule 3).</summary>
    public static List<Request> ImpliedRequests(IEnumerable<(string Interface, TypeNode Via)> implied,
        TypeNode[] interfaces, IEnumerable<FunctionDecl> methods)
    {
        var written = new HashSet<string>(methods.Select(m => m.Name), StringComparer.Ordinal);
        var listed = new HashSet<string>(interfaces.OfType<NamedType>().Where(n => n.Path.Length == 1).Select(n => n.Path[0]), StringComparer.Ordinal);
        var requests = new List<Request>();
        foreach (var (iface, via) in implied)
            if (iface is "Default" or "Display" && !listed.Contains(iface) && !written.Contains(MethodOf(iface))
                && requests.All(r => r.Interface != iface))
                requests.Add(new Request(iface, via));
        return requests;
    }

    /// <summary>The refusal of <c>Identity</c> to a struct or an enum, said alike where the
    /// synthesis meets it and where a block written by hand does.</summary>
    public static string IdentityOfAValue(string name) =>
        $"'Identity' is an object's, and '{name}' is a value, which has none — it compares by its fields with ':: [Equatable]'";

    /// <summary>The block for one request, as source; <c>null</c> where the form is not written
    /// yet (reported by the caller).</summary>
    public static string? Block(Request request, string name, GenericParam[] generics, FieldDecl[] fields,
        EnumVariant[]? variants, bool isClass, SourceManager sm, out string? refusal)
    {
        refusal = null;
        var iface = request.Interface;
        var identity = request.ByIdentity || iface == "Identity";
        var typeText = generics.Length == 0 ? name : $"{name}<{string.Join(", ", generics.Select(g => g.Name))}>";
        // By identity the parameters are asked nothing: the object is compared, not what it holds.
        var head = generics.Length == 0
            ? $"extend {typeText} :: [{iface}] {{\n"
            : identity
                ? $"extend<{string.Join(", ", generics.Select(g => g.Name))}> {typeText} :: [{iface}] {{\n"
                : $"extend<{string.Join(", ", generics.Select(g => $"{g.Name} :: [{BoundOf(iface)}]"))}> {typeText} :: [{iface}] {{\n";
        // The family's members are std.core's defaults over Self's fields
        // and variants, set in place per conformer — the block carries the conformance and its
        // condition (05 §14 rule 4), nothing else. 'Default' stays written here: a static member of
        // an interface declares only (SEM0127), so no default can build 'Self' for it.
        if (!request.ByIdentity && iface is "Equatable" or "Hashable" or "Ordered" or "TotalOrder" or "Clone")
            return head + "}\n";
        if (iface == "Identity")
        {
            if (!isClass) refusal = IdentityOfAValue(name);
            else if (request.Contradicted is { } member)
                refusal = $"'Identity' compares '{name}' by the object and hashes its address, and the '{member}' "
                    + "it writes would be a second answer — write neither, or leave 'Identity' out";
            return refusal is null ? head + "}\n" : null;
        }
        if (request.ByIdentity)
            return head + (iface == "Equatable"
                ? $"    fn equals(o: {typeText}): bool {{\n        return same(this, o);\n    }}\n"
                : "    fn hash<H :: [Hasher]>(&h: H): void {\n        hashIdentityInto(this, &h);\n    }\n") + "}\n";
        string Text(TypeNode t) => sm.Slice(t.Span).ToString();
        var body = variants is null
            ? StructBody(iface, name, typeText, fields, Text, out refusal)
            : EnumBody(iface, name, variants, out refusal);
        return body is null ? null : head + body + "}\n";
    }

    // --- structs and classes ---------------------------------------------------------------

    private static string? StructBody(string iface, string name, string typeText, FieldDecl[] fields,
        Func<TypeNode, string> text, out string? refusal)
    {
        refusal = null;
        var sb = new StringBuilder();
        switch (iface)
        {
            case "Default":
            {
                var parts = new List<string>();
                foreach (var f in fields)
                {
                    if (f.Default is not null) continue; // the initializer fills it (03 §4)
                    var value = DefaultOf(f.Type, text);
                    if (value is null) { refusal = $"a field of type '{text(f.Type)}' has no synthesized default"; return null; }
                    parts.Add($"            {f.Name} = {value}");
                }
                sb.Append($"    static fn default(): {typeText} {{\n        return {typeText} {{\n{string.Join(",\n", parts)}\n        }};\n    }}\n");
                break;
            }
            case "Display":
                sb.Append("    fn show(): string { return this.debug(); }\n");
                break;
            case "Debug":
                sb.Append("    fn debug(): string {\n        return ");
                if (fields.Length == 0) sb.Append($"\"{name} {{ }}\"");
                else
                {
                    sb.Append($"\"{name} {{ \"\n");
                    for (var i = 0; i < fields.Length; i++)
                        sb.Append($"            + \"{(i > 0 ? ", " : "")}{fields[i].Name} = \" + {DebugOf($"this.{fields[i].Name}", fields[i].Type)}\n");
                    sb.Append("            + \" }\"");
                }
                sb.Append(";\n    }\n");
                break;
        }
        return sb.ToString();
    }

    /// <summary>The value a field of this type takes in a synthesized <c>default()</c>.</summary>
    private static string? DefaultOf(TypeNode type, Func<TypeNode, string> text) => type switch
    {
        ArrayType { Length: null } => "[]",
        // 'T[N] :: [Default]' conditionally: every slot the element's default.
        ArrayType { Length: { } n, Element: var e } => DefaultOf(e, text) is { } one
            ? "[" + string.Join(", ", Enumerable.Repeat(one, n)) + "]" : null,
        NullableType => "null",
        AST.TupleType t => t.Elements.Select(e => DefaultOf(e, text)).ToArray() is var parts && parts.All(p => p is not null)
            ? "(" + string.Join(", ", parts) + ")" : null,
        FunctionType => null,
        _ => $"{text(type)}.default()",
    };

    /// <summary>The text rendering a value of this type in a synthesized <c>debug()</c>: the
    /// value's own, or a helper of <c>std.core</c> for a shape that has none. An inline array's own
    /// text is the unrolled one, <c>[1, 2]</c>.</summary>
    private static string DebugOf(string expr, TypeNode type) => type switch
    {
        ArrayType { Length: null } => $"debugArray({expr})",
        NullableType => $"debugOptional({expr})",
        NamedType { Path: ["Slice"], TypeArguments.Length: 1 } => $"debugSlice({expr})",
        AST.TupleType t => "\"(\" + " + string.Join(" + \", \" + ", t.Elements.Select((e, i) => DebugOf($"{expr}.{i}", e))) + " + \")\"",
        _ => $"{expr}.debug()",
    };

    // --- enums --------------------------------------------------------------------------------

    private static string? EnumBody(string iface, string name, EnumVariant[] variants, out string? refusal)
    {
        refusal = null;
        var sb = new StringBuilder();
        // The pattern binding a variant's payload under a prefix, and the names bound.
        static (string Pattern, string[] Names, TypeNode[] Types) Bind(EnumVariant v, string prefix)
        {
            if (v.TupleFields is { Length: > 0 } tuple)
            {
                var names = tuple.Select((_, i) => $"{prefix}{i}").ToArray();
                return ($".{v.Name}({string.Join(", ", names)})", names, tuple);
            }
            if (v.StructFields is { Length: > 0 } fields)
            {
                var names = fields.Select(f => $"{prefix}{f.Name}").ToArray();
                return ($".{v.Name} {{ {string.Join(", ", fields.Select((f, i) => $"{f.Name} = {names[i]}"))} }}", names, fields.Select(f => f.Type).ToArray());
            }
            return ($".{v.Name}", [], []);
        }
        switch (iface)
        {
            case "Default":
                refusal = "a default of an enum — say which variant"; return null;
            case "Display":
                sb.Append("    fn show(): string { return this.debug(); }\n");
                break;
            case "Debug":
                sb.Append("    fn debug(): string {\n        return match (this) {\n");
                foreach (var v in variants)
                {
                    var (p, names, types) = Bind(v, "a_");
                    string rendered;
                    if (v.TupleFields is { Length: > 0 })
                        rendered = $"\"{name}.{v.Name}(\" + " + string.Join(" + \", \" + ", names.Select((n, i) => DebugOf(n, types[i]))) + " + \")\"";
                    else if (v.StructFields is { Length: > 0 } fs)
                        rendered = $"\"{name}.{v.Name} {{ \" + " + string.Join(" + \", \" + ", fs.Select((f, i) => $"\"{f.Name} = \" + {DebugOf(names[i], types[i])}")) + " + \" }\"";
                    else rendered = $"\"{name}.{v.Name}\"";
                    sb.Append($"            {p} => {rendered},\n");
                }
                sb.Append("        };\n    }\n");
                break;
        }
        return sb.ToString();
    }
}
