using Lyric.Lsp.Protocol;

namespace Lyric.Lsp.Analysis;

/// <summary>
/// Bounded latency percentiles and cache/cancellation counters for <c>lyric/serverStatus</c>.
/// </summary>
public sealed class LspTelemetry
{
    private const int WindowSize = 128;

    private readonly object _gate = new();
    private readonly double[] _analysisSamples = new double[WindowSize];
    private int _analysisCount;
    private int _analysisCursor;

    private readonly double[] _completionSamples = new double[WindowSize];
    private int _completionCount;
    private int _completionCursor;

    private long _packageCacheHits;
    private long _packageCacheMisses;
    private long _syntaxCacheHits;
    private int _cancelledRequests;

    public void RecordAnalysis(TimeSpan duration) =>
        Record(_analysisSamples, ref _analysisCount, ref _analysisCursor, duration.TotalMilliseconds);

    public void RecordCompletion(TimeSpan duration) =>
        Record(_completionSamples, ref _completionCount, ref _completionCursor, duration.TotalMilliseconds);

    public void RecordPackageCacheHit() => Interlocked.Increment(ref _packageCacheHits);
    public void RecordPackageCacheMiss() => Interlocked.Increment(ref _packageCacheMisses);
    public void RecordSyntaxCacheHit() => Interlocked.Increment(ref _syntaxCacheHits);
    public void RecordCancellation() => Interlocked.Increment(ref _cancelledRequests);

    private void Record(double[] ring, ref int count, ref int cursor, double ms)
    {
        lock (_gate)
        {
            ring[cursor] = ms;
            cursor = (cursor + 1) % WindowSize;
            if (count < WindowSize) count++;
        }
    }

    public ServerStatusResult Snapshot(string version, int openDocuments, int analysedFiles, int indexedSymbols)
    {
        double a50, a95, c50, c95;
        lock (_gate)
        {
            (a50, a95) = Percentiles(_analysisSamples, _analysisCount);
            (c50, c95) = Percentiles(_completionSamples, _completionCount);
        }

        return new ServerStatusResult
        {
            Version = version,
            OpenDocuments = openDocuments,
            AnalysedFiles = analysedFiles,
            IndexedSymbols = indexedSymbols,
            CancelledRequests = Volatile.Read(ref _cancelledRequests),
            PackageCacheHits = Interlocked.Read(ref _packageCacheHits),
            PackageCacheMisses = Interlocked.Read(ref _packageCacheMisses),
            SyntaxCacheHits = Interlocked.Read(ref _syntaxCacheHits),
            ManagedMemoryMb = Math.Round(GC.GetTotalMemory(false) / (1024.0 * 1024.0), 2),
            AnalysisP50Ms = Math.Round(a50, 2),
            AnalysisP95Ms = Math.Round(a95, 2),
            CompletionP50Ms = Math.Round(c50, 2),
            CompletionP95Ms = Math.Round(c95, 2),
        };
    }

    private static (double P50, double P95) Percentiles(double[] ring, int count)
    {
        if (count == 0) return (0.0, 0.0);
        var copy = new double[count];
        Array.Copy(ring, copy, count);
        Array.Sort(copy);
        var p50 = copy[(int)((count - 1) * 0.50)];
        var p95 = copy[(int)((count - 1) * 0.95)];
        return (p50, p95);
    }
}
