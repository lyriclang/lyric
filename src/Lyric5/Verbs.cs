using System.Text;

namespace Lyric5;

/// <summary>An option of a verb: its name, a second spelling, the value it takes — a placeholder, or
/// <c>null</c> for a flag —, whether a <c>--no-</c> form turns it off, and what it does.</summary>
internal sealed record Opt(string Name, string? Value, string Help, string? Alias = null, bool Negatable = false)
{
    /// <summary>How the option is written in a help line: <c>--[no-]lto</c>, <c>-f, --filter &lt;text&gt;</c>.</summary>
    public string Written =>
        (Alias is null ? "" : Alias + ", ")
        + (Negatable ? "--[no-]" + Name[2..] : Name)
        + (Value is null ? "" : " " + Value);
}

/// <summary>A verb: its name, the words it takes, what it does, its own options, and whether it takes
/// the lint options and <c>--json</c> (spec 16 §4, §5).</summary>
internal sealed record VerbSpec(string Name, string Words, string Summary, Opt[] Options, bool Lints = false, bool Json = false);

/// <summary>What a command line said to a verb: the values of its options by their names, the flags
/// it set, its words, and what stands after a <c>--</c>.</summary>
internal sealed record Parsed(Dictionary<string, string> Values, HashSet<string> Flags, List<string> Words, string[]? Rest);

/// <summary>
/// The verbs and their options as data (spec 14 §5): the one table the command line is read by,
/// <c>help</c> prints, the completions are made of and the spec's table is printed from — so that
/// none of them says what another does not.
/// </summary>
internal static class Verbs
{
    // the options several verbs share
    private static readonly Opt Dir = new("-C", "<dir>", "look for the package from <dir> instead of here");
    private static readonly Opt Offline = new("--offline", null, "fetch nothing: packages from git come from the cache");
    private static readonly Opt Locked = new("--locked", null, "take lyric.lock as it is: fail where the manifests read otherwise, write no lock");
    private static readonly Opt Profile = new("--profile", "<name>", "the build profile: debug, release, asan, tsan, or one of the manifest's (default: LYRIC_PROFILE, else debug)");
    private static readonly Opt Target = new("--target", "<triple>", "a Tier 1 target (default: this machine)");
    private static readonly Opt Bin = new("--bin", "<name>", "the package's program");
    private static readonly Opt Verbose = new("-v", null, "say each command of the C compiler, the archiver and the linker as it starts", Alias: "--verbose");
    private static readonly Opt Quiet = new("-q", null, "say no status lines: what is compiled, linked and run", Alias: "--quiet");

    /// <summary>The profile's fields, for one build (spec 15 §4).</summary>
    public static readonly Opt[] Fields =
    [
        new("--opt", "<0-3>", "the profile's optimization level"),
        new("--gc", "<immix|boehm>", "the profile's collector"),
        new("--lto", null, "link-time optimization", Negatable: true),
        new("--debug-info", null, "debug information in the binary", Negatable: true),
        new("--deny-warnings", null, "every warning an error", Negatable: true),
        new("--overflow-checks", null, "integer overflow panics", Negatable: true),
        new("--fast-math", null, "floating point without strict IEEE order", Negatable: true),
    ];

    private static readonly Opt[] Building = [Profile, .. Fields, Target, Dir, Bin, Offline, Locked, Verbose, Quiet];

