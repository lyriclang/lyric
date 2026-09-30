using System.Reflection;
using Lyric.Compiler;
using Lyric.Ir;
using Lyric5.Compiler;
using Lyric5.Toolchain;

namespace Lyric5;

/// <summary>
/// The Lyric 5 driver: one binary with verbs (design/v5/spec/11 C1–C3), strict about what it
/// does not know (exit 2), reporting what the program did wrong with exit 1 (C5). The verbs grow
/// milestone by milestone; today: <c>version</c>, <c>help</c>, and <c>build --emit ir|c</c> — the
/// front end behind the subset gate, up to the IR (M2 S1) or the C (S2).
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 0) return Usage(Console.Out, 0);
        return args[0] switch
        {
            "--version" or "-V" or "version" => Version(),
            "--help" or "-h" or "help" => Usage(Console.Out, 0),
            "build" => Build(args[1..]),
            _ => Unknown($"unknown verb '{args[0]}'"),
        };
    }

    private static int Usage(TextWriter output, int exit)
    {
        output.WriteLine("usage: lyric5 <verb> [options]");
        output.WriteLine();
        output.WriteLine("  build <file.lyr> --emit ir|c   compile up to the IR, or to the C, and print it");
        output.WriteLine("  version                      the toolchain and the C compiler it found");
        output.WriteLine("  help                         this");
        output.WriteLine();
        output.WriteLine("The Lyric 5 command line, in development (design/v5/spec/13).");
        return exit;
    }

    private static int Unknown(string what)
    {
        Console.Error.WriteLine($"error[LYR-CLI0003]: {what}");
        Console.Error.WriteLine("  = help: lyric5 help lists what exists");
        return 2;
    }

    // --- build ---------------------------------------------------------------------------------

    private static int Build(string[] args)
    {
        string? file = null, emit = null;
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg == "--emit")
            {
                if (i + 1 >= args.Length) return Unknown("'--emit' needs a value: ir");
                emit = args[++i];
            }
            else if (arg.StartsWith("--emit=", StringComparison.Ordinal)) emit = arg["--emit=".Length..];
            else if (arg.StartsWith('-')) return Unknown($"unknown option '{arg}' for 'build'");
            else if (file is null) file = arg;
            else return Unknown($"'build' takes one file, got '{file}' and '{arg}'");
        }
        if (file is null) return Unknown("'build' needs a file: lyric5 build <file.lyr>");
        if (emit is null) return Unknown("'build' can only '--emit ir' or '--emit c' yet (M2 S2); the binary comes with S4");
        if (emit is not ("ir" or "c")) return Unknown($"unknown emission '{emit}': ir, c");
        if (!File.Exists(file))
        {
            Console.Error.WriteLine($"error[LYR-CLI0001]: no such file '{file}'");
            return 2;
        }

        // lyric5 compiles against the seed of the Lyric 5 standard library beside the binary, never
        // against the 4.x stdlib the front end's own project carries (stdlib5/README.md).
        var options = new CompilerOptions { StdlibRoot = Path.Combine(AppContext.BaseDirectory, "stdlib5") };
        var result = SourceCompiler.Lower(file, options);
        if (!result.Render(Console.Error) || result.Ir is null) return 1;
        if (!SubsetGate.Check(result.Ir, result.Diagnostics))
        {
            result.Diagnostics.RenderText(Console.Error);
            return 1;
        }
        if (emit == "ir")
        {
            Console.Out.Write(IrPrinter.Dump(result.Ir));
            return 0;
        }
        try
        {
            Console.Out.Write(CEmitter.Emit(result.Ir, result.Sources));
            return 0;
        }
        catch (CEmitter.NotYetException notYet)
        {
            Console.Error.WriteLine($"error[{SubsetGate.NotYet}]: {notYet.Message}");
            return 1;
        }
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
    private static string DisplayVersion()
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
