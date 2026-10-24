using System.Text;
using System.Text.RegularExpressions;
using Lyric.Core;
using Lyric.Formatting;
using Lyric5.Doc;
using Lyric5.Toolchain;

namespace Lyric5.Tests;

/// <summary>
/// The formatter keeps a program's meaning: every program of the corpus that builds — the example
/// packages and files, the Book's snippets — is emitted as C twice, once as written against the
/// standard library as written, once formatted against the standard library formatted, and the
/// two C texts are the same but for where the source said each thing. Both sides compile in the
/// test's own process — the second against a formatted copy of <c>stdlib5</c> — and start no
/// process of their own.
/// </summary>
public partial class FormatterProofTests
{
    private static readonly string Root = RuntimeLayout.FindRoot(AppContext.BaseDirectory);

    /// <summary>One program: its name, and its files relative to its package.</summary>
    public sealed record Program(string Name, IReadOnlyList<(string Path, string Text)> Files);

    public static IEnumerable<Program> Programs()
    {
        var examples = Path.Combine(Root, "examples");
        foreach (var dir in Directory.GetDirectories(examples).Order(StringComparer.Ordinal))
        {
            if (File.Exists(Path.Combine(dir, "lyric.toml")))
                yield return new Program("examples/" + Path.GetFileName(dir), FilesOf(dir));
        }
        foreach (var file in Directory.GetFiles(examples, "*.lyr").Order(StringComparer.Ordinal))
            yield return new Program("examples/" + Path.GetFileName(file),
                [("lyric.toml", PackageFixture.AppManifest), ("src/main.lyr", File.ReadAllText(file))]);

        var book = Path.Combine(Root, "docs", "book");
        foreach (var chapter in Directory.GetFiles(book, "*.md").Order(StringComparer.Ordinal))
        foreach (var snippet in Snippet.Extract(Path.GetFileName(chapter), File.ReadAllText(chapter)))
            if (snippet.Kind != SnippetKind.CompileFail)
                yield return new Program("book/" + snippet, [("lyric.toml", PackageFixture.AppManifest), ("src/main.lyr", snippet.Program())]);
    }

