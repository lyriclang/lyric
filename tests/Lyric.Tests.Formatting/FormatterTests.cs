using Lyric.Core;
using Lyric.Formatting;

namespace Lyric.Tests.Formatting;

/// <summary>
/// Source in, formatted source out — the shape of the output pinned case by case, and every
/// case checked for idempotence: the second pass must change nothing, or the formatter argues
/// with itself in every save hook.
///
/// <para>Comment handling is the next slice; the inputs here carry none.</para>
/// </summary>
public class FormatterTests
{
    private static string Format(string source)
    {
        var sm = new SourceManager();
        var id = sm.AddVirtual("<test>", source);
        var de = new DiagnosticEngine(sm);
        var formatted = Formatter.Format(sm, id, de);

        Assert.False(de.HasErrors, "the test source did not parse");
        Assert.NotNull(formatted);

        // Idempotence, on every case of this file: format(format(x)) == format(x).
        var sm2 = new SourceManager();
        var id2 = sm2.AddVirtual("<test2>", formatted);
        var second = Formatter.Format(sm2, id2, new DiagnosticEngine(sm2));
        Assert.Equal(formatted, second);

        return formatted;
    }

    [Fact]
    public void Whitespace_is_normalized_and_a_blank_line_survives_as_one()
    {
        Assert.Equal("""
            fn main(): int {
                let x = 1;

                return x;
            }

            """, Format("fn   main( ):int{let x=1;\n\n\n   return x;}"));
    }

    [Fact]
    public void A_var_field_keeps_its_word()
    {
        // 'var' is the one word that makes a field writable (design/v5/spec/02 M2): a formatter
        // that dropped it would change what the program may do.
        Assert.Equal("""
            struct Counter {
                var n: int = 0,
                step: int,
            }

            """, Format("struct Counter{var   n:int=0,step:int}"));
    }

    [Fact]
    public void A_nested_optional_type_is_written_without_a_gap()
    {
        // '??T' is a type (design/v5/spec/03 T4 O1). The lexer hands '??' over as one token, the
        // coalesce operator; in type position it is two levels, however they were spaced.
        Assert.Equal("""
            fn f(x: ??int, y: ???string): ??int {
                return x ?? null;
            }

            """, Format("fn f(x: ? ?int, y: ?? ?string): ??int { return x??null; }"));
    }

    [Fact]
    public void An_implicit_member_keeps_its_dot()
    {
        // '.Red' names a member of the type the position expects (design/v5/spec/08 Y6, Y9); the
        // dot is what tells it from a binding in a pattern and from a name in an expression.
        Assert.Equal("""
            fn f(c: Color, s: Shape): int {
                let m = match (s) {
                    .Num(n) => n,
                    .Rect { w = 0, h } => h,
                    .Empty => 0,
                };
                return take(.Red, .Rect { w = 1, h = 2 }) + (if (c == .Blue) m else 0);
            }

            """, Format("fn f(c:Color,s:Shape):int{let m=match(s){.Num(n)=>n,.Rect{w=0,h}=>h,.Empty=>0};return take(.Red,.Rect{w=1,h=2})+(if(c==.Blue)m else 0);}"));
    }

    [Fact]
    public void A_from_end_index_keeps_its_caret_against_the_operand()
    {
        // '^n' is a prefix inside the brackets (design/v5/spec/03 T14 N6); between operands the
        // same character is the exclusive or and keeps its spaces.
        Assert.Equal("""
            fn f(xs: int[], a: int, b: int): int {
                return xs[^1] + xs[a ^ b] + xs.length();
            }

            """, Format("fn f(xs:int[],a:int,b:int):int{return xs[ ^ 1]+xs[a^b]+xs.length();}"));
    }

