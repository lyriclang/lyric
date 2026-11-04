using Lyric.Core;
using Lyric.Compiler;
using Lyric5.Toolchain;

namespace Lyric5.Build;

/// <summary>
/// The front end's phases as status lines (spec 14 §1.1). Reading, parsing and loading the imports
/// are one step — the loader parses each module it loads, and only then is their number known —;
/// the others are one each. Disposing it ends a step left open by a front end that stopped early.
/// </summary>
internal sealed class StatusProgress : ICompilerProgress, IDisposable
{
    private BuildStatus.Step? _step;

    public void BeginPhase(Phase phase, string detail = "")
    {
        switch (phase)
        {
            case Phase.Parse:
                Start("Parsing", "the program");
                break;
            case Phase.Check when detail == SourceCompiler.MacrosDetail:
                Start("Expanding", detail);
                break;
            case Phase.Check when detail == SourceCompiler.SitesDetail:
                Start("Evaluating", detail);
                break;
            case Phase.Check:
                Start("Analyzing", detail);
                break;
            case Phase.Lower:
                Start("Lowering", "the program");
                break;
        }
    }

    public void UpdateDetail(string detail)
    {
        // while the imports load, the detail is a module's name; the step says their number at its end
        if (_step is { } step && !_loading) step.What = detail;
    }

    public void EndPhase(TimeSpan? elapsedOverride = null)
    {
        // parsing goes on: the imports are loaded and parsed under the phase after it
        if (_step is null || _loading) return;
        Dispose();
    }

    public void ReportPhase(Phase phase, string detail, TimeSpan elapsed)
    {
        if (phase != Phase.Resolve || !_loading || _step is not { } step) return;
        step.What = detail;
        _loading = false;
        Dispose();
    }

    public void Dispose()
    {
        _step?.Dispose();
        _step = null;
        _loading = false;
    }

    private bool _loading;

    private void Start(string word, string what)
    {
        Dispose();
        _step = BuildStatus.Begin(word, what);
        _loading = word == "Parsing";
    }
}
