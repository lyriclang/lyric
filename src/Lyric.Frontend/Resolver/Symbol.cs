using Lyric.AST;

namespace Lyric.Resolver;

// Symbols are identity objects, without value equality, and are built incrementally
// built up in stages: declared first, enriched with type information later. Hence mutable classes
// rather than records.

/// <summary>
/// Who may name a declaration (design/v5/spec/07 V2): its module, its package, everyone. What a
/// declaration without a word gets is the reader's rule — <see cref="Internal"/> in Lyric 5,
/// <see cref="Private"/> for the 4.x tools, whose unmarked names were their module's.
/// </summary>
public enum Visibility
{
    Private,  // 'private': the declaring module's
    Internal, // 'internal': the declaring package's
    Public,   // 'pub': exported
}

public enum TypeSymbolKind
{
    Struct, Class, Enum, Interface, Alias, Builtin
}

public abstract class Symbol
{
    public string Name { get; }
    public Node? Declaration { get; } // the AST node of the declaration; null for builtins and synthetic symbols

    protected Symbol(string name, Node? declaration)
    {
        Name = name;
        Declaration = declaration;
    }

    /// <summary>The module that declares it — a top-level name, a member, a method of an
    /// <c>extend</c> block, or an import, whose module is the one importing. <c>null</c> for a
    /// builtin, a local, a type parameter and what the compiler makes up.</summary>
    public ModuleSymbol? Home { get; set; }
}

/// <summary>A module (one file). Members holds its top-level symbols.</summary>
public sealed class ModuleSymbol : Symbol
{
    public string[] Path { get; }
    public SymbolTable Members { get; }

    public ModuleSymbol(string[] path, SymbolTable members, Node? declaration = null)
        : base(path.Length > 0 ? path[^1] : "<root>", declaration)
    {
        Path = path;
        Members = members;
    }

    public string FullName => string.Join('.', Path);
}

/// <summary>struct / class / enum / interface / type-Alias / Builtin.</summary>
public sealed class TypeSymbol : Symbol
{
    public TypeSymbolKind Kind { get; }
    public Visibility Visibility { get; }
    public SymbolTable Members { get; } // fields, methods and enum variants; empty for a builtin or an alias
    public GenericParamSymbol[] Generics { get; set; } = []; // the type parameters, set after the declaration

    /// <summary>An interface's <c>Self</c> (design/v5/spec/03 T5): the conforming type, as a type
    /// parameter of its own — bound to the implementer at a conformance, to the type parameter
    /// at a constraint. <c>null</c> for every other kind of type, whose <c>Self</c> is itself.</summary>
    public GenericParamSymbol? SelfParam { get; init; }

    public TypeSymbol(string name, TypeSymbolKind kind, Visibility visibility, SymbolTable members, Node? declaration)
        : base(name, declaration)
    {
        Kind = kind;
        Visibility = visibility;
        Members = members;
    }
}

public sealed class FunctionSymbol : Symbol
{
    public Visibility Visibility { get; }
    public bool IsMut { get; }

    /// <summary>A member without a receiver: no <c>this</c>, reachable only through the type.
    /// Always <c>false</c> for a free function.</summary>
    public bool IsStatic { get; }

    public GenericParamSymbol[] Generics { get; set; } = [];

    public FunctionSymbol(string name, Visibility visibility, bool isMut, Node? declaration,
        bool isStatic = false)
        : base(name, declaration)
    {
        Visibility = visibility;
        IsMut = isMut;
        IsStatic = isStatic;
    }
}

/// <summary>A generic type parameter (`T`) with its constraint interfaces, still as unresolved
/// TypeNodes; the sema resolves them.</summary>
public sealed class GenericParamSymbol : Symbol
{
    /// <summary>The interface whose <c>Self</c> this is (03 T5), <c>null</c> for an ordinary
    /// type parameter.</summary>
    public TypeSymbol? SelfOf { get; set; }

    public TypeNode[] Constraints { get; }

    public GenericParamSymbol(string name, TypeNode[] constraints, Node? declaration) : base(name, declaration)
        => Constraints = constraints;
}