    [Fact]
    public void A_view_range_keeps_its_open_side_open()
    {
        // 'xs[a..b]' and the open forms (design/v5/spec/03 T13 A2): the range has no spaces, an
        // open side prints nothing, and the type is written as any generic name.
        Assert.Equal("""
            fn f(xs: int[]): Slice<int> {
                let v = xs[1..^1];
                return v[..2][1..][..][..=0];
            }

            """, Format("fn f(xs:int[]):Slice<int>{let v=xs[ 1 .. ^1 ];return v[..2][1..][ .. ][..=0];}"));
    }

    [Fact]
    public void An_inline_array_type_keeps_its_length()
    {
        // 'T[N]' (design/v5/spec/03 T13 A4) is a type of its own; the length is printed as written,
        // and an optional element keeps its parentheses as it does for 'T[]'.
        Assert.Equal("""
            struct Mat {
                m: float[16],
                grid: (?int)[2][2],
            }

            """, Format("struct Mat{m:float[ 16 ],grid:(?int)[2][2]}"));
    }

    [Fact]
    public void A_tuple_keeps_its_labels_and_positions()
    {
        // '(x: int, y: int)' names the elements (design/v5/spec/03 T16); '.0' reads one by position.
        Assert.Equal("""
            fn f(p: (x: int, y: int), t: (int, string)): int {
                return p.x + t.0 + p.1;
            }

            """, Format("fn f(p:(x :int,y: int),t:(int,string)):int{return p.x+t.0+p.1;}"));
    }

    [Fact]
    public void A_with_keeps_its_fields_on_one_line_while_they_fit()
    {
        // 'p with { x = 1 }' (design/v5/spec/02 M6): a postfix with the fields of an initializer,
        // a path on the left as written.
        Assert.Equal("""
            fn f(p: Point): Point {
                let q = p with { x = 1, pos.y = p.y };
                return q with { x = 2 }.x;
            }

            """, Format("fn f(p:Point):Point{let q=p with{x=1,pos.y=p.y};return (q with {x=2}).x;}"));
    }

    [Fact]
    public void A_trailing_block_keeps_its_parameters_in_the_head()
    {
        // '{ acc, x => … }' (design/v5/spec/08 Y11 F2): the parameters stand before '=>' in the
        // block's head; a one-line body stays on the line, a longer one breaks like a block.
        Assert.Equal("""
            fn f(xs: int[]): int {
                let a = fold(xs, 0) { acc, x => acc + x };
                let b = fold(xs, 1) { acc, x =>
                    let y = acc * x;
                    y + 1
                };
                return a + b + count(xs) { it + 1 };
            }

            """, Format("fn f(xs:int[]):int{let a=fold(xs,0){acc,x=>acc+x};let b=fold(xs,1){acc,x=>let y=acc*x;y+1};return a+b+count(xs){it+1};}"));
    }

    // On the 'bit' case: '&' binds TIGHTER than '==' in this grammar (§6.1, level 8 against 12),
    // unlike in C — the parentheses there are redundant and go like any others.
    [Fact]
    public void A_throw_expression_keeps_its_place_as_a_prefix()
    {
        Assert.Equal("""
            fn f(o: ?int): int throws E {
                let v = o ?? throw E { };
                return if (v > 0) v else throw E { };
            }

            """, Format("""
            fn f(o: ?int): int throws E {
                let v = (o ?? (throw E {}));
                return if (v > 0) v else (throw E {});
            }
            """));
    }

    [Fact]
    public void A_loop_label_stays_on_its_loop_and_on_its_jumps()
    {
        Assert.Equal("""
            fn f(): int {
                outer: for (i in 0..3) {
                    inner: while (true) {
                        if (i == 1) {
                            continue outer;
                        }
                        break inner;
                    }
                }
                return 0;
            }

            """, Format("""
            fn f(): int {
                outer:for (i in 0..3) { inner : while (true) { if (i == 1) { continue   outer; } break inner ; } }
                return 0;
            }
            """));
    }

    [Fact]
    public void Redundant_parentheses_go_and_needed_ones_stay()
    {
        Assert.Equal("""
            fn f(a: int, b: int): int {
                let keep = (a + b) * 2;
                let drop = a * b + 2;
                let bit = a & 3 == 1;
                return keep + drop;
            }

            """, Format("""
            fn f(a: int, b: int): int {
                let keep = ((a + b)) * 2;
                let drop = ((a * b)) + (2);
                let bit = ((a & 3) == 1);
                return (keep) + (drop);
            }
            """));
    }

