using System.Runtime.CompilerServices;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Ir.Lowering;

/// <summary>
/// How an instance is keyed and named — <c>first&lt;Cat&gt;</c>, <c>Box&lt;Cat&gt;</c>,
/// <c>&lt;extend&gt;.Cat[].head</c> — from the types it is made of.
///
/// <para>By the type, not by its name: two modules may each declare a <c>Cat</c>, and the two are
/// two types (04 §2 rule 9). The display writes a type without its module, and a key built from it
/// made one instance of the two — one layout, one body, the second caller's values in the first
/// one's shape. A type whose name two modules declare is written with its module in front; every
/// other keeps the name it had, so a program without such a pair is named as before.</para>
/// </summary>
internal static class InstanceNames
{
    /// <summary>Per program, the type names more than one module declares.</summary>
    private static readonly ConditionalWeakTable<Compilation, HashSet<string>> Shared = new();

    public static string Of(LyrType type, Compilation? compilation) =>
        compilation is null ? TypeFacts.Display(type) : TypeFacts.Display(type, symbol => NameOf(symbol, compilation));

    private static string NameOf(TypeSymbol symbol, Compilation compilation) =>
        symbol.Home is { } home && Shared.GetValue(compilation, Collect).Contains(symbol.Name)
            ? $"{home.FullName}.{symbol.Name}"
            : symbol.Name;

    private static HashSet<string> Collect(Compilation compilation)
    {
        var once = new HashSet<string>(StringComparer.Ordinal);
        var twice = new HashSet<string>(StringComparer.Ordinal);
        foreach (var module in compilation.Modules)
            foreach (var symbol in module.Members.Symbols)
                if (symbol is TypeSymbol { Kind: not TypeSymbolKind.Builtin } type && ReferenceEquals(type.Home, module)
                    && !once.Add(type.Name))
                    twice.Add(type.Name);
        return twice;
    }
}
