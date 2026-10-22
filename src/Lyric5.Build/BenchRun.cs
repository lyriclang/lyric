using System.Text;
using Lyric5.Toolchain;

namespace Lyric5.Build;

/// <summary>One benchmark <c>lyric bench</c> runs: the function, by its module's path and its name.</summary>
public sealed record BenchCase(string Module, string Function)
{
    /// <summary>How the run names it: <c>app.tests.maps.insert</c>.</summary>
    public string Name => $"{Module}.{Function}";
}

/// <summary>
/// <c>lyric bench</c>: every function the package marks
/// <c>@Bench</c> — <c>fn name(&amp;b: Bench)</c>, in its modules under <c>src/</c> and under
/// <c>tests/</c> — built into one program and run, one after another. As <c>lyric test</c> does it
/// (<see cref="TestRun"/>): a module the toolchain writes, which hands the benchmarks to
/// <c>std.test.runBenches</c>; that measures each and writes its line.
/// </summary>
public static class BenchRun
{
    /// <summary>Builds the package's benchmarks — those whose name holds <paramref name="filter"/>,
    /// where one is given — and runs them, what they print passed on as it comes;
    /// <paramref name="save"/> and <paramref name="compare"/> are the files the program writes and
    /// reads, full paths or empty. The answer is the program's exit: 0, or 1 where a file could not
    /// be read or written; a refused program is 1 as well, a build that could not run 2.</summary>
    public static int Run(Project project, BuildProfile profile, Target target, CCompiler compiler, string version, string? filter,
        string save, string compare, TextWriter output, TextWriter error)
    {
        // the scripts first, so that a benchmark may import what they generate
        var (ran, _) = BuildScripts.Run(project, Target.Host, profile, compiler, version, error);
        if (ran != 0) return ran;
        var marked = TestRun.Marked(project, "Bench", error);
        if (marked is null) return 1;
        var benches = marked.Select(m => new BenchCase(m.Module, m.Function.Name)).ToList();
        var chosen = filter is null ? benches : benches.Where(b => b.Name.Contains(filter, StringComparison.Ordinal)).ToList();
        if (chosen.Count == 0)
        {
            output.WriteLine(benches.Count == 0 ? "no benchmarks" : $"no benchmark's name holds '{filter}'");
            return 0;
        }
        var program = project with { EntryText = Driver(chosen, save, compare) };
        return TestRun.BuildAndRun(program, profile, target, compiler, version, output, error);
    }

    /// <summary>The program: each benchmark imported under a name of its own and called from a
    /// lambda that hands on the bench's place, the list handed to <c>std.test.runBenches</c> with the
    /// two files, whose answer is the exit.</summary>
    public static string Driver(IReadOnlyList<BenchCase> benches, string save, string compare)
    {
        var text = new StringBuilder();
        text.Append("// The benchmark program lyric bench writes.\n");
        text.Append("import std.test { BenchCase, runBenches };\n");
        var named = new List<(BenchCase Bench, string Alias)>();
        foreach (var module in benches.GroupBy(b => b.Module))
        {
            var imports = new List<string>();
            foreach (var bench in module)
            {
                var alias = $"bench{named.Count}";
                named.Add((bench, alias));
                imports.Add($"{bench.Function} as {alias}");
            }
            text.Append($"import {module.Key} {{ {string.Join(", ", imports)} }};\n");
        }
        text.Append("\nfn main(): int {\n    return runBenches([\n");
        foreach (var (bench, alias) in named)
            text.Append($"        BenchCase {{ name = {TestRun.Quote(bench.Name)}, body = (&b) => {alias}(&b) }},\n");
        text.Append($"    ], save: {TestRun.Quote(save)}, compare: {TestRun.Quote(compare)});\n}}\n");
        return text.ToString();
    }
}
