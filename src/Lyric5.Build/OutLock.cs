namespace Lyric5.Build;

/// <summary>
/// <c>out/.lock</c> (design/v5/spec/11 W2 P5): one build at a time writes into a package's
/// <c>out/</c> — a second one waits until the first is done, as Cargo's build directory lock has
/// it. The lock is the operating system's on an open file (<c>flock</c> where there is one), so
/// it ends with its process however that ends: no stale lock file to clear by hand.
/// </summary>
public sealed class OutLock : IDisposable
{
    private readonly FileStream _held;

    private OutLock(FileStream held) => _held = held;

    /// <summary>The lock on <paramref name="outDir"/>, waited for while another build holds it —
    /// said once on <paramref name="error"/>.</summary>
    public static OutLock Take(string outDir, TextWriter error)
    {
        Directory.CreateDirectory(outDir);
        var file = Path.Combine(outDir, ".lock");
        var said = false;
        while (true)
        {
            try
            {
                return new OutLock(new FileStream(file, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
            }
            catch (IOException)
            {
                if (!said) error.WriteLine($"waiting for another build in {outDir} to finish");
                said = true;
                Thread.Sleep(100);
            }
        }
    }

    public void Dispose() => _held.Dispose();
}
