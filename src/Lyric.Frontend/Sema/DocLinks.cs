using System.Text;
using System.Text.RegularExpressions;
using Lyric.AST;
using Lyric.Resolver;

namespace Lyric.Sema;

/// <summary>What a link in documentation names: the symbol it reaches; for a member, the type it
/// is a member of; and — where the name is a member the type has through an <c>extend</c> block or
/// an interface rather than in its own table — that member's name beside the type, which is then
/// <see cref="Symbol"/>. <see cref="Symbol"/> is <c>null</c> for a name that is known but has no
/// page, a type parameter.</summary>
public sealed record DocTarget(Symbol? Symbol, string? Member = null, TypeSymbol? Owner = null);

/// <summary>
/// The links of documentation comments — <c>[Type]</c>, <c>[Type.member]</c>, <c>[module.Type]</c>,
/// <c>[fn]</c> — and what each names. One resolution for the <c>broken-doc-link</c> lint, which
/// reports the links that name nothing, and for <c>lyric doc</c>, which makes the others links: the
/// two never disagree. Code in backticks or a fence is not read.
/// </summary>
public sealed class DocLinks
{
    private readonly Compilation _comp;
    private readonly BindingResult _binding;
    private readonly TypeResult _types;

    public DocLinks(Compilation comp, BindingResult binding, TypeResult types)
    {
        _comp = comp;
        _binding = binding;
        _types = types;
    }

    /// <summary>A link's brackets: not after a word or a closing bracket (an index, <c>xs[i]</c>),
    /// not before <c>(</c> or <c>[</c> (Markdown's own links), and not a reference definition —
    /// <c>[label]:</c> at the start of a line —, where <c>[IoError]:</c> inside a sentence is a link.</summary>
    private static readonly Regex DocLink = new(@"(?<![\w\]\)!])\[([A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)\](?![\(\[])(?!(?<=^[ \t]*\[[^\]]*\]):)",
        RegexOptions.CultureInvariant | RegexOptions.Multiline);

    /// <summary>One link of a declaration's documentation: the declaration, the text between the
    /// brackets, and what it names — <c>null</c> where it names nothing the module sees.</summary>
    public readonly record struct Link(Node Node, string Target, DocTarget? Hit);

    /// <summary>The links of every documented declaration of <paramref name="ast"/>, the module's
    /// tree, and of their members, in source order.</summary>
    public IEnumerable<Link> Of(ModuleSymbol module, Module ast)
    {
        var docs = _comp.Documentation;
        var modules = ModuleNames(ast);
        IEnumerable<Link> Read(Node node, TypeSymbol? owner, GenericParam[] outer)
        {
            if (docs.Of(node) is not { } text || node is not INamedDecl) yield break;
            // a type parameter of the declaration, or of the type around it, is a name its text may use
            var generics = outer.Concat(GenericsOf(node)).Select(g => g.Name).ToHashSet(StringComparer.Ordinal);
            foreach (var target in LinksIn(text))
                yield return new Link(node, target,
                    generics.Contains(target) ? new DocTarget(null) : Resolve(target.Split('.'), module, modules, owner));
        }
        foreach (var decl in ast.Declarations)
        {
            foreach (var link in Read(decl, null, [])) yield return link;
            var owner = decl is INamedDecl n ? module.Members.LookupLocal(n.Name) as TypeSymbol : null;
            IEnumerable<Node> members = decl switch
            {
                StructDecl s => s.Members,
                ClassDecl c => c.Members,
                EnumDecl e => [.. e.Variants, .. e.Methods],
                InterfaceDecl i => i.Members,
                ExtendDecl x => x.Methods,
                _ => [],
            };
            if (decl is ExtendDecl extend) owner = (_binding.Resolve(extend.Target) ?? _types.RefOf(extend.Target)) as TypeSymbol;
            foreach (var member in members)
            foreach (var link in Read(member, owner, GenericsOf(decl)))
                yield return link;
        }
    }

    /// <summary>What the links of a module's own documentation — its <c>//!</c> text — name, read in
    /// the module's scope.</summary>
    public IEnumerable<(string Target, DocTarget? Hit)> OfModuleText(ModuleSymbol module, Module ast, string text)
    {
        var modules = ModuleNames(ast);
        foreach (var target in LinksIn(text))
            yield return (target, Resolve(target.Split('.'), module, modules, owner: null));
    }

    /// <summary>A module's documentation: the <c>//!</c> lines that open its file, without the
    /// marks; <c>null</c> where it has none.</summary>
    public static string? ModuleText(string fileText)
    {
        var lines = fileText.TrimStart('﻿').Replace("\r\n", "\n").Split('\n');
        var i = 0;
        while (i < lines.Length && lines[i].Trim().Length == 0) i++;
        var block = new List<string>();
        while (i < lines.Length && lines[i].TrimStart().StartsWith("//!", StringComparison.Ordinal))
        {
            var line = lines[i].TrimStart()[3..];
            block.Add(line.StartsWith(' ') ? line[1..] : line);
            i++;
        }
        return block.Count == 0 ? null : string.Join('\n', block).Trim('\n');
    }

