using System.Text;
using System.Text.RegularExpressions;

namespace Lyric5.Doc.Api;

/// <summary>
/// A package's reference as pages: an index of its modules, a page per module — its documentation,
/// its types listed, its functions, constants, aliases and extensions in full — and a page per type
/// with its members. Paths sit under the package's name, <c>std/json/</c>, <c>std/json/JsonValue/</c>;
/// a member's anchor says what it is, <c>#method.push</c>, <c>#field.len</c>, <c>#variant.Some</c>,
/// <c>#assoc.Item</c>, <c>#const.MAX</c>, and on a module page <c>#fn.encode</c>, <c>#type.Bytes</c>,
/// <c>#ext.int</c>. A second member of one name and kind takes <c>-2</c>, a third <c>-3</c>.
/// </summary>
public static partial class DocPages
{
    /// <summary>The paths the reference's pages take under the version root, the index first.</summary>
    public static IReadOnlyList<string> PathsOf(Surface surface)
    {
        var paths = new List<string>();
        if (RootModule(surface) is null) paths.Add(IndexPath(surface));
        foreach (var module in surface.Modules)
        {
            paths.Add(ModulePath(surface, module));
            paths.AddRange(module.Items.Where(HasPage).Select(i => TypePath(surface, module, i)));
        }
        return paths;
    }

    /// <summary>The reference's pages, written for <paramref name="site"/>, whose page list holds
    /// <see cref="PathsOf"/>.</summary>
    public static IReadOnlyList<Page> Build(Surface surface, SiteContext site)
    {
        var targets = TargetsOf(surface);
        var pages = new List<Page>();
        if (RootModule(surface) is null) pages.Add(Index(surface, site, targets));
        foreach (var module in surface.Modules)
        {
            pages.Add(ModulePage(surface, site, module, targets));
            pages.AddRange(module.Items.Where(HasPage).Select(i => TypePage(surface, site, module, i, targets)));
        }
        return pages;
    }

    /// <summary>The index of the reference: <c>std/</c>. A package's root module — the one named
    /// by the package alone, its <c>src/lib.lyr</c> — is that page, its modules listed under it.</summary>
    public static string IndexPath(Surface surface) => $"{surface.Package}/index.html";

    /// <summary>A module's page: <c>std/json/</c>.</summary>
    public static string ModulePath(Surface surface, SurfaceModule module) =>
        ModuleSegment(surface, module) is { Length: > 0 } segment ? $"{surface.Package}/{segment}/index.html" : IndexPath(surface);

    /// <summary>A type's page: <c>std/json/JsonValue/</c>; a type of the root module right under
    /// the package, <c>geo/Point/</c>.</summary>
    public static string TypePath(Surface surface, SurfaceModule module, Item item) =>
        ModuleSegment(surface, module) is { Length: > 0 } segment
            ? $"{surface.Package}/{segment}/{item.Name}/index.html"
            : $"{surface.Package}/{item.Name}/index.html";

    private static SurfaceModule? RootModule(Surface surface) => surface.Modules.FirstOrDefault(m => m.Path == surface.Package);

    private static string ModuleSegment(Surface surface, SurfaceModule module) =>
        module.Path == surface.Package ? ""
        : module.Path.StartsWith(surface.Package + ".", StringComparison.Ordinal) ? module.Path[(surface.Package.Length + 1)..]
        : module.Path;

    private static bool HasPage(Item item) =>
        item.Kind is ItemKind.Struct or ItemKind.Class or ItemKind.Enum or ItemKind.Interface;

    private static string Dir(string page) => page[..(page.Length - "index.html".Length)];

