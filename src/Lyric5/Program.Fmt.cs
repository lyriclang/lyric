using Lyric5.Build;

namespace Lyric5;

public static partial class Program
{
    /// <summary><c>fmt</c>: the package's Lyric files — <c>src/</c>, <c>tests/</c>, <c>build.lyr</c>
    /// — or the files named, in the one form; <c>--check</c> writes nothing and fails where a file
    /// would change, <c>--diff</c> shows how.</summary>
    private static int Fmt(string[] args)
    {
        if (Verbs.Read(Verbs.Find("fmt")!, args, Unknown, out var unread) is not { } parsed) return unread;
        if (parsed.Rest is not null) return Unknown("unknown option '--' for 'fmt'");
        var check = parsed.Flags.Contains("--check");
        var diff = parsed.Flags.Contains("--diff");
        if (diff && !check) return Unknown("'--diff' goes with '--check': lyric5 fmt --check --diff");
        var baseDirectory = Path.GetFullPath(parsed.Values.GetValueOrDefault("-C") ?? Directory.GetCurrentDirectory());
        if (!Directory.Exists(baseDirectory))
        {
            Say.Error(Console.Error, "LYR-CLI0001", $"no such directory '{baseDirectory}'");
            return 2;
        }

        List<string> files = [];
        if (parsed.Words.Count > 0)
        {
            foreach (var word in parsed.Words)
            {
                var file = Path.GetFullPath(word, baseDirectory);
                if (!File.Exists(file))
                {
                    Say.Error(Console.Error, "LYR-CLI0001", $"no such file '{file}'");
                    return 2;
                }
                files.Add(file);
            }
        }
        else
        {
            if (Project.FindManifest(baseDirectory) is not { } manifest)
            {
                Say.Error(Console.Error, "LYR-CLI0004", "no lyric.toml here or above", "lyric5 fmt <file.lyr> formats files alone");
                return 2;
            }
            files.AddRange(FmtRun.PackageFiles(Path.GetDirectoryName(manifest)!));
        }

        string Shown(string path) => Path.GetRelativePath(baseDirectory, path).Replace('\\', '/');
        return FmtRun.Run(files, check, diff, Shown, Console.Out, Console.Error);
    }
}
