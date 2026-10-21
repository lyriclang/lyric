using Lyric5.Build;

namespace Lyric5;

public static partial class Program
{
    // --- new, init and completions -------------------------------------------------------------

    /// <summary>
    /// <c>lyric new &lt;name&gt;</c> and <c>lyric init</c> (spec 14 §7): the package's first files in a
    /// directory <c>new</c> makes, or in the one <c>init</c> runs in, named after it unless
    /// <c>--name</c> says otherwise. A name that is no package name is refused with the one it would
    /// be; <c>--git</c>/<c>--no-git</c> decide the repository, and without either
    /// <c>LYRIC_GIT_ON_NEW</c> does.
    /// </summary>
    private static int New(string[] args, bool here)
    {
        var verb = here ? "init" : "new";
        if (Verbs.Read(Verbs.Find(verb)!, args, Unknown, out var unread) is not { } parsed) return unread;
        if (parsed.Rest is not null) return Unknown($"unknown option '--' for '{verb}'");
        if (here ? parsed.Words.Count > 0 : parsed.Words.Count != 1)
            return Unknown(here ? $"'init' takes no name but --name, got '{parsed.Words[0]}'" : "'new' takes the package's name, one: lyric5 new app");
        var git = parsed.Flags.Contains("--git") ? NewPackage.GitChoice.Yes
            : parsed.Flags.Contains("--no-git") ? NewPackage.GitChoice.No
            : NewPackage.GitChoice.Environment;
        if (parsed.Flags.Contains("--git") && parsed.Flags.Contains("--no-git"))
            return Unknown("'--git' and '--no-git' ask for opposite things");

        var start = Path.GetFullPath(parsed.Values.GetValueOrDefault("-C") ?? Directory.GetCurrentDirectory());
        if (!Directory.Exists(start))
        {
            Say.Error(Console.Error, "LYR-CLI0001", $"no such directory '{start}'");
            return 2;
        }
        var dir = here ? start : Path.Combine(start, parsed.Words[0]);
        var name = parsed.Values.GetValueOrDefault("--name") ?? (here ? Path.GetFileName(Path.TrimEndingDirectorySeparator(dir)) : parsed.Words[0]);
        if (!NewPackage.IsName(name))
            return Unknown($"'{name}' is no package name: a lowercase letter, then letters, digits and '_'"
                           + (NewPackage.NameFor(name) is { } better ? $" — '{better}' is one" + (here ? " (--name names it)" : "") : ""));
        if (!here && Path.Exists(dir))
        {
            Say.Error(Console.Error, "LYR-CLI0021", $"'{dir}' exists already", "lyric5 init makes a package in a directory that is there");
            return 2;
        }
        if (here && File.Exists(Path.Combine(dir, "lyric.toml")))
        {
            Say.Error(Console.Error, "LYR-CLI0021", $"'{Path.Combine(dir, "lyric.toml")}' is there: the package exists already");
            return 2;
        }
        Directory.CreateDirectory(dir);
        var (said, refused) = NewPackage.Create(dir, name, parsed.Flags.Contains("--lib"), git);
        foreach (var line in said) Console.Out.WriteLine(line);
        if (refused is null) return 0;
        Say.Error(Console.Error, "LYR-CLI0021", refused);
        return 2;
    }

    /// <summary><c>lyric completions &lt;shell&gt;</c> (spec 14 §7): the script that completes the verbs
    /// and their options, made from the table, printed — never run.</summary>
    private static int Completions(string[] args)
    {
        if (Verbs.Read(Verbs.Find("completions")!, args, Unknown, out var unread) is not { } parsed) return unread;
        if (parsed.Words.Count != 1 || parsed.Rest is not null) return Unknown("'completions' takes the shell: bash, zsh, fish or pwsh");
        var script = Completion.For(parsed.Words[0]);
        if (script is null) return Unknown($"no completions for '{parsed.Words[0]}': bash, zsh, fish, pwsh");
        Console.Out.Write(script);
        return 0;
    }
}
