using System.Reflection;
using Lyric5.Build;
using Lyric5.Toolchain;

namespace Lyric5;

/// <summary>
/// The Lyric 5 driver: one binary with verbs, strict about what it does not know (exit 2),
/// reporting what the program did wrong with exit 1, passing a program's own exit through. The
/// verbs and their options are the table in <see cref="Verbs"/>; each verb is read against it in a
/// file of its own.
/// </summary>
public static partial class Program
{
    public static int Main(string[] args)
    {
        try
        {
            if (TakeRenderOptions(ref args, out var lints) is { } refused) return refused;
            if (args.Length == 0) return Usage(Console.Out, 0);
            var exit = Dispatch(args, lints);
            // the JSON form closes with the count (16 §4); 'run' said it before its program ran
            Lyric.Core.Rendering.RenderStyle.Current.Events?.Summary();
            return exit;
        }
        catch (Exception crash)
        {
            return Crashed(crash);
        }
    }

    private static int Dispatch(string[] args, LintArgs lints)
    {
        if (args[0] is "--version" or "-V") return Version();
        if (args[0] is "--help" or "-h") return Usage(Console.Out, 0);
        if (Verbs.Find(args[0]) is not { } verb) return Unknown($"unknown verb '{args[0]}'");
        var rest = args[1..];
        // '--help' among the verb's own options, before a '--', asks for them
        var dash = Array.IndexOf(rest, "--");
        if (verb.Name != "help" && (dash < 0 ? rest : rest[..dash]).Any(a => a is "--help" or "-h"))
        {
            Verbs.Help(Console.Out, verb);
            return 0;
        }
        return verb.Name switch
        {
            "build" => Build(rest, lints, BuildVerb.Build),
            "run" => Build(rest, lints, BuildVerb.Run),
            "check" => Build(rest, lints, BuildVerb.Check),
            "expand" => Build(rest, lints, BuildVerb.Expand),
            "test" => Test(rest, lints),
            "bench" => Bench(rest, lints),
            "fix" => Fix(rest, lints),
            "env" => Env(rest),
            "update" => Update(rest),
            "add" => Add(rest),
            "remove" => Remove(rest),
            "clean" => Clean(rest),
            "metadata" => Metadata(rest),
            "explain" => Explain(rest),
            "version" => Verbs.Read(verb, rest, Unknown, out var unread) is { Words.Count: 0, Rest: null } ? Version()
                : unread != 0 ? unread : Unknown("'version' takes no words"),
            _ => Help(rest),
        };
    }

    /// <summary><c>help [&lt;verb&gt;]</c>: the verbs, or the verb's options; <c>--markdown</c>, the
    /// table of spec 14 §5.</summary>
    private static int Help(string[] args)
    {
        if (Verbs.Read(Verbs.Find("help")!, args, Unknown, out var exit) is not { } parsed) return exit;
        if (parsed.Flags.Contains("--markdown"))
        {
            Verbs.Markdown(Console.Out);
            return 0;
        }
        if (parsed.Words.Count == 0) return Usage(Console.Out, 0);
        if (parsed.Words.Count > 1 || Verbs.Find(parsed.Words[0]) is not { } verb)
            return Unknown($"no verb '{string.Join(' ', parsed.Words)}'");
        Verbs.Help(Console.Out, verb);
        return 0;
    }

    /// <summary>The toolchain's own failure (11 G4): not the program's fault, and worth a report.</summary>
    private static int Crashed(Exception crash)
    {
        {
            var message = $"the toolchain failed: {crash.GetType().Name}: {crash.Message}";
            var place = Lyric.Core.CompilerPosition.Describe();
            var trace = crash is Lyric.Core.CompilerDepthException ? Head(crash.StackTrace, 24) : crash.StackTrace;
            if (Lyric.Core.Rendering.RenderStyle.Current.Events is { } events)
            {
                // the JSON form (16 §4) says it as an event of its own, and closes the stream
                events.Ice("LYR-ICE0001", message, place, trace);
                events.Summary();
                return 101;
            }
            Console.Error.WriteLine($"error[LYR-ICE0001]: {message}");
            if (place is not null) Console.Error.WriteLine($"  while {place}");
            Console.Error.WriteLine("  = help: this is a bug in lyric5 — please report it with the program that triggered it");
            Console.Error.WriteLine(trace);
            return 101;
        }
    }

