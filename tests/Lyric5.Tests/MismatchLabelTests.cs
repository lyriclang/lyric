using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// A mismatch at a binding with a written type (spec 16 G1): the initializer that has the wrong type is
/// the place, and the written type carries the label "expected 'int' here".
/// </summary>
[Collection("console")]
public class MismatchLabelTests
{
    [Fact]
    public void A_binding_marks_its_initializer_and_labels_the_written_type()
    {
        var root = Package(("lyric.toml", AppManifest), ("src/main.lyr", "fn main(): void {\n    let x: int = \"three\";\n}\n"));
        var (exit, _, error) = RunRaw("check", "-C", root, "--color", "never");
        Assert.Equal(1, exit);
        Assert.Contains("error[LYR-SEM0001]: cannot assign 'string' to 'int'", error);
        Assert.Contains("main.lyr:2:18", error);
        Assert.Contains("expected 'int' here", error);
    }
}
