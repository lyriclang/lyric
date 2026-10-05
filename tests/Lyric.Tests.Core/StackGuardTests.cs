using Lyric.Core;
using Xunit;

namespace Lyric.Tests.Core;

public class StackGuardTests
{
    private static int Descend(int depth)
    {
        StackGuard.Check("descending");
        // Not a tail call: the frame has to stay.
        return Descend(depth + 1) + 1;
    }

    /// <summary>A recursion without an end, on a thread with a small stack: it ends in the guard's
    /// exception. Without the guard the process would end here, and the test run with it.</summary>
    [Fact]
    public void A_recursion_without_an_end_stops_at_the_guard()
    {
        Exception? caught = null;
        var thread = new Thread(() =>
        {
            try { Descend(0); }
            catch (Exception e) { caught = e; }
        }, maxStackSize: 512 * 1024);
        thread.Start();
        thread.Join();

        var depth = Assert.IsType<CompilerDepthException>(caught);
        Assert.StartsWith("descending did not end", depth.Message);
    }

    [Fact]
    public void A_shallow_call_passes()
    {
        StackGuard.Check("nothing deep");
    }

    [Fact]
    public void The_position_names_the_declaration_and_its_place()
    {
        var sources = new SourceManager();
        var file = sources.AddVirtual("src/main.lyr", "fn first(): int { return 1; }\n\nfn total(): int { return 2; }\n");
        var total = new Span(file, 31, 59);

        CompilerPosition.Begin(sources);
        Assert.Null(CompilerPosition.Describe());

        CompilerPosition.At("checking", "total", total);
        Assert.Equal("checking 'total' at src/main.lyr:3:1", CompilerPosition.Describe());

        // A new compilation forgets the last one's place.
        CompilerPosition.Begin(new SourceManager());
        Assert.Null(CompilerPosition.Describe());
    }

    [Fact]
    public void The_position_is_a_thread_s_own()
    {
        var sources = new SourceManager();
        var file = sources.AddVirtual("a.lyr", "fn a(): void { }\n");
        CompilerPosition.Begin(sources);
        CompilerPosition.At("checking", "a", new Span(file, 0, 16));

        // A thread of its own, not a task: a pooled thread may carry another test's place.
        string? elsewhere = "not asked";
        var thread = new Thread(() => elsewhere = CompilerPosition.Describe());
        thread.Start();
        thread.Join();

        Assert.Null(elsewhere);
        Assert.Equal("checking 'a' at a.lyr:1:1", CompilerPosition.Describe());
    }
}