    private static Page Index(Surface surface, SiteContext site, Targets targets)
    {
        var path = IndexPath(surface);
        var sb = new StringBuilder();
        sb.Append("<ul class=\"listing\">\n");
        foreach (var module in surface.Modules)
            sb.Append($"<li>{Layout.Anchor(site, path, new Link(module.Path, Dir(ModulePath(surface, module))))}")
              .Append(Summary(module.Doc, path, targets)).Append("</li>\n");
        sb.Append("</ul>\n");
        var navigation = surface.Modules.Select(m => new Link(m.Path, Dir(ModulePath(surface, m)))).ToList();
        return new Page(path, Pages.Reference(site, path,
            new ReferenceContent(surface.Package, sb.ToString(), navigation, surface.Package, [])));
    }

    private static Page ModulePage(Surface surface, SiteContext site, SurfaceModule module, Targets targets)
    {
        var path = ModulePath(surface, module);
        var ids = new Ids();
        var sb = new StringBuilder();
        var headings = new List<Heading>();
        if (module.Doc is { } doc) sb.Append(Text(doc, path, targets));
        if (module.Path == surface.Package && surface.Modules.Count > 1)
        {
            sb.Append("<h2 id=\"modules\">Modules</h2>\n<ul class=\"listing\">\n");
            headings.Add(new Heading(2, "Modules", "modules"));
            foreach (var other in surface.Modules.Where(m => m != module))
                sb.Append($"<li>{Layout.Anchor(site, path, new Link(other.Path, Dir(ModulePath(surface, other))))}")
                  .Append(Summary(other.Doc, path, targets)).Append("</li>\n");
            sb.Append("</ul>\n");
        }

        void Section(string title, string id, IEnumerable<Item> items, Action<Item> write)
        {
            var list = items.ToList();
            if (list.Count == 0) return;
            sb.Append($"<h2 id=\"{id}\">{Html.Text(title)}</h2>\n");
            headings.Add(new Heading(2, title, id));
            foreach (var item in list) write(item);
        }

        Section("Types", "types", module.Items.Where(HasPage), item =>
            sb.Append("<div class=\"listed\">")
              .Append(Layout.Anchor(site, path, new Link(item.Name, Dir(TypePath(surface, module, item)))))
              .Append($" <span class=\"kind\">{KindWord(item.Kind)}</span>")
              .Append(Summary(item.Doc, path, targets)).Append("</div>\n"));
        Section("Functions", "functions", module.Items.Where(i => i.Kind == ItemKind.Function),
            item => Member(sb, headings, ids.Next("fn", item.Name), item, path, targets));
        Section("Constants", "constants", module.Items.Where(i => i.Kind == ItemKind.Constant),
            item => Member(sb, headings, ids.Next("const", item.Name), item, path, targets));
        Section("Type aliases", "aliases", module.Items.Where(i => i.Kind == ItemKind.Alias),
            item => Member(sb, headings, ids.Next("type", item.Name), item, path, targets));
        Section("Extensions", "extensions", module.Items.Where(i => i.Kind == ItemKind.Extension), item =>
        {
            var id = ids.Next("ext", item.Name);
            Member(sb, headings, id, item, path, targets);
            foreach (var member in item.Members)
                Member(sb, null, ids.Next(id + ".method", member.Name), member, path, targets);
        });

        return new Page(path, Pages.Reference(site, path,
            new ReferenceContent(module.Path, sb.ToString(), Navigation(surface, module), module.Path, headings)));
    }

