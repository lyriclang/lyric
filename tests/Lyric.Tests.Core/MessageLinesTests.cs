using Lyric.Core;

namespace Lyric.Tests.Core;

/// <summary>
/// A message that quotes a tool's output — a C compiler's, a linker's (<c>LYR-BLD0001</c>) — keeps
/// its lines: a line break stays a break, <c>\r\n</c> is one, and every other control character is
/// still replaced (<see cref="DiagnosticEscapeTests"/>).
/// </summary>
public sealed class MessageLinesTests
{
    [Fact]
    public void A_messages_lines_stay_lines_and_its_escapes_do_not()
    {
        Assert.Equal("compiling a.c failed:\nzig cc -c a.c\na.c:2: error: ␛[31m",
            DiagnosticEngine.PrintableLines("compiling a.c failed:\r\nzig cc -c a.c\na.c:2: error: \u001b[31m"));
        Assert.Equal("one line ␛", DiagnosticEngine.PrintableLines("one line \u001b"));
    }

    [Fact]
    public void A_rendered_message_keeps_its_lines()
    {
        var de = new DiagnosticEngine(new SourceManager());
        de.Report("LYR-BLD0001", Severity.Error, default, "linking app failed:\nld.lld: error: something\u0007");
        var writer = new StringWriter();
        de.RenderText(writer);
        var rendered = writer.ToString();
        Assert.Contains("error[LYR-BLD0001]: linking app failed:\nld.lld: error: something␇", rendered);
        Assert.DoesNotContain("␊", rendered);
    }
}
