using Lyric.AST;
using Lyric.Compiler;
using Lyric.Core;
using Lyric.Resolver;

namespace Lyric.Lsp.Analysis;

/// <summary>
/// Where a name was declared: the file it stands in, the span of the whole declaration, and the
/// span of the name inside it.
///
/// <para>Both spans, because the two answer different questions and the protocol asks for both:
/// what the jump reveals is the declaration, what it selects is the name.
/// <paramref name="NameSpan"/> lies within <paramref name="Span"/>.</para>
/// </summary>
public sealed record DefinitionTarget(FileId File, Span Span, Span NameSpan);

/// <summary>
/// The declaration a name refers to.
///
/// <para>Needs nothing the compiler did not already record. <see cref="Symbol.Declaration"/> holds
/// the AST node a symbol came from and every node carries its span, so the whole feature is a
/// lookup — which is why it follows hover rather than preceding it: hover built the search for the
/// node under a cursor, and this asks the same search a different question.</para>
///
/// <para>A target in ANOTHER file is the ordinary case, not an edge one: every call into the
/// standard library lands there. Those files are read from disk with their real paths, so the
/// span's file id names something an editor can open.</para>
/// </summary>
public static class DefinitionProvider
{
    /// <param name="root">The module AST of the file the cursor is in — see
    /// <see cref="HoverProvider.At"/> for why it is passed.</param>
    public static DefinitionTarget? At(SemanticModel model, Module root, FileId file, int offset)
    {
        var path = NodeFinder.PathAt(root, file, offset);

        // '_' names nothing; the pattern around it is not what the cursor is on.
        if (path is [.., WildcardPattern]) return null;

        // From the inside out, like hover: the innermost node is the most specific, and the nodes
        // above it are the fallback when it binds to nothing.
        for (var i = path.Count - 1; i >= 0; i--)
        {
            var symbol = model.Types.RefOf(path[i]) ?? model.Binding.Resolve(path[i]);
            if (symbol is null)
            {
                // A field of a variant's payload has no symbol of its own: '.Other { code = 0 }'
                // names it, and the walk outwards would answer with the variant.
                if (path[i] is StructInitField field && i > 0 && PayloadField(model, path[i - 1], field) is { } payload
                    && field.NameSpan.File == file && offset >= field.NameSpan.Start && offset <= field.NameSpan.End)
                    return new DefinitionTarget(payload.Span.File, payload.Span, payload.NameSpan);
                continue;
            }

            // Reached from inside rather than from its name: the cursor is on something this
            // declaration contains, not on the declaration. Answering with it would send the reader
            // somewhere they did not point at, which is worse than the null below.
            if (!NodeFinder.Answers(path[i], symbol, file, offset)) return null;

            // A member access answers for its member's name, an initializer's field for its own,
            // an initializer for its path: in '(u with { … }).show()' the cursor on 'with' is not on
            // 'show', in 'n = items.length()' the cursor on 'length' is not on 'n'.
            if (path[i] switch
                {
                    MemberExpr { MemberSpan: var member } => member.File.IsValid && (offset < member.Start || offset > member.End),
                    StructInitField { NameSpan: var name } => name.File.IsValid && (offset < name.Start || offset > name.End),
                    StructInitExpr { NameSpan: var last } => last.File.IsValid && offset > last.End,
                    _ => false,
                }) return null;

            // 'I.Iter.Item' binds to its first name; a later one is an associated type, found
            // along the interfaces of the name before it.
            if (path[i] is NamedType { Path.Length: > 1 } dotted && symbol.Name == dotted.Path[0]
                && SegmentAt(model, dotted, offset) is > 0 and var segment
                && Along(model, dotted, symbol, segment) is { } associated)
                symbol = associated;

            // 'Shape.Rect { … }' binds to the variant; the cursor on 'Shape' means the enum, found
            // by its name where the file stands.
            if (path[i] is StructInitExpr { Path.Length: > 1 } init && symbol is EnumVariantSymbol
                && SegmentAt(model, init.Span, init.Path, offset) is var written && written >= 0 && written < init.Path.Length - 1
                && ModuleOf(model, file)?.Members.LookupLocal(init.Path[written]) is { } enumName)
                symbol = ReferenceProvider.Target(enumName);

            // 'Iterator<Item = int>': the name of an argument is the interface's associated type.
            if (path[i] is NamedType { ArgumentNames: { } names } named && symbol is TypeSymbol iface
                && ArgumentNameAt(model, named, names, offset) is { } argument
                && iface.Members.LookupLocal(argument) is AssociatedTypeSymbol fixedOne)
                symbol = fixedOne;

            // Code the compiler wrote — a synthesized 'hash' of 'T[4]' — has no file to open.
            if (Declaration(symbol) is { } target)
                return model.Compilation.IsSynthesized(target.File) ? null : target;

            // A symbol was found and it has no declaration to jump to. Stopping here rather than
            // continuing outwards: the enclosing statement is not the definition of this name, and
            // offering it would send the reader somewhere they did not ask about.
            return null;
        }

        return null;
    }

