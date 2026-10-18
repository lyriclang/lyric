namespace Lyric5.Toolchain;

/// <summary>
/// The runtime's collector (design/v5/spec/01 L1; 13 M11 P11), a profile's field <c>gc</c>:
/// <see cref="Immix"/>, the runtime's own (stages 2 and 3), the default; <see cref="Boehm"/>, stage 1,
/// the Boehm–Demers–Weiser collector kept to compare against. A build compiles the runtime with one
/// of them and the program against it: Boehm's build says <c>LYR_GC_BOEHM</c>, which keeps the
/// allocation functions out of line.
/// </summary>
public enum Collector
{
    Immix,
    Boehm,
}

public static class Collectors
{
    /// <summary>The field's value for a collector: <c>"immix"</c>, <c>"boehm"</c>.</summary>
    public static string Name(this Collector collector) => collector == Collector.Boehm ? "boehm" : "immix";

    /// <summary>The collector a field's value names, or <c>null</c>.</summary>
    public static Collector? Parse(string name) => name switch
    {
        "immix" => Collector.Immix,
        "boehm" => Collector.Boehm,
        _ => null,
    };
}