    [Fact]
    public void Right_associative_coalesce_keeps_its_shape()
    {
        var formatted = Format("""
            fn f(a: ?int, b: ?int, c: int): int {
                let flat = a ?? b ?? c;
                let forced = (a ?? b) ?? c;
                return flat + forced;
            }
            """);

        Assert.Contains("let flat = a ?? b ?? c;", formatted);
        Assert.Contains("let forced = (a ?? b) ?? c;", formatted);
    }

    [Fact]
    public void A_call_that_does_not_fit_breaks_one_argument_per_line()
    {
        var wide = "a" + new string('x', 80);
        var expected = $"fn f(): void {{\n    someFunction(\n        {wide},\n"
                       + "        second,\n        third\n    );\n}\n";

        Assert.Equal(expected, Format($"fn f(): void {{ someFunction({wide}, second, third); }}"));
    }

    [Fact]
    public void A_struct_initializer_fits_flat_and_breaks_with_a_trailing_comma()
    {
        var formatted = Format("""
            struct P {
                x: int,
                y: int,
            }

            fn f(): void {
                let flat = P { x = 1, y = 2 };
                let broken = P { x = 1111111111111111111, y = 2222222222222222222 + 3333333333333333333 + 4444444444 };
            }
            """);

        Assert.Contains("let flat = P { x = 1, y = 2 };", formatted);
        Assert.Contains("""
                let broken = P {
                    x = 1111111111111111111,
                    y = 2222222222222222222 + 3333333333333333333 + 4444444444,
                };
            """, formatted);
    }

    [Fact]
    public void Match_arms_stand_one_per_line()
    {
        Assert.Equal("""
            fn f(n: int): int {
                return match (n) {
                    0 => 1,
                    1 | 2 => 2,
                    3..=9 => 3,
                    _ => {
                        return 4;
                    }
                };
            }

            """, Format("fn f(n: int): int { return match (n) { 0 => 1, 1 | 2 => 2, 3..=9 => 3, _ => { return 4; } }; }"));
    }

    [Fact]
    public void An_enum_parts_variants_from_methods_with_a_semicolon()
    {
        Assert.Equal("""
            enum Shape {
                Circle(float),
                Rectangle(float, float);

                fn area(): float {
                    return match (this) {
                        Circle(r) => 3.14 * r * r,
                        Rectangle(w, h) => w * h,
                    };
                }
            }

            """, Format("""
            enum Shape { Circle(float), Rectangle(float, float);
            fn area(): float { return match (this) { Circle(r) => 3.14 * r * r, Rectangle(w, h) => w * h, }; } }
            """));
    }

    [Fact]
    public void An_else_if_ladder_stays_a_ladder()
    {
        Assert.Equal("""
            fn f(n: int): int {
                if (n < 0) {
                    return -1;
                } else if (n == 0) {
                    return 0;
                } else {
                    return 1;
                }
            }

            """, Format("fn f(n: int): int { if (n<0) { return -1; } else if (n==0) { return 0; } else { return 1; } }"));
    }

    [Fact]
    public void Imports_sit_together_and_declarations_breathe()
    {
        Assert.Equal("""
            import std.io.console;
            import std.collections { emptyList, emptyMap };

            fn a(): void { }

            fn b(): void { }

            """, Format("""
            import std.io.console;

            import std.collections {emptyList,emptyMap};
            fn a(): void {}
            fn b(): void {}
            """));
    }

