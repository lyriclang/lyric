using Lyric.Compiler;
using Lyric5.Toolchain;

namespace Lyric5.Tests;

/// <summary>
/// The front end as <c>lyric5 build</c> runs it on a package: the text is the package's
/// <c>src/main.lyr</c>, the module <c>app.main</c>, and Lyric 5's module rules hold — names from
/// paths, visibility, the prelude, the entry's contract (design/v5/spec/07).
/// </summary>
/// <remarks>
/// Until the review of 2026-10-05 the tests that compile in memory passed no package roots, and
/// the front end read that as the 4.x module rules: the prelude was all of <c>std.core</c>, a
/// re-export gave nothing on, <c>for</c> over an array took 4.x's way. A test program could do
/// what <c>lyric5 build</c> refuses, and the reverse: the examples of M8a did not compile there
/// and had to run elsewhere. One mode, the one a user meets.
/// </remarks>
internal static class TestCompiler
{
    /// <summary>The tree's standard library, as the toolchain ships it.</summary>
    public static readonly string Stdlib = Path.Combine(RuntimeLayout.FindRoot(AppContext.BaseDirectory), "stdlib5");

    /// <summary>The module every test program is.</summary>
    public const string Module = "app.main";

    /// <summary>The package's source root: a directory that holds nothing, so <c>import app.x</c>
    /// finds no module — a test program is one file.</summary>
    private static readonly string NoSources = Path.Combine(Path.GetTempPath(), "lyric5-tests", "no-sources");

    public static CompilerOptions Options(string? stdlib = null) => new()
    {
        StdlibRoot = stdlib ?? Stdlib,
        PackageRoots = new Dictionary<string, string> { ["app"] = NoSources },
    };

    /// <summary>Source to IR. <paramref name="displayName"/> is what diagnostics, <c>#line</c> and
    /// traces call the file — the same on every machine.</summary>
    public static CompileResult Lower(string displayName, string text, string? stdlib = null) =>
        SourceCompiler.Lower(ScriptSource.FromBuffer(displayName, text, Module), Options(stdlib));
}
