using Lyric.Compiler;
using Lyric.Core;

namespace Lyric.Lsp.Analysis;

/// <summary>A progress display the client draws for one analysis: reported to while the analysis
/// runs, ended when it is disposed.</summary>
public interface IAnalysisProgress : IDisposable
{
    void Report(string message, int percentage);
}

/// <summary>
/// The phases of one analysis as the client's progress: what the front end says it is doing,
/// said as a message with a share of the whole.
///
/// <para>The display is begun at once, or only once the analysis has run for a while. The first
/// analysis of a file is the wait before anything answers there and is shown from its start; a
/// later one is shown only when it outlasts <c>delay</c> — a package of fifty thousand lines takes
/// seconds at every keystroke, and a bar that came and went for every hundred milliseconds would
/// be a flicker.</para>
/// </summary>
internal sealed class AnalysisPhases : ICompilerProgress, IDisposable
{
    private readonly Func<string, IAnalysisProgress?> _begin;
    private readonly string _what;
    private readonly object _gate = new();
    private readonly CancellationTokenSource? _delay;
    private IAnalysisProgress? _shown;
    private (string Message, int Percentage)? _last;
    private bool _ended;

    private AnalysisPhases(Func<string, IAnalysisProgress?> begin, string what, TimeSpan? after)
    {
        _begin = begin;
        _what = what;
        if (after is not { } delay || delay <= TimeSpan.Zero)
        {
            Show();
            return;
        }

        _delay = new CancellationTokenSource();
        _ = Task.Delay(delay, _delay.Token).ContinueWith(
            _ => Show(), CancellationToken.None, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
    }

    /// <summary>Shown from the start.</summary>
    public static AnalysisPhases? Now(Func<string, IAnalysisProgress?>? begin, string what) =>
        begin is null ? null : new AnalysisPhases(begin, what, null);

    /// <summary>Shown once the analysis has run for <paramref name="delay"/>.</summary>
    public static AnalysisPhases? After(Func<string, IAnalysisProgress?>? begin, string what, TimeSpan delay) =>
        begin is null ? null : new AnalysisPhases(begin, what, delay);

    private void Show()
    {
        lock (_gate)
        {
            if (_ended || _shown is not null) return;
            _shown = _begin(_what);
            if (_last is { } last) _shown?.Report(last.Message, last.Percentage);
        }
    }

    private void Say(string message, int percentage)
    {
        lock (_gate)
        {
            if (_ended) return;
            _last = (message, percentage);
            _shown?.Report(message, percentage);
        }
    }

    public void BeginPhase(Phase phase, string detail = "")
    {
        switch (phase)
        {
            case Phase.Parse:
                Say(detail.Length > 0 ? $"parsing {detail}" : "parsing", 10);
                break;
            case Phase.Resolve:
                Say(detail.Length > 0 ? $"resolving names ({detail})" : "resolving names", 25);
                break;
            case Phase.Check when detail == SourceCompiler.SitesDetail:
                Say("evaluating compile-time sites", 80);
                break;
            case Phase.Check when detail == SourceCompiler.MacrosDetail:
                Say("expanding macros", 35);
                break;
            case Phase.Check:
                Say(detail.Length > 0 ? $"checking {detail}" : "checking", 40);
                break;
        }
    }

    public void UpdateDetail(string detail) { }

    public void EndPhase(TimeSpan? elapsedOverride = null) { }

    public void ReportPhase(Phase phase, string detail, TimeSpan elapsed) { }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_ended) return;
            _ended = true;
        }
        _delay?.Cancel();
        _delay?.Dispose();
        _shown?.Dispose();
    }
}
