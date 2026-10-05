using System.Text.RegularExpressions;
using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// The word of a member that answers an interface (design/v5/spec/07 V2 S4, S5; the review's
/// M7-2). A conformance is as visible as its type and its interface, whichever is narrower, and
/// what answers it says at least that word itself: a method written narrower in the type's body
/// could be called through the interface where its own name is refused
/// (<c>LYR-SEM0167</c>). In a conformance block the members take the block's visibility and
/// write no word (<c>LYR-SEM0150</c>, unchanged). Through <c>Main</c>, so in the console
/// collection.
/// </summary>
[Collection("console")]
public class WitnessWordTests
{
    private const string Shape = "pub interface Shape {\n    fn area(): int;\n}\n\n";
    private const string Quiet = "interface Quiet {\n    fn area(): int;\n}\n\n";

    private static (int Exit, string Error) Build(string declarations)
    {
        var dir = Package(("main.lyr", declarations + "\nfn main(): void {\n}\n"));
        var (exit, _, error) = Run("build", Path.Combine(dir, "main.lyr"), "--emit", "ir");
        return (exit, error);
    }

    [Theory]
    // a pub type, a pub interface: the conformance is pub
    [InlineData(Shape + "pub struct Sq :: [Shape] {\n    pub s: int,\n    fn area(): int {\n        return this.s;\n    }\n}\n",
        "'area' answers 'Shape.area'", "write 'pub fn area'")]
    [InlineData(Shape + "pub struct Sq :: [Shape] {\n    pub s: int,\n    internal fn area(): int {\n        return this.s;\n    }\n}\n",
        "'area' answers 'Shape.area'", "write 'pub fn area'")]
    [InlineData(Shape + "pub struct Sq :: [Shape] {\n    pub s: int,\n    private fn area(): int {\n        return this.s;\n    }\n}\n",
        "'area' answers 'Shape.area'", "write 'pub fn area'")]
    [InlineData(Shape + "pub class Sq :: [Shape] {\n    pub s: int,\n    fn area(): int {\n        return this.s;\n    }\n}\n",
        "'area' answers 'Shape.area'", "write 'pub fn area'")]
    [InlineData(Shape + "pub enum Sq :: [Shape] {\n    One, Two;\n\n    fn area(): int {\n        return 1;\n    }\n}\n",
        "'area' answers 'Shape.area'", "write 'pub fn area'")]
    // the narrower of the two decides: an internal conformance takes no private member
    [InlineData(Shape + "struct Sq :: [Shape] {\n    s: int,\n    private fn area(): int {\n        return this.s;\n    }\n}\n",
        "'area' answers 'Shape.area'", "write 'fn area'")]
    [InlineData(Quiet + "pub struct Sq :: [Quiet] {\n    pub s: int,\n    private fn area(): int {\n        return this.s;\n    }\n}\n",
        "'area' answers 'Quiet.area'", "write 'fn area'")]
    // every kind of member an interface asks for
    [InlineData("pub interface Counter {\n    mut fn bump(): void;\n}\n\npub struct C :: [Counter] {\n    pub var n: int,\n    mut fn bump(): void {\n        this.n += 1;\n    }\n}\n",
        "'bump' answers 'Counter.bump'", "write 'pub mut fn bump'")]
    [InlineData("pub interface Made {\n    static fn make(): Self;\n}\n\npub struct N :: [Made] {\n    pub n: int,\n    static fn make(): N {\n        return N { n = 0 };\n    }\n}\n",
        "'make' answers 'Made.make'", "write 'pub static fn make'")]
    [InlineData("pub interface Zero {\n    static let zero: Self;\n}\n\npub struct N :: [Zero] {\n    pub n: int,\n    static let zero: N = N { n = 0 };\n}\n",
        "'zero' answers 'Zero.zero'", "write 'pub static let zero'")]
    // a parent the list implies
    [InlineData("pub interface A {\n    fn a(): int;\n}\n\npub interface B :: [A] {\n    fn b(): int;\n}\n\npub struct S :: [B] {\n    pub n: int,\n    fn a(): int {\n        return 1;\n    }\n    pub fn b(): int {\n        return 2;\n    }\n}\n",
        "'a' answers 'A.a'", "write 'pub fn a'")]
    // the conformance stands in a block, the member in the body — or in a block of its own
    [InlineData(Shape + "pub struct Sq {\n    pub s: int,\n    fn area(): int {\n        return this.s;\n    }\n}\n\nextend Sq :: [Shape] {\n}\n",
        "'area' answers 'Shape.area'", "write 'pub fn area'")]
    [InlineData(Shape + "pub struct Sq :: [Shape] {\n    pub s: int,\n}\n\nextend Sq {\n    fn area(): int {\n        return this.s;\n    }\n}\n",
        "'area' answers 'Shape.area'", "write 'pub fn area'")]
    public void A_member_that_answers_an_interface_says_the_conformances_word(string declarations, string what, string hint)
    {
        var (exit, error) = Build(declarations);
        Assert.True(exit == 1, $"exit {exit}\n{error}");
        Assert.Contains("error[LYR-SEM0167]: " + what, error);
        Assert.Contains(hint, error);
        Assert.Single(Regex.Matches(error, @"error\["));
    }