    public static readonly VerbSpec[] All =
    [
        new("build", "[<file.lyr>]", "compile the package here — its src/main.lyr and its [[bin]] programs —, or the file, to binaries under out/",
            [.. Building, new("--emit", "<ir|c>", "print the IR or the C instead of building"),
             new("--lib", null, "build the package's src/lib.lyr as a library C links: lib<name>.a and its header <name>.h, and say what C links beside it"),
             new("--shared", null, "with --lib: a shared library — lib<name>.so, lib<name>.dylib, <name>.dll — instead of the archive")],
            Lints: true, Json: true),
        new("run", "[<file.lyr>] [-- <args>…]", "build, then run the binary with the arguments after --", Building, Lints: true, Json: true),
        new("check", "[<file.lyr>]", "what build says of the package or the file, without making C or a binary", Building, Lints: true, Json: true),
        new("expand", "[<file.lyr>]", "print the program's entry module with its macros expanded", Building, Lints: true),
        new("test", "", "build the package's tests and run them, each in a task of its own",
            [Profile, .. Fields, Target, Dir, Offline, Locked, Verbose, Quiet, new("-f", "<text>", "only the tests whose name holds <text>", Alias: "--filter"),
             new("--no-run", null, "build the tests and run none: the binary's path is printed")], Lints: true, Json: true),
        new("bench", "", "build the package's benchmarks in the release profile and run them",
            [Profile, .. Fields, Target, Dir, Offline, Locked, Verbose, Quiet, new("-f", "<text>", "only the benchmarks whose name holds <text>", Alias: "--filter"),
             new("--save", "<file>", "write the times to <file>"), new("--compare", "<file>", "read the times of <file> and say how they changed")], Lints: true),
        new("fix", "[<file.lyr>]", "make the safe changes the findings suggest in the package's own files, then say what is left",
            [Dir, Offline, new("--dry-run", null, "print the changes as a diff and write nothing")], Lints: true, Json: true),
        new("fmt", "[<file.lyr>…]", "put the package's Lyric files — src/, tests/, build.lyr — or the files in the one form",
            [Dir, new("--check", null, "write nothing; name each file that would change, and fail if one would"),
             new("--diff", null, "with --check: the changes as a diff")]),
        new("doc", "", "write the package's reference — its pub modules, types and functions with their documentation — under out/doc/",
            [Dir, Offline, new("--out", "<dir>", "write it into <dir> instead"),
             new("--std", null, "the standard library's reference instead of a package's")]),
        new("api", "", "print the package's public surface, one line per element; with --diff, what changed against a git revision",
            [Dir, new("--json", null, "the surface, or the changes, as one JSON document"),
             new("--diff", "<rev>", "compare with the package at the git revision <rev>; exit 1 where something breaks"),
             new("--std", null, "the standard library's surface instead of a package's")]),
        new("lsp", "", "the language server: an editor speaks LSP with it on the standard input and output",
            [new("--stdio", null, "over the standard input and output — the one way there is; editors pass it")]),
        new("dap", "", "the debug adapter: an editor speaks DAP with it on the standard input and output, lldb-dap behind it", []),
        new("env", "", "every setting a build reads, with its value and where it comes from",
            [Dir, Profile, Target, .. Fields, Offline, new("--json", null, "the settings as one JSON document")], Lints: true),
        new("new", "<name>", "make the directory <name> and a package in it: lyric.toml, src/main.lyr, .gitignore",
            [new("--lib", null, "a library: src/lib.lyr instead of src/main.lyr"),
             new("--git", null, "make a git repository too (without the flag: LYRIC_GIT_ON_NEW=1 asks for one)", Negatable: true), Dir]),
        new("init", "", "make a package in the directory here, named after it",
            [new("--lib", null, "a library: src/lib.lyr instead of src/main.lyr"), new("--name", "<name>", "the package's name instead of the directory's"),
             new("--git", null, "make a git repository too (without the flag: LYRIC_GIT_ON_NEW=1 asks for one)", Negatable: true), Dir]),
        new("update", "[<package>…]", "read the packages and source trees from git anew — all, or those named —, raise a tag to the newest of its line in lyric.toml, and write lyric.lock",
            [Dir, Offline]),
        new("add", "<name>", "write the dependency, or with --vendor the source tree, into lyric.toml",
            [new("--path", "<dir>", "a dependency in a directory"), new("--git", "<url>", "a dependency or a tree in a repository"),
             new("--tag", "<tag>", "the repository's tag"), new("--branch", "<branch>", "the repository's branch"), new("--rev", "<commit>", "the repository's commit"),
             new("--vendor", null, "a source tree of [native.vendor], not a package"), new("--url", "<url>", "a tree in a file at a URL, written with its hash"),
             Dir, Offline]),
        new("remove", "<name>", "take the dependency, or with --vendor the source tree, out of lyric.toml",
            [new("--vendor", null, "a source tree of [native.vendor], not a package"), Dir, Offline]),
        new("bindgen", "<header>", "write the module a C header makes to standard output, with the package's include and defines",
            [Target, Dir, Offline]),
        new("clean", "", "remove the package's out/", [Dir]),
        new("metadata", "", "the package as JSON: its graph, programs, profiles, targets",
            [Dir, Offline, new("--json", null, "the one form there is")]),
        new("toolchain", "<install|list|default|remove|update> [<version>|zig]",
            "install toolchains side by side under LYRIC_HOME (install dev, stable or a version — or zig, the C compiler), list them, say or set the default, remove one, update the default to the newest of its channel",
            [new("--available", null, "with list: the versions the channels offer, not the ones installed")]),
        new("explain", "<code>","what a diagnostic code or a lint means, with a wrong and a right program", []),
        new("completions", "<bash|zsh|fish|pwsh>", "print the shell's completion script for these verbs and options", []),
        new("version", "", "the toolchain and the C compiler it found", []),
        new("help", "[<verb>]", "the verbs, or one verb's options",
            [new("--markdown", null, "the verbs and their options as a Markdown table, the one the spec prints")]),
    ];

    /// <summary>The options every verb takes, read before the verb's own (spec 16 §1, §4, §5).</summary>
    public static readonly Opt[] Global =
    [
        new("--color", "<auto|always|never>", "colour the diagnostics (default auto: a terminal, unless NO_COLOR is set or TERM is dumb)"),
        new("--absolute-paths", null, "paths as they are, not relative to the package"),
        new("--max-errors", "<n>", "draw the first n errors and count the rest (default 50, 0 for all)"),
        new("--json", null, "the findings and the events as a line of JSON each on the standard output (the verbs marked json)"),
        new("-A", "<lint>", "allow a lint — a name, a group, a code, or warnings (the verbs marked lints)"),
        new("-W", "<lint>", "warn of a lint"),
        new("-D", "<lint>", "deny a lint"),
        new("--warn-deps", null, "say the warnings of the dependencies and of std too"),
        new("-h", null, "this verb's options", Alias: "--help"),
    ];

