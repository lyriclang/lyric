using Lyric.AST;
using Lyric.Compiler;
using Lyric.Core;
using Lyric.Embedding;

namespace Lyric.Cli.Build;

/// <summary>
/// <c>lyrbuild</c> — builds a project: runs its <c>build.lyr</c> and compiles what it declares,
/// or builds by convention when there is no script.
///
/// <para>The second binary that holds both libraries, for the same reason as <c>lyrrepl</c>: a
/// build script is a Lyric program that has to RUN, and what it declares has to be COMPILED
/// afterwards. Two subprocesses cannot do it — the artifacts live in the objects the script was
/// handed.</para>
///
/// <para>The script is never the entry point. The runner writes an entry AROUND it — an import
/// of <c>build.lyr</c>, a <c>main</c> that calls <c>build()</c>, then <c>std.build.finish()</c>,
/// then <c>after()</c> if the script has one — and compiles that. Everything the script says
/// about its artifacts is Lyric, in <c>std.build</c>; the entry is what lets that Lyric run
/// after <c>build</c> has returned, because a standard library function is nobody's root.</para>
///
/// <para>A build script runs with every capability. It writes files and starts processes, which
/// is the point of it being a script rather than a manifest — and it means <c>lyric build</c>
/// in a repository you did not write runs code you did not write, exactly as <c>make</c> and
/// <c>cmake</c> do.</para>
/// </summary>
public static class Program
{
    /// <summary>The file searched for in the directory the build was pointed at.</summary>
    public const string FileName = "build.lyr";

    /// <summary>The module name of the entry the runner writes around the script: never on
    /// disk, and an identifier no script would take.</summary>
    private const string EntryName = "__lyrbuild";

    private sealed record Flags(string? Directory, string? Stdlib, Profile Profile,
        IReadOnlyDictionary<string, string> Defines, IReadOnlyList<string> Only,
        bool Help, bool Version);

    public static int Main(string[] args)
    {
        ConsoleStreams.UseUtf8WhenRedirected();

        if (Parse(args) is not { } flags) return ExitCodes.Usage;

        if (flags.Version)
        {
            Console.Out.WriteLine($"lyrbuild {ToolchainVersion.Value}");
            return ExitCodes.Success;
        }

        var directory = Path.GetFullPath(flags.Directory ?? Directory.GetCurrentDirectory());
        var script = Path.Combine(directory, FileName);

        if (flags.Help)
        {
            PrintHelp();
            // In a project, the script's own options follow the fixed ones. They exist only
            // once the script has run, so it runs: 'build' collects, and nothing is compiled.
            return File.Exists(script) ? DescribeOptions(script, directory, flags) : ExitCodes.Success;
        }

        try
        {
            // The roots belong to the project and not to the build: lyric.json answers them
            // once, for the script itself and for everything it declares.
            var project = ProjectFile.Discover(directory);
            foreach (var warning in project?.Warnings ?? [])
                Console.Error.WriteLine(
                    $"warning: {Path.Combine(project!.Directory, ProjectFile.FileName)}: {warning}");

            return File.Exists(script)
                ? RunScript(script, directory, project, flags)
                : BuildByConvention(directory, project, flags);
        }
        catch (ProjectFileException broken)
        {
            return CliDiagnostics.Fail(Console.Error, broken.Code,
                $"{broken.Path}: {broken.Message}", ExitCodes.Failure);
        }
    }

