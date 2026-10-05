using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// An enum's constants at run time (the review's M8a-10): one with a literal is the literal where
/// it is read, one with another initializer is filled before the entry runs — of the enum's own
/// type too. Through <c>Main</c>, so in the console collection.
/// </summary>
[Collection("console")]
public class EnumStaticRunTests
{
    [Fact]
    public void An_enums_constants_are_read_under_its_name()
    {
        var dir = Package(("lyric.toml", AppManifest),
            ("src/main.lyr", """
                import std.io { println };

                enum Level {
                    Low, High;

                    static let fallback: Level = Level.Low;
                    static let count: int = 2;
                    static let all: Level[] = [Level.Low, Level.High];

                    fn name(): string {
                        return match (this) { .Low => "low", .High => "high" };
                    }
                }

                fn main(): void {
                    println(f"{Level.fallback.name()} {Level.count} {Level.all.length()} {Level.all[1].name()}");
                }
                """));
        Assert.Equal("low 2 2 high\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }
}