    /// <summary>Which name of a dotted type path the offset stands in, counted from 0; -1 between
    /// them.</summary>
    private static int SegmentAt(SemanticModel model, NamedType type, int offset) =>
        SegmentAt(model, type.Span, type.Path, offset);

    private static int SegmentAt(SemanticModel model, Span span, string[] names, int offset)
    {
        var text = model.Compilation.Sources.GetText(span.File);
        var at = span.Start;
        for (var k = 0; k < names.Length; k++)
        {
            at = text.IndexOf(names[k], at, StringComparison.Ordinal);
            if (at < 0 || at >= span.End) return -1;
            if (offset >= at && offset <= at + names[k].Length) return k;
            at += names[k].Length;
        }
        return -1;
    }

    /// <summary>The module a file holds.</summary>
    private static ModuleSymbol? ModuleOf(SemanticModel model, FileId file) =>
        model.Compilation.Modules.FirstOrDefault(m => model.Compilation.AstOf(m).Span.File == file);

    /// <summary>The name of a named type argument the offset stands on — <c>Item</c> of
    /// <c>Item = int</c> —, written before its argument; <c>null</c> elsewhere.</summary>
    private static string? ArgumentNameAt(SemanticModel model, NamedType type, string?[] names, int offset)
    {
        var text = model.Compilation.Sources.GetText(type.Span.File);
        for (var k = 0; k < names.Length && k < type.TypeArguments.Length; k++)
        {
            if (names[k] is not { } name) continue;
            var at = text.LastIndexOf(name, type.TypeArguments[k].Span.Start, StringComparison.Ordinal);
            if (at >= type.Span.Start && offset >= at && offset <= at + name.Length) return name;
        }
        return null;
    }

    /// <summary>The associated type a dotted path names at <paramref name="segment"/>: each name
    /// after the first is looked up in the interfaces of the one before — a type parameter's
    /// bounds, an associated type's own bounds, an interface itself.</summary>
    private static Symbol? Along(SemanticModel model, NamedType type, Symbol head, int segment)
    {
        var current = head;
        for (var k = 1; k <= segment; k++)
        {
            IEnumerable<TypeSymbol> interfaces = current switch
            {
                GenericParamSymbol g => g.Constraints.Select(c => model.Binding.Resolve(c)).OfType<TypeSymbol>(),
                AssociatedTypeSymbol a => a.Bounds.Select(b => b switch
                {
                    Sema.NamedRef n => n.Symbol,
                    Sema.GenericInstance g => g.Definition,
                    _ => null,
                }).OfType<TypeSymbol>(),
                TypeSymbol t => [t],
                _ => [],
            };
            var name = type.Path[k];
            if (interfaces.Select(i => i.Members.LookupLocal(name)).OfType<AssociatedTypeSymbol>().FirstOrDefault() is not { } next)
                return null;
            current = next;
        }
        return current;
    }

    /// <summary>The payload field an initializer of a variant names, where <paramref name="init"/>
    /// makes a variant with fields.</summary>
    private static FieldDecl? PayloadField(SemanticModel model, Node init, StructInitField field)
    {
        var variant = model.Types.RefOf(init) ?? (init is VariantInitExpr v ? model.Types.RefOf(v.Variant) ?? model.Binding.Resolve(v.Variant) : null);
        return variant is EnumVariantSymbol { Declaration: EnumVariant { StructFields: { } fields } }
            ? Array.Find(fields, f => f.Name == field.Name)
            : null;
    }

    private static DefinitionTarget? Declaration(Symbol symbol) => symbol switch
    {
        // The name of an import stands for what it imports. Jumping to the import line would answer
        // "where did this name enter this file", which is a different question and one the reader
        // is already looking at.
        ImportBindingSymbol import => Declaration(import.Target),

        // Nothing to jump to, each for its own reason: a builtin has no declaration in any file, a
        // module outside the compilation was never parsed, and an unresolved name has no target by
        // definition — the diagnostic on the same span says so already.
        ExternalSymbol or ErrorSymbol => null,

        // A module is its file, and a file has no name in it: the jump goes to its start, selecting
        // nothing — the module's node spans the whole file, and selecting it selected everything.
        ModuleSymbol { Declaration: { } module } when module.Span.File.IsValid =>
            new DefinitionTarget(module.Span.File, new Span(module.Span.File, 0, 0), new Span(module.Span.File, 0, 0)),

        // A node that declares a name knows where the name stands. One that does not is its own
        // best answer: the span it covers is all there is to point at.
        { Declaration: INamedDecl named } when named.Span.File.IsValid =>
            new DefinitionTarget(named.Span.File, named.Span, named.NameSpan),

        { Declaration: { } node } when node.Span.File.IsValid =>
            new DefinitionTarget(node.Span.File, node.Span, node.Span),

        _ => null,
    };
}
