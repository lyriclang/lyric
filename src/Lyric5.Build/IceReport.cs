using System.Runtime.InteropServices;

namespace Lyric5.Build;

/// <summary>
/// The report the toolchain writes beside its own failure (<c>LYR-ICE0001</c>): the version, the
/// command as it was given, the directory it ran in, the machine, where the compiler was, the
/// exception and its trace — what a bug report needs, in one file to attach, as rustc writes
/// <c>rustc-ice-*.txt</c>. It goes under <c>out/</c> of the package the command ran in, else of the
/// working directory, else into the temporary directory.
/// </summary>
public static class IceReport
{
    /// <summary>Writes the report; its path, or <c>null</c> where no directory takes it — a report
    /// of a failure does not fail over itself.</summary>
    public static string? Write(string baseDirectory, string version, IReadOnlyList<string> command, string message, string? place, string? trace)
    {
        var name = $"lyric5-ice-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Environment.ProcessId}.txt";
        var text = string.Join('\n',
            $"lyric5 {version} failed: this is a bug in lyric5 — please report it with this file and the program that triggered it",
            "",
            $"command:   lyric5 {string.Join(' ', command.Select(Quote))}",
            $"directory: {Environment.CurrentDirectory}",
            $"machine:   {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})",
            $"while:     {place ?? "(no declaration was being checked or lowered)"}",
            "",
            message,
            "",
            trace ?? "(no trace)",
            "");
        foreach (var directory in new[] { Path.Combine(baseDirectory, "out"), Path.GetTempPath() })
        {
            try
            {
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, name);
                File.WriteAllText(path, text);
                return path;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
        }
        return null;
    }

    private static string Quote(string argument) =>
        argument.Length > 0 && !argument.Any(c => char.IsWhiteSpace(c) || c is '"' or '\'') ? argument : $"'{argument.Replace("'", "'\\''")}'";
}