    private static List<(string, string)> FilesOf(string dir) =>
        Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(dir, f).Replace('\\', '/'))
            .Where(r => !r.Split('/').Any(s => s is "out" or "bin" or "obj" or ".git"))
            .Order(StringComparer.Ordinal)
            .Select(r => (r, File.ReadAllText(Path.Combine(dir, r))))
            .ToList();

    public static TheoryData<string> Names()
    {
        var data = new TheoryData<string>();
        foreach (var p in Programs()) data.Add(p.Name);
        return data;
    }

    /// <summary>A Lyric source formatted, or as it is where it does not parse.</summary>
    public static string Formatted(string text, string name)
    {
        var sm = new SourceManager { Lyric5 = true };
        var id = sm.AddVirtual(name, text);
        return Formatter.Format(sm, id, new DiagnosticEngine(sm)) ?? text;
    }

    /// <summary>The directory of the standard library beside the tests, and that of a copy of it
    /// with every file formatted; each holds a <c>stdlib5</c>.</summary>
    private static readonly Lazy<(string Original, string Formatted)> Compilers = new(() =>
    {
        var copy = TestDirectories.Fresh("formatted-std-");
        var std = Path.Combine(AppContext.BaseDirectory, "stdlib5");
        foreach (var file in Directory.GetFiles(std, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(AppContext.BaseDirectory, file);
            var target = Path.Combine(copy, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (file.EndsWith(".lyr", StringComparison.Ordinal))
                File.WriteAllText(target, Formatted(File.ReadAllText(file), relative));
            else
                File.Copy(file, target);
        }
        return (AppContext.BaseDirectory, copy);
    }, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>The program's C against the <c>stdlib5</c> in <paramref name="compiler"/>;
    /// <c>null</c> where it does not build.</summary>
    private static string? EmitC(string compiler, string package)
    {
        if (Lyric5.Build.Project.ProgramsOf(package) is not [var project, ..]) return null;
        return Lyric5.Build.Pipeline.EmitC(project, Path.Combine(compiler, "stdlib5"), TextWriter.Null);
    }

    [GeneratedRegex(@"^#line .*$\n?", RegexOptions.Multiline)]
    private static partial Regex LineDirective();

    /// <summary>The C without what only says where the source wrote a thing: the <c>#line</c>
    /// directives, and the directories the two sides were built from.</summary>
    internal static string Normalized(string c, params string[] places)
    {
        var text = LineDirective().Replace(c.Replace("\r\n", "\n"), "");
        foreach (var place in places)
            text = text.Replace(place.TrimEnd('/', '\\'), "<place>");
        return text;
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void A_formatted_program_compiles_to_the_same_C(string name)
    {
        var program = Programs().Single(p => p.Name == name);
        var (original, formatted) = Compilers.Value;

        var after = PackageFixture.Package([.. program.Files.Select(f => f.Path.EndsWith(".lyr", StringComparison.Ordinal)
            ? (f.Path, Formatted(f.Text, f.Path)) : f)]);

        var (before, asWritten) = AsWritten(program).Value;
        if (asWritten is null) return; // a 4.x example or a program that does not build as written
        var asFormatted = EmitC(formatted, after);
        Assert.True(asFormatted is not null, $"{name}: the formatted program does not build");

        var a = Normalized(asWritten, before, original);
        var b = Normalized(asFormatted!, after, formatted);
        if (a == b) return;
        var i = 0;
        while (i < a.Length && i < b.Length && a[i] == b[i]) i++;
        string Around(string s) => s.Substring(Math.Max(0, i - 80), Math.Min(160, s.Length - Math.Max(0, i - 80)));
        Assert.Fail($"{name}: the C differs at {i}:\n--- as written\n{Around(a)}\n--- formatted\n{Around(b)}");
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<(string Package, string? C)>> Written = new();

    /// <summary>The program as written, on disk, and its C from the compiler beside the tests —
    /// once per program, for the case and for the count below.</summary>
    private static Lazy<(string Package, string? C)> AsWritten(Program program) =>
        Written.GetOrAdd(program.Name, _ => new Lazy<(string, string?)>(() =>
        {
            var dir = PackageFixture.Package([.. program.Files]);
            return (dir, EmitC(Compilers.Value.Original, dir));
        }, LazyThreadSafetyMode.ExecutionAndPublication));

    /// <summary>
    /// The same comparison over the single-file conformance cases under
    /// <c>LYRIC_FMT_PROOF_CASES</c>, side by side, written as a report to
    /// <c>LYRIC_FMT_PROOF_C_REPORT</c>; without both it does nothing. Too slow for every change,
    /// so a run by hand, for a change of the formatter.
    /// </summary>
    [Fact]
    public void Report_over_the_conformance_cases()
    {
        var cases = Environment.GetEnvironmentVariable("LYRIC_FMT_PROOF_CASES");
        var report = Environment.GetEnvironmentVariable("LYRIC_FMT_PROOF_C_REPORT");
        if (string.IsNullOrEmpty(cases) || string.IsNullOrEmpty(report)) return;

        var (original, formatted) = Compilers.Value;
        var files = Directory.GetDirectories(cases).SelectMany(d => Directory.GetFiles(d, "*.lyr")).Order(StringComparer.Ordinal).ToList();
        var outcomes = new System.Collections.Concurrent.ConcurrentBag<(string Name, string Outcome)>();
        Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, file =>
        {
            var name = Path.GetRelativePath(cases, file).Replace('\\', '/');
            var text = File.ReadAllText(file);
            var before = PackageFixture.Package(("lyric.toml", PackageFixture.AppManifest), ("src/main.lyr", text));
            var asWritten = EmitC(original, before);
            if (asWritten is null) { outcomes.Add((name, "does not build as written")); return; }
            var after = PackageFixture.Package(("lyric.toml", PackageFixture.AppManifest), ("src/main.lyr", Formatted(text, name)));
            var asFormatted = EmitC(formatted, after);
            outcomes.Add((name, asFormatted is null ? "FORMATTED DOES NOT BUILD"
                : Normalized(asWritten, before, original) == Normalized(asFormatted, after, formatted) ? "same C" : "C DIFFERS"));
        });

        var sb = new StringBuilder();
        foreach (var group in outcomes.GroupBy(o => o.Outcome).OrderBy(g => g.Key, StringComparer.Ordinal))
            sb.AppendLine($"{group.Key}: {group.Count()}");
        sb.AppendLine();
        foreach (var (name, outcome) in outcomes.Where(o => o.Outcome is "C DIFFERS" or "FORMATTED DOES NOT BUILD").OrderBy(o => o.Name, StringComparer.Ordinal))
            sb.AppendLine($"- {name}: {outcome}");
        File.WriteAllText(report, sb.ToString());
    }

    [Fact]
    public void The_proof_compares_programs_and_does_not_pass_empty()
    {
        var built = Programs().Count(p => AsWritten(p).Value.C is not null);
        Assert.True(built >= 16, $"only {built} programs of the corpus build as written");
    }
}
