using Lyric.AST;
using Lyric.Core;

namespace Lyric.Resolver;

/// <summary>
/// A side table binding AST reference nodes to their resolved symbols without touching the
/// immutable AST.
/// type names (<see cref="NamedType"/> to symbol).
/// </summary>
public sealed class BindingResult
{
    private readonly Dictionary<Node, Symbol> _bindings = new(ReferenceEqualityComparer.Instance);

    public void Bind(Node node, Symbol symbol) => _bindings[node] = symbol;

    public Symbol? Resolve(Node node) => _bindings.TryGetValue(node, out var s) ? s : null;

    /// <summary>
    /// Every bound node with its symbol.
    ///
    /// <para>The table answers "what does this node mean". Turning it round — "which nodes mean this
    /// symbol" — has no answer without walking it, and a second table built alongside would be a
    /// second truth about one relation. A consumer that needs the reverse direction builds it from
    /// here.</para>
    /// </summary>
    public IEnumerable<KeyValuePair<Node, Symbol>> All => _bindings;

    public int Count => _bindings.Count;

    private readonly List<(FileId File, Symbol Symbol)> _qualifiers = [];

    /// <summary>
    /// An import a QUALIFIED TYPE PATH stepped through: the <c>look</c> of <c>look.Eye</c>.
    ///
    /// <para>It needs a place of its own because it has no node. A qualified expression is a
    /// <see cref="MemberExpr"/> over an <see cref="IdentifierExpr"/>, so its qualifier is a node
    /// and lands in the table above; a <see cref="NamedType"/> carries its path as strings, so
    /// the segments before the name are mentioned in the source and present nowhere else. The
    /// file is what the recording needs — a use marks an import in the file that writes it.</para>
    /// </summary>
    public void MarkQualifier(FileId file, Symbol symbol) => _qualifiers.Add((file, symbol));

    /// <summary>Every import stepped through by a qualified type path, with the file it was
    /// written in.</summary>
    public IEnumerable<(FileId File, Symbol Symbol)> Qualifiers => _qualifiers;
}
