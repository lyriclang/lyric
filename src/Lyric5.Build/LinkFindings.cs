using System.Text.RegularExpressions;
using Lyric.Compiler;
using Lyric.Core;
using Lyric5.Toolchain;

namespace Lyric5.Build;

/// <summary>What a link finding is about.</summary>
public enum LinkFindingKind
{
    /// <summary>A symbol nothing the link takes defines (<c>LYR-LNK0001</c>).</summary>
    Undefined,

    /// <summary>A library by name the linker does not find (<c>LYR-LNK0002</c>).</summary>
    MissingLibrary,

    /// <summary>A macOS framework the linker does not find (<c>LYR-LNK0002</c>).</summary>
    MissingFramework,

    /// <summary>A symbol two inputs define (<c>LYR-LNK0003</c>).</summary>
    Duplicate,
}

/// <summary>One finding read from the linker's text: the symbol or the library, and the places the
/// linker gave — what references the symbol, or each input that defines it, as the linker wrote them.</summary>
public sealed record LinkFinding(LinkFindingKind Kind, string Name, IReadOnlyList<string> Places);

/// <summary>
/// The linker's text, read for the findings of spec 15 §5.2 rule 12: lld (<c>ld.lld</c>,
/// <c>lld-link</c>) and zig's own Mach-O linker, which <c>zig cc</c> drives, and Apple's ld64 and GNU
/// ld, which clang and gcc drive. A line it does not know is left over, and the link's failure is
/// then said as it is besides (<c>LYR-BLD0001</c>) — the reading never guesses.
/// </summary>
public static partial class LinkerText
{
    /// <summary>The findings in <paramref name="text"/>, each once, and whether an error is left that
    /// none of them says. A Mach-O symbol is named as C names it, without the underscore before it.</summary>
    public static (IReadOnlyList<LinkFinding> Findings, bool LeftOver) Read(string text, Target target)
    {
        var findings = new List<LinkFinding>();
        var rest = false;
        var lines = text.Replace("\r\n", "\n").Split('\n');
        string Symbol(string name) => target.Os == TargetOs.MacOs && name.StartsWith('_') ? name[1..] : name;
        void Add(LinkFindingKind kind, string name, List<string> places)
        {
            var same = findings.FindIndex(f => f.Kind == kind && f.Name == name);
            if (same < 0) findings.Add(new LinkFinding(kind, name, places));
            else findings[same] = findings[same] with { Places = [.. findings[same].Places, .. places.Where(p => !findings[same].Places.Contains(p))] };
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd();
            Match m;
            // lld: the finding, then '>>>' lines; zig's Mach-O linker: the finding, then 'note:' lines
            if ((m = LldUndefined().Match(line)).Success)
                Add(LinkFindingKind.Undefined, Symbol(m.Groups[1].Value), Follow(lines, ref i, "referenced by "));
            else if ((m = LldDuplicate().Match(line)).Success)
                Add(LinkFindingKind.Duplicate, Symbol(m.Groups[1].Value), Definers(lines, ref i));
            else if ((m = ZigLibrary().Match(line)).Success || (m = LldLibrary().Match(line)).Success
                     || (m = Ld64Library().Match(line)).Success || (m = GnuLibrary().Match(line)).Success)
                Add(LinkFindingKind.MissingLibrary, m.Groups[1].Value, []);
            else if ((m = Framework().Match(line)).Success)
                Add(LinkFindingKind.MissingFramework, m.Groups[1].Value, []);
            // ld64: a block of '"_sym", referenced from:' after its head, and 'duplicate symbol' with the inputs below
            else if (line.StartsWith("Undefined symbols for architecture", StringComparison.Ordinal))
            {
                while (i + 1 < lines.Length && Ld64Undefined().Match(lines[i + 1]) is { Success: true } u)
                {
                    i++;
                    var places = new List<string>();
                    while (i + 1 < lines.Length && lines[i + 1].StartsWith("      ", StringComparison.Ordinal)) places.Add(lines[++i].Trim());
                    Add(LinkFindingKind.Undefined, Symbol(u.Groups[1].Value), places);
                }
            }
            else if ((m = Ld64Duplicate().Match(line)).Success)
            {
                var places = new List<string>();
                while (i + 1 < lines.Length && lines[i + 1].StartsWith("    ", StringComparison.Ordinal)) places.Add(lines[++i].Trim());
                Add(LinkFindingKind.Duplicate, Symbol(m.Groups[1].Value), places);
            }
            // GNU ld: one line each, the place before the finding
            else if ((m = GnuUndefined().Match(line)).Success)
                Add(LinkFindingKind.Undefined, m.Groups[2].Value, [m.Groups[1].Value.Trim()]);
            else if ((m = GnuDuplicate().Match(line)).Success)
                Add(LinkFindingKind.Duplicate, m.Groups[2].Value, [m.Groups[1].Value.Trim(), m.Groups[3].Value.Trim()]);
            // what drives the linker says it failed, which the findings have said already
            else if (Wrapper().IsMatch(line)) { }
            else if (line.Contains("error", StringComparison.OrdinalIgnoreCase)) rest = true;
        }
        return (findings, rest);
    }

