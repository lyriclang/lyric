using Lyric.AST;
using Lyric.Compiler;
using Lyric.Core;
using Lyric.Ir;
using Lyric.Lexing;

namespace Lyric.Cli.Compiler;

/// <summary>
/// <c>lyrc</c> — the compiler.
///
/// <para>One job per invocation; it never executes anything. The debug dumps
/// (<c>tokenize</c>, <c>parse</c>, <c>lower</c>) live here rather than in the driver.</para>
///
/// <para>Every command runs through <see cref="SourceCompiler"/>. This program holds no pipeline
/// logic, only argument handling and output.</para>
/// </summary>
public static class Program
{
    public static int Main(string[] rawArgs)
    {
        ConsoleStreams.UseUtf8WhenRedirected();

        var (options, args, optionError) = ToolOptions.Parse(rawArgs);
        if (optionError is not null)
            return CliDiagnostics.Fail(Console.Error, CliDiagnostics.UnknownCommand,
                optionError, ExitCodes.Usage);

        using var terminal = new TerminalOutput(Console.Out, Console.Error, options);

        if (args.Length == 0) { PrintHelp(); return ExitCodes.Success; }

        try
        {
            return args[0] switch
            {
                "--version" or "-v" => Version(terminal),
                "--help" or "-h" => Help(),
                "build" => WithFile(args, "build", terminal, Build),
                "check" => Check(args, terminal),
                "lower" => WithFile(args, "lower", terminal, Lower),
                "parse" => WithFile(args, "parse", terminal, Parse),
                "tokenize" => WithFile(args, "tokenize", terminal, Tokenize),
                _ => CliDiagnostics.Fail(Console.Error, CliDiagnostics.UnknownCommand,
                    $"unknown command: {args[0]} — try 'lyrc --help'", ExitCodes.Usage),
            };
        }
        catch (ProjectFileException broken)
        {
            // Caught here rather than where it is read: 'Options' builds a value and has nowhere to
            // put a diagnostic, and threading a result type through five commands would put the
            // handling in five places for a failure that ends all of them the same way.
            return CliDiagnostics.Fail(Console.Error, broken.Code,
                $"{broken.Path}: {broken.Message}", ExitCodes.Failure);
        }
        catch (InternalCompilationException bug)
        {
            // §12.4: the compiler failing is not the program failing. Escaping, this was a stack
            // trace with no code, no position and no file -- and under --json it landed in the
            // middle of the document and destroyed it for whatever was reading.
            return CliDiagnostics.FailInternal(Console.Error, bug);
        }
    }

    /// <summary>
    /// The options behind <c>command file</c>, parsed once and strictly: an option nobody knows is
    /// refused rather than overlooked. A flag that silently does nothing is the one that costs an
    /// afternoon — the reason a <c>lyric.json</c> key nobody knows warns, applied to the command
    /// line, where the person is right there to be told.
    /// </summary>
    private sealed record Flags
    {
        public string? Output { get; init; }
        public string? Stdlib { get; init; }
        public bool Emit { get; init; }
        public bool DenyWarnings { get; init; }

        /// <summary>The bundle. The three nullable fields below each override one of its
        /// entries; unset, the entry stands.</summary>
        public Profile Profile { get; init; } = Profile.Default;
        public bool? Optimize { get; init; }
        public bool? SourceMap { get; init; }
        public bool? DebugInfo { get; init; }

        public IrPasses Passes { get; init; } = IrPasses.All;
        public bool Fusion { get; init; } = true;
    }

    private sealed record Refusal(string Code, string Message);

