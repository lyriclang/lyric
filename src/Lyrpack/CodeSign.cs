using System.Diagnostics;

namespace Lyric.Cli.Packer;

/// <summary>
/// Ad-hoc signing on macOS, by the program macOS ships for it.
///
/// <para>An unsigned Mach-O is killed on launch, and a signature is a CodeDirectory of page hashes
/// inside a SuperBlob — crypto this packer would otherwise own forever, verifiable only on the
/// platform it is for. <c>/usr/bin/codesign</c> is part of the base system, and packing for macOS
/// happens on macOS anyway: the packer only ever finds the stub of its own platform.</para>
///
/// <para>Ad-hoc — <c>--sign -</c> — means signed without an identity: it establishes that the file
/// has not changed since packing, not who made it. That is what the loader needs and all a build
/// without a developer certificate can give. Notarising a packed program is the author's business,
/// on the finished file, with their own identity.</para>
/// </summary>
internal static class CodeSign
{
    private const string Tool = "/usr/bin/codesign";

    /// <exception cref="InvalidOperationException">The tool is missing, cannot run, or refuses the
    /// file. Its own message is carried through: it names what it found wrong far better than a
    /// sentence written here in advance could.</exception>
    public static void AdHoc(string path)
    {
        if (!File.Exists(Tool))
            throw new InvalidOperationException(
                $"cannot sign the packed program: {Tool} is missing. macOS refuses to run an "
                + "unsigned executable, so packing cannot finish without it.");

        var start = new ProcessStartInfo(Tool)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };

        // --force: there is no signature on the file at this point, and there must be none left
        // over from a previous pack over the same output either.
        start.ArgumentList.Add("--force");
        start.ArgumentList.Add("--sign");
        start.ArgumentList.Add("-");
        start.ArgumentList.Add(path);

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException($"cannot sign the packed program: {Tool} did not start");

        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"cannot sign the packed program: {Tool} exited {process.ExitCode}. "
                + error.Trim());
    }
}
