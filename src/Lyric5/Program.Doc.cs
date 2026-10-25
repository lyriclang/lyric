using Lyric5.Build;

namespace Lyric5;

public static partial class Program
{
    /// <summary><c>doc</c>: the package's reference — its <c>pub</c> modules, types and functions
    /// with their <c>///</c> — under <c>out/doc/</c>, or the standard library's with <c>--std</c>.</summary>
    private static int Doc(string[] args)
    {
        if (Verbs.Read(Verbs.Find("doc")!, args, Unknown, out var unread) is not { } parsed) return unread;
        if (parsed.Words.Count > 0) return Unknown($"'doc' takes no file, got '{parsed.Words[0]}'");
        if (parsed.Rest is not null) return Unknown("unknown option '--' for 'doc'");
        var baseDirectory = Path.GetFullPath(parsed.Values.GetValueOrDefault("-C") ?? Directory.GetCurrentDirectory());
        if (!Directory.Exists(baseDirectory))
        {
            Say.Error(Console.Error, "LYR-CLI0001", $"no such directory '{baseDirectory}'");
            return 2;
        }
        var outOption = parsed.Values.GetValueOrDefault("--out");

        if (parsed.Flags.Contains("--std"))
        {
            if (DocRun.StdSurface(DisplayVersion(), Console.Error) is not { } std) return 1;
            return DocRun.Write(std, Path.GetFullPath(outOption ?? Path.Combine("out", "doc"), baseDirectory), Console.Out);
        }

        Manifest manifest;
        PackageGraph graph;
        try
        {
            if (Project.ProgramsOf(baseDirectory, parsed.Flags.Contains("--offline"), locked: false) is not { } programs)
            {
                Say.Error(Console.Error, "LYR-CLI0004", "no lyric.toml here or above", "lyric5 doc --std writes the standard library's reference");
                return 2;
            }
            manifest = programs[0].Manifest!;
            graph = programs[0].Graph!;
        }
        catch (ManifestException refused)
        {
            Say.Manifest(Console.Error, refused);
            return refused.Exit;
        }
        catch (LibraryException library)
        {
            manifest = library.Manifest;
            graph = library.Graph;
        }

        if (DocRun.PackageSurface(manifest, graph, Console.Error) is not { } surface) return 1;
        return DocRun.Write(surface, Path.GetFullPath(outOption ?? Path.Combine(manifest.Root, "out", "doc"), baseDirectory), Console.Out);
    }
}
