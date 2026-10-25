using Lyric.AST;
using Lyric.Core;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric5.Doc.Api;

/// <summary>What a declaration of the surface is.</summary>
public enum ItemKind
{
    Function,
    Struct,
    Class,
    Enum,
    Interface,
    Alias,
    Constant,
    Field,
    Method,
    Variant,
    AssociatedType,
    Extension,
}

/// <summary>
/// One <c>pub</c> element of a package: its kind, its name, its signature as the source writes it,
/// its documentation, and what stands under it — a type's fields, variants, members and associated
/// types, an extension's members.
/// </summary>
/// <param name="Signature">The declaration's head as written, without its body, its attributes or
/// its documentation; a head over several lines keeps them, indented from its first column.</param>
/// <param name="Doc">The <c>///</c> text above it, or <c>null</c>.</param>
/// <param name="Conformances">The interfaces a type conforms to — its own <c>::</c> list and the
/// lists of the module's <c>extend</c> blocks for it — as written.</param>
/// <param name="Target">For an extension, the type it extends, as written.</param>
/// <param name="Default">For an interface's method, whether it has a body a conformer takes.</param>
public sealed record Item(
    ItemKind Kind,
    string Name,
    string Signature,
    string? Doc,
    IReadOnlyList<Item> Members,
    IReadOnlyList<string> Conformances,
    string? Target = null,
    bool Default = false);

/// <summary>A module of the surface: its dotted path, its documentation and its items in the
/// order the source declares them.</summary>
public sealed record SurfaceModule(string Path, string? Doc, IReadOnlyList<Item> Items);

/// <summary>A package's public surface: its modules by path.</summary>
public sealed record Surface(string Package, string Version, IReadOnlyList<SurfaceModule> Modules);

/// <summary>
/// Reads the public surface of a package from a checked compilation: the modules it names, each
/// with its <c>pub</c> declarations and their documentation. Nothing is lowered; the tree and the
/// documentation table are what the front end left.
/// </summary>
public static class SurfaceReader
{
    /// <summary>The surface of <paramref name="modules"/>, ordered by path; within a module in
    /// source order.</summary>
    public static Surface Read(Compilation compilation, IEnumerable<ModuleSymbol> modules, string package, string version)
    {
        var read = modules
            .OrderBy(m => m.FullName, StringComparer.Ordinal)
            .Select(m => ReadModule(compilation, m))
            .ToList();
        return new Surface(package, version, read);
    }

    private static SurfaceModule ReadModule(Compilation compilation, ModuleSymbol module)
    {
        var ast = compilation.LoadedAstOf(module);
        var text = compilation.Sources.GetText(ast.Span.File);
        var docs = new LinkedDocs(compilation, module, ast);
        var reader = new Reader(text, docs);

        var items = new List<Item>();
        var extended = new Dictionary<string, (List<string> Conformances, List<Item> Members)>(StringComparer.Ordinal);
        foreach (var extend in ast.Declarations.OfType<ExtendDecl>())
        {
            var target = TypeNameOf(reader.Slice(extend.Target.Span));
            if (!extended.TryGetValue(target, out var entry)) extended[target] = entry = ([], []);
            entry.Conformances.AddRange(extend.Interfaces.Select(i => reader.Slice(i.Span)));
            entry.Members.AddRange(Visible(extend).Select(reader.Method));
        }

        var declared = new HashSet<string>(StringComparer.Ordinal);
        foreach (var decl in ast.Declarations)
        {
            if (reader.Read(decl) is not { } item) continue;
            declared.Add(item.Name);
            if (extended.TryGetValue(item.Name, out var more))
                item = item with
                {
                    Conformances = [.. item.Conformances, .. more.Conformances],
                    Members = [.. item.Members, .. more.Members],
                };
            items.Add(item);
        }

        // An extension of a type the module does not declare — a builtin, or another module's.
        foreach (var decl in ast.Declarations.OfType<ExtendDecl>())
        {
            var target = TypeNameOf(reader.Slice(decl.Target.Span));
            if (declared.Contains(target)) continue;
            var members = Visible(decl).Select(reader.Method).ToList();
            var conformances = decl.Interfaces.Select(i => reader.Slice(i.Span)).ToList();
            if (members.Count == 0 && conformances.Count == 0) continue;
            items.Add(new Item(ItemKind.Extension, reader.Slice(decl.Target.Span), reader.Head(decl), docs.Of(decl), members,
                conformances, reader.Slice(decl.Target.Span)));
        }

        return new SurfaceModule(module.FullName, docs.OfModule(ModuleDoc(text)), items);
    }

    /// <summary>The members an extension shows: a conformance block's all — they are as visible as the
    /// conformance —, another block's <c>pub</c> ones.</summary>
    private static IEnumerable<FunctionDecl> Visible(ExtendDecl extend) =>
        extend.Interfaces.Length > 0 ? extend.Methods : extend.Methods.Where(m => m.IsPublic);

