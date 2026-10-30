using Lyric4.From4;
using Lyric5.Build;

namespace Lyric5;

public static partial class Program
{
    /// <summary>The report beside the manifest, or beside a file migrated alone.</summary>
    private const string From4Report = "fix-from-4.txt";

    /// <summary>
    /// <c>fix --from-4</c>: a Lyric 4 program — the <c>lyric.json</c> here, or the file
    /// named — read with Lyric 4.6's frozen front end, rewritten by the migration's rules, its
    /// <c>lyric.json</c> made a <c>lyric.toml</c>. A program 4.6 refuses is not touched. What the rules
    /// leave to a person stands in the report and is said; <c>--dry-run</c> prints the changes as a
    /// diff and writes nothing. Whether the result compiles, <c>lyric check</c> says.
    /// </summary>
    private static int FixFrom4(string? file, string baseDirectory, bool dryRun)
    {
        string? lyricJson = null;
        string root;
        Model model;
        try
        {
            if (file is not null)
            {
                file = Path.GetFullPath(file, baseDirectory);
                if (!File.Exists(file))
                {
                    Say.Error(Console.Error, "LYR-CLI0001", $"no such file '{file}'");
                    return 2;
                }
                root = Path.GetDirectoryName(file)!;
                model = From4.CheckFile(file);
            }
            else
            {
                lyricJson = Path.Combine(baseDirectory, "lyric.json");
                if (!File.Exists(lyricJson))
                {
                    Say.Error(Console.Error, "LYR-CLI0004", $"no lyric.json in {baseDirectory}", "lyric5 fix --from-4 <file.lyr> migrates one file alone");
                    return 2;
                }
                root = baseDirectory;
                model = From4.CheckProject(lyricJson);
            }
        }
        catch (Lyric4.Core.ProjectFileException refused)
        {
            Say.Error(Console.Error, "LYR-MIG0002", $"{refused.Path}: {refused.Message}", "the migration reads a lyric.json as Lyric 4.6 did");
            return 2;
        }

        if (model.HasErrors)
        {
            foreach (var d in model.Diagnostics.Diagnostics.Where(d => d.Severity == Lyric4.Core.Severity.Error))
            {
                var at = d.Span.IsEmpty && d.Span.Start == 0 ? "" : $"{Shown(root, model.Sources.GetPath(d.Span.File))}:{model.Sources.LocateStart(d.Span).Line}:{model.Sources.LocateStart(d.Span).Column}: ";
                Console.Error.WriteLine($"  4.6: {at}error[{d.Code}]: {d.Message}");
            }
            Say.Error(Console.Error, "LYR-MIG0001", "Lyric 4.6 refuses the program, and it is not migrated", "make it check with Lyric 4.6 first: the migration reads what 4.6 accepted");
            return 1;
        }

        var migration = Migrator.Run(model, lyricJson);
        var files = migration.Files(File.ReadAllText);
        var notes = migration.Ordered().Select(n => Migration.Line(n, root)).ToList();
        if (dryRun)
        {
            foreach (var (path, before, after) in files) FixRun.Diff(Console.Out, Shown(root, path), before, after);
            foreach (var (path, text) in migration.Written.OrderBy(w => w.Key, StringComparer.Ordinal))
                FixRun.Diff(Console.Out, Shown(root, path), "", text);
            foreach (var path in migration.Removed.Order(StringComparer.Ordinal)) Console.Out.WriteLine($"remove {Shown(root, path)}");
        }
        else
        {
            migration.Apply();
            File.WriteAllText(Path.Combine(root, From4Report),
                $"lyric5 fix --from-4: what the rules leave to a person ({notes.Count})\n\n" + string.Concat(notes.Select(n => n + "\n")));
        }
        foreach (var note in notes) Console.Error.WriteLine(note);
        var changed = files.Count + migration.Written.Count;
        Console.Out.WriteLine(dryRun
            ? $"would change {changed} file{(changed == 1 ? "" : "s")}; {notes.Count} note{(notes.Count == 1 ? "" : "s")}"
            : $"migrated {changed} file{(changed == 1 ? "" : "s")}; {notes.Count} note{(notes.Count == 1 ? "" : "s")} in {From4Report} — lyric5 check says what is left");
        return 0;
    }

    private static string Shown(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');
}
