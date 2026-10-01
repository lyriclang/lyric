namespace Lyric5.Compiler;

/// <summary>
/// The natively backed functions of <c>stdlib5/</c> and the runtime call each becomes (M2 S3).
/// A provisional table with a date: M8a rewrites <c>std</c> in Lyric, source-first, and what the
/// runtime keeps in C (01 L9) is reached through the C ABI from then on.
/// </summary>
public static class Intrinsics
{
    private static readonly IReadOnlyDictionary<string, string> Runtime = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["std.io.print"] = "lyr_print",
        ["std.io.println"] = "lyr_println",
        ["std.string.concat"] = "lyr_str_concat",
        ["std.string.fromInt"] = "lyr_str_from_int",
        ["std.string.fromUint"] = "lyr_str_from_uint",
        ["std.string.fromBool"] = "lyr_str_from_bool",
        ["std.string.fromFloat"] = "lyr_str_from_float",
        ["std.string.fromChar"] = "lyr_str_from_char",
        ["std.core.panic"] = "lyr_panic_message",
        // The catalogue (05 E8): 'assert' a check at the call, so its trace starts in the program;
        // the two that never return are panics of their own codes.
        ["std.core.assert"] = "LYR_ASSERT",
        ["std.core.unreachable"] = "lyr_panic_unreachable",
        ["std.core.todo"] = "lyr_panic_todo",
    };

    /// <summary>The names the <see cref="SubsetGate"/> lets through as <c>CallImport</c>.</summary>
    public static IReadOnlySet<string> Names { get; } = Runtime.Keys.ToHashSet(StringComparer.Ordinal);

    /// <summary>The C call for a native function with the given argument expressions.</summary>
    public static string Call(string name, string[] arguments) =>
        $"{Runtime[name]}({string.Join(", ", arguments)})";
}
