using Lyric4.AST;
using Lyric4.Core;

namespace Lyric4.From4;

/// <summary>What a rule says beside a change it makes: <see cref="Review"/> — the change is a
/// proposal a person decides (12 R2, art R); <see cref="Hand"/> — no change, only the place and what
/// to do (art H).</summary>
public enum NoteKind { Review, Hand }

/// <summary>A replacement in one of the program's files: the text from <paramref name="Start"/> to
/// <paramref name="End"/> becomes <paramref name="Text"/>.</summary>
public sealed record Edit(string Path, int Start, int End, string Text, string Rule);

/// <summary>A note of a rule: its code (<c>LYR-MIG…</c>), the place, what changed or has to, and
/// what to write instead where there is a proposal.</summary>
public sealed record Note(string Rule, string Code, NoteKind Kind, string Path, int Line, int Column, string Message, string? Suggestion);

/// <summary>
/// The outcome of the rules over one program: the edits to its files, the notes, the files to write
/// anew (<c>lyric.toml</c>) and the ones to remove (<c>lyric.json</c>). Edits are never applied to a
/// tree: they are spans of the 4.x text, so comments and layout stay as written.
/// </summary>
public sealed class Migration
{
    private readonly List<Edit> _edits = [];
    private readonly List<Note> _notes = [];

    public IReadOnlyList<Edit> Edits => _edits;
    public IReadOnlyList<Note> Notes => _notes;
    public Dictionary<string, string> Written { get; } = new(StringComparer.Ordinal);
    public HashSet<string> Removed { get; } = new(StringComparer.Ordinal);

    internal void Add(Edit edit) => _edits.Add(edit);
    internal void Add(Note note) => _notes.Add(note);

    /// <summary>Each changed file with its new text, in path order.</summary>
    /// <exception cref="InvalidOperationException">Two rules change the same place: a fault of the
    /// catalogue, which a test finds.</exception>
    public IReadOnlyList<(string Path, string Before, string After)> Files(Func<string, string> read)
    {
        var files = new List<(string, string, string)>();
        foreach (var group in _edits.GroupBy(e => e.Path, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var edits = group.Distinct().OrderBy(e => e.Start).ThenBy(e => e.End).ToList();
            for (var i = 1; i < edits.Count; i++)
                if (edits[i].Start < edits[i - 1].End || (edits[i].Start == edits[i - 1].Start && edits[i].End == edits[i - 1].End))
                    throw new InvalidOperationException(
                        $"the rules {edits[i - 1].Rule} and {edits[i].Rule} change the same place of {group.Key} ({edits[i - 1].Start}..{edits[i - 1].End}, {edits[i].Start}..{edits[i].End})");
            var before = read(group.Key);
            var after = new System.Text.StringBuilder(before);
            foreach (var edit in Enumerable.Reverse(edits))
                after.Remove(edit.Start, edit.End - edit.Start).Insert(edit.Start, edit.Text);
            files.Add((group.Key, before, after.ToString()));
        }
        return files;
    }

    /// <summary>The notes in the order of their places.</summary>
    public IEnumerable<Note> Ordered() =>
        _notes.OrderBy(n => n.Path, StringComparer.Ordinal).ThenBy(n => n.Line).ThenBy(n => n.Column).ThenBy(n => n.Code, StringComparer.Ordinal);

    /// <summary>Writes the migration to disk: each changed file, each new one, and the removals.</summary>
    public void Apply()
    {
        foreach (var (path, _, after) in Files(File.ReadAllText)) File.WriteAllText(path, after);
        foreach (var (path, text) in Written) File.WriteAllText(path, text);
        foreach (var path in Removed) File.Delete(path);
    }

    /// <summary>A note as a line of the report, its path relative to <paramref name="root"/>:
    /// <c>proposal[LYR-MIG0106]: src/a.lyr:3:1: … — help: …</c>.</summary>
    public static string Line(Note note, string root)
    {
        var kind = note.Kind == NoteKind.Review ? "proposal" : "by hand";
        var path = Path.GetRelativePath(root, note.Path).Replace('\\', '/');
        return $"{kind}[{note.Code}] {path}:{note.Line}:{note.Column}: {note.Message}" + (note.Suggestion is { } s ? $" — {s}" : "");
    }
}

/// <summary>What a rule reads and writes: the checked 4.x program, its own files' texts, and the
/// migration it adds to.</summary>
public sealed class RuleContext(Model model, Migration migration, string? lyricJson)
{
    public Model Model { get; } = model;

    /// <summary>The project's <c>lyric.json</c>, or <c>null</c> for a single file.</summary>
    public string? LyricJson { get; } = lyricJson;

    /// <summary>The program's own modules — not the standard library's, not a dependency's.</summary>
    public IEnumerable<Module> Modules => Model.Roots;

    public string PathOf(Node node) => Model.Sources.GetPath(node.Span.File);

    public string TextOf(Module module) => Model.Sources.GetText(module.Span.File);

    public void Replace(string rule, Span span, string text) =>
        migration.Add(new Edit(Model.Sources.GetPath(span.File), span.Start, span.End, text, rule));

    public void Replace(string rule, FileId file, int start, int end, string text) =>
        migration.Add(new Edit(Model.Sources.GetPath(file), start, end, text, rule));

    public void Note(string rule, string code, NoteKind kind, Span span, string message, string? suggestion = null)
    {
        var at = Model.Sources.LocateStart(span);
        migration.Add(new Note(rule, code, kind, Model.Sources.GetPath(span.File), at.Line, at.Column, message, suggestion));
    }

    /// <summary>A note about a file as a whole, at its first line.</summary>
    public void Note(string rule, string code, NoteKind kind, string path, string message, string? suggestion = null) =>
        migration.Add(new Note(rule, code, kind, path, 1, 1, message, suggestion));

    public void Write(string path, string text) => migration.Written[path] = text;

    public void Remove(string path) => migration.Removed.Add(path);
}

/// <summary>A rule of 12 R2: it reads the checked 4.x program and adds edits and notes.</summary>
public interface IRule
{
    /// <summary>Its number in 12 R2 — <c>K01</c>, <c>S04</c>.</summary>
    string Id { get; }

    void Apply(RuleContext context);
}

/// <summary>Runs every rule over a checked 4.x program.</summary>
public static class Migrator
{
    /// <summary>The rules in their order in 12 R2.</summary>
    public static IReadOnlyList<IRule> Rules { get; } =
    [
        new Rules.ModuleHeader(),
        new Rules.Manifest(),
        new Rules.Imports(),
        new Rules.Visibility(),
        new Rules.BuildScript(),
    ];

    /// <exception cref="InvalidOperationException">The program does not check with 4.6.</exception>
    public static Migration Run(Model model, string? lyricJson = null)
    {
        if (model.HasErrors) throw new InvalidOperationException("Lyric 4.6 refuses the program; it is not migrated");
        var migration = new Migration();
        var context = new RuleContext(model, migration, lyricJson);
        foreach (var rule in Rules) rule.Apply(context);
        return migration;
    }
}