    [Fact]
    public void Literal_spelling_survives()
    {
        var formatted = Format("""
            fn f(): void {
                let hex = 0xFF_EC;
                let grouped = 1_000_000;
                let suffixed = 3u8;
                let sci = 1.5e-3f32;
                let s = "a\tb";
                let c = '\n';
                let msg = f"n = {grouped:N0}, done";
            }
            """);

        Assert.Contains("0xFF_EC", formatted);
        Assert.Contains("1_000_000", formatted);
        Assert.Contains("3u8", formatted);
        Assert.Contains("1.5e-3f32", formatted);
        Assert.Contains("\"a\\tb\"", formatted);
        Assert.Contains("'\\n'", formatted);
        Assert.Contains("f\"n = {grouped:N0}, done\"", formatted);
    }

    [Fact]
    public void Types_keep_their_binding_parentheses()
    {
        var formatted = Format("""
            fn f(a: ?int[], b: (?int)[], c: (fn(int) -> bool)[], d: fn(int) -> int[]): void { }
            """);

        Assert.Contains("a: ?int[]", formatted);
        Assert.Contains("b: (?int)[]", formatted);
        Assert.Contains("c: (fn(int) -> bool)[]", formatted);
        Assert.Contains("d: fn(int) -> int[]", formatted);
    }

    [Fact]
    public void A_lambda_as_an_operand_is_parenthesized_and_as_an_argument_is_not()
    {
        var formatted = Format("""
            fn apply(f: fn(int) -> int, n: int): int {
                return f(n);
            }

            fn g(): int {
                let direct = apply((n: int) => n * 2, 10);
                return direct;
            }
            """);

        Assert.Contains("apply((n: int) => n * 2, 10)", formatted);
    }

    [Fact]
    public void Members_with_bodies_get_air_and_fields_sit_together()
    {
        Assert.Equal("""
            pub struct Vec2 :: [Add<Vec2, Vec2>] {
                x: float,
                y: float,

                fn add(other: Vec2): Vec2 {
                    return Vec2 { x = this.x + other.x, y = this.y + other.y };
                }
            }

            """, Format("""
            pub struct Vec2::[Add<Vec2, Vec2>] { x: float, y: float,
            fn add(other: Vec2): Vec2 { return Vec2 { x = this.x + other.x, y = this.y + other.y }; } }
            """));
    }

    [Fact]
    public void A_typed_underscore_catch_keeps_its_type()
    {
        // 'catch (_: Boom)' selects by its type without binding a name. The formatter printed
        // the '_' and DROPPED the type, turning a selective catch into a catch-all — the one
        // thing a formatter must never do: change what the program means. The form never stood
        // in the corpus because it crashed the compiler until 3.6.0 (#115); it parses since
        // forever, so the formatter could always be handed one.
        Assert.Equal("""
            fn f(): void throws {
                try {
                    risky();
                } catch (_: IoError) {
                    swallow();
                } catch (_) {
                    other();
                }
            }

            """, Format("""
            fn f(): void throws { try { risky(); }
            catch (_: IoError) { swallow(); } catch (_) { other(); } }
            """));
    }

    [Fact]
    public void Coroutines_defer_and_try_round_trip()
    {
        Assert.Equal("""
            fn f(): void throws {
                defer {
                    cleanup();
                }
                try {
                    risky();
                } catch (e: IoError) {
                    handle(e);
                } catch (_) {
                    swallow();
                }
            }

            """, Format("""
            fn f(): void throws { defer { cleanup(); } try { risky(); }
            catch (e: IoError) { handle(e); } catch (_) { swallow(); } }
            """));
    }

    [Fact]
    public void Attributes_stand_on_their_own_lines()
    {
        Assert.Equal("""
            @Component
            pub struct Health {
                value: int,
                max: int = 100,
            }

            @System { order = 10 }
            pub fn tick(dt: float): void { }

            """, Format("""
            @Component pub struct Health { value: int, max: int = 100 }
            @System{order=10} pub fn tick(dt: float): void {}
            """));
    }

    [Fact]
    public void A_module_header_leads_with_a_blank_line_after_it()
    {
        Assert.Equal("""
            module geometry.shapes;

            pub fn area(r: float): float {
                return 3.14 * r * r;
            }

            """, Format("module geometry.shapes;\npub fn area(r: float): float { return 3.14*r*r; }"));
    }