    /// <summary>
    /// Reads the options behind <c>command file</c>. Which command takes which option is decided
    /// here rather than tolerated: <c>-o</c> belongs to the one command that writes, <c>--emit</c>
    /// to the one that reads back, and the profile flags to every command that lowers.
    /// </summary>
    /// <param name="firstOption">Where the options start: behind the file, or behind the
    /// command itself for the one command that takes no file.</param>
    private static (Flags? Flags, Refusal? Refused) ParseFlags(string command, string[] args,
        int firstOption = 2)
    {
        var flags = new Flags();
        for (var i = firstOption; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-o" or "--output":
                    if (command != "build")
                        return Refuse(CliDiagnostics.UnknownCommand,
                            $"{args[i]}: only 'build' writes a file");
                    if (++i >= args.Length)
                        return Refuse(CliDiagnostics.MissingArgument, "-o: missing path argument");
                    // An EMPTY path is the same failure as a missing one, and it has to be caught
                    // here: further down it reaches File.WriteAllBytes, which throws
                    // ArgumentException, which nothing catches — the compiler ended a successful
                    // build with a stack trace. A shell writes it by accident often enough
                    // ('-o "$OUT"' with OUT unset) that the message names the shape.
                    if (string.IsNullOrWhiteSpace(args[i]))
                        return Refuse(CliDiagnostics.MissingArgument,
                            "-o: the path is empty — an unset variable in the command line looks "
                            + "exactly like this");
                    flags = flags with { Output = args[i] };
                    break;

                case "--stdlib":
                    if (++i >= args.Length)
                        return Refuse(CliDiagnostics.MissingArgument,
                            "--stdlib: missing directory argument");
                    flags = flags with { Stdlib = args[i] };
                    break;

                case "--emit":
                    if (command != "check")
                        return Refuse(CliDiagnostics.UnknownCommand,
                            "--emit: only 'check' has a step to add");
                    flags = flags with { Emit = true };
                    break;

                case "--deny-warnings":
                    flags = flags with { DenyWarnings = true };
                    break;

                case "--profile":
                    if (++i >= args.Length)
                        return Refuse(CliDiagnostics.MissingArgument,
                            "--profile: missing name (debug or release)");
                    if (Profile.Named(args[i]) is not { } named)
                        return Refuse(CliDiagnostics.UnknownCommand,
                            $"--profile: unknown profile '{args[i]}' (expected debug or release)");
                    flags = flags with { Profile = named };
                    break;

                case "--release":
                    flags = flags with { Profile = Profile.Release };
                    break;

                case "--debug":
                    flags = flags with { Profile = Profile.Debug };
                    break;

                case "--optimize":
                    flags = flags with { Optimize = true };
                    break;

                case "--no-optimize":
                    flags = flags with { Optimize = false };
                    break;

                case "--source-map":
                    flags = flags with { SourceMap = true };
                    break;

                case "--no-source-map":
                    flags = flags with { SourceMap = false };
                    break;

                case "--debug-info":
                    flags = flags with { DebugInfo = true };
                    break;

                case "--no-debug-info":
                    flags = flags with { DebugInfo = false };
                    break;

                case "--no-inline":
                    flags = flags with { Passes = flags.Passes & ~IrPasses.Inline };
                    break;

                case "--no-scalar-replacement":
                    flags = flags with { Passes = flags.Passes & ~IrPasses.ScalarReplacement };
                    break;

                case "--no-devirtualize":
                    flags = flags with { Passes = flags.Passes & ~IrPasses.Devirtualize };
                    break;

                case "--no-fusion":
                    flags = flags with { Fusion = false };
                    break;

                default:
                    return Refuse(CliDiagnostics.UnknownCommand, args[i].StartsWith('-')
                        ? $"unknown option '{args[i]}' — try 'lyrc --help'"
                        : $"unexpected argument '{args[i]}' — {command} takes one file");
            }
        }

        return (flags, null);

