using System.Text;
using Lyric.Core;
using Lyric.Formatting;

namespace Lyric5.Build;

/// <summary>
/// <c>lyric fmt</c>: Lyric files in the one form. A file that does not parse is left as it is and
/// its findings are said; a file already in the form is not written, so its time stays. The form
/// writes LF line ends and one final newline.
/// </summary>
public static class FmtRun
{
    /// <summary>The package's files the formatter takes: its sources, its tests and its build
    /// script, in path order.</summary>
    public static IReadOnlyList<string> PackageFiles(string root)
    {
        var files = new List<string>();
        foreach (var dir in new[] { "src", "tests" })
        {
            var full = Path.Combine(root, dir);
            if (Directory.Exists(full)) files.AddRange(Directory.EnumerateFiles(full, "*.lyr", SearchOption.AllDirectories));
        }
        var script = Path.Combine(root, "build.lyr");
        if (File.Exists(script)) files.Add(script);
        files.Sort(StringComparer.Ordinal);
        return files;
    }

    /// <summary>The file in the one form, or <c>null</c> where it does not parse; its findings
    /// then stand in <paramref name="diagnostics"/>.</summary>
    public static string? Format(string path, string text, out DiagnosticEngine diagnostics)
    {
        var sources = new SourceManager { Lyric5 = true };
        var id = sources.AddVirtual(path, text);
        diagnostics = new DiagnosticEngine(sources);
        return Formatter.Format(sources, id, diagnostics)?.ReplaceLineEndings("\n");
    }

    /// <summary>Formats <paramref name="files"/>: writes each that changes, or with
    /// <paramref name="check"/> writes nothing and names it — with <paramref name="diff"/> as a
    /// unified diff. <paramref name="shown"/> gives the name a file is said by. 0 when every file
    /// parsed and, with <paramref name="check"/>, none would change; 1 otherwise.</summary>
    public static int Run(IReadOnlyList<string> files, bool check, bool diff, Func<string, string> shown,
        TextWriter output, TextWriter error)
    {
        var refused = 0;
        var changed = 0;
        foreach (var file in files)
        {
            var before = File.ReadAllText(file);
            if (Format(file, before, out var diagnostics) is not { } after)
            {
                diagnostics.RenderText(error);
                error.WriteLine($"{shown(file)}: not formatted — it does not parse");
                refused++;
                continue;
            }
            if (after == before) continue;
            changed++;
            if (!check)
            {
                File.WriteAllText(file, after, new UTF8Encoding(false));
                output.WriteLine($"formatted {shown(file)}");
            }
            else if (diff) FixRun.Diff(output, shown(file), before.ReplaceLineEndings("\n"), after);
            else output.WriteLine(shown(file));
        }
        if (check && changed > 0)
            error.WriteLine(changed == 1 ? "1 file is not in the form; lyric5 fmt formats it" : $"{changed} files are not in the form; lyric5 fmt formats them");
        return refused > 0 || check && changed > 0 ? 1 : 0;
    }
}
