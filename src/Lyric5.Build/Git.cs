using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Lyric5.Build;

/// <summary>
/// The git command line, as the toolchain reads packages from git with it (design/v5/spec/11 W2
/// P9): the user's own git — its configuration and its credentials — that never asks on the
/// terminal (<c>GIT_TERMINAL_PROMPT=0</c>): a build waiting for a password nobody is asked for
/// hangs.
/// </summary>
internal static class Git
{
    /// <param name="Exit">git's exit code.</param>
    /// <param name="Output">Its standard output as bytes: an archive is no text.</param>
    /// <param name="Error">Its standard error.</param>
    public sealed record Result(int Exit, byte[] Output, string Error)
    {
        public string Text => Encoding.UTF8.GetString(Output).Trim();
    }

    /// <summary>Runs git with <paramref name="arguments"/> to its end, or kills it after
    /// <paramref name="timeout"/> (exit -1).</summary>
    /// <exception cref="GitMissingException">There is no git to run.</exception>
    public static Result Run(IEnumerable<string> arguments, TimeSpan timeout)
    {
        var info = new ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        info.Environment["GIT_TERMINAL_PROMPT"] = "0";
        Process? started;
        try { started = Process.Start(info); }
        catch (Win32Exception) { throw new GitMissingException(); }
        using var process = started ?? throw new GitMissingException();
        using var output = new MemoryStream();
        var copied = process.StandardOutput.BaseStream.CopyToAsync(output);
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeout))
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            return new Result(-1, [], $"git did not finish within {timeout.TotalMinutes:0} minutes");
        }
        copied.Wait();
        return new Result(process.ExitCode, output.ToArray(), error.Result.Trim());
    }
}

/// <summary>No git on the PATH.</summary>
internal sealed class GitMissingException() : Exception("git was not found on the PATH");