    private static GenericParam[] GenericsOf(Node node) => node switch
    {
        FunctionDecl f => f.Generics,
        StructDecl s => s.Generics,
        ClassDecl c => c.Generics,
        EnumDecl e => e.Generics,
        InterfaceDecl i => i.Generics,
        _ => [],
    };

    /// <summary>The names whole-module imports bind in a file: <c>import std.io;</c> binds <c>io</c>.</summary>
    internal static Dictionary<string, string> ModuleNames(Module ast)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var import in ast.Declarations.OfType<ImportDecl>())
            switch (import.Clause)
            {
                case null: names[import.Path[^1]] = string.Join('.', import.Path); break;
                case ImportAlias alias: names[alias.Alias] = string.Join('.', import.Path); break;
            }
        return names;
    }

    /// <summary>The link targets of a doc text, its code spans and fences left out.</summary>
    public static IEnumerable<string> LinksIn(string text) =>
        DocLink.Matches(Prose(text)).Select(m => m.Groups[1].Value);

    /// <summary>The doc text with each link <c>[target]</c> that <paramref name="replace"/> answers
    /// for replaced by its answer; the others, and code, as they are. The links are those
    /// <see cref="LinksIn"/> finds, at the places it finds them.</summary>
    public static string Rewrite(string text, Func<string, string?> replace)
    {
        var sb = new StringBuilder();
        var at = 0;
        foreach (Match m in DocLink.Matches(Prose(text)))
        {
            if (replace(m.Groups[1].Value) is not { } answer) continue;
            sb.Append(text, at, m.Index - at).Append(answer);
            at = m.Index + m.Length;
        }
        return sb.Append(text, at, text.Length - at).ToString();
    }

    /// <summary>The text with its code spans and fences blanked, every other character in its place.</summary>
    private static string Prose(string text)
    {
        var prose = new StringBuilder(text.Length);
        var fenced = false;
        var lines = text.Split('\n');
        for (var n = 0; n < lines.Length; n++)
        {
            var line = lines[n];
            if (n > 0) prose.Append('\n');
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal)) { fenced = !fenced; prose.Append(' ', line.Length); continue; }
            if (fenced) { prose.Append(' ', line.Length); continue; }
            var inCode = false;
            foreach (var c in line)
            {
                if (c == '`') { inCode = !inCode; prose.Append(' '); continue; }
                prose.Append(inCode ? ' ' : c);
            }
        }
        return prose.ToString();
    }

    private DocTarget? Resolve(string[] path, ModuleSymbol module, Dictionary<string, string> modules, TypeSymbol? owner)
    {
        Symbol? at = owner?.Members.LookupLocal(path[0]);
        var of = at is null ? null : owner;
        at ??= module.Members.Lookup(path[0])
               ?? _comp.PreludeMember(path[0])
               ?? _comp.Builtins.Lookup(path[0]);
        if (at is null && modules.TryGetValue(path[0], out var full)) at = _comp.FindModule(full.Split('.'));
        if (at is null && _comp.FindModule([path[0]]) is { } top) at = top;
        string? member = null;
        for (var i = 1; at is not null && i < path.Length; i++)
        {
            while (at is ImportBindingSymbol shell) at = shell.Target;
            member = null;
            switch (at)
            {
                case ModuleSymbol m:
                    at = m.Members.LookupLocal(path[i]) ?? _comp.FindModule([.. m.Path, path[i]]);
                    of = null;
                    break;
                case TypeSymbol t when t.Members.LookupLocal(path[i]) is { } own:
                    at = own;
                    of = t;
                    break;
                case TypeSymbol t when HasMember(t, path[i]):
                    member = path[i]; // the type stays, its member is beyond its own table
                    of = null;
                    break;
                default:
                    at = null;
                    break;
            }
        }
        while (at is ImportBindingSymbol shell) at = shell.Target;
        return at is null ? null : new DocTarget(at, member, of);
    }

    /// <summary>Whether the type has the member beyond its own table: a method an <c>extend</c> block
    /// declares for it, or a member of an interface it conforms to — in its declaration or in a block —,
    /// a default among them.</summary>
    private bool HasMember(TypeSymbol type, string name)
    {
        var interfaces = new List<TypeNode>(type.Declaration switch
        {
            StructDecl s => s.Interfaces,
            ClassDecl c => c.Interfaces,
            EnumDecl e => e.Interfaces,
            InterfaceDecl i => i.Interfaces,
            _ => [],
        });
        foreach (var module in _comp.Modules)
            foreach (var extend in _comp.AstOf(module).Declarations.OfType<ExtendDecl>())
                if (ReferenceEquals(_binding.Resolve(extend.Target) ?? _types.RefOf(extend.Target), type))
                {
                    if (extend.Methods.Any(m => m.Name == name)) return true;
                    interfaces.AddRange(extend.Interfaces);
                }
        var seen = new HashSet<InterfaceDecl>(ReferenceEqualityComparer.Instance);
        bool Declares(TypeNode node) =>
            (_binding.Resolve(node) ?? _types.RefOf(node)) is TypeSymbol { Declaration: InterfaceDecl i } iface && seen.Add(i)
            && (iface.Members.LookupLocal(name) is not null || i.Members.Any(m => m.Name == name) || i.Interfaces.Any(Declares));
        return interfaces.Any(Declares);
    }
}
