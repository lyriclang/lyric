using Lyric.Core;
using Lyric.Formatting;

namespace Lyric.Tests.Formatting;

/// <summary>
/// The formatter on Lyric 5's constructs: source in, formatted source out, each case checked for
/// idempotence. A formatted case is its own golden — it comes back unchanged.
/// </summary>
public class Lyric5FormatterTests
{
    private static string Format(string source)
    {
        var sm = new SourceManager { Lyric5 = true };
        var id = sm.AddVirtual("<test>", source);
        var de = new DiagnosticEngine(sm);
        var formatted = Formatter.Format(sm, id, de);

        Assert.False(de.HasErrors, "the test source did not parse");
        Assert.NotNull(formatted);

        var sm2 = new SourceManager { Lyric5 = true };
        var id2 = sm2.AddVirtual("<test2>", formatted);
        Assert.Equal(formatted, Formatter.Format(sm2, id2, new DiagnosticEngine(sm2)));
        return formatted;
    }

    private static void Keeps(string formatted) => Assert.Equal(formatted, Format(formatted));

    [Fact]
    public void A_long_list_of_interfaces_puts_one_to_a_line()
    {
        Keeps("""
            interface Equatable { }

            struct Short :: [Equatable] {
                n: int,
            }

            struct Wide :: [
                Equatable,
                Ordered,
                TotalOrder,
                Hashable,
                Display,
                Debug,
                Encode,
                Decode,
                Default,
                Clone,
                Copy
            ] {
                n: int,
            }

            """);
    }

    [Fact]
    public void A_broken_if_expression_puts_its_else_one_level_in()
    {
        Assert.Equal("""
            fn f(took: int, n: int): int {
                let budgetedPace = if (took > 0) ((n as float) * 120000000.0 / (took as float)) as int
                    else n * 100;
                return budgetedPace;
            }

            """, Format("fn f(took: int, n: int): int { let budgetedPace = if (took > 0) ((n as float) * 120000000.0 / (took as float)) as int else n * 100; return budgetedPace; }"));
    }

    [Fact]
    public void Written_parentheses_stay_and_none_are_added()
    {
        Assert.Equal("""
            fn f(a: bool, b: bool, c: bool, x: int): int {
                let k = (a && b) && c;
                let m = ((x * 2)) + (x);
                return (m + 1) * 2;
            }

            """, Format("fn f(a: bool, b: bool, c: bool, x: int): int { let k = (a && b) && c; let m = ((x*2)) + (x); return (m+1)*2; }"));
    }

    [Fact]
    public void An_attribute_put_on_its_own_line_brings_no_blank_line_on_the_next_pass()
    {
        // Format checks the second pass: the attribute's own line must not read as a gap
        Assert.Equal("""
            struct Child {
                n: int,
                @Keep
                parent: int,
            }

            enum Shape {
                @Wire(1)
                Dot,
                @Wire(2)
                Box,
            }

            """, Format("struct Child { n: int, @Keep parent: int, }\n\nenum Shape { @Wire(1) Dot, @Wire(2) Box }"));
    }

    [Fact]
    public void Each_attribute_stands_on_its_own_line_on_every_declaration()
    {
        Keeps("""
            @NoInline
            @MustUse
            fn pick(): int {
                return 1;
            }

            @Tag("iface")
            interface Greets {
                fn greet(): string;
            }

            struct S {
                @Level(3)
                a: int,
            }

            """);
    }

    [Fact]
    public void Inline_keeps_its_place_among_the_words_before_fn()
    {
        Keeps("""
            extern "C" inline fn abs(x: int32): int32;

            inline fn twice(x: int): int {
                return x * 2;
            }

            """);
    }

    [Fact]
    public void Comptime_if_and_for_keep_their_word_and_an_else_if_does_not_repeat_it()
    {
        Keeps("""
            fn f<T>(): string {
                var out = "";
                comptime for (f in fields<T>()) {
                    out += f.name;
                }
                comptime if (size<T>() > 16) {
                    out += "big";
                } else if (size<T>() > 8) {
                    out += "medium";
                } else {
                    out += "small";
                }
                return out;
            }

            fn g(): int {
                return comptime if (1 < 2) 1 else 2;
            }

            """);
    }

    [Fact]
    public void A_local_function_is_written_as_a_function()
    {
        Keeps("""
            fn main(): void {
                fn fact(n: int): int {
                    return if (n <= 1) 1 else n * fact(n - 1);
                }
                let _ = fact(5);
            }

            """);
    }

    [Fact]
    public void A_field_and_a_payload_by_their_info_and_an_initializer_by_rounds()
    {
        Keeps("""
            fn same<T>(a: T, b: T): bool {
                comptime for (f in fields<T>()) {
                    if (!(a.[f] == b.[f])) {
                        return false;
                    }
                }
                return true;
            }

            fn copy<T>(a: T): T {
                return T { comptime for (f in fields<T>()) [f] = a.[f] };
            }

            """);
    }

    [Fact]
    public void An_enums_type_arguments_stand_before_its_variant()
    {
        Keeps("""
            enum Ev<T> {
                Hit { v: T },
            }

            fn hit<T>(v: T): Ev<T> {
                return Ev<T>.Hit { v = v };
            }

            """);
    }

    [Fact]
    public void A_lambda_does_not_see_the_labels_of_the_loops_around_it()
    {
        Keeps("""
            fn main(): void {
                outer: for (i in 0..3) {
                    let f = () => loop {
                        break outer;
                    };
                }
            }

            """);
    }

    [Fact]
    public void An_if_with_block_branches_is_an_operand_without_parentheses()
    {
        Keeps("""
            fn f(c: bool): int {
                let whole = if (c) {
                    1
                } else {
                    2
                } + 3;
                return whole;
            }

            """);
    }
}
