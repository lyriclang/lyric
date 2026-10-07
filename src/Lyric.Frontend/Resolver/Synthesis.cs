using System.Text;
using Lyric.AST;
using Lyric.Core;

namespace Lyric.Resolver;

/// <summary>
/// Conformance synthesis (design/v5/spec/04 D7), provisional in the compiler until area 09
/// decides where it lives: a conformance written without a body — <c>struct P :: [Equatable]</c>
/// with no <c>equals</c> — is implemented from the fields, in an <c>extend</c> block of the type's
/// own module; a generic type conditionally, with the derive-bound on every parameter
/// (<c>extend&lt;T :: [Equatable]&gt; Pair&lt;T&gt; :: [Equatable]</c>). <c>Debug</c> is given to every type that
/// writes none. The block is written as SOURCE and parsed: one grammar, one checker, one lowering,
/// and nothing to keep in step with them.
///
/// <para>The text is laid out one field per line on purpose: a diagnostic from inside the block
/// is re-pointed at the declaration (<see cref="DiagnosticEngine"/>) with the line it stood on,
/// and that line then names the field the rule asks to be named (D7: "fehlende Feldkonformanz
/// nennt das Feld").</para>
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
    /// <c>Debug</c> unasked where <c>std.core</c> declares it. A class listing <c>Identity</c>
    /// (02 M10) has its <c>Equatable</c> and <c>Hashable</c> by identity, listed beside it or not.</summary>
    public static List<Request> RequestsOf(TypeNode[] interfaces, IEnumerable<FunctionDecl> methods, bool coreHasDebug,
        bool coreHasIdentity = false, bool isClass = false)
    {
        var written = new HashSet<string>(methods.Select(m => m.Name), StringComparer.Ordinal);
        var listed = new HashSet<string>(StringComparer.Ordinal);
        var requests = new List<Request>();
        var identity = coreHasIdentity && isClass
            && interfaces.Any(n => n is NamedType { Path: ["Identity"], TypeArguments.Length: 0 });
        foreach (var node in interfaces)
            if (node is NamedType { Path: [var name], TypeArguments.Length: 0 } && Names.Contains(name)
                && (name != "Identity" || coreHasIdentity) && listed.Add(name) && !written.Contains(MethodOf(name)))
                requests.Add(new Request(name, node, identity && name is "Equatable" or "Hashable",
                    name == "Identity" ? new[] { "equals", "hash" }.FirstOrDefault(written.Contains) : null));
        foreach (var request in requests.ToArray())
            foreach (var parent in ParentsOf(request.Interface))
                if (!listed.Contains(parent) && !written.Contains(MethodOf(parent)) && requests.All(r => r.Interface != parent)
                    && (request.Interface != "Identity" || identity && request.Contradicted is null))
                    requests.Add(new Request(parent, request.Node, identity && parent is "Equatable" or "Hashable"));
        if (coreHasDebug && !written.Contains("debug") && !listed.Contains("Debug"))
            requests.Add(new Request("Debug", null));
        return requests;
    }

    /// <summary>The refusal of <c>Identity</c> to a struct or an enum, said alike where the
    /// synthesis meets it and where a block written by hand does.</summary>
    public static string IdentityOfAValue(string name) =>
        $"'Identity' is an object's, and '{name}' is a value, which has none — it compares by its fields with ':: [Equatable]'";

    /// <summary>The block for one request, as source; <c>null</c> where the form is not written
    /// yet (reported by the caller).</summary>
    public static string? Block(Request request, string name, GenericParam[] generics, FieldDecl[] fields,
        EnumVariant[]? variants, bool isClass, SourceManager sm, bool streamsHash, out string? refusal)
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
            ? StructBody(iface, name, typeText, fields, Text, streamsHash, out refusal)
            : EnumBody(iface, name, typeText, variants, Text, streamsHash, out refusal);
        return body is null ? null : head + body + "}\n";
    }

    // --- structs and classes ---------------------------------------------------------------

    private static string? StructBody(string iface, string name, string typeText, FieldDecl[] fields,
        Func<TypeNode, string> text, bool streamsHash, out string? refusal)
    {
        refusal = null;
        var sb = new StringBuilder();
        switch (iface)
        {
            case "Equatable":
                sb.Append($"    fn equals(o: {typeText}): bool {{\n        return ");
                sb.Append(fields.Length == 0 ? "true" : string.Join("\n            && ", fields.Select(f => EqOf($"this.{f.Name}", $"o.{f.Name}", f.Type, text))));
                sb.Append(";\n    }\n");
                break;
            case "Hashable" when streamsHash:
                // Every field into the hasher, in order, one line each (10 K2).
                sb.Append("    fn hash<H :: [Hasher]>(&h: H): void {\n");
                foreach (var f in fields)
                {
                    if (HashInto($"this.{f.Name}", f.Type) is not { } written)
                    {
                        refusal = $"the field '{f.Name}' is an array or a view, which is no Hashable (10 C8)";
                        return null;
                    }
                    sb.Append($"        {written}\n");
                }
                sb.Append("    }\n");
                break;
            case "Hashable":
                sb.Append("    fn hash(): int {\n        var h = 17;\n");
                foreach (var f in fields) sb.Append($"        h = h *% 31 +% {HashOf($"this.{f.Name}", f.Type, text)};\n");
                sb.Append("        return h;\n    }\n");
                break;
            case "Ordered":
                sb.Append($"    fn compare(o: {typeText}): ?Ordering {{\n");
                for (var i = 0; i < fields.Length; i++)
                    sb.Append($"        let c{i} = this.{fields[i].Name}.compare(o.{fields[i].Name});\n"
                        + $"        if (c{i} == null) {{ return null; }}\n"
                        + $"        match (c{i}) {{ .Equal => {{ }}, _ => {{ return c{i}; }} }}\n"); // narrowed past the null check
                sb.Append("        return Ordering.Equal;\n    }\n");
                break;
            case "TotalOrder":
                sb.Append($"    fn totalCompare(o: {typeText}): Ordering {{\n");
                for (var i = 0; i < fields.Length; i++)
                    sb.Append($"        let c{i} = this.{fields[i].Name}.totalCompare(o.{fields[i].Name});\n"
                        + $"        match (c{i}) {{ .Equal => {{ }}, _ => {{ return c{i}; }} }}\n");
                sb.Append("        return Ordering.Equal;\n    }\n");
                break;
            case "Clone":
            {
                var parts = new List<string>();
                foreach (var f in fields)
                {
                    var cloned = CloneOf($"this.{f.Name}", f.Type, text);
                    if (cloned is null) { refusal = $"a field of type '{text(f.Type)}' has no synthesized clone"; return null; }
                    parts.Add($"            {f.Name} = {cloned}");
                }
                sb.Append($"    fn clone(): {typeText} {{\n        return {typeText} {{\n{string.Join(",\n", parts)}\n        }};\n    }}\n");
                break;
            }
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
                        sb.Append($"            + \"{(i > 0 ? ", " : "")}{fields[i].Name} = \" + {DebugOf($"this.{fields[i].Name}", fields[i].Type, text)}\n");
                    sb.Append("            + \" }\"");
                }
                sb.Append(";\n    }\n");
                break;
        }
        return sb.ToString();
    }

    /// <summary><c>a == b</c> for a field of this type: the value's own equality, a helper for a
    /// shape, element by element for a tuple.</summary>
    private static string EqOf(string a, string b, TypeNode type, Func<TypeNode, string> text) => type switch
    {
        ArrayType { Length: null } => $"equalArrays({a}, {b})",
        // An inline array element by element, unrolled: a view of it is taken only on the heap
        // (03 T13), and 'this' of a struct lies in a frame.
        ArrayType { Length: { } n, Element: var e } => n == 0 ? "true"
            : "(" + string.Join(" && ", Enumerable.Range(0, n).Select(i => EqOf($"{a}[{i}]", $"{b}[{i}]", e, text))) + ")",
        NullableType => $"equalOptionals({a}, {b})",
        NamedType { Path: ["Slice"], TypeArguments.Length: 1 } => $"equalSlices({a}, {b})",
        AST.TupleType t => "(" + string.Join(" && ", t.Elements.Select((e, i) => EqOf($"{a}.{i}", $"{b}.{i}", e, text))) + ")",
        _ => $"{a} == {b}",
    };

    private static string HashOf(string expr, TypeNode type, Func<TypeNode, string> text) => type switch
    {
        ArrayType { Length: null } => $"hashArray({expr})",
        ArrayType { Length: { } n, Element: var e } => Enumerable.Range(0, n).Select(i => HashOf($"{expr}[{i}]", e, text)).Aggregate("11", (acc, h) => $"({acc} *% 31 +% {h})"),
        NullableType => $"hashOptional({expr})",
        NamedType { Path: ["Slice"], TypeArguments.Length: 1 } => $"hashSlice({expr})",
        AST.TupleType t => t.Elements.Select((e, i) => HashOf($"{expr}.{i}", e, text)).Aggregate("7", (acc, h) => $"({acc} *% 31 +% {h})"),
        _ => $"{expr}.hash()",
    };

    /// <summary>A field written into the hasher <c>h</c> (10 K2): an optional through its mark, a tuple
    /// and an inline array element by element; <c>null</c> for an array or a view, which is no
    /// <c>Hashable</c> (C8).</summary>
    private static string? HashInto(string expr, TypeNode type) => type switch
    {
        ArrayType { Length: null } => null,
        ArrayType { Length: { } n, Element: var e } => Enumerable.Range(0, n).Select(i => HashInto($"{expr}[{i}]", e)).ToArray() is var parts && parts.All(p => p is not null)
            ? string.Join(" ", parts) : null,
        NullableType => $"hashOptionalInto({expr}, &h);",
        NamedType { Path: ["Slice"], TypeArguments.Length: 1 } => null,
        AST.TupleType t => t.Elements.Select((e, i) => HashInto($"{expr}.{i}", e)).ToArray() is var items && items.All(p => p is not null)
            ? string.Join(" ", items) : null,
        _ => $"{expr}.hash(&h);",
    };

    /// <summary><c>a.compare(b)</c> (or <c>totalCompare</c>) for a payload of this type; a shape
    /// has no ordering yet and falls to the method call, which the checker then refuses.</summary>
    private static string CompareOf(string a, string b, string method) => $"{a}.{method}({b})";

    private static string? CloneOf(string expr, TypeNode type, Func<TypeNode, string> text) => type switch
    {
        ArrayType { Length: null } => $"cloneArray({expr})",
        ArrayType { Length: { } n, Element: var e } => Enumerable.Range(0, n).Select(i => CloneOf($"{expr}[{i}]", e, text)).ToArray() is var items && items.All(p => p is not null)
            ? "[" + string.Join(", ", items) + "]" : null,
        NullableType => $"cloneOptional({expr})",
        NamedType { Path: ["Slice"], TypeArguments.Length: 1 } => null,
        FunctionType => null,
        AST.TupleType t => t.Elements.Select((e, i) => CloneOf($"{expr}.{i}", e, text)).ToArray() is var parts && parts.All(p => p is not null)
            ? "(" + string.Join(", ", parts) + ")" : null,
        _ => $"{expr}.clone()",
    };

    /// <summary>The value a field of this type takes in a synthesized <c>default()</c>.</summary>
    private static string? DefaultOf(TypeNode type, Func<TypeNode, string> text) => type switch
    {
        ArrayType { Length: null } => "[]",
        // 'T[N] :: [Default]' conditionally (10 C7): every slot the element's default.
        ArrayType { Length: { } n, Element: var e } => DefaultOf(e, text) is { } one
            ? "[" + string.Join(", ", Enumerable.Repeat(one, n)) + "]" : null,
        NullableType => "null",
        AST.TupleType t => t.Elements.Select(e => DefaultOf(e, text)).ToArray() is var parts && parts.All(p => p is not null)
            ? "(" + string.Join(", ", parts) + ")" : null,
        FunctionType => null,
        _ => $"{text(type)}.default()",
    };

    /// <summary>The text rendering a value of this type in a synthesized <c>debug()</c>: the
    /// value's own, or a helper of <c>std.core</c> for a shape that has none.</summary>
    private static string DebugOf(string expr, TypeNode type, Func<TypeNode, string> text) => type switch
    {
        ArrayType { Length: null } => $"debugArray({expr})",
        ArrayType { Length: { } n, Element: var e } => n == 0 ? "\"[]\""
            : "\"[\" + " + string.Join(" + \", \" + ", Enumerable.Range(0, n).Select(i => DebugOf($"{expr}[{i}]", e, text))) + " + \"]\"",
        NullableType => $"debugOptional({expr})",
        NamedType { Path: ["Slice"], TypeArguments.Length: 1 } => $"debugSlice({expr})",
        AST.TupleType t => "\"(\" + " + string.Join(" + \", \" + ", t.Elements.Select((e, i) => DebugOf($"{expr}.{i}", e, text))) + " + \")\"",
        _ => $"{expr}.debug()",
    };

    // --- enums --------------------------------------------------------------------------------

    private static string? EnumBody(string iface, string name, string typeText, EnumVariant[] variants,
        Func<TypeNode, string> text, bool streamsHash, out string? refusal)
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
        // The pattern matching a variant whatever its payload.
        static string Wild(EnumVariant v) =>
            v.TupleFields is { Length: > 0 } tuple ? $".{v.Name}({string.Join(", ", tuple.Select(_ => "_"))})"
            : v.StructFields is { Length: > 0 } fields ? $".{v.Name} {{ {string.Join(", ", fields.Select(f => $"{f.Name} = _"))} }}"
            : $".{v.Name}";
        switch (iface)
        {
            case "Equatable":
                sb.Append($"    fn equals(o: {typeText}): bool {{\n        return match (this) {{\n");
                foreach (var v in variants)
                {
                    var (pa, an, at) = Bind(v, "a_");
                    var (pb, bn, _) = Bind(v, "b_");
                    var same = an.Length == 0 ? "true" : string.Join(" && ", an.Select((n, i) => EqOf(n, bn[i], at[i], text)));
                    sb.Append($"            {pa} => match (o) {{ {pb} => {same}, _ => false }},\n");
                }
                sb.Append("        };\n    }\n");
                break;
            case "Hashable" when streamsHash:
                // The variant's position, then its payload (10 K2).
                sb.Append("    fn hash<H :: [Hasher]>(&h: H): void {\n        match (this) {\n");
                for (var i = 0; i < variants.Length; i++)
                {
                    var (p, names, types) = Bind(variants[i], "a_");
                    var parts = new List<string> { $"h.writeInt({i});" };
                    for (var k = 0; k < names.Length; k++)
                    {
                        if (HashInto(names[k], types[k]) is not { } written)
                        {
                            refusal = $"a payload of '{variants[i].Name}' is an array or a view, which is no Hashable (10 C8)";
                            return null;
                        }
                        parts.Add(written);
                    }
                    var joined = string.Join(" ", parts);
                    sb.Append($"            {p} => {{ {joined} }},\n");
                }
                sb.Append("        }\n    }\n");
                break;
            case "Hashable":
                sb.Append("    fn hash(): int {\n        return match (this) {\n");
                for (var i = 0; i < variants.Length; i++)
                {
                    var (p, names, types) = Bind(variants[i], "a_");
                    var h = $"{i + 1}";
                    for (var k = 0; k < names.Length; k++) h = $"({h} *% 31 +% {HashOf(names[k], types[k], text)})";
                    sb.Append($"            {p} => {h},\n");
                }
                sb.Append("        };\n    }\n");
                break;
            case "Ordered" or "TotalOrder":
            {
                // Lexicographic as for a struct, the variant's position first (D7 "Enums: wie
                // Structs"): a variant before another is Less whatever the payloads; the same
                // variant compares its payload field by field.
                var ret = iface == "Ordered" ? "?Ordering" : "Ordering";
                var method = iface == "Ordered" ? "compare" : "totalCompare";
                sb.Append($"    fn {method}(o: {typeText}): {ret} {{\n        match (this) {{\n");
                for (var i = 0; i < variants.Length; i++)
                {
                    var (pa, an, at) = Bind(variants[i], "a_");
                    var (pb, bn, _) = Bind(variants[i], "b_");
                    sb.Append($"            {pa} => {{ match (o) {{\n");
                    for (var j = 0; j < i; j++) sb.Append($"                {Wild(variants[j])} => {{ return Ordering.Greater; }},\n");
                    sb.Append($"                {pb} => {{\n");
                    for (var k = 0; k < an.Length; k++)
                    {
                        sb.Append($"                    let c{k} = {CompareOf(an[k], bn[k], method)};\n");
                        if (iface == "Ordered") sb.Append($"                    if (c{k} == null) {{ return null; }}\n");
                        sb.Append($"                    match (c{k}) {{ .Equal => {{ }}, _ => {{ return c{k}; }} }}\n");
                    }
                    sb.Append("                    return Ordering.Equal;\n                },\n");
                    if (i < variants.Length - 1) sb.Append("                _ => { return Ordering.Less; },\n");
                    sb.Append("            } },\n");
                }
                sb.Append("        }\n        return Ordering.Equal;\n    }\n");
                break;
            }
            case "Clone":
                sb.Append($"    fn clone(): {typeText} {{\n        return match (this) {{\n");
                foreach (var v in variants)
                {
                    var (p, names, types) = Bind(v, "a_");
                    var cloned = names.Select((n, i) => CloneOf(n, types[i], text)).ToArray();
                    if (cloned.Any(c => c is null)) { refusal = "a payload that has no synthesized clone"; return null; }
                    var rebuilt = v.TupleFields is { Length: > 0 }
                        ? $".{v.Name}({string.Join(", ", cloned)})"
                        : v.StructFields is { Length: > 0 } fs
                            ? $".{v.Name} {{ {string.Join(", ", fs.Select((f, i) => $"{f.Name} = {cloned[i]}"))} }}"
                            : $".{v.Name}";
                    sb.Append($"            {p} => {rebuilt},\n");
                }
                sb.Append("        };\n    }\n");
                break;
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
                        rendered = $"\"{name}.{v.Name}(\" + " + string.Join(" + \", \" + ", names.Select((n, i) => DebugOf(n, types[i], text))) + " + \")\"";
                    else if (v.StructFields is { Length: > 0 } fs)
                        rendered = $"\"{name}.{v.Name} {{ \" + " + string.Join(" + \", \" + ", fs.Select((f, i) => $"\"{f.Name} = \" + {DebugOf(names[i], types[i], text)}")) + " + \" }\"";
                    else rendered = $"\"{name}.{v.Name}\"";
                    sb.Append($"            {p} => {rendered},\n");
                }
                sb.Append("        };\n    }\n");
                break;
        }
        return sb.ToString();
    }
}