    private static Page TypePage(Surface surface, SiteContext site, SurfaceModule module, Item type, Targets targets)
    {
        var path = TypePath(surface, module, type);
        var ids = new Ids();
        var sb = new StringBuilder();
        var headings = new List<Heading>();
        sb.Append($"<pre class=\"signature\"><code class=\"language-lyric\">{Highlighter.Highlight(type.Signature)}</code></pre>\n");
        if (type.Conformances.Count > 0)
            sb.Append("<p class=\"conforms\">Conforms to ")
              .Append(string.Join(", ", type.Conformances.Select(c => $"<code>{Html.Text(c)}</code>")))
              .Append(".</p>\n");
        if (type.Doc is { } doc) sb.Append(Text(doc, path, targets));

        void Section(string title, string id, ItemKind kind, string prefix)
        {
            var list = type.Members.Where(m => m.Kind == kind).ToList();
            if (list.Count == 0) return;
            sb.Append($"<h2 id=\"{id}\">{Html.Text(title)}</h2>\n");
            headings.Add(new Heading(2, title, id));
            foreach (var member in list) Member(sb, headings, ids.Next(prefix, member.Name), member, path, targets);
        }

        Section("Associated types", "associated-types", ItemKind.AssociatedType, "assoc");
        Section("Variants", "variants", ItemKind.Variant, "variant");
        Section("Fields", "fields", ItemKind.Field, "field");
        Section("Constants", "constants", ItemKind.Constant, "const");
        Section("Methods", "methods", ItemKind.Method, "method");

        var title = $"{KindWord(type.Kind)} {type.Name}";
        return new Page(path, Pages.Reference(site, path,
            new ReferenceContent(title, sb.ToString(), Navigation(surface, module), module.Path, headings)));
    }

    /// <summary>A module's items as the side navigation: the module, its types, its functions.</summary>
    private static List<Link> Navigation(Surface surface, SurfaceModule module)
    {
        var dir = Dir(ModulePath(surface, module));
        var links = new List<Link> { new(module.Path, dir) };
        links.AddRange(module.Items.Where(HasPage).Select(i => new Link(i.Name, Dir(TypePath(surface, module, i)))));
        var ids = new Ids();
        foreach (var item in module.Items.Where(i => i.Kind == ItemKind.Function))
            links.Add(new Link(item.Name + "()", dir + "#" + ids.Next("fn", item.Name)));
        return links;
    }

    private static void Member(StringBuilder sb, List<Heading>? headings, string id, Item item, string path, Targets targets)
    {
        sb.Append($"<section class=\"member\" id=\"{Html.Attr(id)}\">\n");
        sb.Append($"<pre class=\"signature\"><code class=\"language-lyric\">{Highlighter.Highlight(item.Signature)}</code></pre>\n");
        if (item.Conformances.Count > 0)
            sb.Append("<p class=\"conforms\">Conforms to ")
              .Append(string.Join(", ", item.Conformances.Select(c => $"<code>{Html.Text(c)}</code>")))
              .Append(".</p>\n");
        if (item.Doc is { } doc) sb.Append(Text(doc, path, targets));
        sb.Append("</section>\n");
        headings?.Add(new Heading(3, item.Name, id));
    }