    [Fact]
    public void Generic_constraints_and_type_arguments_round_trip()
    {
        var formatted = Format("""
            fn total<T :: [Add<T, T>]>(values: T[], zero: T): T {
                var sum = zero;
                for (v in values) {
                    sum = sum + v;
                }
                return sum;
            }

            fn use(): int {
                return total<int>([1, 2, 3], 0);
            }
            """);

        Assert.Contains("fn total<T :: [Add<T, T>]>(values: T[], zero: T): T", formatted);
        Assert.Contains("total<int>([1, 2, 3], 0)", formatted);
    }

    [Fact]
    public void A_default_type_argument_keeps_its_spelling()
    {
        var formatted = Format("interface Add<Rhs=Self> { fn add(o: Rhs): Self; }");
        Assert.Contains("interface Add<Rhs = Self>", formatted);
    }

    [Fact]
    public void An_extend_on_a_shape_keeps_its_target()
    {
        var formatted = Format("extend<T>   T[] { fn first(): T { return this[0]; } }\nextend<T> ?T { fn some(): bool { return this != null; } }");
        Assert.Contains("extend<T> T[] {", formatted);
        Assert.Contains("extend<T> ?T {", formatted);
    }

    [Fact]
    public void A_generic_extend_keeps_its_parameters()
    {
        var formatted = Format("extend<T::[Display]>   List<T> :: [Display] { fn show(): string { return \"l\"; } }");
        Assert.Contains("extend<T :: [Display]> List<T> :: [Display]", formatted);
    }

    [Fact]
    public void A_sealed_interface_keeps_its_word()
    {
        var formatted = Format("pub   sealed   interface Shape { fn area(): int; }");
        Assert.Contains("pub sealed interface Shape", formatted);
    }

    [Fact]
    public void A_type_test_and_a_type_pattern_keep_their_spelling()
    {
        var formatted = Format("fn f(s: Shape): int { if (s is Circle&&s.r>0) { return s.r; } return match (s) { c:Circle => c.r, _:Rect => 2, _ => 0 }; }");
        Assert.Contains("s is Circle && s.r > 0", formatted);
        Assert.Contains("c: Circle => c.r", formatted);
        Assert.Contains("_: Rect => 2", formatted);
    }

    [Fact]
    public void An_associated_type_keeps_its_spelling()
    {
        var formatted = Format("interface C { type Item; type Out=Self; fn f(): Self.Item; }\n"
            + "struct B :: [C] { type Item=int; v: int }\n"
            + "fn s<T :: [C<Item=int>]>(c: T): int { return 0; }");
        Assert.Contains("type Item;", formatted);
        Assert.Contains("type Out = Self;", formatted);
        Assert.Contains("type Item = int;", formatted);
        Assert.Contains("C<Item = int>", formatted);
    }

    [Fact]
    public void A_delegated_conformance_keeps_its_by()
    {
        var formatted = Format("class Dog :: [Walker by legs,Named] { var legs: Legs }");
        Assert.Contains(":: [Walker by legs, Named]", formatted);
    }

    [Fact]
    public void A_named_argument_keeps_its_name()
    {
        var formatted = Format("fn f(): int { return connect(1,port :80, retries:  3); }");
        Assert.Contains("connect(1, port: 80, retries: 3)", formatted);
    }

    [Fact]
    public void An_instantiated_function_and_a_placeholder_keep_their_spelling()
    {
        // 'ident<int>' as a value and '_' among the type arguments (03 T8, T17) are printed as
        // written: the arguments in angle brackets, comma-separated, with nothing else.
        var formatted = Format("fn f(): int { let g = ident<int>; return apply(g, collect< _ ,string >(1, \"y\")); }");
        Assert.Contains("let g = ident<int>;", formatted);
        Assert.Contains("collect<_, string>(1, \"y\")", formatted);
    }

