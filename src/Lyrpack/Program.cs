using System.Runtime.InteropServices;
using Lyric.Core;

namespace Lyric.Cli.Packer;

/// <summary>
/// <c>lyrpack</c> — one <c>.lyrbc</c> module in, one executable out.
///
/// <para>The output is a copy of the stub runtime with the module and a
/// <see cref="PackFooter"/> appended; nothing is linked, translated or optimized here. It takes
/// modules only: compiling belongs to <c>lyrc</c>, and <c>lyric pack app.lyr</c> composes the
/// two the way <c>lyric run</c> does.</para>
///
/// <para>The module's content is not validated beyond its extension — the packer would need the
/// runtime's reader for that, and the pack pairs the module with exactly that reader anyway.
/// <c>lyrvm verify</c> answers the question on demand; the first run of the packed program
/// answers it definitively.</para>
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

        return args[0] switch
        {
            "--version" or "-v" => Version(terminal),
            "--help" or "-h" => Help(),
            _ => Pack(args, terminal),
        };
    }

    private static int Pack(string[] args, TerminalOutput terminal)
    {
        var input = args[0];
        if (!Path.GetExtension(input).Equals(".lyrbc", StringComparison.OrdinalIgnoreCase))
            return CliDiagnostics.Fail(Console.Error, CliDiagnostics.WrongFileKind,
                $"expected a .lyrbc module, got '{input}' — lyrpack does not compile. "
                + "Use 'lyrc build' first, or 'lyric pack' to do both.", ExitCodes.Usage);

        if (ResolveStub(Flag(args, "--stub")) is not { } stub)
            return CliDiagnostics.Fail(Console.Error, CliDiagnostics.StubNotFound,
                "no stub runtime found — looked at '--stub', $LYRIC_STUB and "
                + $"'stubs{Path.DirectorySeparatorChar}{RuntimeInformation.RuntimeIdentifier}' "
                + "beside lyrpack. The toolchain archive carries the stubs; a source build gets "
                + "one by building src/Lyrpack.", ExitCodes.Failure);

        var output = Flag(args, "-o") ?? Flag(args, "--output") ?? DefaultOutput(input);

        // Refuse to write over either input. '-o' on the stub would destroy the template of
        // every later pack; '-o' on the module would eat the program being packed.
        var outputFull = Path.GetFullPath(output);
        if (outputFull == Path.GetFullPath(stub) || outputFull == Path.GetFullPath(input))
            return CliDiagnostics.Fail(Console.Error, CliDiagnostics.OutputUnwritable,
                $"refusing to overwrite '{output}': it is an input of this pack", ExitCodes.Usage);

        long moduleLength;
        var signed = false;
        try
        {
            // Both inputs open BEFORE the output exists, and a failed write deletes it: a pack
            // that did not finish must not leave an executable that starts and then explains
            // itself as empty or damaged.
            using var template = File.OpenRead(stub);
            using var module = File.OpenRead(input);
            moduleLength = module.Length;

            try
            {
                using (var result = new FileStream(output, FileMode.Create, FileAccess.ReadWrite))
                {
                    template.CopyTo(result);

                    // Decided by the FILE, not by the host: a Mach-O describes where it ends, so
                    // the payload cannot simply follow it. The existing signature is dropped first
                    // — the payload takes its place — and 'codesign' writes a new one at the end
                    // afterwards. See MachO.
                    var mach = MachO.Looks(result, out var refusal);
                    if (refusal is not null)
                        throw new InvalidOperationException($"cannot pack this stub: {refusal}");

                    // The signature is the half only macOS can do, and an unsigned Mach-O is killed
                    // on launch. Refused here rather than written: a packed program that cannot
                    // start is worse than a pack that says why it did not happen.
                    if (mach && !OperatingSystem.IsMacOS())
                        throw new InvalidOperationException(
                            "cannot pack a macOS program on this system: the executable has to be "
                            + "signed, and only macOS carries the signer. Pack it on macOS.");

                    if (mach)
                    {
                        if (MachO.SignatureOffset(result) is { } signature)
                            result.SetLength(signature);
                        MachO.RemoveSignatureCommand(result);
                    }

                    result.Seek(0, SeekOrigin.End);
                    module.CopyTo(result);
                    PackFooter.Write(result, moduleLength);

                    if (mach) MachO.GrowLinkEditToEnd(result);
                    signed = mach;
                }

                // Signed last, over the finished file: macOS kills an executable whose signature
                // does not match, and everything before this line changed the bytes it covers.
                if (signed) CodeSign.AdHoc(output);
            }
            catch
            {
                try { File.Delete(output); } catch (IOException) { /* the report below stands */ }
                throw;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return CliDiagnostics.Fail(Console.Error, CliDiagnostics.OutputUnwritable,
                $"cannot pack {input}: {ex.Message}", ExitCodes.Failure);
        }
        catch (InvalidOperationException ex)
        {
            // The Mach-O half: a stub this packer cannot edit, a host that cannot sign, or a
            // signer that refused. Its own words carry through.
            return CliDiagnostics.Fail(Console.Error, CliDiagnostics.OutputUnwritable,
                ex.Message, ExitCodes.Failure);
        }

        // What the OS needs to run it. The stub's own execute bit does not survive the byte copy.
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(output, File.GetUnixFileMode(output)
                | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);

        terminal.Info($"{output}: {new FileInfo(output).Length} bytes "
            + $"({moduleLength} bytes program)");
        return ExitCodes.Success;
    }

    /// <summary>The module's own name as an executable, next to the module.</summary>
    private static string DefaultOutput(string input)
    {
        var directory = Path.GetDirectoryName(input) ?? "";
        var name = Path.GetFileNameWithoutExtension(input);
        return Path.Combine(directory, OperatingSystem.IsWindows() ? $"{name}.exe" : name);
    }

    /// <summary>The stub to copy: <c>--stub</c> beats <c>LYRIC_STUB</c> beats
    /// <c>stubs/&lt;rid&gt;/</c> beside this binary. Missing is an answer, not an exception —
    /// the caller words the diagnostic.</summary>
    private static string? ResolveStub(string? fromFlag)
    {
        if (!string.IsNullOrWhiteSpace(fromFlag))
            return File.Exists(fromFlag) ? fromFlag : null;

        var configured = Environment.GetEnvironmentVariable("LYRIC_STUB");
        if (!string.IsNullOrWhiteSpace(configured))
            return File.Exists(configured) ? configured : null;

        var bundled = Path.Combine(AppContext.BaseDirectory,
            "stubs", RuntimeInformation.RuntimeIdentifier,
            OperatingSystem.IsWindows() ? "lyrstub.exe" : "lyrstub");
        return File.Exists(bundled) ? bundled : null;
    }

    /// <summary>The value of an option. The window starts behind the input file.</summary>
    private static string? Flag(string[] args, string name)
    {
        for (var i = 1; i < args.Length - 1; i++)
            if (args[i] == name) return args[i + 1];
        return null;
    }

    private static int Version(TerminalOutput terminal)
    {
        terminal.Payload($"lyrpack {ToolchainVersion.Value}\n");
        return ExitCodes.Success;
    }

    private static int Help() { PrintHelp(); return ExitCodes.Success; }

    private static void PrintHelp()
    {
        Console.Out.WriteLine("lyrpack — packs a compiled module into a standalone executable");
        Console.Out.WriteLine();
        Console.Out.WriteLine("Usage: lyrpack <file.lyrbc> [options]");
        Console.Out.WriteLine();
        Console.Out.WriteLine("Options:");
        Console.Out.WriteLine("  -o, --output <file>   Where the executable lands; defaults to the");
        Console.Out.WriteLine("                        module's name beside it");
        Console.Out.WriteLine("  --stub <path>         Stub runtime to pack with (beats $LYRIC_STUB,");
        Console.Out.WriteLine("                        then the bundled stubs/<rid>/)");
        Console.Out.WriteLine("  --json                Diagnostics as JSON on stderr");
        Console.Out.WriteLine("  --quiet, -q           Suppress success messages");
        Console.Out.WriteLine("  --version, -v         Show the toolchain version");
        Console.Out.WriteLine("  --help, -h            Show this help");
        Console.Out.WriteLine();
        Console.Out.WriteLine("The executable runs the program with every capability and passes its");
        Console.Out.WriteLine("whole command line through. lyrpack does not compile: use 'lyrc build'");
        Console.Out.WriteLine("first, or 'lyric pack app.lyr' to do both in one step.");
    }
}