public sealed class FieldSymbol : Symbol
{
    /// <summary>The field's word (design/v5/spec/07 V2 S0): a field follows the rule of every
    /// member.</summary>
    public Visibility Visibility { get; }

    public FieldSymbol(string name, Node? declaration, Visibility visibility) : base(name, declaration)
        => Visibility = visibility;
}

/// <summary>
/// An associated type (design/v5/spec/03 T6). Declared on an interface, <c>type Item;</c>, it
/// is a question every conformer answers: <see cref="Bindings"/> holds the answer per
/// conforming type once the checker has read the conformance. Declared on a struct, class,
/// enum or in a conformance block, <c>type Item = int;</c>, it is one conformer's answer.
/// </summary>
public sealed class AssociatedTypeSymbol : Symbol
{
    public AssociatedTypeSymbol(string name, Node? declaration) : base(name, declaration) { }

    /// <summary>The interface that declares it; <c>null</c> for a conformer's answer.</summary>
    public TypeSymbol? Owner { get; set; }

    // The answers, per conformer and per conformance INSTANCE (04 D6): 'Mul<int>' and
    // 'Mul<float>' of one type each answer 'Out' for themselves. The instance is the interface
    // at the conformance's arguments with 'Self' as the conformer.
    private readonly Dictionary<TypeSymbol, List<(Sema.LyrType Instance, Sema.LyrType Answer)>> _answers =
        new(ReferenceEqualityComparer.Instance);

    public void Bind(TypeSymbol conformer, Sema.LyrType instance, Sema.LyrType answer)
    {
        if (!_answers.TryGetValue(conformer, out var list)) _answers[conformer] = list = new();
        list.Add((instance, answer));
    }

    /// <summary>The conformer's answer: for the instance asked, else — the question reached
    /// here without one — the first conformance's. <c>null</c> where the type gave none.</summary>
    public Sema.LyrType? Answer(TypeSymbol conformer, Sema.LyrType? instance, bool exact = false)
    {
        if (!_answers.TryGetValue(conformer, out var list) || list.Count == 0) return null;
        if (instance is not null)
            foreach (var (at, answer) in list)
                if (Sema.LyrType.Equal(at, instance)) return answer;
        return exact ? null : list[0].Answer;
    }

    /// <summary>A built-in conformer's answer, the builtin named by its type name ('int'):
    /// the primitive types have no symbol of their own in the types.</summary>
    public Sema.LyrType? BuiltinAnswer(string name, Sema.LyrType? instance)
    {
        foreach (var conformer in _answers.Keys)
            if (conformer.Kind == TypeSymbolKind.Builtin && conformer.Name == name)
                return Answer(conformer, instance);
        return null;
    }
}

public sealed class EnumVariantSymbol : Symbol
{
    public EnumVariantSymbol(string name, Node? declaration) : base(name, declaration) { }
}

public sealed class GlobalSymbol : Symbol
{
    public Visibility Visibility { get; }

    public GlobalSymbol(string name, Visibility visibility, Node? declaration) : base(name, declaration)
    {
        Visibility = visibility;
    }
}

/// <summary>A name bound from an import whose target module is in the compilation: for a
/// namespace import the module itself, otherwise the imported symbol.</summary>
public sealed class ImportBindingSymbol : Symbol
{
    public Symbol Target { get; }

    public ImportBindingSymbol(string name, Symbol target, Node? declaration) : base(name, declaration)
    {
        Target = target;
    }

    /// <summary>Bound by a <c>pub import</c> (design/v5/spec/07 V3 I4): visible as a 'pub'
    /// declaration of its module, where every other import is its module's own.</summary>
    public bool Reexported { get; set; }
}

/// <summary>An import from a module outside the compilation.
/// Opaque: it prevents "unknown name" errors but carries no structure.</summary>
public sealed class ExternalSymbol : Symbol
{
    public string[] SourcePath { get; } // the module path it comes from

    public ExternalSymbol(string name, string[] sourcePath, Node? declaration) : base(name, declaration)
    {
        SourcePath = sourcePath;
    }
}

/// <summary>Recovery sentinel for names that cannot be resolved.</summary>
public sealed class ErrorSymbol : Symbol
{
    public ErrorSymbol(string name) : base(name, null) { }
}
