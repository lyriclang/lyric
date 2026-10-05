using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Ir.Lowering;

/// <summary>
/// How an instance is keyed and named — <c>first&lt;app.main.Cat&gt;</c>,
/// <c>Box&lt;app.main.Cat&gt;</c>, <c>&lt;extend&gt;.app.main.Cat[].head</c> — from the types it
/// is made of.
///
/// <para>By the type, not by its name: two modules may each declare a <c>Cat</c>, and the two are
/// two types (04 §2 rule 9). The display writes a type without its module, and a key built from it
/// made one instance of the two — one layout, one body, the second caller's values in the first
/// one's shape.</para>
///
/// <para>A declared type is written with its module in front, always (07 K2: "generic instances
/// carry the full type path"). It was written so only where two modules declared the name, and
/// then a second <c>Cat</c> anywhere in the program renamed every instance over the first: its
/// frames in a trace, its unit in the cache, its names in the C. A name that depends on nothing
/// but the instance is the same in every program that has it.</para>
/// </summary>
internal static class InstanceNames
{
    public static string Of(LyrType type, Compilation? compilation) =>
        compilation is null ? TypeFacts.Display(type) : TypeFacts.Display(type, Qualified);

    /// <summary>
    /// An instance of a declared generic type as its entry in the type table is NAMED: the
    /// definition plain and the arguments in full — <c>Box&lt;app.main.Cat&gt;</c>. The entry
    /// carries its module beside the name, and the two together are <see cref="Of"/>, the key.
    /// </summary>
    public static string Entry(TypeSymbol definition, IReadOnlyList<LyrType> arguments, Compilation? compilation) =>
        $"{definition.Name}<{string.Join(", ", arguments.Select(a => Of(a, compilation)))}>";

    private static string Qualified(TypeSymbol symbol) =>
        symbol.Home is { } home ? $"{home.FullName}.{symbol.Name}" : symbol.Name;
}