    /// <summary>A doc text as HTML: its headings two levels down, so <c># Panics</c> stands under
    /// the member it belongs to.</summary>
    private static string Text(string doc, string path, Targets targets)
    {
        var shifted = new StringBuilder();
        var fenced = false;
        foreach (var line in Linked(doc, path, targets).Split('\n'))
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal)) fenced = !fenced;
            shifted.Append(!fenced && line.StartsWith('#') ? "###" + line : line).Append('\n');
        }
        return Markdown.Render(shifted.ToString()).Html;
    }

    /// <summary>The first sentence of a doc text, for a listing; nothing where there is no text.</summary>
    private static string Summary(string? doc, string path, Targets targets)
    {
        if (doc is null) return "";
        var paragraph = Linked(doc, path, targets).Replace("\r\n", "\n").Split("\n\n")[0].Replace('\n', ' ').Trim();
        var end = SentenceEnd().Match(paragraph);
        var first = end.Success ? paragraph[..(end.Index + 1)] : paragraph;
        var html = Markdown.Render(first).Html.Trim();
        if (html.StartsWith("<p>", StringComparison.Ordinal) && html.EndsWith("</p>", StringComparison.Ordinal))
            html = html[3..^4];
        return $" <span class=\"summary\">— {html}</span>";
    }

    [GeneratedRegex(@"[.!?](?=\s+[A-Z`\[(])")]
    private static partial Regex SentenceEnd();

    /// <summary>Where each key of the surface's links stands: a page under the version root, with
    /// the member's anchor.</summary>
    private sealed class Targets : Dictionary<string, string>
    {
        public Targets() : base(StringComparer.Ordinal) { }
    }

    private static Targets TargetsOf(Surface surface)
    {
        var targets = new Targets();
        foreach (var module in surface.Modules)
        {
            var page = Dir(ModulePath(surface, module));
            targets.TryAdd($"M:{module.Path}", page);
            foreach (var item in module.Items)
            {
                switch (item.Kind)
                {
                    case ItemKind.Function: targets.TryAdd($"F:{module.Path}:{item.Name}", page + "#" + Anchor("fn", item.Name)); break;
                    case ItemKind.Constant: targets.TryAdd($"C:{module.Path}:{item.Name}", page + "#" + Anchor("const", item.Name)); break;
                    case ItemKind.Alias: targets.TryAdd($"A:{module.Path}:{item.Name}", page + "#" + Anchor("type", item.Name)); break;
                }
                if (!HasPage(item)) continue;
                var typePage = Dir(TypePath(surface, module, item));
                targets.TryAdd($"T:{module.Path}:{item.Name}", typePage);
                foreach (var member in item.Members)
                {
                    var kind = member.Kind switch
                    {
                        ItemKind.Method => "method",
                        ItemKind.Field => "field",
                        ItemKind.Variant => "variant",
                        ItemKind.AssociatedType => "assoc",
                        ItemKind.Constant => "const",
                        _ => null,
                    };
                    if (kind is not null) targets.TryAdd($"m:{module.Path}:{item.Name}:{kind}:{member.Name}", typePage + "#" + Anchor(kind, member.Name));
                }
            }
        }
        return targets;
    }

    /// <summary>The first anchor of a name and kind, as <see cref="Ids"/> gives it.</summary>
    private static string Anchor(string prefix, string name) => new Ids().Next(prefix, name);

    [GeneratedRegex(@"\[([^\]\n]+)\]\(" + SurfaceReader.LinkScheme + @"([^)\s]+)\)")]
    private static partial Regex KeyedLink();

    /// <summary>The doc text with each keyed link pointing at its page from <paramref name="path"/>; a
    /// key the reference has no page for — a member it does not list falls back to its type's page
    /// — leaves its label as text.</summary>
    private static string Linked(string doc, string path, Targets targets) =>
        KeyedLink().Replace(doc, m =>
        {
            var key = m.Groups[2].Value;
            if (!targets.TryGetValue(key, out var target) && key.StartsWith("m:", StringComparison.Ordinal))
            {
                var parts = key.Split(':');
                targets.TryGetValue($"T:{parts[1]}:{parts[2]}", out target);
            }
            return target is null ? m.Groups[1].Value : $"[{m.Groups[1].Value}]({Layout.Href(path, target)})";
        });

    private static string KindWord(ItemKind kind) => kind switch
    {
        ItemKind.Struct => "struct",
        ItemKind.Class => "class",
        ItemKind.Enum => "enum",
        ItemKind.Interface => "interface",
        _ => kind.ToString().ToLowerInvariant(),
    };

    /// <summary>Anchors of one page: the second of a name and kind takes <c>-2</c>; a name's
    /// characters outside letters, digits and <c>_</c> are left out, so <c>List&lt;T&gt;</c> is <c>ListT</c>.</summary>
    private sealed class Ids
    {
        private readonly Dictionary<string, int> _seen = new(StringComparer.Ordinal);

        public string Next(string prefix, string name)
        {
            var id = $"{prefix}.{new string([.. name.Where(c => char.IsAsciiLetterOrDigit(c) || c == '_')])}";
            var n = _seen.GetValueOrDefault(id) + 1;
            _seen[id] = n;
            return n == 1 ? id : $"{id}-{n}";
        }
    }
}