    /// <summary>What the rule found in the library: the messages of <c>std.task</c>'s three
    /// errors were read through <c>Error</c> and refused by their own name
    /// (<c>LYR-RES0009</c>).</summary>
    [Fact]
    public void The_librarys_errors_say_their_message_by_their_own_name()
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", """
            import std.io { println };
            import std.task { Cancelled, TimedOut, ChannelClosed };

            fn main(): void {
                let a = Cancelled { };
                let b = TimedOut { };
                let c = ChannelClosed { };
                println(f"{a.message()}, {b.message()}, {c.message()}");
            }
            """));
        Assert.Equal("cancelled, timed out, channel closed\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    [Theory]
    [InlineData(Shape + "pub struct Sq :: [Shape] {\n    pub s: int,\n    pub fn area(): int {\n        return this.s;\n    }\n}\n")]
    // an internal type: the conformance is internal, and no word says that
    [InlineData(Shape + "struct Sq :: [Shape] {\n    s: int,\n    fn area(): int {\n        return this.s;\n    }\n}\n")]
    [InlineData(Shape + "struct Sq :: [Shape] {\n    s: int,\n    internal fn area(): int {\n        return this.s;\n    }\n}\n")]
    // an internal interface likewise
    [InlineData(Quiet + "pub struct Sq :: [Quiet] {\n    pub s: int,\n    fn area(): int {\n        return this.s;\n    }\n}\n")]
    // a private type: every word is as wide as its conformance
    [InlineData(Shape + "private struct Sq :: [Shape] {\n    s: int,\n    private fn area(): int {\n        return this.s;\n    }\n}\n")]
    // a conformance block: its members take the block's visibility and write no word
    [InlineData(Shape + "pub struct Sq {\n    pub s: int,\n}\n\nextend Sq :: [Shape] {\n    fn area(): int {\n        return this.s;\n    }\n}\n")]
    // a default nobody overrides answers itself
    [InlineData("pub interface Shape {\n    fn area(): int {\n        return 0;\n    }\n}\n\npub struct Sq :: [Shape] {\n    pub s: int,\n}\n")]
    // a method no interface asks for keeps its own word
    [InlineData(Shape + "pub struct Sq :: [Shape] {\n    pub s: int,\n    pub fn area(): int {\n        return this.side();\n    }\n    private fn side(): int {\n        return this.s;\n    }\n}\n")]
    public void A_word_as_wide_as_the_conformance_is_enough(string declarations)
    {
        var (exit, error) = Build(declarations);
        Assert.True(exit == 0, $"exit {exit}\n{error}");
        Assert.DoesNotContain("LYR-SEM0167", error);
    }
}