    /// <summary>
    /// The options of every verb that say how its diagnostics read (design/v5/spec/11 G5, G6, C7;
    /// M12a S2): <c>--color auto|always|never</c>, <c>--absolute-paths</c>, <c>--max-errors N</c>
    /// (0 for all), anywhere before a <c>--</c>. They are taken out of <paramref name="args"/> and
    /// set as <see cref="Lyric.Core.Rendering.RenderStyle.Current"/> — every verb, the same style, and
    /// set afresh by each command, so none leaks into the next. Paths are said relative to the
    /// package's root: the one <c>-C</c> names, or the one around the working directory.
    /// </summary>
    private static int? TakeRenderOptions(ref string[] args, out LintArgs lintArgs)
    {
        lintArgs = LintArgs.None;
        var kept = new List<string>();
        var color = "auto";
        var absolute = false;
        var json = false;
        var maxErrors = Lyric.Core.Rendering.RenderStyle.DefaultMaxErrors;
        string? from = null;
        var lints = new List<Lyric.Core.LintSettings.Rule>();
        var warnDeps = false;
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg == "--") { kept.AddRange(args[i..]); break; }
            if (arg == "--absolute-paths") { absolute = true; continue; }
            // the JSON form (16 §4) for the verbs that report findings; metadata's --json is its own
            if (arg == "--json" && kept.Count > 0 && Verbs.Find(kept[0]) is { Json: true }) { json = true; continue; }
            // the lints' levels (16 §5) for the verbs that compile: in their order, the later deciding among equals
            var compiles = kept.Count > 0 && Verbs.Find(kept[0]) is { Lints: true };
            if (compiles && arg == "--warn-deps") { warnDeps = true; continue; }
            if (compiles && arg is "-A" or "-W" or "-D")
            {
                if (i + 1 >= args.Length) return Unknown($"'{arg}' takes a lint, a group or 'warnings'");
                var selector = args[++i];
                if (Lyric.Core.DiagnosticCatalog.Select(selector) is null)
                    return Unknown(Lyric.Core.DiagnosticCatalog.Nearest(selector) is { } near
                        ? $"no lint, group or warning code '{selector}'; did you mean '{near}'?"
                        : $"no lint, group or warning code '{selector}' — 'lyric5 explain <name>' says what a lint is");
                var level = arg switch { "-A" => Lyric.Core.LintLevel.Allow, "-W" => Lyric.Core.LintLevel.Warn, _ => Lyric.Core.LintLevel.Deny };
                lints.Add(new Lyric.Core.LintSettings.Rule(selector, level, $"'{arg} {selector}' on the command line"));
                continue;
            }
            if (arg is "--color" or "--max-errors")
            {
                if (i + 1 >= args.Length) return Unknown($"'{arg}' takes a value");
                var value = args[++i];
                if (arg == "--color")
                {
                    if (value is not ("auto" or "always" or "never")) return Unknown($"'--color' takes auto, always or never, not '{value}'");
                    color = value;
                }
                else if (!int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out maxErrors))
                    return Unknown($"'--max-errors' takes a count, 0 for all, not '{value}'");
                continue;
            }
            if (arg == "-C" && i + 1 < args.Length) from = args[i + 1];
            kept.Add(arg);
        }
        args = [.. kept];

        var start = Path.GetFullPath(from ?? Directory.GetCurrentDirectory());
        var manifest = Directory.Exists(start) ? Project.FindManifest(start) : null;
        Lyric.Core.Rendering.RenderStyle.Current = new Lyric.Core.Rendering.RenderStyle
        {
            Color = Lyric.Core.Rendering.RenderStyle.ColorFor(color, !Console.IsErrorRedirected, Environment.GetEnvironmentVariable)!.Value,
            AbsolutePaths = absolute,
            MaxErrors = maxErrors,
            BaseDirectory = manifest is not null ? Path.GetDirectoryName(manifest) : Directory.GetCurrentDirectory(),
            Events = json ? new Lyric.Core.Rendering.EventWriter(Console.Out) : null,
        };
        lintArgs = new LintArgs(lints, warnDeps);
        return null;
    }

    /// <summary>The first lines of a stack trace: a recursion that reached the end of the stack
    /// has a few thousand frames, and they repeat after the first dozen.</summary>
    internal static string Head(string? trace, int lines)
    {
        if (trace is null) return "";
        var all = trace.Split('\n');
        return all.Length <= lines
            ? trace
            : string.Join('\n', all.Take(lines)) + $"\n   … {all.Length - lines} more frames";
    }

    private static int Usage(TextWriter output, int exit)
    {
        Verbs.Usage(output);
        return exit;
    }

    private static int Unknown(string what)
    {
        Say.Error(Console.Error, "LYR-CLI0003", $"{what}", "lyric5 help lists what exists");
        return 2;
    }

    // --- version -------------------------------------------------------------------------------

    private static int Version()
    {
        Console.Out.WriteLine($"lyric {DisplayVersion()}");
        Console.Out.WriteLine("  edition     5");
        Console.Out.WriteLine($"  c compiler  {DescribeCompiler(CCompiler.Locate())}");
        return 0;
    }

    /// <summary>
    /// The version a person reads: the informational version, which a dev build stamps as
    /// <c>5.0.0-dev.&lt;date&gt;+&lt;sha&gt;</c>. A local build carries the SDK's full source revision
    /// after the '+'; it is shortened to the seven characters a commit is usually named by.
    /// </summary>
    public static string DisplayVersion()
    {
        var stamped = typeof(Program).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "5.0.0";
        var plus = stamped.IndexOf('+');
        if (plus < 0) return stamped;
        var revision = stamped[(plus + 1)..];
        return revision.Length > 7 && revision.All(char.IsAsciiHexDigit)
            ? stamped[..(plus + 1)] + revision[..7]
            : stamped;
    }

    private static string DescribeCompiler(CCompiler? compiler) =>
        compiler?.ToString() ?? "none found — install zig (recommended), clang or gcc";
}
