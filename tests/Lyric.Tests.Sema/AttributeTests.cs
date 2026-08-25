using System.Runtime.CompilerServices;
using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Tests.Sema;

/// <summary>
/// What an attribute is allowed to name, and what its arguments are allowed to hold.
///
/// <para>The pinned decisions: an attribute is a STRUCT, and where it may sit is the marker
/// interface it declares — conformance, not the name, the same nominal rule the operators follow.
/// Arguments are values at compile time, because they end up in a bytecode section — a literal,
/// or since 2.4 a <c>let</c> bound to one — and the emitted row is complete: a field the use does
/// not write needs such a value as its default.</para>
/// </summary>
public class AttributeTests
{
    private static string RepoRoot([CallerFilePath] string thisFile = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));

    private static DiagnosticEngine Check(string source)
    {
        var sm = new SourceManager();
        var id = sm.AddVirtual("test.lyr", source);
        var de = new DiagnosticEngine(sm);
        var comp = new Compilation(sm, de);
        comp.ModuleLoader = StdlibLoader.ForRoot(Path.Combine(RepoRoot(), "stdlib"), sm, de);
        comp.AddModule(new Parser(sm, id, de).ParseModule());
        Semantics.Analyze(comp, comp.Resolve(), de);
        return de;
    }

    private static void AssertClean(string source)
    {
        var de = Check(source);
        Assert.False(de.HasErrors,
            "expected this to check clean, but got:\n"
            + string.Join("\n", de.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    private static void AssertReports(string code, string messagePart, string source)
    {
        var found = Check(source).Diagnostics.FirstOrDefault(d => d.Code == code);
        Assert.NotNull(found);
        Assert.Contains(messagePart, found.Message);
    }

    private const string Markers = """
        import std.core { OnModule, OnType, OnFunction };

        struct Plugin :: [OnModule] { name: string, api: int }
        struct Component :: [OnType] { }
        struct System :: [OnFunction] { order: int = 0 }
        """;

    // ------------------------------------------------------------------ the positive cases

    [Fact]
    public void An_attribute_with_the_right_marker_checks_clean_on_each_target() =>
        AssertClean(Markers + """

            @Component
            struct Health { value: int, max: int }

            @System { order = 10 }
            fn tick(dt: float): void { }

            fn main(): int { return 0; }
            """);

    [Fact]
    public void A_module_attribute_checks_against_OnModule() =>
        AssertClean("@Plugin { name = \"m\", api = 2 }\nmodule m;\n" + Markers
            + "\nfn main(): int { return 0; }");

    [Fact]
    public void A_missing_field_with_a_literal_default_is_no_error() =>
        // 'order' has '= 0'; the emitted row fills it in, so '@System' alone is complete.
        AssertClean(Markers + """

            @System
            fn tick(): void { }

            fn main(): int { return 0; }
            """);

    [Fact]
    public void A_negative_literal_is_a_literal() =>
        AssertClean(Markers + """

            @System { order = -1 }
            fn tick(): void { }

            fn main(): int { return 0; }
            """);

    [Fact]
    public void A_class_and_an_enum_take_OnType() =>
        AssertClean(Markers + """

            @Component
            class World { }

            @Component
            enum Phase { Start, End }

            fn main(): int { return 0; }
            """);

    /// <summary>One struct may carry more than one marker; it then sits on both kinds of
    /// target. Listing conformances is this language's way of saying "both" — there is no
    /// interface inheritance to combine them.</summary>
    [Fact]
    public void Two_markers_admit_two_kinds_of_target() =>
        AssertClean("""
            import std.core { OnType, OnFunction };

            struct Tag :: [OnFunction, OnType] { }

            @Tag
            struct S { v: int }

            @Tag
            fn f(): void { }

            fn main(): int { return 0; }
            """);

    // ------------------------------------------------------------------ what stays rejected

    [Fact]
    public void An_unknown_name_is_an_unknown_type() =>
        AssertReports("LYR-SEM0011", "unknown type 'Nope'", Markers + """

            @Nope
            fn f(): void { }

            fn main(): int { return 0; }
            """);

    [Fact]
    public void A_struct_without_the_marker_is_rejected_with_the_declare_hint() =>
        AssertReports("LYR-SEM0065", ":: [OnFunction]", Markers + """

            struct Plain { v: int }

            @Plain
            fn f(): void { }

            fn main(): int { return 0; }
            """);

    [Fact]
    public void The_wrong_marker_for_the_target_is_rejected() =>
        // 'System' declares OnFunction; on a struct the message asks for OnType.
        AssertReports("LYR-SEM0065", ":: [OnType]", Markers + """

            @System
            struct S { v: int }

            fn main(): int { return 0; }
            """);

    [Fact]
    public void A_module_attribute_needs_OnModule() =>
        AssertReports("LYR-SEM0065", ":: [OnModule]",
            "@Component\nmodule m;\n" + Markers + "\nfn main(): int { return 0; }");

    [Fact]
    public void A_class_cannot_be_an_attribute() =>
        AssertReports("LYR-SEM0065", "an attribute is a struct", Markers + """

            class NotAnAttr { }

            @NotAnAttr
            fn f(): void { }

            fn main(): int { return 0; }
            """);

    [Fact]
    public void An_interface_cannot_be_an_attribute() =>
        AssertReports("LYR-SEM0065", "an attribute is a struct", Markers + """

            @OnFunction
            fn f(): void { }

            fn main(): int { return 0; }
            """);

    [Fact]
    public void A_generic_attribute_type_is_rejected() =>
        AssertReports("LYR-SEM0065", "generic", """
            import std.core { OnFunction };

            struct Tag<T> :: [OnFunction] { }

            @Tag
            fn f(): void { }

            fn main(): int { return 0; }
            """);

    [Fact]
    public void A_generic_target_is_rejected() =>
        AssertReports("LYR-SEM0067", "generic declaration", Markers + """

            @Component
            struct Pool<T> { v: T }

            fn main(): int { return 0; }
            """);

    [Fact]
    public void The_same_attribute_twice_is_rejected() =>
        AssertReports("LYR-SEM0068", "twice", Markers + """

            @Component
            @Component
            struct S { v: int }

            fn main(): int { return 0; }
            """);

    [Fact]
    public void The_same_field_twice_is_rejected() =>
        AssertReports("LYR-SEM0068", "sets 'order' twice", Markers + """

            @System { order = 1, order = 2 }
            fn f(): void { }

            fn main(): int { return 0; }
            """);

    [Fact]
    public void A_computed_argument_is_rejected() =>
        AssertReports("LYR-SEM0066", "must be a value at compile time", Markers + """

            @System { order = 1 + 2 }
            fn f(): void { }

            fn main(): int { return 0; }
            """);

    [Fact]
    public void Null_is_not_an_attribute_argument() =>
        AssertReports("LYR-SEM0066", "must be a value at compile time", """
            import std.core { OnFunction };

            struct Tag :: [OnFunction] { label: ?string }

            @Tag { label = null }
            fn f(): void { }

            fn main(): int { return 0; }
            """);

    // ---------------------------------------------------------------- a name for the value (2.4)

    [Fact]
    public void A_module_let_bound_to_a_literal_is_an_argument() =>
        // The point of the whole exercise: a program can publish its vocabulary and have the uses
        // checked, instead of repeating a raw string where a typo is a silent receiver.
        AssertClean(Markers + """

            let PRIORITY = 10;

            @System { order = PRIORITY }
            fn f(): void { }

            fn main(): int { return 0; }
            """);

    [Fact]
    public void A_chain_of_bindings_resolves() =>
        AssertClean(Markers + """

            let BASE = 10;
            let PRIORITY = BASE;

            @System { order = PRIORITY }
            fn f(): void { }

            fn main(): int { return 0; }
            """);

    [Fact]
    public void A_static_let_is_an_argument() =>
        AssertClean(Markers + """

            struct Order {
                static let LATE: int = 10;
            }

            @System { order = Order.LATE }
            fn f(): void { }

            fn main(): int { return 0; }
            """);

    [Fact]
    public void A_named_default_fills_the_row() =>
        AssertClean("""
            import std.core { OnFunction };

            let DEFAULT_LIMIT = 3;

            struct Retry :: [OnFunction] { limit: int = DEFAULT_LIMIT }

            @Retry
            fn f(): void { }

            fn main(): int { return 0; }
            """);

    [Fact]
    public void A_let_bound_to_an_expression_is_still_refused() =>
        // The line is where the VALUE is written, not what the compiler could work out: 1 + 2 is
        // computable and has nowhere to be computed.
        AssertReports("LYR-SEM0066", "must be a value at compile time", Markers + """

            let PRIORITY = 5 + 5;

            @System { order = PRIORITY }
            fn f(): void { }

            fn main(): int { return 0; }
            """);

    [Fact]
    public void A_field_is_not_a_constant() =>
        // Only a global binding: a field is bound per instance, and the row has one value.
        AssertReports("LYR-SEM0066", "must be a value at compile time", Markers + """

            struct Settings { order: int }

            fn take(s: Settings): void { }

            @System { order = s.order }
            fn f(): void { }

            fn main(): int { return 0; }
            """);

    [Fact]
    public void A_function_call_is_still_refused() =>
        AssertReports("LYR-SEM0066", "must be a value at compile time", Markers + """

            fn ten(): int { return 10; }

            @System { order = ten() }
            fn f(): void { }

            fn main(): int { return 0; }
            """);

    [Fact]
    public void A_wrongly_typed_argument_is_an_ordinary_assignability_error() =>
        AssertReports("LYR-SEM0001", "", Markers + """

            @System { order = "nope" }
            fn f(): void { }

            fn main(): int { return 0; }
            """);

    [Fact]
    public void An_unknown_field_is_an_ordinary_no_such_field_error() =>
        AssertReports("LYR-SEM0015", "has no field 'nope'", Markers + """

            @System { nope = 1 }
            fn f(): void { }

            fn main(): int { return 0; }
            """);

    [Fact]
    public void A_field_without_value_and_without_default_is_rejected() =>
        // 'Plugin' has 'name' and 'api', neither with a default; '@Plugin' alone leaves both empty.
        AssertReports("LYR-SEM0069", "without a value",
            "@Plugin\nmodule m;\n" + Markers + "\nfn main(): int { return 0; }");

    [Fact]
    public void A_non_literal_default_cannot_fill_the_row() =>
        AssertReports("LYR-SEM0069", "not a value at compile time", """
            import std.core { OnFunction };

            fn compute(): int { return 3; }

            struct Tag :: [OnFunction] { order: int = compute() }

            @Tag
            fn f(): void { }

            fn main(): int { return 0; }
            """);

    /// <summary>The attribute is a reference to its type: the side table records it, which is what
    /// go-to-definition and find-references read.</summary>
    [Fact]
    public void The_attribute_use_survives_alongside_ordinary_uses_of_the_struct() =>
        // 'System' as attribute AND as ordinary value in one program: the struct stays a struct.
        AssertClean(Markers + """

            @System { order = 1 }
            fn f(): void { }

            fn main(): int {
                let s = System { order = 2 };
                return s.order;
            }
            """);

    // ---------------------------------------------------------------- an enum value (2.10)

    [Fact]
    public void A_unit_variant_is_an_argument() =>
        // The form the whole exercise is for: a vocabulary the TYPE system checks, where a string
        // would be checked by whoever reads the row much later, or never.
        AssertClean("""
            import std.core { OnFunction };

            enum Stage { Input, Physics, Render }

            struct System :: [OnFunction] { stage: Stage }

            @System { stage = Stage.Physics }
            fn f(): void { }

            fn main(): int { return 0; }
            """);

    [Fact]
    public void A_variant_fills_a_default_too() =>
        AssertClean("""
            import std.core { OnFunction };

            enum Stage { Input, Physics }

            struct System :: [OnFunction] { stage: Stage = Stage.Input }

            @System
            fn f(): void { }

            fn main(): int { return 0; }
            """);

    [Fact]
    public void A_let_bound_to_a_variant_is_an_argument() =>
        // The 2.4 rule and the 2.10 one compose: one resolution walk answers both.
        AssertClean("""
            import std.core { OnFunction };

            enum Stage { Input, Physics }

            struct System :: [OnFunction] { stage: Stage }

            let DEFAULT_STAGE = Stage.Physics;

            @System { stage = DEFAULT_STAGE }
            fn f(): void { }

            fn main(): int { return 0; }
            """);

    [Fact]
    public void A_variant_with_a_payload_is_refused() =>
        // A row holds one value per field; a payload is values of its own. The message says that
        // rather than "must be a literal", which tells someone who wrote a variant nothing.
        AssertReports("LYR-SEM0066", "carries a payload", """
            import std.core { OnFunction };

            enum Shape { Dot, Circle(float) }

            struct Tag :: [OnFunction] { shape: Shape }

            @Tag { shape = Shape.Circle(1.0) }
            fn f(): void { }

            fn main(): int { return 0; }
            """);

    [Fact]
    public void A_variant_of_another_enum_is_an_ordinary_assignability_error() =>
        AssertReports("LYR-SEM0001", "", """
            import std.core { OnFunction };

            enum Stage { Input, Render }
            enum Other { Left, Right }

            struct Tag :: [OnFunction] { stage: Stage }

            @Tag { stage = Other.Left }
            fn f(): void { }

            fn main(): int { return 0; }
            """);

    [Fact]
    public void A_misspelled_variant_is_a_compile_error() =>
        // The fault class this closes: with a string the typo produced a row nobody matched.
        AssertReports("LYR-SEM0012", "no static member 'Phyiscs'", """
            import std.core { OnFunction };

            enum Stage { Input, Physics }

            struct Tag :: [OnFunction] { stage: Stage }

            @Tag { stage = Stage.Phyiscs }
            fn f(): void { }

            fn main(): int { return 0; }
            """);

    // ------------------------------------------------------------------ the 3.9 forms:
    // one parenthesized value, admitted by WithArg<T>; and the group spelling.

    private const string EventVocabulary = """
        import std.core { OnFunction, WithArg };

        enum Event { Damage, Heal }

        struct On :: [OnFunction, WithArg<Event>] { event: Event, order: int = 0 }
        struct Retry :: [OnFunction, WithArg<int>] { limit: int }
        struct Tag :: [OnFunction] { }
        """;

    [Fact]
    public void A_positional_value_fills_the_first_field() =>
        AssertClean(EventVocabulary + """

            @On(Event.Damage)
            fn onDamage(): void { }

            @Retry(3)
            fn fetch(): void { }

            fn main(): int { return 0; }
            """);

    [Fact]
    public void A_positional_value_may_name_its_literal() =>
        AssertClean(EventVocabulary + """

            let LIMIT = 5;

            @Retry(LIMIT)
            fn fetch(): void { }

            fn main(): int { return 0; }
            """);

    [Fact]
    public void The_positional_form_needs_the_conformance() =>
        AssertReports("LYR-SEM0094", "WithArg", EventVocabulary + """

            @Tag(3)
            fn f(): void { }

            fn main(): int { return 0; }
            """);

    [Fact]
    public void WithArg_must_name_the_first_fields_type() =>
        AssertReports("LYR-SEM0095", "first field", """
            import std.core { OnFunction, WithArg };

            struct Named :: [OnFunction, WithArg<string>] { order: int, label: string = "" }

            fn main(): int { return 0; }
            """);

    [Fact]
    public void WithArg_on_a_fieldless_struct_is_refused() =>
        AssertReports("LYR-SEM0095", "no field", """
            import std.core { OnFunction, WithArg };

            struct Bare :: [OnFunction, WithArg<int>] { }

            fn main(): int { return 0; }
            """);

    [Fact]
    public void A_second_witharg_instance_cannot_also_hold() =>
        // Multi-conformance admits the list entry; the promise check then measures both
        // against the one first field, and the second cannot fit beside the first.
        AssertReports("LYR-SEM0095", "first field", """
            import std.core { OnFunction, WithArg };

            struct Both :: [OnFunction, WithArg<int>, WithArg<string>] { limit: int }

            fn main(): int { return 0; }
            """);

    [Fact]
    public void A_positional_value_is_a_value_at_compile_time() =>
        AssertReports("LYR-SEM0066", "value at compile time", EventVocabulary + """

            @Retry(1 + 2)
            fn f(): void { }

            fn main(): int { return 0; }
            """);

    [Fact]
    public void A_positional_variant_with_a_payload_is_named() =>
        AssertReports("LYR-SEM0066", "payload", """
            import std.core { OnFunction, WithArg };

            enum Shape { Dot, Circle(float) }

            struct Drawn :: [OnFunction, WithArg<Shape>] { shape: Shape }

            @Drawn(Shape.Circle(1.0))
            fn f(): void { }

            fn main(): int { return 0; }
            """);

    [Fact]
    public void A_group_is_the_stacked_list() =>
        AssertClean(EventVocabulary + """

            @[Tag, Retry(3), On { event = Event.Heal, order = 2 }]
            fn grouped(): void { }

            fn main(): int { return 0; }
            """);

    [Fact]
    public void A_group_duplicate_is_the_stacked_duplicate() =>
        AssertReports("LYR-SEM0068", "twice", EventVocabulary + """

            @[Tag, Tag]
            fn f(): void { }

            fn main(): int { return 0; }
            """);

    [Fact]
    public void The_witharg_promise_travels_the_parent_chain() =>
        // 3.9.1: the conformance is reached through an interface parent, and the first-field
        // check runs at the entry that reaches it — without it the mismatch surfaced at every
        // use as a plain assignment error.
        AssertReports("LYR-SEM0095", "first field", """
            import std.core { OnFunction, WithArg };

            interface Carries :: [WithArg<int>] { }

            struct Retry :: [OnFunction, Carries] { limit: string }

            fn main(): int { return 0; }
            """);

    [Fact]
    public void A_field_no_row_can_hold_refuses_the_use() =>
        // 3.9.1: the sema accepted 'n: ?int' with 'n = 3' — the literal adapts — and the
        // bytecode writer, which has no encoding for an optional, took the compiler down.
        AssertReports("LYR-SEM0096", "row", """
            import std.core { OnFunction };

            struct Odd :: [OnFunction] { n: ?int }

            @Odd { n = 3 }
            fn f(): void { }

            fn main(): int { return 0; }
            """);

    [Fact]
    public void A_group_on_a_member_obeys_the_member_rule() =>
        AssertReports("LYR-SEM0065", "only '@Deprecated'", EventVocabulary + """

            struct Holder {
                @[Tag]
                pub fn m(): void { }
            }

            fn main(): int { return 0; }
            """);
}