    /// <summary>The lines after a finding that belong to it — lld's <c>&gt;&gt;&gt;</c>, zig's indented
    /// <c>note:</c> —, each the text after <paramref name="lead"/> where it has it.</summary>
    private static List<string> Follow(string[] lines, ref int i, string lead)
    {
        var places = new List<string>();
        while (i + 1 < lines.Length && Continuation(lines[i + 1]) is { } text)
        {
            i++;
            if (text.StartsWith(lead, StringComparison.Ordinal)) places.Add(text[lead.Length..].Trim());
        }
        return places;
    }

    /// <summary>The inputs that define a duplicate symbol: one place for each <c>defined at</c> or
    /// <c>defined by</c>, with the lines after it that say the same input more nearly (the object,
    /// the archive it came from).</summary>
    private static List<string> Definers(string[] lines, ref int i)
    {
        var places = new List<string>();
        while (i + 1 < lines.Length && Continuation(lines[i + 1]) is { } text)
        {
            i++;
            if (text.StartsWith("defined at ", StringComparison.Ordinal)) places.Add(text["defined at ".Length..].Trim());
            else if (text.StartsWith("defined by ", StringComparison.Ordinal)) places.Add(text["defined by ".Length..].Trim());
            else if (places.Count > 0) places[^1] += "\n" + text.Trim();
        }
        return places;
    }

    /// <summary>A line that continues the finding before it, without its lead.</summary>
    private static string? Continuation(string line)
    {
        var trimmed = line.TrimStart();
        if (trimmed.StartsWith(">>>", StringComparison.Ordinal)) return trimmed[3..].Trim();
        if (line.StartsWith(' ') && trimmed.StartsWith("note: ", StringComparison.Ordinal)) return trimmed["note: ".Length..].Trim();
        return null;
    }

    [GeneratedRegex(@"error: undefined symbol: (\S+)$")]
    private static partial Regex LldUndefined();

    [GeneratedRegex(@"error: duplicate symbol(?: definition)?: (\S+)$")]
    private static partial Regex LldDuplicate();

    [GeneratedRegex(@"unable to find (?:dynamic|static) system library '([^']+)'")]
    private static partial Regex ZigLibrary();

    [GeneratedRegex(@"error: unable to find library -l(\S+)")]
    private static partial Regex LldLibrary();

    /// <summary>ld64, the older form and the newer.</summary>
    [GeneratedRegex(@"^ld: library (?:not found for -l|')([^'\s]+)")]
    private static partial Regex Ld64Library();

    [GeneratedRegex(@"cannot find -l([^\s:]+)")]
    private static partial Regex GnuLibrary();

    /// <summary>ld64, the older form and the newer, and zig's.</summary>
    [GeneratedRegex(@"(?:framework not found |framework '|unable to find framework ')([^\s']+)")]
    private static partial Regex Framework();

    [GeneratedRegex(@"^\s+""(\S+)"", referenced from:$")]
    private static partial Regex Ld64Undefined();

    [GeneratedRegex(@"^(?:ld: )?duplicate symbol '(\S+)' in:$")]
    private static partial Regex Ld64Duplicate();

    [GeneratedRegex(@"^(.*): undefined reference to [`']([^']+)'$")]
    private static partial Regex GnuUndefined();

