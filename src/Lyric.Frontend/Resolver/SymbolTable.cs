using Lyric.AST;

namespace Lyric.Resolver;

/// <summary>
/// A lexical scope: name to symbol, plus an optional parent for the lookup chain (built-in,
/// module, type member, …). It keeps an insertion-ordered list for deterministic dumps; the
/// dictionary is the O(1) lookup.
/// </summary>
public sealed class SymbolTable
{
    private readonly Dictionary<string, Symbol> _byName = new();
    private readonly List<Symbol> _ordered = new();

    /// <summary>The FURTHER functions of a name, when there are several — the overload set minus
    /// its first member, which lives in <see cref="_byName"/> like any other symbol.
    ///
    /// <para>Held beside rather than inside the main table on purpose: a name with ONE function
    /// behaves exactly as it did before overloading existed, so every lookup that wants a symbol
    /// keeps working and only a CALL — the one site that has argument types to choose by — asks
    /// for the set.</para></summary>
    private Dictionary<string, List<FunctionSymbol>>? _overloads;

    public SymbolTable? Parent { get; }

    public SymbolTable(SymbolTable? parent = null) => Parent = parent;

    public IReadOnlyList<Symbol> Symbols => _ordered;

    /// <summary>Declares a symbol. Returns false when the name is already taken in THIS scope;
    /// the first symbol then stays.
    ///
    /// <para>A FUNCTION beside a function is not a collision but an overload, and is accepted:
    /// the two are told apart by their parameter lists, which the caller checks (LYR-RES0006 for
    /// two that cannot be). Everything else keeps one name, one symbol.</para></summary>
    public bool TryDeclare(Symbol symbol)
    {
        if (_byName.TryGetValue(symbol.Name, out var standing))
        {
            if (FunctionBehind(symbol) is not { } added || FunctionBehind(standing) is null)
                return false;

            _overloads ??= new Dictionary<string, List<FunctionSymbol>>(StringComparer.Ordinal);
            if (!_overloads.TryGetValue(symbol.Name, out var further))
                _overloads[symbol.Name] = further = new List<FunctionSymbol>();
            further.Add(added);
            _ordered.Add(symbol);
            return true;
        }

        _byName[symbol.Name] = symbol;
        _ordered.Add(symbol);
        return true;
    }

    /// <summary>Every function of this name in THIS scope, declaration order, empty when the name
    /// is not a function. One entry is the ordinary case and is not an overload set.</summary>
    public IReadOnlyList<FunctionSymbol> OverloadsLocal(string name)
    {
        if (_byName.TryGetValue(name, out var first) && FunctionBehind(first) is { } fn)
        {
            if (_overloads is null || !_overloads.TryGetValue(name, out var further)) return [fn];
            return [fn, .. further];
        }
        return [];
    }

    /// <summary>The function a symbol stands for: itself, or what an import binding points at.
    ///
    /// <para>An import is a name for something declared elsewhere, and an overload set imported by
    /// name is still a set — one binding per member, all under the one name, exactly as they stand
    /// in the module they come from.</para></summary>
    private static FunctionSymbol? FunctionBehind(Symbol symbol) => symbol switch
    {
        FunctionSymbol fn => fn,
        ImportBindingSymbol { Target: FunctionSymbol imported } => imported,
        _ => null,
    };

    /// <summary>The function symbol of ONE declaration, among the overloads of its name.
    ///
    /// <para>What a by-name lookup cannot answer once two functions share a name: it gives the
    /// first, so a pass walking DECLARATIONS would hand the second one's body to the first one's
    /// symbol. Every such walk asks this instead.</para></summary>
    public FunctionSymbol? FunctionFor(string name, Node declaration)
    {
        foreach (var candidate in OverloadsLocal(name))
            if (ReferenceEquals(candidate.Declaration, declaration))
                return candidate;
        return null;
    }

    /// <summary>Every function of this name, this scope first, then up the parent chain — but from
    /// ONE scope only: an inner declaration hides an outer one whole, as it always did. Overloading
    /// works within a scope, never across two.</summary>
    public IReadOnlyList<FunctionSymbol> Overloads(string name)
    {
        for (var scope = this; scope is not null; scope = scope.Parent)
            if (scope._byName.ContainsKey(name))
                return scope.OverloadsLocal(name);
        return [];
    }

    /// <summary>This scope only, without the parent chain.</summary>
    public Symbol? LookupLocal(string name) =>
        _byName.TryGetValue(name, out var s) ? s : null;

    /// <summary>This scope, then up the parent chain.</summary>
    public Symbol? Lookup(string name)
    {
        for (var scope = this; scope is not null; scope = scope.Parent)
            if (scope._byName.TryGetValue(name, out var s))
                return s;
        return null;
    }
}
