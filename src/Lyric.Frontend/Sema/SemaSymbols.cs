using Lyric.AST;
using Lyric.Resolver;

namespace Lyric.Sema;

// Body-local symbols. They live in Lyric.Sema rather than Lyric.Resolver because they carry a
// LyrType, and the resolver must not point at the sema. They inherit the resolver's symbol base,
// so they fit into a SymbolTable.

public sealed class ParameterSymbol : Symbol
{
    public LyrType Type { get; }

    /// <summary>A place parameter, <c>&amp;x: T</c> (design/v5/spec/03 T12): the name is the
    /// caller's place, read and written through, and no value of its own.</summary>
    public bool IsPlace { get; init; }

    public ParameterSymbol(string name, LyrType type, Node? declaration) : base(name, declaration)
        => Type = type;
}

public sealed class LocalSymbol : Symbol
{
    public LyrType Type { get; }
    public bool IsMutable { get; } // var against let

    public LocalSymbol(string name, LyrType type, bool isMutable, Node? declaration) : base(name, declaration)
    {
        Type = type;
        IsMutable = isMutable;
    }
}