        static (Flags?, Refusal?) Refuse(string code, string message) =>
            (null, new Refusal(code, message));
    }

    /// <summary>Compiles to <c>.lyrbc</c>. Without <c>-o</c> the output lands next to the
    /// source.</summary>
    private static int Build(string path, string[] args, TerminalOutput terminal)
    {
        var (flags, refused) = ParseFlags("build", args);
        if (flags is null)
            return CliDiagnostics.Fail(Console.Error, refused!.Code, refused.Message, ExitCodes.Usage);

        var output = flags.Output ?? Path.ChangeExtension(path, ".lyrbc");

        var result = SourceCompiler.Compile(path, Options(path, flags, terminal, out var suspect));
        terminal.Render(result.Diagnostics);
        if (!result.Ok || result.Bytes is null) return ExitCodes.Failure;
        if (DeniedWarnings(flags, result, suspect) is { } denied) return denied;

        try
        {
            File.WriteAllBytes(output, result.Bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return CliDiagnostics.Fail(Console.Error, CliDiagnostics.OutputUnwritable,
                $"cannot write {output}: {ex.Message}", ExitCodes.Failure);
        }

        terminal.Info($"{output}: {new FileInfo(output).Length} bytes");
        return ExitCodes.Success;
    }

    /// <summary>
    /// Everything a build does except writing the file — up to the IR by default, and with
    /// <c>--emit</c> all the way through the bytes.
    ///
    /// <para>The flag exists because the two answers differ. A program can type-check and lower in
    /// silence and still produce a module the loader refuses; that happened, it was found by a
    /// test that opened a window, and a project which compiles every one of its files as an entry
    /// has no other way to ask. <c>--emit</c> writes nothing — the bytes are produced, read back
    /// and dropped.</para>
    /// </summary>
    /// <summary>
    /// <c>check</c> is the one command that takes a PROJECT as well as a file: without an
    /// argument, or with a directory, every module of the project is checked as ONE
    /// compilation.
    ///
    /// <para>That is a different question from checking each file on its own, and a stricter
    /// one: two modules claiming one name, an import that resolves nowhere, a test that no
    /// longer matches the function it tests are all answered only when the files are read
    /// together. It is what the language server does for an open project, from the command
    /// line.</para>
    /// </summary>
    private static int Check(string[] args, TerminalOutput terminal)
    {
        var target = args.Length > 1 && !args[1].StartsWith('-') ? args[1] : null;

        if (target is not null && !Directory.Exists(target))
            return Check(target, args, terminal);

        var (flags, refused) = ParseFlags("check", args, target is null ? 1 : 2);
        if (flags is null)
            return CliDiagnostics.Fail(Console.Error, refused!.Code, refused.Message, ExitCodes.Usage);

        if (flags.Emit)
            return CliDiagnostics.Fail(Console.Error, CliDiagnostics.UnknownCommand,
                "--emit: a project has no one module to emit — name a file", ExitCodes.Usage);

        // Without an argument there has to BE a project: a directory that merely holds .lyr
        // files is not one, and reading every file under the working directory because
        // somebody typed 'check' is not a default anybody asked for. A directory named
        // outright is a deliberate act and needs no manifest.
        if (target is null && ProjectFile.Discover(Directory.GetCurrentDirectory()) is null)
            return CliDiagnostics.Fail(Console.Error, CliDiagnostics.MissingArgument,
                "check: missing file argument — 'check <dir>' checks a directory, and a "
                + $"{ProjectFile.FileName} makes this one a project", ExitCodes.Usage);

        return CheckProject(Path.GetFullPath(target ?? "."), flags, terminal);
    }

    /// <summary>
    /// Every <c>.lyr</c> under the source root as one compilation, and the test root as a
    /// second one that imports it.
    ///
    /// <para>Two compilations rather than one: a test file and a source file may both declare
    /// <c>main</c>, and the tests are not part of what ships — the same split
    /// <c>lyrtest</c> makes, and the reason <c>@Test</c> needs no build rule.</para>
    /// </summary>
    private static int CheckProject(string directory, Flags flags, TerminalOutput terminal)
    {
        var project = ProjectFile.Discover(directory);
        foreach (var warning in project?.Warnings ?? [])
            CliDiagnostics.Warn(Console.Error, CliDiagnostics.ProjectFileSuspect,
                $"{Path.Combine(project!.Directory, ProjectFile.FileName)}: {warning}");

        var sourceRoot = project?.SourceRoot ?? directory;
        var options = flags.Profile.Options() with
        {
            StdlibRoot = flags.Stdlib,
            Progress = terminal,
            SourceRoot = sourceRoot,
            NativeRoots = project?.NativeRoots,
            DependencyRoots = project?.Dependencies,
            Optimize = flags.Optimize ?? flags.Profile.Optimize,
            SourceMap = flags.SourceMap ?? flags.Profile.SourceMap,
            DebugInfo = flags.DebugInfo ?? flags.Profile.DebugInfo,
            Passes = flags.Passes,
            Fusion = flags.Fusion,
        };

        var sources = Modules(sourceRoot);
        if (sources.Count == 0)
            return CliDiagnostics.Fail(Console.Error, CliDiagnostics.FileUnreadable,
                $"no .lyr file under {sourceRoot} — 'check' takes a file, a directory or "
                + "nothing at all", ExitCodes.Usage);

        var warnings = project?.Warnings.Count ?? 0;
        var checkedModules = sources.Count;

        var result = SourceCompiler.CheckProject(sources, options);
        terminal.Render(result.Diagnostics);
        warnings += result.Diagnostics.WarningCount;
        if (!result.Ok) return ExitCodes.Failure;

        // The tests, against the same source root: they import what the project imports. The
        // root is the one lyrtest uses, the named one or the conventional 'tests/' — a project
        // whose tests are only found by one of the two tools would be worse than none.
        var testRoot = project?.TestRoot
                       ?? Path.Combine(project?.Directory ?? directory, "tests");
        if (Directory.Exists(testRoot))
        {
            var tests = Modules(testRoot);
            if (tests.Count > 0)
            {
                var checkedTests = SourceCompiler.CheckProject(tests, options);
                terminal.Render(checkedTests.Diagnostics);
                warnings += checkedTests.Diagnostics.WarningCount;
                if (!checkedTests.Ok) return ExitCodes.Failure;
                checkedModules += tests.Count;
            }
        }

        if (warnings > 0 && (flags.DenyWarnings || flags.Profile.DenyWarnings))
            return CliDiagnostics.Fail(Console.Error, CliDiagnostics.WarningsDenied,
                warnings == 1 ? "1 warning denied by --deny-warnings"
                    : $"{warnings} warnings denied by --deny-warnings",
                ExitCodes.Failure);

        terminal.Info($"{directory}: {checkedModules} "
            + $"{(checkedModules == 1 ? "module" : "modules")} ok");
        return ExitCodes.Success;
    }

    /// <summary>Every module under a root, named the way an import of it would be — the
    /// inverse of module path to file path, so a root is the module an import finds rather
    /// than a second copy of it.</summary>
    private static List<ScriptSource> Modules(string root)
    {
        if (!Directory.Exists(root)) return [];

        var files = Directory.GetFiles(root, "*.lyr", SearchOption.AllDirectories);
        Array.Sort(files, StringComparer.Ordinal);

        return files
            .Select(file => ScriptSource.FromDisk(file, ScriptSource.ModuleNameUnder(root, file)))
            .ToList();
    }

    private static int Check(string path, string[] args, TerminalOutput terminal)
    {
        var (flags, refused) = ParseFlags("check", args);
        if (flags is null)
            return CliDiagnostics.Fail(Console.Error, refused!.Code, refused.Message, ExitCodes.Usage);

        var options = Options(path, flags, terminal, out var suspect);
        var result = flags.Emit
            ? SourceCompiler.Compile(path, options)
            : SourceCompiler.Check(path, options);

        terminal.Render(result.Diagnostics);
        if (!result.Ok) return ExitCodes.Failure;
        if (DeniedWarnings(flags, result, suspect) is { } denied) return denied;

        terminal.Info($"{path}: ok");
        return ExitCodes.Success;
    }

    /// <summary>
    /// The <c>--deny-warnings</c> gate, AFTER the render: the warnings keep their severity in the
    /// output, and one error at the end carries the policy into the exit code. Deliberately not
    /// rustc's way (<c>-D</c> relabels them as errors) — what a diagnostic IS must not depend on a
    /// flag. A profile may carry the policy too, which is how a build script asks for it.
    /// </summary>
    private static int? DeniedWarnings(Flags flags, CompileResult result, int suspect)
    {
        var warnings = result.Diagnostics.WarningCount + suspect;
        if (warnings == 0 || !(flags.DenyWarnings || flags.Profile.DenyWarnings)) return null;

        return CliDiagnostics.Fail(Console.Error, CliDiagnostics.WarningsDenied,
            warnings == 1 ? "1 warning denied by --deny-warnings"
                : $"{warnings} warnings denied by --deny-warnings",
            ExitCodes.Failure);
    }

    /// <summary>Debug output of the mid-level IR. Lowers only when sema reported no errors; the
    /// profile and the diagnostic switches apply, which is what makes the dump useful for
    /// looking at what one pass did.</summary>
    private static int Lower(string path, string[] args, TerminalOutput terminal)
    {
        var (flags, refused) = ParseFlags("lower", args);
        if (flags is null)
            return CliDiagnostics.Fail(Console.Error, refused!.Code, refused.Message, ExitCodes.Usage);

        var result = SourceCompiler.Lower(path, Options(path, flags, terminal, out _));
        terminal.Render(result.Diagnostics);
        if (!result.Ok || result.Ir is null) return ExitCodes.Failure;

        terminal.Payload(IrPrinter.Dump(result.Ir));
        return ExitCodes.Success;
    }

    private static int Parse(string path, string[] args, TerminalOutput terminal)
    {
        var (flags, refused) = ParseFlags("parse", args);
        if (flags is null)
            return CliDiagnostics.Fail(Console.Error, refused!.Code, refused.Message, ExitCodes.Usage);

        var (sources, diagnostics, id) = SourceCompiler.Read(path);
        if (!id.IsValid) { terminal.Render(diagnostics); return ExitCodes.Failure; }

        var module = new Parsing.Parser(sources, id, diagnostics).ParseModule();
        terminal.Payload(AstDumper.Dump(module, sources));
        terminal.Render(diagnostics);
        return diagnostics.HasErrors ? ExitCodes.Failure : ExitCodes.Success;
    }

    private static int Tokenize(string path, string[] args, TerminalOutput terminal)
    {
        var (flags, refused) = ParseFlags("tokenize", args);
        if (flags is null)
            return CliDiagnostics.Fail(Console.Error, refused!.Code, refused.Message, ExitCodes.Usage);

        var (sources, diagnostics, id) = SourceCompiler.Read(path);
        if (!id.IsValid) { terminal.Render(diagnostics); return ExitCodes.Failure; }

        var lexer = new Lexer(sources, id, diagnostics);
        var tokens = new List<Token>();
        Token token;
        do
        {
            token = lexer.Next();
            tokens.Add(token);
        } while (token.TokenKind != TokenKind.Eof);

        terminal.Payload(TokenDumper.Dump(tokens, sources));
        terminal.Render(diagnostics);
        return diagnostics.HasErrors ? ExitCodes.Failure : ExitCodes.Success;
    }

    /// <summary>
    /// What the compiler needs besides the file: the profile's options with the flags' overrides,
    /// <c>--stdlib</c> (beats <c>LYRIC_STDLIB</c>), and the roots.
    ///
    /// <para>A <c>lyric.json</c> above the source supplies the module root and the native roots.
    /// Without one nothing changes: the entry file's directory is the root, as it was before the
    /// file existed.</para>
    /// </summary>
    private static CompilerOptions Options(string path, Flags flags, TerminalOutput terminal,
        out int suspect)
    {
        var project = ProjectFile.Discover(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");

        // A key nobody knows is tolerated, so a file written for a later version still loads — the
        // same rule the bytecode reader follows for a section it does not know. The warning is what
        // keeps a typo from being silent. It counts for --deny-warnings like any other, which is
        // why the count travels out.
        suspect = project?.Warnings.Count ?? 0;
        foreach (var warning in project?.Warnings ?? [])
            CliDiagnostics.Warn(Console.Error, CliDiagnostics.ProjectFileSuspect,
                $"{Path.Combine(project!.Directory, ProjectFile.FileName)}: {warning}");

        var profile = flags.Profile;
        return profile.Options() with
        {
            StdlibRoot = flags.Stdlib,
            Progress = terminal,
            SourceRoot = project?.SourceRoot,
            NativeRoots = project?.NativeRoots,
            DependencyRoots = project?.Dependencies,
            Optimize = flags.Optimize ?? profile.Optimize,
            SourceMap = flags.SourceMap ?? profile.SourceMap,
            DebugInfo = flags.DebugInfo ?? profile.DebugInfo,
            Passes = flags.Passes,
            Fusion = flags.Fusion,
            // The VM in a sandbox evaluates 'comptime' sites; see VmComptimeRunner.
            ComptimeRunner = new Lyric.Vm.VmComptimeRunner(),
        };
    }

    /// <summary>Every command here takes exactly one required file; the check lives in one
    /// place.</summary>
    private static int WithFile(string[] args, string command, TerminalOutput terminal,
        Func<string, string[], TerminalOutput, int> run)
    {
        if (args.Length < 2)
            return CliDiagnostics.Fail(Console.Error, CliDiagnostics.MissingArgument,
                $"{command}: missing file argument", ExitCodes.Usage);
        return run(args[1], args, terminal);
    }

    private static int Version(TerminalOutput terminal)
    {
        terminal.Payload($"lyrc {ToolchainVersion.Value}\n");
        return ExitCodes.Success;
    }

    private static int Help() { PrintHelp(); return ExitCodes.Success; }

    private static void PrintHelp()
    {
        Console.Out.WriteLine("lyrc — the Lyric compiler");
        Console.Out.WriteLine();
        Console.Out.WriteLine("Usage: lyrc <command> <file> [options]");
        Console.Out.WriteLine();
        Console.Out.WriteLine("Commands:");
        Console.Out.WriteLine("  build <file> [-o <out>]  Compile .lyr to .lyrbc");
        Console.Out.WriteLine("  check <file> [--emit]    Compile without writing a file");
        Console.Out.WriteLine("  check [<dir>]            Check a whole project: its source root as");
        Console.Out.WriteLine("                           one compilation, its test root as another");
        Console.Out.WriteLine("  lower <file>             Print the mid-IR dump (debug)");
        Console.Out.WriteLine("  parse <file>             Print the AST dump (debug)");
        Console.Out.WriteLine("  tokenize <file>          Print the token stream (debug)");
        Console.Out.WriteLine();
        Console.Out.WriteLine("Profile (debug unless $LYRIC_PROFILE says release; every field overridable):");
        Console.Out.WriteLine("  --profile <name>         debug or release");
        Console.Out.WriteLine("  --debug, --release       The same, shorter");
        Console.Out.WriteLine("  --optimize, --no-optimize        Run or skip the IR optimizations");
        Console.Out.WriteLine("  --source-map, --no-source-map    Keep or omit line numbers; a panic then names the function");
        Console.Out.WriteLine("  --debug-info, --no-debug-info    Keep or omit slot names; a debugger then shows indices");
        Console.Out.WriteLine();
        Console.Out.WriteLine("Options:");
        Console.Out.WriteLine("  --stdlib <dir>           Where the stdlib lives (beats $LYRIC_STDLIB)");
        Console.Out.WriteLine("  --emit                   check: emit the bytes and load them, writing neither");
        Console.Out.WriteLine("  --deny-warnings          Exit nonzero when the run reports warnings (CI)");
        Console.Out.WriteLine("  --json                   Diagnostics as JSON on stderr");
        Console.Out.WriteLine("  --quiet, -q              Suppress success messages");
        Console.Out.WriteLine("  --verbose                Print a per-phase timing breakdown");
        Console.Out.WriteLine("  --progress <mode>        auto (default), never or always");
        Console.Out.WriteLine("  --version, -v            Show the toolchain version");
        Console.Out.WriteLine("  --help, -h               Show this help");
        Console.Out.WriteLine();
        Console.Out.WriteLine("Diagnostic switches, for bisecting an optimizer finding (no part of any profile):");
        Console.Out.WriteLine("  --no-inline, --no-scalar-replacement, --no-devirtualize   Skip one IR pass");
        Console.Out.WriteLine("  --no-fusion              Emit the unfused instruction forms");
        Console.Out.WriteLine();
        Console.Out.WriteLine("lyrc does not execute anything. Use 'lyrvm run' or 'lyric run'.");
    }
}
