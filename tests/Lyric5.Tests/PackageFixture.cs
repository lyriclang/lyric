namespace Lyric5.Tests;

/// <summary>
/// The test run's temporary directories: all under one root of this process, removed when the
/// process ends. Left behind, one per test, they filled a tmpfs <c>/tmp</c> within a day, and a
/// build then failed for want of space.
/// </summary>
/// <remarks>
/// A test process that is killed — a timeout does that — runs no exit handler. So a process also
/// removes, when it starts, the roots that are older than <see cref="DeadAfter"/>: nothing that
/// still runs is that old.
/// </remarks>
internal static class TestDirectories
{
    /// <summary>The roots of every test process, under the system's temporary directory.</summary>
    private static readonly string Roots = Path.Combine(Path.GetTempPath(), "lyric5-tests");

    private static readonly TimeSpan DeadAfter = TimeSpan.FromDays(1);

    private static readonly string Root = Path.Combine(Roots, Guid.NewGuid().ToString("N")[..8]);

    static TestDirectories()
    {
        RemoveOlder(Roots, DateTime.UtcNow - DeadAfter);
        Directory.CreateDirectory(Root);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Remove(Root);
    }

    /// <summary>A new directory under this process's root, named with the prefix.</summary>
    public static string Fresh(string prefix)
    {
        var dir = Path.Combine(Root, prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Removes the directories directly under <paramref name="parent"/> that were last
    /// written before <paramref name="limit"/>; how many went.</summary>
    internal static int RemoveOlder(string parent, DateTime limit)
    {
        if (!Directory.Exists(parent)) return 0;
        var removed = 0;
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(parent))
                if (Directory.GetLastWriteTimeUtc(dir) < limit && Remove(dir)) removed++;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return removed;
    }

    private static bool Remove(string dir)
    {
        try { Directory.Delete(dir, recursive: true); return true; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}

/// <summary>
/// A package on disk for a test, and the driver run over it through <c>Main</c> — with the console
/// redirected, so a class that uses it belongs to the console collection.
/// </summary>
internal static class PackageFixture
{
    /// <summary>The manifest of the package <c>app</c>.</summary>
    public const string AppManifest = "[package]\nname = \"app\"\nversion = \"0.1.0\"\n";

    /// <summary>A fresh directory holding <paramref name="files"/>.</summary>
    public static string Package(params (string Path, string Text)[] files)
    {
        var dir = TestDirectories.Fresh("lyric5-package-");
        foreach (var (path, text) in files)
        {
            var full = Path.Combine(dir, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, text);
        }
        return dir;
    }

    /// <summary>The driver with <paramref name="args"/>: its exit code and what it wrote.</summary>
    public static (int Exit, string Out, string Err) Run(params string[] args)
    {
        var savedOut = Console.Out;
        var savedErr = Console.Error;
        var output = new StringWriter();
        var error = new StringWriter();
        Console.SetOut(output);
        Console.SetError(error);
        try
        {
            var exit = Program.Main(args);
            return (exit, output.ToString().Replace("\r\n", "\n"), error.ToString().Replace("\r\n", "\n"));
        }
        finally
        {
            Console.SetOut(savedOut);
            Console.SetError(savedErr);
        }
    }

    /// <summary>Builds through <c>Main</c>, then runs the binary — <c>run</c> would hand the
    /// program the console itself, past the test's capture.</summary>
    public static string BuildAndRun(string dir, string binary, params string[] args)
    {
        var (exit, _, error) = Run(args);
        Assert.True(exit == 0, error);
        var host = Lyric5.Toolchain.Target.Host;
        var exe = Path.Combine(dir, "out", "debug", host.Triple, binary + host.ExecutableSuffix);
        Assert.True(File.Exists(exe), $"no binary {exe}");
        var ran = Lyric5.Toolchain.ProcessRunner.Run(exe, [], TimeSpan.FromMinutes(1));
        Assert.True(ran.ExitCode == 0, ran.Stderr);
        return ran.Stdout.Replace("\r\n", "\n");
    }
}
