using System.Collections.Concurrent;
using System.Text;
using Lyric5.Toolchain;

namespace Lyric5.Build;

/// <summary>Why <c>pkg-config</c> gave nothing: it is not there, or it does not know a name.</summary>
public sealed class PkgConfigException(string message) : Exception(message);

/// <summary>What <c>pkg-config --cflags --libs</c> says of libraries of the system, taken apart: what
/// the package's C sources are compiled with — include directories and definitions —, and what the
/// link takes — directories, libraries by name, frameworks, and every other flag as it is.</summary>
public sealed record PkgConfigAnswer(
    IReadOnlyList<string> Include,
    IReadOnlyList<string> Defines,
    IReadOnlyList<string> SearchDirs,
    IReadOnlyList<string> Libraries,
    IReadOnlyList<string> Frameworks,
    IReadOnlyList<string> Args)
{
    public static readonly PkgConfigAnswer None = new([], [], [], [], [], []);
}

/// <summary>
/// The libraries of the system as <c>pkg-config</c> knows them (spec 15 §5 rule 10): asked on the
/// host, for a build for the host — a library of the system is the machine's. Each set of names
/// is asked once in a process.
/// </summary>
public static class PkgConfig
{
    private static readonly ConcurrentDictionary<string, PkgConfigAnswer> Asked = new(StringComparer.Ordinal);

    /// <summary>What <c>pkg-config</c> says of <paramref name="names"/>.</summary>
    /// <exception cref="PkgConfigException">pkg-config is not on the PATH, or a name is not one it knows.</exception>
    public static PkgConfigAnswer Query(IReadOnlyList<string> names)
    {
        if (names.Count == 0) return PkgConfigAnswer.None;
        var program = Environment.GetEnvironmentVariable("PKG_CONFIG") is { Length: > 0 } chosen ? chosen : "pkg-config";
        // the same names asked of another pkg-config are another question
        var key = program + "\n" + string.Join('\n', names);
        if (Asked.TryGetValue(key, out var known)) return known;
        var ran = ProcessRunner.TryRun(program, ["--cflags", "--libs", .. names], TimeSpan.FromMinutes(1));
        if (ran is null)
            throw new PkgConfigException($"pkg-config is not on the PATH (or PKG_CONFIG), and {string.Join(", ", names)} needs it — install it, or link the library with 'libs'");
        if (ran.ExitCode != 0)
        {
            var said = ran.Stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            throw new PkgConfigException(said.LastOrDefault() ?? $"pkg-config exited with {ran.ExitCode}");
        }
        var answer = Parse(ran.Stdout);
        Asked[key] = answer;
        return answer;
    }

    /// <summary>The flags pkg-config wrote, sorted by where they go.</summary>
    public static PkgConfigAnswer Parse(string flags)
    {
        List<string> include = [], defines = [], dirs = [], libraries = [], frameworks = [], args = [];
        var words = Words(flags);
        for (var i = 0; i < words.Count; i++)
        {
            var word = words[i];
            if (word.StartsWith("-I", StringComparison.Ordinal) && word.Length > 2) include.Add(word[2..]);
            else if (word.StartsWith("-D", StringComparison.Ordinal) && word.Length > 2) defines.Add(word[2..]);
            else if (word.StartsWith("-L", StringComparison.Ordinal) && word.Length > 2) dirs.Add(word[2..]);
            else if (word.StartsWith("-l", StringComparison.Ordinal) && word.Length > 2) libraries.Add(word[2..]);
            else if (word == "-framework" && i + 1 < words.Count) frameworks.Add(words[++i]);
            // a compiler's flag of the C sources other than -I and -D (-pthread is the link's too)
            else if (word == "-pthread") args.Add(word);
            else if (!word.StartsWith("-f", StringComparison.Ordinal) && !word.StartsWith("-m", StringComparison.Ordinal)) args.Add(word);
        }
        return new PkgConfigAnswer(include, defines, dirs, libraries, frameworks, args);
    }

    /// <summary>The words of a line as a shell splits it: blanks between, quotes and backslashes kept together.</summary>
    private static List<string> Words(string text)
    {
        var words = new List<string>();
        var current = new StringBuilder();
        var any = false;
        char? quote = null;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quote is { } q)
            {
                if (c == q) quote = null;
                else if (c == '\\' && q == '"' && i + 1 < text.Length) current.Append(text[++i]);
                else current.Append(c);
            }
            else if (c is '"' or '\'') { quote = c; any = true; }
            else if (c == '\\' && i + 1 < text.Length) { current.Append(text[++i]); any = true; }
            else if (char.IsWhiteSpace(c))
            {
                if (any || current.Length > 0) words.Add(current.ToString());
                current.Clear();
                any = false;
            }
            else current.Append(c);
        }
        if (any || current.Length > 0) words.Add(current.ToString());
        return words;
    }
}
