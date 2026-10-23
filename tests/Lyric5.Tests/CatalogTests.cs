using Lyric.Core;
using System.Text.RegularExpressions;
using Lyric5.Build;
using Lyric5.Toolchain;
using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// The diagnostic catalog against the compiler: every code the
/// sources report stands in it (a), every code in it is reported or retired (b), each entry's programs
/// report what it says under its severity (c), and the messages they bring out begin in lowercase and
/// end without a period (e). (d) — a red and a green conformance case per warning and lint — comes
/// with the lints.
/// </summary>
[Collection("console")]
public partial class CatalogTests
{
    private static readonly string Root = RuntimeLayout.FindRoot(AppContext.BaseDirectory);

    [GeneratedRegex(@"""(LYR-[A-Z]+[0-9]{4})""|\[(LYR-[A-Z]+[0-9]{4})\]")]
    private static partial Regex CodeInSource();

    [GeneratedRegex(@"^(error|warning|hint)\[(LYR-[A-Z]+[0-9]{4})\]: (.*)$", RegexOptions.Multiline)]
    private static partial Regex Head();

    [GeneratedRegex(@"panic \[(LYR-RT[0-9]{4})\]")]
    private static partial Regex Panic();

    /// <summary>The codes the toolchain's sources write — a string <c>"LYR-…"</c> or a head
    /// <c>[LYR-…]</c> outside a comment —, with where. The runtime's C writes its panic codes the same way.</summary>
    internal static Dictionary<string, List<string>> CodesInSources()
    {
        var found = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var files = Directory.EnumerateFiles(Path.Combine(Root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Concat(Directory.EnumerateFiles(Path.Combine(Root, "runtime"), "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".c", StringComparison.Ordinal) || f.EndsWith(".h", StringComparison.Ordinal)));
        foreach (var file in files)
        {
            var number = 0;
            foreach (var line in File.ReadLines(file))
            {
                number++;
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("//") || trimmed.StartsWith("/*") || trimmed.StartsWith("*")) continue;
                foreach (Match m in CodeInSource().Matches(line))
                {
                    var code = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
                    if (!found.TryGetValue(code, out var at)) found[code] = at = [];
                    at.Add($"{Path.GetRelativePath(Root, file)}:{number}");
                }
            }
        }
        return found;
    }

    [Fact]
    public void Every_code_the_sources_report_stands_in_the_catalog()
    {
        var missing = CodesInSources().Where(c => !DiagnosticCatalog.Entries.ContainsKey(c.Key))
            .Select(c => $"{c.Key} ({c.Value[0]})").Order().ToList();
        Assert.True(missing.Count == 0, "not in diagnostics.toml:\n" + string.Join("\n", missing));
    }

    [Fact]
    public void Every_code_of_the_catalog_is_reported_unless_retired()
    {
        var sources = CodesInSources();
        var silent = DiagnosticCatalog.Entries.Values.Where(e => e.Retired is null && !sources.ContainsKey(e.Code) && !NoTriggerYet.ContainsKey(e.Code))
            .Select(e => e.Code).ToList();
        Assert.True(silent.Count == 0, "listed, reported nowhere — retire them:\n" + string.Join("\n", silent));
        var reported = DiagnosticCatalog.Entries.Values
            .Where(e => e.Retired is not null && sources.ContainsKey(e.Code) && !StillInTheSharedFrontend.ContainsKey(e.Code))
            .Select(e => $"{e.Code} ({sources[e.Code][0]})").ToList();
        Assert.True(reported.Count == 0, "retired, reported still:\n" + string.Join("\n", reported));
        // the list shrinks as the sites go: an entry is retired, and its site is still there
        foreach (var code in StillInTheSharedFrontend.Keys)
        {
            Assert.True(DiagnosticCatalog.Entries[code].Retired is not null, $"{code} is listed as retired for Lyric 5 and is not");
            Assert.True(sources.ContainsKey(code), $"{code}'s site is gone: take it off the list");
        }
    }

    /// <summary>Lints named before anything can set them off: listed, so that a level can name them,
    /// and reported nowhere yet.</summary>
    private static readonly Dictionary<string, string> NoTriggerYet = new(StringComparer.Ordinal)
    {
        ["LYR-SEM0214"] = "deprecated-edition: there is no edition yet to retire a construct",
    };

    /// <summary>
    /// Codes retired for Lyric 5 whose sites stay in source: the front end still serves the 4.x
    /// semantics its own suites test (`stdlib/`, the Sema and Ir suites — until M17 replaces it), the
    /// language server reads the 4.x <c>lyric.json</c> (until M13b), and a single-item parse reports
    /// into an engine of its own. No <c>lyric5</c> program reaches one of them (M12a S3, each checked).
    /// </summary>
    private static readonly Dictionary<string, string> StillInTheSharedFrontend = new(StringComparer.Ordinal)
    {
        ["LYR-PAR0001"] = "the single-item parse of std.syntax and a macro's read-back reports into its own engine",
        ["LYR-RES0006"] = "a module header that disagrees with its path: behind !Lyric5Modules",
        ["LYR-SEM0061"] = "constructing a host type: the 4.x native modules",
        ["LYR-SEM0066"] = "an attribute value that is not a literal: the 4.x attribute rows",
        ["LYR-SEM0069"] = "an attribute field without a value: the 4.x attribute rows",
        ["LYR-SEM0090"] = "a range value without std.core: lyric5 always loads it",
        ["LYR-SEM0091"] = "walking optional elements without the protocol: lyric5's std.core has the protocol",
        ["LYR-SEM0093"] = "making an opaque type's value by 'as': refused first by SEM0175 in Lyric 5",
        ["LYR-SEM0095"] = "'WithArg': the 4.x stdlib's",
        ["LYR-SEM0096"] = "an attribute that cannot form a row: the 4.x attribute rows",
        ["LYR-CLI0010"] = "a lyric.json that cannot be read: the language server's 4.x project file",
        ["LYR-CLI0018"] = "a lyric.json that asks for a newer toolchain: the same",
    };

    [Fact]
    public void The_catalog_is_in_code_order_and_its_titles_read_as_titles()
    {
        var codes = DiagnosticCatalog.Entries.Keys.ToList();
        var text = File.ReadAllText(Path.Combine(Root, "src", "Lyric.Core", "diagnostics.toml"));
        var order = DiagnosticCatalog.Read(text).Select(e => e.Code).ToList();
        Assert.Equal(order.Order(StringComparer.Ordinal), order);
        Assert.Equal(codes.Count, order.Count);
        foreach (var entry in DiagnosticCatalog.Entries.Values)
        {
            Assert.False(entry.Title.EndsWith('.'), $"{entry.Code}: the title ends with a period");
            Assert.False(char.IsUpper(entry.Title[0]) && !entry.Title.StartsWith("LYR", StringComparison.Ordinal),
                $"{entry.Code}: the title begins in uppercase");
        }
    }

    public static TheoryData<string> Checked() =>
        new(DiagnosticCatalog.Entries.Values.Where(e => e.Wrong is not null || (e.Program is not null && e.Severity != "panic"))
            .Select(e => e.Code));

    public static TheoryData<string> Panics() =>
        new(DiagnosticCatalog.Entries.Values.Where(e => e.Program is not null && e.Severity == "panic").Select(e => e.Code));

    /// <summary>A catalog program as a package: <c>//- path</c> lines start files, else the text is
    /// <c>src/main.lyr</c>; the manifest is written where the program brings none.</summary>
    internal static string Materialize(string program)
    {
        var files = new List<(string, string)>();
        if (program.Split('\n').Any(l => l.StartsWith("//- ", StringComparison.Ordinal)))
        {
            string? path = null;
            var body = new List<string>();
            foreach (var line in program.Split('\n').Append("//- "))
            {
                if (!line.StartsWith("//- ", StringComparison.Ordinal)) { body.Add(line); continue; }
                if (path is not null) files.Add((path, string.Join("\n", body) + "\n"));
                path = line[4..].Trim();
                body.Clear();
            }
        }
        else files.Add(("src/main.lyr", program + "\n"));
        if (!files.Any(f => f.Item1 == "lyric.toml")) files.Add(("lyric.toml", AppManifest));
        return Package([.. files]);
    }

    private static List<(string Severity, string Code, string Message)> Reported(string program)
    {
        var (_, _, error) = RunRaw("build", "-C", Materialize(program), "--emit", "ir", "--color", "never", "--max-errors", "0");
        return Head().Matches(error).Select(m => (m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value)).ToList();
    }

    /// <summary>(e): a message begins in lowercase — a quote, a backtick or a digit may begin it — and
    /// ends without a period.</summary>
    private static void HouseStyle(string code, string message)
    {
        Assert.False(message.Length > 0 && char.IsUpper(message[0]), $"{code}: the message begins in uppercase: {message}");
        Assert.False(message.EndsWith('.') && !message.EndsWith("..", StringComparison.Ordinal),
            $"{code}: the message ends with a period: {message}");
    }

    [Theory]
    [MemberData(nameof(Checked))]
    public void The_entrys_programs_report_what_it_says(string code)
    {
        var entry = DiagnosticCatalog.Entries[code];
        if (entry.Wrong is { } wrong)
        {
            var said = Reported(wrong);
            var mine = said.Where(d => d.Code == code).ToList();
            Assert.True(mine.Count > 0, $"{code}: 'wrong' does not report it; it reports {string.Join(", ", said.Select(d => d.Code))}");
            Assert.All(mine, d => Assert.Equal(entry.Severity, d.Severity));
            foreach (var d in mine) HouseStyle(code, d.Message);
            if (entry.Severity == "error")
            {
                var others = said.Where(d => d.Severity == "error" && d.Code != code).Select(d => d.Code).Distinct().ToList();
                Assert.True(others.Count == 0, $"{code}: 'wrong' reports other errors too: {string.Join(", ", others)}");
            }
            else Assert.DoesNotContain(said, d => d.Severity == "error");
            var right = Reported(entry.Right!);
            Assert.True(right.Count == 0, $"{code}: 'right' is not clean: {string.Join(", ", right.Select(d => d.Code))}");
        }
        else
        {
            var said = Reported(entry.Program!);
            var mine = said.Where(d => d.Code == code).ToList();
            Assert.True(mine.Count > 0, $"{code}: 'program' does not report it; it reports {string.Join(", ", said.Select(d => d.Code))}");
            Assert.All(mine, d => Assert.Equal(entry.Severity, d.Severity));
            foreach (var d in mine) HouseStyle(code, d.Message);
            Assert.DoesNotContain(said, d => d.Severity == "error");
        }
    }

    [Theory]
    [MemberData(nameof(Panics))]
    public void The_entrys_program_panics_with_its_code(string code)
    {
        var root = Materialize(DiagnosticCatalog.Entries[code].Program!);
        var (exit, _, error) = RunRaw("build", "-C", root, "--color", "never");
        Assert.True(exit == 0, error);
        var host = Target.Host;
        var exe = Path.Combine(root, "out", "debug", host.Triple, "app" + host.ExecutableSuffix);
        var ran = ProcessRunner.Run(exe, [], TimeSpan.FromMinutes(1));
        Assert.Equal(101, ran.ExitCode);
        Assert.Equal(code, Panic().Match(ran.Stderr).Groups[1].Value);
    }

    [Fact]
    public void Explain_prints_the_entry_and_refuses_a_code_it_does_not_hold()
    {
        var (exit, output, _) = RunRaw("explain", "sem0001");
        Assert.Equal(0, exit);
        Assert.StartsWith("LYR-SEM0001: ", output);
        Assert.Contains("\nWrong:\n\n    fn main(): void {\n", output);
        Assert.Contains("\nRight:\n\n", output);
        var version = RunRaw("--version").Item2.Split('\n')[0]["lyric ".Length..].Trim();
        Assert.EndsWith($"\n\nOn the web: {DiagnosticCatalog.PageUrl("LYR-SEM0001", version)}\n", output.ReplaceLineEndings("\n"));
        var (refused, _, error) = RunRaw("explain", "LYR-XYZ9999");
        Assert.Equal(2, refused);
        Assert.Contains("error[LYR-CLI0003]: no code or lint 'LYR-XYZ9999' in the catalog", error);
    }
}