    /// <summary>Strict: an option nobody knows is refused by name, because an option that is
    /// accepted and does nothing is the one that costs an afternoon.</summary>
    private static Flags? Parse(string[] args)
    {
        string? directory = null, stdlib = null;
        var profile = Profile.Default;
        var defines = new Dictionary<string, string>(StringComparer.Ordinal);
        var only = new List<string>();
        var help = false;
        var version = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--help" or "-h":
                    help = true;
                    break;

                case "--version" or "-v":
                    version = true;
                    break;

                case "--stdlib":
                    if (Value(args, ref i, "directory") is not { } stdlibValue) return null;
                    stdlib = stdlibValue;
                    break;

                case "--profile":
                    if (Value(args, ref i, "name (debug or release)") is not { } name) return null;
                    if (Profile.Named(name) is not { } named)
                    {
                        CliDiagnostics.Fail(Console.Error, CliDiagnostics.UnknownCommand,
                            $"--profile: unknown profile '{name}' (expected debug or release)",
                            ExitCodes.Usage);
                        return null;
                    }
                    profile = named;
                    break;

                case "--release":
                    profile = Profile.Release;
                    break;

                case "--debug":
                    profile = Profile.Debug;
                    break;

                case "--only":
                    if (Value(args, ref i, "artifact name") is not { } artifact) return null;
                    only.Add(artifact);
                    break;

                case "-D":
                    if (Value(args, ref i, "name or name=value") is not { } define) return null;
                    if (!Define(defines, define)) return null;
                    break;

                default:
                    // '-Dname=value', the spelling every C compiler taught.
                    if (args[i].StartsWith("-D", StringComparison.Ordinal) && args[i].Length > 2)
                    {
                        if (!Define(defines, args[i][2..])) return null;
                        break;
                    }

                    if (args[i].StartsWith('-') || directory is not null)
                    {
                        CliDiagnostics.Fail(Console.Error, CliDiagnostics.UnknownCommand,
                            $"unknown argument: {args[i]} — try 'lyrbuild --help'", ExitCodes.Usage);
                        return null;
                    }

                    directory = args[i];
                    break;
            }
        }

        return new Flags(directory, stdlib, profile, defines, only, help, version);
    }

    private static string? Value(string[] args, ref int i, string what)
    {
        if (++i < args.Length) return args[i];

        CliDiagnostics.Fail(Console.Error, CliDiagnostics.MissingArgument,
            $"{args[i - 1]}: missing {what} argument", ExitCodes.Usage);
        return null;
    }

    /// <summary><c>name</c> or <c>name=value</c>. A name given twice keeps the last value, as
    /// every other option does.</summary>
    private static bool Define(Dictionary<string, string> defines, string text)
    {
        var separator = text.IndexOf('=');
        var name = separator < 0 ? text : text[..separator];
        if (name.Length == 0)
        {
            CliDiagnostics.Fail(Console.Error, CliDiagnostics.MissingArgument,
                "-D: missing the option's name", ExitCodes.Usage);
            return false;
        }

        defines[name] = separator < 0 ? "" : text[(separator + 1)..];
        return true;
    }

    /// <summary>The compile of the SCRIPT: the debug profile whatever the build's is, because
    /// a script is run and never shipped, and its own root is the project directory, where
    /// <c>build.lyr</c> lies — the project's <c>sourceRoot</c> is for the artifacts.</summary>
    private static CompilerOptions ScriptOptions(string directory, Flags flags) =>
        Profile.Debug.Options() with { StdlibRoot = flags.Stdlib, SourceRoot = directory };

    private static int RunScript(string script, string directory, ProjectFile? project, Flags flags)
    {
        var scriptOptions = ScriptOptions(directory, flags);

        // Checked first, on its own. The entry the runner writes imports the script, and an
        // error in the script has to be reported against the script — with file, line and
        // column like every other Lyric error — rather than as an import that failed inside a
        // file nobody wrote. The check also says which hooks the script has.
        var analysis = SourceCompiler.Check(script, scriptOptions);
        analysis.Diagnostics.RenderText(Console.Error);
        if (!analysis.Ok || analysis.Model is null) return ExitCodes.Failure;

        if (Hook(analysis.Model, "build") is not { } build)
            return CliDiagnostics.Fail(Console.Error, CliDiagnostics.BuildScriptFailed,
                $"{FileName}: no 'build' function — a build script declares 'pub fn build() {{ … }}'",
                ExitCodes.Failure);
        if (build.Parameters.Length > 0)
            return CliDiagnostics.Fail(Console.Error, CliDiagnostics.BuildScriptFailed,
                $"{FileName}: 'build' takes no parameters", ExitCodes.Failure);

        var after = Hook(analysis.Model, "after");
        if (after is { Parameters.Length: > 0 })
            return CliDiagnostics.Fail(Console.Error, CliDiagnostics.BuildScriptFailed,
                $"{FileName}: 'after' takes no parameters", ExitCodes.Failure);

        var session = new BuildSession(directory, project, flags.Stdlib, flags.Profile,
            flags.Defines, flags.Only, Console.Out, Console.Error);

        if (Execute(Entry(after is not null), directory, scriptOptions, session) is not { } exit)
            return ExitCodes.Failure;

        return exit == ExitCodes.Success ? ExitCodes.Success
            : session.UsageError ? ExitCodes.Usage
            : ExitCodes.Failure;
    }

    /// <summary>A <c>pub fn</c> of the script by name, or <c>null</c>. The script's own
    /// declarations only; what it imports is nobody's hook.</summary>
    private static FunctionDecl? Hook(SemanticModel model, string name) =>
        model.Entry.Declarations.OfType<FunctionDecl>()
            .FirstOrDefault(f => f.IsPublic && f.Name == name);

    /// <summary>
    /// The entry around the script. <c>build</c> runs first and alone; <c>finish</c> hands the
    /// artifacts over and compiles them; <c>after</c>, when the script has one, runs only when
    /// every artifact was written. The exit code is one bit: the failures were reported where
    /// they happened.
    /// </summary>
    private static string Entry(bool withAfter) => withAfter
        ? """
          import build { build, after };
          import std.build as b;

          fn main(): int {
              build();
              let failed = b.finish();
              if (failed == 0) {
                  after();
              }
              return if (failed == 0) 0 else 1;
          }
          """
        : """
          import build { build };
          import std.build as b;

          fn main(): int {
              build();
              return if (b.finish() == 0) 0 else 1;
          }
          """;

    /// <summary>The entry for <c>--help</c>: <c>build</c> runs and collects, nothing is
    /// compiled and nothing runs after.</summary>
    private const string HelpEntry = """
        import build { build };

        fn main(): int {
            build();
            return 0;
        }
        """;

    /// <summary>
    /// Compiles the entry, binds the natives and runs it in the project directory.
    ///
    /// <para>Returns the entry's exit code, or <c>null</c> for a failure it has already
    /// reported: the seam between entry and script, a panic, a question the toolchain could
    /// not answer.</para>
    /// </summary>
    private static int? Execute(string entry, string directory, CompilerOptions scriptOptions,
        BuildSession session)
    {
        // Disposed with the build: a build script that leaves a file open holds it until this
        // process ends otherwise, and on Windows that locks it against whatever runs next.
        using var vm = new LangVm(new HostOptions
        {
            // A build script writes files and starts processes. Withholding that would leave a
            // manifest with parentheses.
            Capabilities = Capability.All,
            StdlibRoot = scriptOptions.StdlibRoot,
            SourceRoot = directory,
            Profile = Profile.Debug,
            Output = Console.Out,
            Error = Console.Error,
        });

        session.Register(vm);

        ScriptModule module;
        try
        {
            module = vm.Compile(entry, EntryName);
        }
        catch (EmbeddingException)
        {
            // The script checked clean, so what failed is the seam: a 'build' the entry cannot
            // call as it is written. Compiled once more to render it with its spans — the
            // exception carries the diagnostics as data but not the sources they point into.
            SourceCompiler.Compile(ScriptSource.FromText(EntryName, entry), scriptOptions)
                .Diagnostics.RenderText(Console.Error);
            return null;
        }

        // A relative path has to mean the same thing everywhere in the script: a
        // 'writeText("src/x.lyr", …)' resolves against the project, as the declarations do.
        var caller = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(directory);
        try
        {
            return vm.Run(module);
        }
        catch (ScriptPanicException panic)
        {
            CliDiagnostics.Fail(Console.Error, CliDiagnostics.BuildScriptFailed,
                $"{FileName}: {panic.Message}", ExitCodes.Failure);
            foreach (var frame in panic.Backtrace)
                Console.Error.WriteLine($"    in {frame}");
            return null;
        }
        catch (HostFunctionException host)
        {
            // A question the script asked that the toolchain could not answer — a profile it
            // does not know. Its own words, without the wrapper's.
            CliDiagnostics.Fail(Console.Error, CliDiagnostics.BuildScriptFailed,
                $"{FileName}: {host.InnerException?.Message ?? host.Message}", ExitCodes.Failure);
            return null;
        }
        catch (ScriptException refused)
        {
            // The module loaded and could not be bound or run: a native the toolchain did not
            // register, a capability it does not grant.
            CliDiagnostics.Fail(Console.Error, CliDiagnostics.BuildScriptFailed,
                $"{FileName}: {refused.Message}", ExitCodes.Failure);
            return null;
        }
        finally
        {
            Directory.SetCurrentDirectory(caller);
        }
    }

    /// <summary>
    /// Without a script, the convention: <c>main.lyr</c> under the source root is the one
    /// program, named after the project, landing in <c>out/&lt;profile&gt;/</c>; a source root
    /// without a <c>main.lyr</c> is a library and is checked; neither is an error that names
    /// both ways.
    /// </summary>
    private static int BuildByConvention(string directory, ProjectFile? project, Flags flags)
    {
        if (flags.Defines.Count > 0 || flags.Only.Count > 0)
            return CliDiagnostics.Fail(Console.Error, CliDiagnostics.UnknownCommand,
                $"-D and --only are answered by a {FileName}, and {directory} has none",
                ExitCodes.Usage);

        var sourceRoot = Path.GetFullPath(project?.SourceRoot ?? directory);
        var name = project?.Name ?? new DirectoryInfo(directory).Name;
        var profile = flags.Profile;
        var session = new BuildSession(directory, project, flags.Stdlib, profile,
            flags.Defines, flags.Only, Console.Out, Console.Error);

        var main = Path.Combine(sourceRoot, "main.lyr");
        if (File.Exists(main))
        {
            var program = new Declared(Declared.Executable, name, main,
                Path.Combine(directory, "out", profile.Name, $"{name}.lyrbc"),
                profile.Optimize, profile.SourceMap, profile.DebugInfo, profile.DenyWarnings);
            return session.Build(program) ? ExitCodes.Success : ExitCodes.Failure;
        }

        if (Directory.Exists(sourceRoot)
            && Directory.EnumerateFiles(sourceRoot, "*.lyr", SearchOption.AllDirectories).Any())
        {
            var library = new Declared(Declared.Library, name, sourceRoot, "",
                profile.Optimize, profile.SourceMap, profile.DebugInfo, profile.DenyWarnings);
            return session.Build(library) ? ExitCodes.Success : ExitCodes.Failure;
        }

        return CliDiagnostics.Fail(Console.Error, CliDiagnostics.NoBuildScript,
            $"nothing to build in {directory}: no {FileName}, and no main.lyr under "
            + $"{sourceRoot} to build by convention", ExitCodes.Usage);
    }

    /// <summary>The script's options after the fixed help: <c>build</c> runs so the script can
    /// ask, and what it asked about is listed.</summary>
    private static int DescribeOptions(string script, string directory, Flags flags)
    {
        var scriptOptions = ScriptOptions(directory, flags);
        var analysis = SourceCompiler.Check(script, scriptOptions);
        if (!analysis.Ok || analysis.Model is null || Hook(analysis.Model, "build") is null)
        {
            analysis.Diagnostics.RenderText(Console.Error);
            return CliDiagnostics.Fail(Console.Error, CliDiagnostics.BuildScriptFailed,
                $"{FileName}: cannot list its options, because it does not run", ExitCodes.Failure);
        }

        var session = new BuildSession(directory, project: null, flags.Stdlib, flags.Profile,
            flags.Defines, flags.Only, TextWriter.Null, Console.Error);
        if (Execute(HelpEntry, directory, scriptOptions, session) is null) return ExitCodes.Failure;

        Console.Out.WriteLine();
        if (session.Options.Count == 0)
        {
            Console.Out.WriteLine($"{FileName} declares no options.");
            return ExitCodes.Success;
        }

        Console.Out.WriteLine($"Options of {FileName} (-D name, or -D name=value):");
        var width = session.Options.Max(o => o.Name.Length);
        foreach (var option in session.Options)
            Console.Out.WriteLine(
                $"  {option.Name.PadRight(width)}   {option.Help}{(option.IsFlag ? "" : " (takes a value)")}");
        return ExitCodes.Success;
    }

    private static void PrintHelp()
    {
        Console.Out.WriteLine("Usage: lyrbuild [directory] [options]");
        Console.Out.WriteLine();
        Console.Out.WriteLine($"Runs the {FileName} in the directory and compiles what it declares.");
        Console.Out.WriteLine("Without one, builds by convention: main.lyr under the source root");
        Console.Out.WriteLine("is the program, named after the project; without that, the source");
        Console.Out.WriteLine("root is a library and is checked. Without a directory, the working");
        Console.Out.WriteLine("directory.");
        Console.Out.WriteLine();
        Console.Out.WriteLine("Options:");
        Console.Out.WriteLine("  --profile <name>   The profile the build starts from:");
        Console.Out.WriteLine("                     debug (the default) or release");
        Console.Out.WriteLine("  --debug, --release The same, shorter");
        Console.Out.WriteLine("  -D <name[=value]>  An option for the script; -D name for a flag");
        Console.Out.WriteLine("  --only <name>      Build this artifact alone; may be repeated");
        Console.Out.WriteLine("  --stdlib <dir>     Where the stdlib lives (beats $LYRIC_STDLIB)");
        Console.Out.WriteLine("  --version, -v      Show the toolchain version");
        Console.Out.WriteLine("  --help, -h         Show this help, and the script's options");
        Console.Out.WriteLine();
        Console.Out.WriteLine("A build script runs with every capability: it may write files and");
        Console.Out.WriteLine("start processes, like make or cmake.");
    }
}
