using Lyric5.Build;
using Lyric5.Toolchain;

namespace Lyric5;

public static partial class Program
{
    /// <summary><c>fix</c> (spec 16 §6): the safe changes the findings suggest, made in the package's
    /// own files — the file's, for a file alone —; <c>--dry-run</c> prints them as a diff and writes
    /// nothing.</summary>
    private static int Fix(string[] args, LintArgs lints)
    {
        if (Verbs.Read(Verbs.Find("fix")!, args, Unknown, out var unread) is not { } parsed) return unread;
        if (parsed.Words.Count > 1) return Unknown($"'fix' takes one file, got '{parsed.Words[0]}' and '{parsed.Words[1]}'");
        if (parsed.Rest is not null) return Unknown("unknown option '--' for 'fix'");
        var file = parsed.Words.Count == 1 ? parsed.Words[0] : null;
        var from = parsed.Values.GetValueOrDefault("-C");
        var dryRun = parsed.Flags.Contains("--dry-run");
        if (parsed.Flags.Contains("--from-4"))
        {
            if (Lyric.Core.Rendering.RenderStyle.Current.Events is not null) return Unknown("'--json' does not go with '--from-4'");
            return FixFrom4(file, Path.GetFullPath(from ?? Directory.GetCurrentDirectory()), dryRun);
        }
        var offline = parsed.Flags.Contains("--offline");
        var baseDirectory = Path.GetFullPath(from ?? Directory.GetCurrentDirectory());
        if (!Directory.Exists(baseDirectory))
        {
            Say.Error(Console.Error, "LYR-CLI0001", $"no such directory '{baseDirectory}'");
            return 2;
        }
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        static bool Under(string dir, string path, StringComparison comparison) =>
            path.StartsWith(Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, comparison);
        List<Func<Lyric.Compiler.CompileResult>> checks;
        Func<string, bool> ours;
        string root;
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
                var project = Project.ForFile(file, offline, locked: false) with { LintArgs = lints };
                checks = [() => Pipeline.Check(project)];
                var manifest = project.Manifest;
                ours = path => string.Equals(path, file, comparison) || manifest is not null && Under(manifest.SourceRoot, path, comparison);
                root = manifest?.Root ?? Path.GetDirectoryName(file)!;
            }
            else
            {
                if (Project.ProgramsOf(baseDirectory, offline, locked: false) is not { } package)
                {
                    Say.Error(Console.Error, "LYR-CLI0004", "no lyric.toml here or above", "lyric5 fix <file.lyr> fixes one file alone");
                    return 2;
                }
                var programs = package.Select(p => p with { LintArgs = lints }).ToList();
                checks = [.. programs.Select(p => (Func<Lyric.Compiler.CompileResult>)(() => Pipeline.Check(p)))];
                var manifest = programs[0].Manifest!;
                ours = path => Under(manifest.SourceRoot, path, comparison);
                root = manifest.Root;
            }
        }
        catch (ManifestException refused)
        {
            Say.Manifest(Console.Error, refused);
            return refused.Exit;
        }
        catch (LibraryException library)
        {
            checks = [() => Pipeline.Check(library, lints)];
            ours = path => Under(library.Manifest.SourceRoot, path, comparison);
            root = library.Manifest.Root;
        }
        return FixRun.Run(checks, ours, root, dryRun, Console.Out, Console.Error);
    }
}
