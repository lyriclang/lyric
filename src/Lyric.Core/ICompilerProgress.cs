namespace Lyric.Core;

/// <summary>What the front end says of its phases as it runs them; a tool shows it its own way.</summary>
public interface ICompilerProgress
{
    /// <summary>A phase begins.</summary>
    void BeginPhase(Phase phase, string detail = "");

    /// <summary>Adds detail to the running phase, such as a module's name.</summary>
    void UpdateDetail(string detail);

    /// <summary>The running phase ends.</summary>
    /// <param name="elapsedOverride">For a phase the caller timed itself.</param>
    void EndPhase(TimeSpan? elapsedOverride = null);

    /// <summary>A phase that ran inside another, timed by itself.</summary>
    void ReportPhase(Phase phase, string detail, TimeSpan elapsed);
}