    [Fact]
    public void An_opaque_type_alias_round_trips()
    {
        Assert.Equal("""
            opaque type Entity = int;

            pub type Meters = int;

            """, Format("opaque   type Entity=int;\npub type Meters = int;"));
    }

    [Fact]
    public void An_interface_parent_list_round_trips()
    {
        Assert.Equal("""
            interface Named {
                fn name(): string;
            }

            interface Labeled :: [Named] {
                fn label(): string;
            }

            """, Format("interface Named{fn name():string;}\ninterface Labeled::[Named]{fn label():string;}"));
    }

    // ------------------------------------------------------------------ operator chains

    [Fact]
    public void A_chain_that_fits_stays_on_its_line() =>
        Assert.Equal("""
            fn f(a: int, b: int, c: int): int {
                return a + b * c;
            }

            """, Format("fn f(a:int,b:int,c:int):int{return a+b*c;}"));

    [Fact]
    public void A_chain_over_the_limit_breaks_before_every_operator()
    {
        // The whole level breaks or none of it does: 'a && b && c' parses as '((a && b) && c)',
        // and formatting that shape as it stands would let the inner pair fit while the outer one
        // breaks — a staircase nobody writes by hand.
        var formatted = Format(
            "fn f(alpha: int, beta: int, gamma: int): int {\n"
            + "    if (alpha > 0 && beta > 0 && gamma > 0 && alpha + beta > gamma"
            + " && beta + gamma > alpha && gamma + alpha > beta) {\n"
            + "        return 1;\n    }\n    return 0;\n}");

        Assert.Equal("""
            fn f(alpha: int, beta: int, gamma: int): int {
                if (alpha > 0
                    && beta > 0
                    && gamma > 0
                    && alpha + beta > gamma
                    && beta + gamma > alpha
                    && gamma + alpha > beta) {
                    return 1;
                }
                return 0;
            }

            """, formatted);

        Assert.All(formatted.Split('\n'), line => Assert.True(line.Length <= 100, line));
    }

    [Fact]
    public void Only_the_level_that_does_not_fit_breaks() =>
        // The '||' chain breaks, the '&&' chains inside it still fit and stay flat. Groups nest,
        // so the decision belongs to a level rather than to an expression.
        Assert.Equal("""
            fn f(): bool {
                let mixedPrecedence = 1 > 0 && 2 > 1 && 3 > 2
                    || 4 > 3 && 5 > 4 && 6 > 5
                    || 7 > 6 && 8 > 7 && 9 > 8;
                return mixedPrecedence;
            }

            """, Format("fn f():bool{let mixedPrecedence=1>0&&2>1&&3>2||4>3&&5>4&&6>5||7>6&&8>7&&9>8;"
            + "return mixedPrecedence;}"));

    [Fact]
    public void A_broken_chain_keeps_the_parentheses_it_needs() =>
        // Flattening walks the associative side only, so a written group on the other side stays
        // a level of its own and gets its parentheses back.
        Assert.Equal("""
            fn f(a: int, b: int, c: int, d: int, e: int, ff: int, g: int, h: int): int {
                return a
                    * (b + c)
                    * (d + e)
                    * (ff + g)
                    * (h + a)
                    * (b + c)
                    * (d + e)
                    * (ff + g)
                    * (h + a)
                    * (b + c);
            }

            """, Format(
            "fn f(a:int,b:int,c:int,d:int,e:int,ff:int,g:int,h:int):int{"
            + "return a*(b+c)*(d+e)*(ff+g)*(h+a)*(b+c)*(d+e)*(ff+g)*(h+a)*(b+c);}"));

    [Fact]
    public void An_operand_that_breaks_by_itself_keeps_the_chain_together() =>
        // A match expression lays itself out over several lines whatever the width says. A group
        // around the chain could never be flat then, and the multiplication would break for a
        // reason that has nothing to do with the width.
        Assert.Equal("""
            enum Rarity {
                Common,
                Rare,
            }

            fn price(price: int, rarity: Rarity): int {
                return price * match (rarity) {
                    Common => 1,
                    Rare => 3,
                };
            }

            """, Format(
            "enum Rarity{Common,Rare}\n"
            + "fn price(price:int,rarity:Rarity):int{"
            + "return price*match(rarity){Common=>1,Rare=>3,};}"));