    /// <summary>The name a type reference starts with: <c>List</c> for <c>List&lt;T&gt;</c>.</summary>
    private static string TypeNameOf(string written)
    {
        var end = written.IndexOfAny(['<', ' ', '[', '?']);
        return end < 0 ? written : written[..end];
    }

    /// <summary>The module's documentation: the <c>//!</c> lines that open the file.</summary>
    public static string? ModuleDoc(string text) => DocLinks.ModuleText(text);

    /// <summary>A link of the documentation as the pages take it: <c>[List.push](lyric-doc:KEY)</c>,
    /// the key naming the page and the anchor — the module, the type, the member's kind and name.</summary>
    public const string LinkScheme = "lyric-doc:";

    /// <summary>Where a link's target stands in the reference, as a key; <c>null</c> for a target
    /// that has no page — a builtin type, a type parameter, what the compiler makes up.</summary>
    public static string? KeyOf(DocTarget? hit)
    {
        if (hit?.Symbol is not { } symbol) return null;
        string? HomeOf(Symbol s) => s.Home?.FullName;
        if (hit.Owner is { } owner && HomeOf(owner) is { } ownerHome)
        {
            var kind = symbol switch
            {
                FunctionSymbol => "method",
                FieldSymbol => "field",
                EnumVariantSymbol => "variant",
                AssociatedTypeSymbol => "assoc",
                GlobalSymbol => "const",
                _ => null,
            };
            return kind is null ? null : $"m:{ownerHome}:{owner.Name}:{kind}:{symbol.Name}";
        }
        return symbol switch
        {
            ModuleSymbol m => $"M:{m.FullName}",
            TypeSymbol { Declaration: not null, Kind: TypeSymbolKind.Alias } a when HomeOf(a) is { } home => $"A:{home}:{a.Name}",
            TypeSymbol { Declaration: not null } type when HomeOf(type) is { } home =>
                hit.Member is { } member ? $"m:{home}:{type.Name}:method:{member}" : $"T:{home}:{type.Name}",
            FunctionSymbol f when HomeOf(f) is { } home => $"F:{home}:{f.Name}",
            GlobalSymbol g when HomeOf(g) is { } home => $"C:{home}:{g.Name}",
            _ => null,
        };
    }

    /// <summary>The documentation of a module's declarations with each link that names something
    /// written as a link to it — by the resolution the <c>broken-doc-link</c> lint reads.</summary>
    private sealed class LinkedDocs
    {
        private readonly DocumentationTable _docs;
        private readonly DocLinks? _links;
        private readonly ModuleSymbol _module;
        private readonly Module _ast;
        private readonly Dictionary<Node, Dictionary<string, DocTarget?>> _hits = new(ReferenceEqualityComparer.Instance);

        public LinkedDocs(Compilation compilation, ModuleSymbol module, Module ast)
        {
            _docs = compilation.Documentation;
            _links = compilation.DocLinks;
            _module = module;
            _ast = ast;
            if (_links is null) return;
            foreach (var link in _links.Of(module, ast))
            {
                if (!_hits.TryGetValue(link.Node, out var targets)) _hits[link.Node] = targets = new(StringComparer.Ordinal);
                targets.TryAdd(link.Target, link.Hit);
            }
        }

        public string? Of(Node node) =>
            _docs.Of(node) is not { } text ? null
            : _hits.TryGetValue(node, out var targets) ? Linked(text, targets) : text;

        public string? OfModule(string? text)
        {
            if (text is null || _links is null) return text;
            var targets = new Dictionary<string, DocTarget?>(StringComparer.Ordinal);
            foreach (var (target, hit) in _links.OfModuleText(_module, _ast, text)) targets.TryAdd(target, hit);
            return Linked(text, targets);
        }

        private static string Linked(string text, Dictionary<string, DocTarget?> targets) =>
            DocLinks.Rewrite(text, target =>
                targets.TryGetValue(target, out var hit) && KeyOf(hit) is { } key ? $"[{target}]({LinkScheme}{key})" : null);
    }

    private sealed class Reader(string text, LinkedDocs docs)
    {
        public string Slice(Span span) => text[span.Start..span.End];

        public Item? Read(Decl decl) => decl switch
        {
            FunctionDecl { IsPublic: true } f => Function(f, ItemKind.Function),
            StructDecl { Visibility: VisibilityWord.Pub } s => Type(s, ItemKind.Struct, s.Name, s.Interfaces, Members(s.Members), []),
            ClassDecl { Visibility: VisibilityWord.Pub } c => Type(c, ItemKind.Class, c.Name, c.Interfaces, Members(c.Members), []),
            EnumDecl { IsPublic: true } e => Type(e, ItemKind.Enum, e.Name, e.Interfaces,
                [.. e.Variants.Select(Variant), .. e.Methods.Where(m => m.IsPublic).Select(Method)],
                e.Types),
            InterfaceDecl { Visibility: VisibilityWord.Pub } i => Type(i, ItemKind.Interface, i.Name, i.Interfaces,
                [.. i.Members.Where(m => !m.IsPrivateHelper).Select(m => Method(m) with { Default = m.Body is not null })], i.Types),
            TypeAliasDecl { Visibility: VisibilityWord.Pub } t => new Item(ItemKind.Alias, t.Name, Whole(t), docs.Of(t), [], []),
            GlobalBindingDecl { IsPublic: true } g => new Item(ItemKind.Constant, g.Name, Binding(g), docs.Of(g), [], []),
            _ => null,
        };

