using System.Text.Json;

namespace Lyric.Tests.Cli;

/// <summary>
/// Under <c>--json</c> the diagnostic stream is JSON, including for a USAGE error.
///
/// <para>It was not. A compile error came out as a document and an unknown option came out as
/// <c>error[LYR-CLI0003]: unknown option '--bogus'</c> — a bare line in a stream a caller is
/// parsing. The one case is the one a wrapper is most likely to hit while it is being written,
/// which is the worst moment to hand somebody a parse failure instead of a message.</para>
///
/// <para>Not every CLI diagnostic could be converted: a few are reported before the shared flags
/// are read at all. These are the ones where <c>--json</c> is already known, which is all of
/// <c>lyrc</c>'s.</para>
/// </summary>
public class JsonDiagnosticStreamTests
{
    /// <summary>Parses the stream and returns the codes, failing with the raw text if it is not
    /// JSON at all — which is precisely the defect, so the message matters.</summary>
    private static string[] CodesIn(string stream)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(stream);
        }
        catch (JsonException e)
        {
            Assert.Fail($"the diagnostic stream is not JSON ({e.Message}): {stream}");
            throw;
        }

        return document.RootElement.GetProperty("diagnostics").EnumerateArray()
            .Select(d => d.GetProperty("code").GetString()!)
            .ToArray();
    }

    [Fact]
    public void An_unknown_option_comes_out_as_json()
    {
        var result = Toolchain.Lyrc("check", "--json", "--bogus", Toolchain.Example("hello.lyr"));

        Assert.Contains("LYR-CLI0003", CodesIn(result.Err));
        Assert.NotEqual(0, result.ExitCode);
    }

    [Fact]
    public void An_unknown_command_comes_out_as_json()
    {
        var result = Toolchain.Lyrc("--json", "frobnicate");

        Assert.Contains("LYR-CLI0003", CodesIn(result.Err));
        Assert.NotEqual(0, result.ExitCode);
    }

    [Fact]
    public void A_missing_argument_comes_out_as_json()
    {
        var result = Toolchain.Lyrc("build", "--json", Toolchain.Example("hello.lyr"), "-o", "");

        Assert.Contains("LYR-CLI0002", CodesIn(result.Err));
        Assert.NotEqual(0, result.ExitCode);
    }

    /// <summary>The half that always worked, as the control: a file that cannot be read.</summary>
    [Fact]
    public void An_unreadable_file_still_comes_out_as_json()
    {
        var result = Toolchain.Lyrc("check", "--json", "no-such-file-here.lyr");

        Assert.Contains("LYR-CLI0001", CodesIn(result.Err));
    }

    /// <summary>
    /// WITHOUT the flag nothing changed. A rendering that became JSON everywhere would break every
    /// reader of the ordinary output, which is most of them.
    /// </summary>
    [Fact]
    public void Without_the_flag_it_is_still_text()
    {
        var result = Toolchain.Lyrc("check", "--bogus", Toolchain.Example("hello.lyr"));

        Assert.Contains("error[LYR-CLI0003]", result.Err, StringComparison.Ordinal);
        Assert.DoesNotContain("\"diagnostics\"", result.Err, StringComparison.Ordinal);
    }
}
