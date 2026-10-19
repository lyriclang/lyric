using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// A type whose declaration did not parse gets nothing synthesized (open point 200): its fields are
/// what the parser's recovery left — a ':' or a keyword taken as a name —, and the <c>Debug</c> written
/// over them failed to parse five times, ahead of the parser's own errors, each "in the 'Debug'
/// synthesized for 'Node'". A type beside it that parsed keeps its synthesis.
/// </summary>
[Collection("console")]
public class SynthesisAfterAParseErrorTests
{
    [Fact]
    public void A_type_that_did_not_parse_gets_no_synthesis()
    {
        var root = Package(("lyric.toml", AppManifest), ("src/main.lyr", """
            class Node { var next: ?Node = null; let value: int; }

            struct Point { x: int, y: int }

            fn main(): void {
                let p = Point { x = 1, y = 2 };
                let _ = p.debug();
            }
            """));
        var (exit, _, error) = Run("build", "-C", root);
        Assert.Equal(1, exit);
        Assert.DoesNotContain("synthesized", error);
        var errors = error.Split('\n').Where(l => l.Contains("error[")).ToArray();
        Assert.NotEmpty(errors);
        Assert.Contains(errors, l => l.Contains("[LYR-PAR"));
        Assert.DoesNotContain(errors, l => l.Contains("Point"));
    }
}