        private Item Type(Decl decl, ItemKind kind, string name, TypeNode[] interfaces, IReadOnlyList<Item> members,
            AssociatedTypeDecl[] types) =>
            new(kind, name, Head(decl), docs.Of(decl), [.. types.Select(AssociatedType), .. members],
                [.. interfaces.Select(i => Slice(i.Span))]);

        private List<Item> Members(Decl[] members)
        {
            var items = new List<Item>();
            foreach (var m in members)
                switch (m)
                {
                    case FieldDecl { Visibility: VisibilityWord.Pub } field:
                        items.Add(new Item(ItemKind.Field, field.Name, Whole(field), docs.Of(field), [], []));
                        break;
                    case StaticBindingDecl { IsPublic: true } constant:
                        items.Add(new Item(ItemKind.Constant, constant.Name, Binding(constant), docs.Of(constant), [], []));
                        break;
                    case FunctionDecl { IsPublic: true } method:
                        items.Add(Method(method));
                        break;
                }
            return items;
        }

        public Item Method(FunctionDecl f) => Function(f, ItemKind.Method);

        private Item Function(FunctionDecl f, ItemKind kind)
        {
            var end = f.Body is { } body ? body.Span.Start : f.Span.End;
            return new Item(kind, f.Name, Dedent(f.Span.Start, Clean(text[f.Span.Start..end])), docs.Of(f), [], []);
        }

        private Item Variant(EnumVariant v) => new(ItemKind.Variant, v.Name, Whole(v), docs.Of(v), [], []);

        private Item AssociatedType(AssociatedTypeDecl t) => new(ItemKind.AssociatedType, t.Name, Whole(t), docs.Of(t), [], []);

        /// <summary>A declaration with no body: all of it, its attributes and its end left out.</summary>
        private string Whole(Node node) => Dedent(node.Span.Start, Clean(Slice(node.Span)));

        /// <summary>A binding: its value too where the whole fits on one short line, else up to the
        /// <c>=</c>.</summary>
        private string Binding(Node node)
        {
            var whole = Clean(Slice(node.Span));
            if (whole.Length <= 80 && !whole.Contains('\n')) return whole;
            var eq = IndexAtDepthZero(whole, '=');
            return Dedent(node.Span.Start, eq < 0 ? whole : whole[..eq].TrimEnd());
        }

        /// <summary>A type's or an extension's head: up to the brace its body opens with.</summary>
        public string Head(Decl decl)
        {
            var written = Slice(decl.Span);
            var brace = IndexAtDepthZero(written, '{');
            return Dedent(decl.Span.Start, Clean(brace < 0 ? written : written[..brace]));
        }

        /// <summary>The attributes' lines left out, the end's <c>;</c> or <c>,</c> and the space
        /// before a body dropped.</summary>
        private static string Clean(string written)
        {
            var lines = written.Replace("\r\n", "\n").Split('\n').SkipWhile(l => l.TrimStart().StartsWith('@'));
            return string.Join('\n', lines).TrimEnd().TrimEnd(';', ',').TrimEnd();
        }

        /// <summary>The lines after the first, moved left by the column the declaration starts at.</summary>
        private string Dedent(int start, string written)
        {
            var column = start - (text.LastIndexOf('\n', Math.Max(0, start - 1)) + 1);
            if (column <= 0 || !written.Contains('\n')) return written;
            var lines = written.Split('\n');
            for (var i = 1; i < lines.Length; i++)
            {
                var cut = 0;
                while (cut < column && cut < lines[i].Length && lines[i][cut] == ' ') cut++;
                lines[i] = lines[i][cut..];
            }
            return string.Join('\n', lines);
        }

        /// <summary>The first <paramref name="wanted"/> outside parentheses, brackets and strings.</summary>
        private static int IndexAtDepthZero(string written, char wanted)
        {
            var depth = 0;
            var quote = false;
            for (var i = 0; i < written.Length; i++)
            {
                var c = written[i];
                if (quote) { if (c == '\\') i++; else if (c == '"') quote = false; continue; }
                switch (c)
                {
                    case '"': quote = true; break;
                    case '(' or '[': depth++; break;
                    case ')' or ']': depth--; break;
                    default:
                        if (c == wanted && depth == 0 && !(wanted == '=' && i + 1 < written.Length && written[i + 1] is '=' or '>'))
                            return i;
                        break;
                }
            }
            return -1;
        }
    }
}