    public static VerbSpec? Find(string name) => All.FirstOrDefault(v => v.Name == name);

    /// <summary>
    /// The verb's command line read against its table, strictly: an option the verb does not have, a
    /// value missing, is refused with exit 2. A flag of a field may come as <c>--no-x</c>; a value as
    /// <c>--x v</c> or <c>--x=v</c>. <c>--</c> ends the options; what follows is <see cref="Parsed.Rest"/>.
    /// </summary>
    public static Parsed? Read(VerbSpec verb, string[] args, Func<string, int> refuse, out int exit)
    {
        exit = 0;
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var flags = new HashSet<string>(StringComparer.Ordinal);
        var words = new List<string>();
        string[]? rest = null;
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg == "--")
            {
                rest = args[(i + 1)..];
                break;
            }
            if (!arg.StartsWith('-') || arg == "-")
            {
                words.Add(arg);
                continue;
            }
            var name = arg.Contains('=') ? arg[..arg.IndexOf('=')] : arg;
            var option = verb.Options.FirstOrDefault(o => o.Name == name || o.Alias == name);
            if (option is null && name == arg && name.StartsWith("--no-", StringComparison.Ordinal)
                && verb.Options.Any(o => o.Negatable && o.Name == "--" + name[5..]))
            {
                flags.Add(name);
                continue;
            }
            if (option is null)
            {
                exit = refuse($"unknown option '{arg}' for '{verb.Name}'");
                return null;
            }
            if (option.Value is null)
            {
                if (name != arg)
                {
                    exit = refuse($"'{option.Name}' takes no value");
                    return null;
                }
                flags.Add(option.Name);
                continue;
            }
            var value = name != arg ? arg[(name.Length + 1)..] : i + 1 < args.Length ? args[++i] : null;
            if (value is null)
            {
                exit = refuse($"'{option.Name}' needs a value");
                return null;
            }
            values[option.Name] = value;
        }
        return new Parsed(values, flags, words, rest);
    }

    /// <summary><c>lyric help</c>: the verbs, each with its words and what it does, and the options
    /// every verb takes.</summary>
    public static void Usage(TextWriter output)
    {
        output.WriteLine("usage: lyric5 <verb> [options]");
        output.WriteLine();
        foreach (var verb in All)
            Line(output, $"{verb.Name} {verb.Words}".TrimEnd(), verb.Summary + Marks(verb));
        output.WriteLine();
        output.WriteLine("options of every verb:");
        foreach (var option in Global) Line(output, option.Written, option.Help);
        output.WriteLine();
        output.WriteLine("lyric5 help <verb>, or lyric5 <verb> --help, lists the verb's own options.");
    }

    /// <summary><c>lyric help &lt;verb&gt;</c>: the verb's words and its options.</summary>
    public static void Help(TextWriter output, VerbSpec verb)
    {
        output.WriteLine($"usage: lyric5 {verb.Name} {verb.Words}".TrimEnd() + (verb.Options.Length > 0 ? " [options]" : ""));
        output.WriteLine();
        output.WriteLine(verb.Summary + Marks(verb));
        if (verb.Options.Length == 0) return;
        output.WriteLine();
        output.WriteLine("options:");
        foreach (var option in verb.Options) Line(output, option.Written, option.Help);
    }

    /// <summary>The table of spec 14 §5, as Markdown: a row a verb, its options in the order of the table.</summary>
    public static void Markdown(TextWriter output)
    {
        output.WriteLine("| verb | words | options | lints | `--json` |");
        output.WriteLine("|---|---|---|---|---|");
        foreach (var verb in All)
        {
            var options = verb.Options.Length == 0 ? "—" : string.Join(", ", verb.Options.Select(o => $"`{o.Written}`"));
            output.WriteLine($"| `{verb.Name}` | {(verb.Words.Length == 0 ? "—" : $"`{verb.Words}`")} | {options} | {(verb.Lints ? "yes" : "—")} | {(verb.Json ? "yes" : "—")} |");
        }
    }

    private static string Marks(VerbSpec verb) =>
        verb.Lints && verb.Json ? " (lints, json)" : verb.Lints ? " (lints)" : verb.Json ? " (json)" : "";

    /// <summary>A help line: the left column 32 wide, the text wrapped at 100.</summary>
    private static void Line(TextWriter output, string left, string text)
    {
        const int column = 33;
        var words = text.Split(' ');
        var line = new StringBuilder("  " + left);
        if (line.Length >= column)
        {
            output.WriteLine(line.ToString());
            line.Clear().Append(' ', column);
        }
        else line.Append(' ', column - line.Length);
        var empty = true;
        foreach (var word in words)
        {
            if (!empty && line.Length + 1 + word.Length > 100)
            {
                output.WriteLine(line.ToString());
                line.Clear().Append(' ', column);
                empty = true;
            }
            if (!empty) line.Append(' ');
            line.Append(word);
            empty = false;
        }
        output.WriteLine(line.ToString());
    }
}