    [GeneratedRegex(@"^(.*): multiple definition of [`']([^']+)'; (.*): first defined here$")]
    private static partial Regex GnuDuplicate();

    [GeneratedRegex(@"linker command failed|collect2: error: ld returned|too many errors emitted|ld returned \d+ exit status|^error: link(?:ing)? failed|^ld: symbol\(s\) not found|^ld: \d+ duplicate symbols?")]
    private static partial Regex Wrapper();
}

/// <summary>Where a library by name or a framework was first named: a key of a package's manifest
/// (<c>libs</c>, <c>frameworks</c>), its build script (<c>script</c>), or what pkg-config gave it
/// (<c>pkgConfig</c>).</summary>
public sealed record LinkOrigin(Manifest Manifest, string Key);

/// <summary>What a link knew of its inputs: whose each object, archive and shared library is, by its
/// path — a package's name, <c>the program</c>, <c>the runtime</c> —, and where each library and
/// framework was named.</summary>
public sealed record LinkSite(
    IReadOnlyDictionary<string, string> Owners,
    IReadOnlyDictionary<string, LinkOrigin> Libraries,
    IReadOnlyDictionary<string, LinkOrigin> Frameworks)
{
    /// <summary>Whose input a place of the linker's text names: a path it holds whole, else a file
    /// name only one owner has, else a path the packages' sources are named by (<c>geo/native/a.c</c>,
    /// spec 15 §5.1). <c>null</c> where none fits.</summary>
    public string? OwnerOf(string place)
    {
        foreach (var (path, owner) in Owners.OrderByDescending(o => o.Key.Length))
            if (place.Contains(path, StringComparison.Ordinal)) return owner;
        var named = Owners.GroupBy(o => Path.GetFileName(o.Key), StringComparer.Ordinal)
            .Where(g => g.Select(o => o.Value).Distinct().Count() == 1)
            .FirstOrDefault(g => Regex.IsMatch(place, $@"(^|[\s/\\(]){Regex.Escape(g.Key)}($|[\s:)(])"));
        if (named is not null) return named.First().Value;
        var packages = Owners.Values.Where(o => o is not ("the program" or "the runtime")).ToHashSet();
        var first = place.Split([' ', '/', '\\'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return first is not null && packages.Contains(first) && place.Contains(first + "/", StringComparison.Ordinal) ? first : null;
    }
}

/// <summary>
/// A failed link said as its findings (spec 15 §5.2 rule 12, 16 §1): a symbol nothing defines at the
/// <c>extern</c> declaration that names it (<c>LYR-LNK0001</c>), a library the linker does not find
/// at the line of the manifest that names it (<c>LYR-LNK0002</c>), a symbol defined twice with the
/// packages that define it (<c>LYR-LNK0003</c>). An error of the linker's that none of them says is
/// said besides, as it is (<c>LYR-BLD0001</c>).
/// </summary>
public static class LinkReport
{
    private const string WhereToName =
        "name the library that defines it in the package's [native] — 'libs', 'archives', 'frameworks' or 'pkgConfig' — or link it from the build script";

    /// <param name="frontEnd">The front end's answer, run again where an up-to-date build did not run it.</param>
    public static void Say(string linker, string failure, Target target, LinkSite site, Func<CompileResult?> frontEnd, TextWriter error)
    {
        var (findings, leftOver) = LinkerText.Read(linker, target);
        CompileResult? compiled = null;
        var asked = false;
        foreach (var finding in findings)
        {
            switch (finding.Kind)
            {
                case LinkFindingKind.Undefined:
                    if (!asked) { compiled = frontEnd(); asked = true; }
                    Undefined(finding, site, compiled, error);
                    break;
                case LinkFindingKind.MissingLibrary:
                case LinkFindingKind.MissingFramework:
                    Missing(finding, site, error);
                    break;
                case LinkFindingKind.Duplicate:
                    Duplicate(finding, site, error);
                    break;
            }
        }
        if (findings.Count == 0 || leftOver) Build.Say.Error(error, "LYR-BLD0001", failure);
    }

    private static void Undefined(LinkFinding finding, LinkSite site, CompileResult? compiled, TextWriter error)
    {
        var message = $"nothing the link takes defines '{finding.Name}'";
        var declared = compiled?.Ir?.Imports.FirstOrDefault(i => i.Name == "C:" + finding.Name && i.Declared is not null);
        if (compiled is not null && declared is { Declared: { } span })
        {
            Build.Say.At(error, compiled.Sources, new Diagnostic("LYR-LNK0001", Severity.Error, span, message,
                [new DiagnosticNote(WhereToName) { Kind = NoteKind.Help }]) { PrimaryLabel = "declared here, and defined nowhere the link looks" });
            return;
        }
        var referenced = finding.Places.Select(p => new DiagnosticNote(site.OwnerOf(p) is { } owner ? $"referenced by {owner}: {p}" : $"referenced by {p}"));
        Build.Say.ErrorWithNotes(error, "LYR-LNK0001", message,
            [.. referenced, new DiagnosticNote(WhereToName) { Kind = NoteKind.Help }]);
    }

    private static void Missing(LinkFinding finding, LinkSite site, TextWriter error)
    {
        var framework = finding.Kind == LinkFindingKind.MissingFramework;
        var message = $"the linker finds no {(framework ? "framework" : "library")} '{finding.Name}'";
        var help = framework
            ? "a framework is the macOS SDK's: is its name written as the SDK has it?"
            : "install its development package, or name the directory that holds it — pkg-config says both ('pkgConfig'), a build script 'linkSearch'";
        if ((framework ? site.Frameworks : site.Libraries).GetValueOrDefault(finding.Name) is not { } origin)
        {
            Build.Say.Error(error, "LYR-LNK0002", message, help);
            return;
        }
        if (origin.Key is "libs" or "frameworks" && Line(origin.Manifest.File, origin.Key, finding.Name) is { } at)
        {
            Build.Say.At(error, at.Sources, new Diagnostic("LYR-LNK0002", Severity.Error, at.Span, message,
                [new DiagnosticNote(help) { Kind = NoteKind.Help }]) { PrimaryLabel = $"{origin.Manifest.Name} names it here" });
            return;
        }
        var by = origin.Key switch
        {
            "pkgConfig" => $"pkg-config named it for {origin.Manifest.Name}'s 'pkgConfig'",
            "script" => $"{origin.Manifest.Name}'s build script named it",
            _ => $"{origin.Manifest.Name}'s '{origin.Key}' names it",
        };
        Build.Say.ErrorWithNotes(error, "LYR-LNK0002", message, [new DiagnosticNote(by), new DiagnosticNote(help) { Kind = NoteKind.Help }]);
    }

    private static void Duplicate(LinkFinding finding, LinkSite site, TextWriter error)
    {
        var owners = finding.Places.Select(p => site.OwnerOf(p) ?? p).Distinct().ToList();
        var message = owners.Count switch
        {
            0 => $"'{finding.Name}' is defined twice",
            1 => $"'{finding.Name}' is defined twice by {owners[0]}",
            _ => $"'{finding.Name}' is defined by {string.Join(" and by ", owners)}",
        };
        Build.Say.ErrorWithNotes(error, "LYR-LNK0003", message,
            [.. finding.Places.Select(p => new DiagnosticNote($"defined at {p.Split('\n')[0]}")),
             new DiagnosticNote("a C symbol is defined once in a program: rename one of them, make one 'static', or leave one of the packages out") { Kind = NoteKind.Help }]);
    }

    /// <summary>The place of <paramref name="name"/> in the manifest's <paramref name="key"/> array —
    /// the first array of that key that holds it, whichever table it stands in.</summary>
    private static (SourceManager Sources, Span Span)? Line(string manifest, string key, string name)
    {
        try
        {
            var sources = new SourceManager();
            var file = sources.AddFromDisk(manifest);
            var text = sources.GetText(file);
            foreach (Match m in Regex.Matches(text, $@"(?m)^\s*{key}\s*=\s*\["))
            {
                var end = text.IndexOf(']', m.Index);
                var at = text.IndexOf($"\"{name}\"", m.Index, StringComparison.Ordinal);
                if (at >= 0 && (end < 0 || at < end)) return (sources, new Span(file, at, at + name.Length + 2));
            }
        }
        catch (Exception unreadable) when (unreadable is IOException or UnauthorizedAccessException) { }
        return null;
    }
}
