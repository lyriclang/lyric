using Lyric.AST;
using Lyric.Core;

namespace Lyric.Ir.Lowering;

/// <summary>
/// What a build leaves out: the declarations whose
/// <c>@When</c> is false, and the modules whose header carries a false one — every declaration of
/// such a module. They are checked as the rest; the lowering builds none of them, their
/// <c>comptime</c> sites are not evaluated, and an active use of one is refused.
/// </summary>
public sealed class Inactivity
{
    private readonly HashSet<FileId> _modules = new();
    private readonly HashSet<Node> _declarations = new(ReferenceEqualityComparer.Instance);
    private readonly List<Span> _extents = new();

    public bool IsEmpty => _modules.Count == 0 && _declarations.Count == 0;

    /// <summary>A module whose header's <c>@When</c> is false: all it declares.</summary>
    public void Module(FileId file) => _modules.Add(file);

    /// <summary>A declaration whose <c>@When</c> is false.</summary>
    public void Declaration(Decl declaration)
    {
        _declarations.Add(declaration);
        _extents.Add(declaration.Span);
    }

    /// <summary>Whether the build leaves <paramref name="declaration"/> out.</summary>
    public bool IsInactive(Decl declaration) =>
        _declarations.Contains(declaration) || _modules.Contains(declaration.Span.File);

    /// <summary>Whether <paramref name="span"/> lies in what the build leaves out.</summary>
    public bool Covers(Span span) =>
        _modules.Contains(span.File)
        || _extents.Exists(e => e.File == span.File && span.Start >= e.Start && span.End <= e.End);
}
