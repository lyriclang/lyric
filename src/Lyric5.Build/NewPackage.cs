using System.Text;

namespace Lyric5.Build;

/// <summary>
/// <c>lyric new</c> and <c>lyric init</c> (spec 14 §7): a package's first files — <c>lyric.toml</c>,
/// <c>src/main.lyr</c> or, for a library, <c>src/lib.lyr</c>, and a <c>.gitignore</c> that keeps
/// <c>out/</c> out —, and a git repository where one is asked for.
/// </summary>
public static class NewPackage
{
    /// <summary>Whether <paramref name="name"/> may name a package: a lowercase letter, then lowercase
    /// letters, digits and <c>_</c> — and not <c>std</c>.</summary>
    public static bool IsName(string name) => Manifest.IsPackageName(name);

    /// <summary>The name a directory's name would be as a package's: lowercase, <c>-</c>, <c>.</c> and
    /// blanks as <c>_</c>, what comes before its first letter left out; <c>null</c> where nothing is left.</summary>
    public static string? NameFor(string written)
    {
        var name = new StringBuilder();
        foreach (var c in written.ToLowerInvariant())
        {
            if (char.IsAsciiLetterLower(c) || (name.Length > 0 && char.IsAsciiDigit(c))) name.Append(c);
            else if (name.Length > 0 && c is '-' or '.' or ' ' or '_') name.Append('_');
        }
        var result = name.ToString().TrimEnd('_');
        return result.Length > 0 && IsName(result) ? result : null;
    }

    /// <summary>What git does for a new package: the flag's answer, else the environment's.</summary>
    public enum GitChoice { Yes, No, Environment }

    /// <summary>
    /// Writes the package's files into <paramref name="dir"/>, which exists and holds no
    /// <c>lyric.toml</c>; a file there of the same name is kept. Then <c>git init</c>: always for
    /// <see cref="GitChoice.Yes"/>, for <see cref="GitChoice.Environment"/> where <c>LYRIC_GIT_ON_NEW</c>
    /// is <c>1</c> or <c>true</c>, no repository holds the directory already and git is found. The
    /// lines it says, or an error message for exit 2.
    /// </summary>
    public static (List<string> Said, string? Refused) Create(string dir, string name, bool library, GitChoice git)
    {
        var said = new List<string>();
        void Write(string relative, string text)
        {
            var path = Path.Combine(dir, relative);
            if (File.Exists(path)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }
        Write("lyric.toml", $"[package]\nname = \"{name}\"\nversion = \"0.1.0\"\n");
        if (library)
            Write("src/lib.lyr", $"//! The {name} library.\n\n/// Twice `x`.\npub fn twice(x: int): int {{\n    return x * 2;\n}}\n");
        else
            Write("src/main.lyr", "import std.io { println };\n\nfn main(): void {\n    println(\"Hello, world!\");\n}\n");
        Write(".gitignore", "out/\n");

        var wanted = git == GitChoice.Yes
                     || git == GitChoice.Environment
                     && Environment.GetEnvironmentVariable("LYRIC_GIT_ON_NEW")?.Trim().ToLowerInvariant() is "1" or "true";
        if (wanted)
        {
            try
            {
                var inside = Git.Run(["-C", dir, "rev-parse", "--is-inside-work-tree"], TimeSpan.FromSeconds(30));
                if (git == GitChoice.Yes || inside.Exit != 0 || inside.Text != "true")
                {
                    var init = Git.Run(["-C", dir, "init", "--quiet"], TimeSpan.FromSeconds(30));
                    if (init.Exit != 0) return (said, $"git init failed: {init.Error.Trim()}");
                    said.Add("initialized a git repository");
                }
            }
            catch (GitMissingException)
            {
                // the environment asks where it can; the flag insists
                if (git == GitChoice.Yes) return (said, "--git asks for a repository, and no git was found");
            }
        }
        said.Insert(0, $"created {(library ? "library" : "program")} package '{name}' in {dir}");
        return (said, null);
    }
}