    [Fact]
    public void A_coroutine_type_keeps_its_throws()
    {
        // The suffix is part of the TYPE, so it has to survive a field, a parameter and a
        // binding — and the typeless form has no trailing space to lose.
        var formatted = Format("""
            class Runner {
                co:  ?Coroutine<int>   throws  Exception = null,
                any: ?Coroutine<int> throws = null,
                fn take(c: Coroutine<int> throws Exception, n: int): int { return n; }
            }
            """);

        Assert.Contains("co: ?Coroutine<int> throws Exception = null,", formatted, StringComparison.Ordinal);
        Assert.Contains("any: ?Coroutine<int> throws = null,", formatted, StringComparison.Ordinal);
        Assert.Contains("fn take(c: Coroutine<int> throws Exception, n: int): int", formatted, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ attributes (3.9)

    [Fact]
    public void A_positional_attribute_keeps_its_parentheses() =>
        Assert.Contains("@Retry(3)", Format("""
            @Retry( 3 )
            fn f(): void { }
            """), StringComparison.Ordinal);

    [Fact]
    public void Two_attributes_take_the_group_shape() =>
        // The one shape: stacked input, grouped output. The parser flattens both spellings to
        // the same list, which is what keeps the rewrite reparse-identical.
        Assert.Contains("@[Tag, System { order = 2 }]", Format("""
            @Tag
            @System { order = 2 }
            fn f(): void { }
            """), StringComparison.Ordinal);

    [Fact]
    public void A_single_entry_group_becomes_the_plain_spelling() =>
        Assert.Contains("@Tag\nfn f", Format("""
            @[Tag]
            fn f(): void { }
            """).Replace("\r\n", "\n"), StringComparison.Ordinal);

    [Fact]
    public void A_wide_group_breaks_one_entry_per_line()
    {
        var formatted = Format("""
            @[Alpha { label = "the first of the long ones, and then some more of it" }, Beta { label = "the second" }, Gamma(9), Delta]
            fn f(): void { }
            """).Replace("\r\n", "\n");

        Assert.Contains("@[\n", formatted, StringComparison.Ordinal);
        Assert.Contains("    Gamma(9),\n    Delta,\n]", formatted, StringComparison.Ordinal);
    }

    // --- errors (design/v5/spec/08 D9, Y4) ---

    [Fact]
    public void A_throws_set_keeps_the_list_rule()
    {
        // One type alone, several in brackets, none for the bare form.
        var formatted = Format("fn a(): int throws E { return 0; }\nfn b(): int throws [ A,B ] { return 0; }\nfn c(): int throws [E] { return 0; }\nfn d(): int throws { return 0; }\n")
            .Replace("\r\n", "\n");
        Assert.Contains("fn a(): int throws E {", formatted, StringComparison.Ordinal);
        Assert.Contains("fn b(): int throws [A, B] {", formatted, StringComparison.Ordinal);
        Assert.Contains("fn c(): int throws E {", formatted, StringComparison.Ordinal);
        Assert.Contains("fn d(): int throws {", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void Try_round_trips_with_what_it_covers()
    {
        var formatted = Format("fn g(): int throws E { let x = try  f()+1; try h(); return try f(); }").Replace("\r\n", "\n");
        Assert.Contains("let x = try f() + 1;", formatted, StringComparison.Ordinal);
        Assert.Contains("    try h();\n", formatted, StringComparison.Ordinal);
        Assert.Contains("return try f();", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void A_parenthesized_try_keeps_its_parentheses() =>
        // '(try f()) + 1' marks the call alone; without the parentheses 'try' would cover the sum.
        Assert.Contains("(try f()) + 1", Format("fn g(): int throws E { return (try f()) + 1; }"), StringComparison.Ordinal);
}
