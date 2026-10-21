using Lyric5.Build;
using Lyric5.Toolchain;

namespace Lyric5;

public static partial class Program
{
    // --- clean and metadata ------------------------------------------------------------------

    /// <summary>The options of a verb about the package of a directory: where it starts and the
    /// flags it set. <c>null</c> after refusing, with the exit code in <paramref name="exit"/>.</summary>
    private static (string Start, HashSet<string> Flags)? PackageOptions(string verb, string[] args, out int exit)
    {
        if (Verbs.Read(Verbs.Find(verb)!, args, Unknown, out exit) is not { } parsed) return null;
        if (parsed.Words.Count > 0 || parsed.Rest is not null)
        {
            exit = Unknown($"unknown option '{(parsed.Words.Count > 0 ? parsed.Words[0] : "--")}' for '{verb}'");
            return null;
        }
        var start = Path.GetFullPath(parsed.Values.GetValueOrDefault("-C") ?? Directory.GetCurrentDirectory());
        if (!Directory.Exists(start))
        {
            Say.Error(Console.Error, "LYR-CLI0001", $"no such directory '{start}'");
            exit = 2;
            return null;
        }
        return (start, parsed.Flags);
    }

    /// <summary><c>lyric clean</c>: the package's <c>out/</c> removed — after the build
    /// that writes into it, if one does, is done.</summary>
    private static int Clean(string[] args)
    {
        if (PackageOptions("clean", args, out var refused) is not { } options) return refused;
        if (Project.FindManifest(options.Start) is not { } file)
        {
            Say.Error(Console.Error, "LYR-CLI0004", "no lyric.toml here or above");
            return 2;
        }
        var outDir = Path.Combine(Path.GetDirectoryName(file)!, "out");
        if (!Directory.Exists(outDir))
        {
            Console.Out.WriteLine("nothing to clean");
            return 0;
        }
        using (OutLock.Take(outDir, Console.Error))
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(outDir))
            {
                if (Path.GetFileName(entry) == ".lock") continue;
                if (Directory.Exists(entry)) DeleteTree(entry);
                else File.Delete(entry);
            }
        }
        DeleteTree(outDir);
        Console.Out.WriteLine($"removed {outDir}");
        return 0;
    }

    /// <summary>Read-only files too: a cache can hold them.</summary>
    private static void DeleteTree(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(directory, recursive: true);
    }

    /// <summary><c>lyric metadata</c>: the package as one JSON object on standard
    /// output — <c>--json</c> names the one form there is.</summary>
    private static int Metadata(string[] args)
    {
        if (PackageOptions("metadata", args, out var refused) is not { } options) return refused;
        if (Project.FindManifest(options.Start) is null)
        {
            Say.Error(Console.Error, "LYR-CLI0004", "no lyric.toml here or above");
            return 2;
        }
        try
        {
            IReadOnlyList<Project> programs;
            PackageGraph graph;
            try
            {
                programs = Project.ProgramsOf(options.Start, options.Flags.Contains("--offline"))!;
                graph = programs[0].Graph!;
            }
            catch (LibraryException library)
            {
                programs = [];
                graph = library.Graph;
            }
            Console.Out.WriteLine(global::Lyric5.Build.Metadata.Json(graph, programs, DisplayVersion()));
            return 0;
        }
        catch (ManifestException manifest)
        {
            Say.Manifest(Console.Error, manifest);
            return manifest.Exit;
        }
    }

    // --- add and remove ------------------------------------------------------------------------

    /// <summary>
    /// <c>lyric add &lt;name&gt; --path &lt;dir&gt; | --git &lt;url&gt; [--tag|--branch|--rev &lt;r&gt;]</c>:
    /// the dependency written into <c>[dependencies]</c> as one line, every other line as it
    /// was, and the graph read with it — a graph that refuses the dependency leaves the manifest as
    /// it was. The path is written relative to the manifest.
    /// </summary>
    private static int Add(string[] args)
    {
        if (Verbs.Read(Verbs.Find("add")!, args, Unknown, out var refused) is not { } parsed) return refused;
        if (parsed.Words.Count > 1) return Unknown($"'add' takes one name, got '{parsed.Words[0]}' and '{parsed.Words[1]}'");
        var values = parsed.Values;
        var name = parsed.Words.Count == 1 ? parsed.Words[0] : null;
        var offline = parsed.Flags.Contains("--offline");
        if (name is null) return Unknown("'add' needs the dependency's name: lyric5 add geo --path ../geo");
        var revisions = new[] { "--tag", "--branch", "--rev" }.Where(values.ContainsKey).ToList();
        if (values.ContainsKey("--path") == values.ContainsKey("--git"))
            return Unknown("'add' reads a dependency from a directory or from a repository: --path or --git, one of them");
        if (revisions.Count > 0 && !values.ContainsKey("--git")) return Unknown($"'{revisions[0]}' names a revision of a repository: it goes with --git");
        if (revisions.Count > 1) return Unknown($"'{revisions[0]}' and '{revisions[1]}': a dependency reads one revision");

        var start = Path.GetFullPath(values.GetValueOrDefault("-C") ?? Directory.GetCurrentDirectory());
        if (Project.FindManifest(start) is not { } file)
        {
            Say.Error(Console.Error, "LYR-CLI0004", "no lyric.toml here or above");
            return 2;
        }
        var root = Path.GetDirectoryName(file)!;
        string value;
        if (values.TryGetValue("--path", out var path))
        {
            // Relative to where the command looks from (-C), as a file 'build' is given is.
            var relative = Path.GetRelativePath(root, Path.GetFullPath(path, start)).Replace('\\', '/');
            value = $"{{ path = {ManifestEdit.Quote(relative)} }}";
        }
        else
        {
            var revision = revisions.Count == 0 ? "" : $", {revisions[0][2..]} = {ManifestEdit.Quote(values[revisions[0]])}";
            value = $"{{ git = {ManifestEdit.Quote(values["--git"])}{revision} }}";
        }
        return EditDependencies(file, offline, text => ManifestEdit.Add(text, name, value), $"added {name} = {value}");
    }

    /// <summary><c>lyric explain &lt;code&gt;</c>: the catalog's entry — what the
    /// code means, why, what to do, a wrong and a right program. A code is read as a reader may write
    /// it (<c>sem0001</c>, <c>LYR-SEM0001</c>), a lint by its name (<c>unused-import</c>); one the
    /// catalog does not hold is exit 2.</summary>
    private static int Explain(string[] args)
    {
        if (Verbs.Read(Verbs.Find("explain")!, args, Unknown, out var refused) is not { } parsed) return refused;
        if (parsed.Words.Count != 1) return Unknown("explain takes one code or lint: lyric5 explain LYR-SEM0001");
        args = [parsed.Words[0]];
        if (!Lyric.Core.DiagnosticCatalog.Lints.TryGetValue(args[0], out var entry)
            && !Lyric.Core.DiagnosticCatalog.Entries.TryGetValue(Lyric.Core.DiagnosticCatalog.Normalize(args[0]), out entry))
            return Unknown($"no code or lint '{args[0]}' in the catalog");
        Console.Out.Write(Lyric.Core.DiagnosticCatalog.Explain(entry));
        return 0;
    }

    /// <summary><c>lyric remove &lt;name&gt;</c>: the dependency's line taken out of
    /// <c>[dependencies]</c>, and the graph — the lock with it — read again.</summary>
    private static int Remove(string[] args)
    {
        if (Verbs.Read(Verbs.Find("remove")!, args, Unknown, out var refused) is not { } parsed) return refused;
        if (parsed.Words.Count != 1) return Unknown("'remove' takes the dependency's name, one: lyric5 remove geo");
        var name = parsed.Words[0];
        var start = Path.GetFullPath(parsed.Values.GetValueOrDefault("-C") ?? Directory.GetCurrentDirectory());
        if (!Directory.Exists(start))
        {
            Say.Error(Console.Error, "LYR-CLI0001", $"no such directory '{start}'");
            return 2;
        }
        var options = (Start: start, parsed.Flags);
        if (Project.FindManifest(options.Start) is not { } file)
        {
            Say.Error(Console.Error, "LYR-CLI0004", "no lyric.toml here or above");
            return 2;
        }
        return EditDependencies(file, options.Flags.Contains("--offline"),
            text => ManifestEdit.Remove(text, name)
                    ?? throw new ArgumentException($"no dependency '{name}' in {file}"),
            $"removed {name}");
    }

    /// <summary>The manifest edited, then read with its graph; what refuses the edit puts the
    /// manifest back as it was.</summary>
    private static int EditDependencies(string file, bool offline, Func<string, string> edit, string done)
    {
        var before = File.ReadAllText(file);
        string after;
        try { after = edit(before); }
        catch (ArgumentException unknown) { return Unknown(unknown.Message); }
        catch (InvalidOperationException other)
        {
            Say.Error(Console.Error, "LYR-CLI0008", $"{other.Message}");
            return 2;
        }
        File.WriteAllText(file, after);
        try
        {
            var graph = Project.Resolve(Manifest.Read(file), offline);
            ToolchainCheck.Check(graph, DisplayVersion());
        }
        catch (ManifestException refused)
        {
            File.WriteAllText(file, before);
            Say.Manifest(Console.Error, refused);
            return refused.Exit;
        }
        Console.Out.WriteLine(done);
        return 0;
    }
}
