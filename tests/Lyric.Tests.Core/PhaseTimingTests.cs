using System.Diagnostics;
using Lyric.Core;

namespace Lyric.Tests.Core;

/// <summary>
/// The <c>--verbose</c> timing table, and the unit mistake that made it wrong on one platform.
///
/// <para>A phase was measured as <c>TimeSpan.FromTicks(stopwatch.ElapsedTicks - start)</c>.
/// <c>TimeSpan</c> ticks are 100 ns; STOPWATCH ticks are whatever <see cref="Stopwatch.Frequency"/>
/// says — on Windows that happens to be the same 10 MHz, and on Linux it is 1 GHz. Every per-phase
/// row was therefore a hundred times too large there.</para>
///
/// <para>THE TABLE CONTRADICTED ITSELF and nobody read it that way: the total row is built from
/// <c>Stopwatch.Elapsed</c>, which converts properly, so on Linux the rows summed to a hundred
/// times the total printed underneath them. A number without a second number beside it is hard to
/// disbelieve.</para>
///
/// <para>THIS TEST IS GREEN ON WINDOWS EITHER WAY, which is worth saying out loud rather than
/// leaving for somebody to discover: the two tick units coincide there. It is red on Linux before
/// the fix, and the CI matrix runs ubuntu — that is what makes it a pin rather than a decoration.</para>
/// </summary>
public class PhaseTimingTests
{
    private static string Table(Action<TerminalOutput> work)
    {
        var error = new StringWriter();
        using (var terminal = new TerminalOutput(TextWriter.Null, error,
                   ToolOptions.Default with { Verbose = true }, isTerminal: false))
            work(terminal);
        return error.ToString();
    }

    private static double MillisecondsIn(string row)
    {
        // "  parse    test.lyr        30.2 ms"
        var text = row.Trim();
        var ms = text[..text.LastIndexOf(" ms", StringComparison.Ordinal)];
        return double.Parse(ms[(ms.LastIndexOf(' ') + 1)..], System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// A phase that took a measurable stretch is reported as that stretch, within an order of
    /// magnitude.
    ///
    /// <para>The tolerance is deliberately enormous — a factor of ten either way. The defect was a
    /// factor of a HUNDRED, and a tight bound on wall-clock time in a test is a flake waiting for
    /// a loaded machine. Cf. the scheduler test whose 20 ms margin went red on CI.</para>
    /// </summary>
    [Fact]
    public void A_phase_is_reported_in_the_unit_the_row_names()
    {
        var table = Table(terminal =>
        {
            terminal.BeginPhase(Phase.Parse, "probe");
            Thread.Sleep(50);
            terminal.EndPhase();
        });

        var row = Assert.Single(table.ReplaceLineEndings("\n").Split('\n'),
            l => l.Contains("parse", StringComparison.Ordinal));

        var reported = MillisecondsIn(row);
        Assert.InRange(reported, 5.0, 5_000.0);
    }

    /// <summary>
    /// The rows do not exceed the total they stand under.
    ///
    /// <para>The self-contradiction, as an assertion: the two numbers come from different
    /// conversions of the same clock, so one of them being wrong is visible without knowing what
    /// the right answer is. This is the half that needs no wall-clock tolerance at all.</para>
    /// </summary>
    [Fact]
    public void No_phase_outlasts_the_run_that_contains_it()
    {
        var table = Table(terminal =>
        {
            terminal.BeginPhase(Phase.Parse, "one");
            Thread.Sleep(20);
            terminal.EndPhase();
            terminal.BeginPhase(Phase.Check, "two");
            Thread.Sleep(20);
            terminal.EndPhase();
        });

        var lines = table.ReplaceLineEndings("\n").Split('\n')
            .Where(l => l.Contains(" ms", StringComparison.Ordinal)).ToArray();

        var total = MillisecondsIn(lines[^1]);
        var phases = lines[..^1].Sum(MillisecondsIn);

        Assert.True(phases <= total,
            $"the phase rows sum to {phases} ms under a total of {total} ms");
    }
}
