namespace Lyric5.Toolchain;

/// <summary>
/// The runtime's collector (design/v5/spec/01 L1; 13 M11 P11): <see cref="Boehm"/>, stage 1, the
/// Boehm–Demers–Weiser collector kept to compare against; <see cref="Immix"/>, the runtime's own
/// (stages 2 and 3). A build compiles the runtime with one of them and the program against it:
/// Boehm's build says <c>LYR_GC_BOEHM</c>, which keeps the allocation functions out of line.
/// </summary>
public enum Collector
{
    Boehm,
    Immix,
}

public static class Collectors
{
    /// <summary>
    /// The collector of a build while M11 grows its own (13 M11 P11): Boehm, unless the
    /// development switch <c>LYRIC5_GC=immix</c> asks for the own one — so the whole suite can run
    /// under it before it is the default. Undocumented; it goes with M11 S5, when the profile
    /// field <c>gc</c> takes its place.
    /// </summary>
    public static Collector Default =>
        Environment.GetEnvironmentVariable("LYRIC5_GC") == "immix" ? Collector.Immix : Collector.Boehm;
}
