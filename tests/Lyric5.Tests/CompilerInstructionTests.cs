using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// The compiler instructions of <c>std.core</c> (design/v5/spec/09 A10, A11; 13 M9a P8, S6):
/// <c>@callerLine</c> and <c>@callerFile</c>, <c>@Deprecated</c> read from its computed value,
/// the C compiler's hints, <c>@MustUse</c>, <c>@NonExhaustive</c>, <c>@Allow</c> with the
/// manifest's list — each one name, each read by its identity. <c>@Test</c> stands in
/// <c>TestCommandTests</c>, <c>@callerExpr</c> in <c>CallerExprTests</c>.
///
/// <para>Through <c>Main</c>, so in the console collection.</para>
/// </summary>
[Collection("console")]
public class CompilerInstructionTests
{
    private static string App(string main) => Package(("lyric.toml", AppManifest), ("src/main.lyr", main));

    private static string[] Lines(string text, string code) =>
        text.Split('\n').Where(l => l.Contains($"[{code}]")).ToArray();

    /// <summary>A call's place fills what it leaves out (A11): the line the call begins on, the
    /// file as the binary's backtrace names it — the package, then the path in it —; through an
    /// interface, in a generic function (SEM0067 falls for compiler instructions), and in a
    /// <c>comptime</c> site.</summary>
    [Fact]
    public void A_call_leaves_its_line_and_file_to_the_parameters_that_ask()
    {
        const string main = """
            import std.io { println };

            fn here(@callerLine line: int = 0, @callerFile file: string = ""): string {
                return f"{file}:{line}";
            }

            interface Sink {
                fn put(@callerLine line: int = 0): string;
            }

            struct A { }

            extend A :: [Sink] {
                fn put(@callerLine line: int = 0): string { return f"A{line}"; }
            }

            fn show<T :: [Display]>(x: T, @callerLine line: int = 0): string {
                return f"{x}@{line}";
            }

            fn main(): void {
                let s: Sink = A { };
                println(here());
                println(here(7));
                println(s.put());
                println(show(5));
                println(comptime here());
            }
            """;
        var dir = App(main);
        Assert.Equal("app/src/main.lyr:23\napp/src/main.lyr:7\nA25\n5@26\napp/src/main.lyr:27\n",
            BuildAndRun(dir, "app", "build", "-C", dir));
    }

    /// <summary>A call through an interface passes what it leaves out as a direct call does (04 D5):
    /// a default — one that reads an earlier parameter —, arguments by name, a variadic rest. It
    /// passed the written arguments alone before (M9a S6), which C refused in debug and the
    /// verifier in release.</summary>
    [Fact]
    public void An_interface_call_passes_defaults_names_and_a_variadic_rest()
    {
        const string main = """
            import std.io { println };

            interface Sink {
                fn put(a: int, b: int = 10, c: int = a + 1): string;
                fn all(xs: int...): int;
            }

            struct A { }

            extend A :: [Sink] {
                fn put(a: int, b: int = 10, c: int = a + 1): string { return f"{a},{b},{c}"; }
                fn all(xs: int...): int { return xs.length(); }
            }

            fn main(): void {
                let s: Sink = A { };
                println(s.put(1));
                println(s.put(b: 2, a: 1));
                println(s.put(5, c: 0));
                println(f"{s.all(1, 2, 3)} {s.all()}");
            }
            """;
        var dir = App(main);
        Assert.Equal("1,10,2\n1,2,2\n5,10,0\n3 0\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    /// <summary>The verifier knows a slot's arity from the table rows (M9a S6): a call through the
    /// interface that passes too few arguments is a finding, not C's "too few arguments" in debug,
    /// where nothing inlines the call into a direct one it would check.</summary>
    [Fact]
    public void The_verifier_counts_the_arguments_of_a_call_through_an_interface()
    {
        const string main = """
            interface Sink {
                fn put(n: int): int;
            }

            struct A { }

            extend A :: [Sink] {
                fn put(n: int): int { return n; }
            }

            fn main(): void {
                let s: Sink = A { };
                let _ = s.put(1);
            }
            """;
        var lowered = TestCompiler.Lower("main.lyr", main);
        Assert.True(lowered.Ok);
        var ir = lowered.Ir!;
        Assert.Empty(Lyric.Ir.IrVerifier.Verify(ir));
        var block = ir.Functions.Single(f => f.Name == "app.main.main").Blocks
            .First(b => b.Insts.Any(op => op is Lyric.Ir.CallVirt));
        var at = block.Insts.FindIndex(op => op is Lyric.Ir.CallVirt);
        var call = (Lyric.Ir.CallVirt)block.Insts[at];
        block.Insts[at] = call with { Args = call.Args[..^1] };
        Assert.Contains(Lyric.Ir.IrVerifier.Verify(ir), f => f.Contains("callvirt to slot 0 (put) passes 1 arg(s) with the receiver, its functions take 2"));
    }

    /// <summary>What the instructions refuse: a place on a parameter of the wrong type or without a
    /// default; two hints that contradict each other; a marker that does not admit the place; an
    /// <c>@Allow</c> naming no code; an <c>until</c> that is no version, or has come due.</summary>
    [Theory]
    [InlineData("fn f(@callerLine line: string = \"\"): void { }", "LYR-SEM0155", "'@callerLine' sits on an 'int' parameter")]
    [InlineData("fn f(@callerFile file: int = 0): void { }", "LYR-SEM0155", "'@callerFile' sits on a 'string' parameter")]
    [InlineData("fn f(@callerLine line: int): void { }", "LYR-SEM0155", "'@callerLine' sits on a parameter with a default")]
    [InlineData("@AlwaysInline\n@NoInline\nfn f(): void { }", "LYR-SEM0177", "'@NoInline' contradicts the '@AlwaysInline' beside it")]
    [InlineData("@Cold\nstruct S { }", "LYR-SEM0065", "'@Cold' cannot sit on a struct")]
    [InlineData("fn f(@Deprecated x: int): int { return x; }", "LYR-SEM0065", "'@Deprecated' cannot sit on a parameter")]
    [InlineData("@Allow(\"dead-cod\")\nfn f(): void { }", "LYR-SEM0179", "there is no lint 'dead-cod'")]
    [InlineData("@Deprecated { until = \"soon\" }\nfn f(): void { }", "LYR-SEM0081", "'soon' is not a version")]
    [InlineData("@Deprecated { until = \"5.0\" }\nfn f(): void { }", "LYR-SEM0081", "this was kept until 5.0 and the toolchain is 5.")]
    public void An_instruction_refuses_what_it_cannot_say(string declarations, string code, string why)
    {
        var dir = App(declarations + "\n\nfn main(): void {\n}\n");
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.True(exit == 1, $"exit {exit}\n{error}");
        var line = Assert.Single(error.Split('\n'), l => l.Contains("error["));
        Assert.Contains($"error[{code}]", line);
        Assert.Contains(why, line);
    }

    /// <summary>
    /// <c>@Deprecated</c> warns at every use (A11) with what its computed value says — a message
    /// made of a <c>let</c>, a replacement to write instead —: of a function, a struct, a variant,
    /// a member of a deprecated <c>extend</c> block, a generic function. Not inside a deprecated
    /// declaration, and an <c>until</c> not yet due builds.
    /// </summary>
    [Fact]
    public void A_deprecated_declaration_warns_where_it_is_used_with_what_its_value_says()
    {
        const string main = """
            import std.io { println };

            let NEW = "neu";

            @Deprecated { message = "use " + NEW, replacement = NEW }
            fn alt(): int { return 1; }

            @Deprecated
            struct S { x: int }

            enum E {
                @Deprecated { message = "gone" } A,
                B,
            }

            @Deprecated
            extend S {
                fn get(): int { return this.x; }
            }

            @Deprecated { message = "inner" }
            fn inside(): int { return alt(); }

            @Deprecated { until = "99.0" }
            fn pick<T>(x: T): T { return x; }

            fn main(): void {
                let s = S { x = 3 };
                let _ = E.A;
                println(f"{alt()} {s.get()} {pick(4)}");
            }
            """;
        var dir = App(main);
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.True(exit == 0, error);
        var warnings = Lines(error, "LYR-SEM0076");
        Assert.Equal(5, warnings.Length);
        Assert.Contains(warnings, w => w.Contains("'alt' is deprecated: use neu"));
        Assert.Contains("note: write 'neu' instead", error);
        Assert.Contains(warnings, w => w.Contains("'S' is deprecated") && w.Contains(":28:"));
        Assert.Contains(warnings, w => w.Contains("'A' is deprecated: gone"));
        Assert.Contains(warnings, w => w.Contains("'get' is deprecated"));
        Assert.Contains(warnings, w => w.Contains("'pick' is deprecated"));
        Assert.DoesNotContain("'inside'", error);
    }

    /// <summary>The C compiler's hints (A11): on the function's C, as a hint; <c>@NoInline</c>
    /// keeps the IR inliner away in release as well, where a function that small is inlined
    /// otherwise.</summary>
    [Fact]
    public void A_hint_reaches_the_C_compiler_and_noinline_the_IR_inliner()
    {
        const string main = """
            import std.io { println };

            @AlwaysInline
            fn twice(x: int): int { return x * 2; }

            @NoInline
            fn small(x: int): int { return x + 1; }

            fn plain(x: int): int { return x + 2; }

            @Cold
            @NoInline
            fn rare(): void { println("rare"); }

            fn main(): void {
                println(f"{twice(3)} {small(4)} {plain(5)}");
                if (twice(1) == 3) { rare(); }
            }
            """;
        var dir = App(main);
        var (exit, c, error) = Run("build", "-C", dir, "--emit", "c");
        Assert.True(exit == 0, error);
        Assert.Contains("LYR_ALWAYS_INLINE int64_t lyr_app_main_twice(int64_t l0_x) {", c);
        Assert.Contains("LYR_NOINLINE int64_t lyr_app_main_small(int64_t l0_x) {", c);
        Assert.Contains("LYR_NOINLINE LYR_COLD void lyr_app_main_rare(void) {", c);
        Assert.Contains(c.Split('\n'), l => l.StartsWith("int64_t lyr_app_main_plain(int64_t l0_x) {", StringComparison.Ordinal));
        var (built, ir, failed) = Run("build", "-C", dir, "--profile", "release", "--emit", "ir");
        Assert.True(built == 0, failed);
        var body = ir[ir.IndexOf("fn app.main.main", StringComparison.Ordinal)..];
        body = body[..body.IndexOf("\n}", StringComparison.Ordinal)];
        Assert.Contains("call app.main.small(", body);
        Assert.DoesNotContain("call app.main.plain(", body);
        Assert.Contains("fn app.main.small -> i64 [noinline] {", ir);
        Assert.Equal("6 5 7\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    /// <summary><c>@MustUse</c> (A11): a dropped value warns where the function carries it or the
    /// value's type does — <c>Result</c>. <c>std.task.spawn</c>'s handle, the only way to the task's
    /// error, has a lint of its own, <c>unused-task</c>. <c>let _ = …</c> drops one on purpose; a
    /// <c>void</c> call gives nothing.</summary>
    [Fact]
    public void A_dropped_value_warns_where_must_use_asks_for_it()
    {
        const string main = """
            import std.io { println };
            import std.task { spawn };

            struct Fail { }
            extend Fail :: [Error] { fn message(): string { return "fail"; } }

            @MustUse
            fn token(): int { return 7; }

            @MustUse
            struct Ticket { n: int }

            fn issue(): Ticket { return Ticket { n = 1 }; }

            fn check(x: int): Result<int, Fail> {
                return if (x > 0) Result.Ok(x) else Result.Err(Fail { });
            }

            @MustUse
            fn nothing(): void { }

            fn main(): void {
                token();
                issue();
                check(1);
                let _ = token();
                let t = token();
                nothing();
                spawn(() => 1);
                println(f"{t}");
            }
            """;
        var dir = App(main);
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.True(exit == 0, error);
        var warnings = Lines(error, "LYR-SEM0178");
        Assert.Equal(3, warnings.Length);
        Assert.Contains(warnings, w => w.Contains(":23:5:") && w.Contains("the value of 'token' is dropped"));
        Assert.Contains(warnings, w => w.Contains(":24:5:") && w.Contains("a 'Ticket' is dropped here"));
        Assert.Contains(warnings, w => w.Contains(":25:5:") && w.Contains("a 'Result<int, Fail>' is dropped here"));
        Assert.Contains(Lines(error, "LYR-SEM0212"), w => w.Contains(":29:5:") && w.Contains("this task's handle is dropped"));
    }

    /// <summary><c>@NonExhaustive</c> (A11): outside the package that declares it a match covers
    /// the enum with a default — a later version may add a variant —; inside, its variants cover
    /// it. <c>std.io.IoErrorKind</c> carries it.</summary>
    [Fact]
    public void A_non_exhaustive_enum_needs_a_default_outside_its_package()
    {
        const string lib = """
            @NonExhaustive
            pub enum Shape { Dot, Line }

            pub fn name(s: Shape): string {
                return match (s) {
                    .Dot => "dot",
                    .Line => "line",
                };
            }
            """;
        const string complete = """
            import geo { Shape, name };
            import std.io { IoErrorKind, println };

            fn local(s: Shape): string {
                return match (s) {
                    .Dot => "d",
                    .Line => "l",
                };
            }

            fn main(): void { println(f"{local(Shape.Dot)} {name(Shape.Line)}"); }
            """;
        var manifest = AppManifest + "\n[dependencies]\ngeo = { path = \"../geo\" }\n";
        var root = Package(("app/lyric.toml", manifest), ("app/src/main.lyr", complete),
            ("geo/lyric.toml", "[package]\nname = \"geo\"\nversion = \"1.0.0\"\n"), ("geo/src/lib.lyr", lib));
        var app = Path.Combine(root, "app");
        var (exit, _, error) = Run("build", "-C", app);
        Assert.Equal(1, exit);
        var refused = Assert.Single(error.Split('\n'), l => l.Contains("error["));
        Assert.Contains("error[LYR-SEM0050]: match on 'Shape' is not exhaustive", refused);
        Assert.Contains("note: 'Shape' is '@NonExhaustive'", error);

        File.WriteAllText(Path.Combine(app, "src", "main.lyr"),
            complete.Replace(".Line => \"l\",", ".Line => \"l\",\n        _ => \"?\","));
        Assert.Equal("d line\n", BuildAndRun(app, "app", "build", "-C", app));

        File.WriteAllText(Path.Combine(app, "src", "main.lyr"), """
            import std.io { IoErrorKind, println };

            fn kind(k: IoErrorKind): string {
                return match (k) {
                    .NotFound => "1", .PermissionDenied => "2", .AlreadyExists => "3", .IsDirectory => "4",
                    .NotDirectory => "5", .InvalidInput => "6", .InvalidData => "7", .UnexpectedEof => "8",
                    .TimedOut => "9", .ConnectionRefused => "10", .ConnectionReset => "11", .AddrInUse => "12",
                    .BrokenPipe => "13", .Closed => "14", .Unsupported => "15", .Other { code } => f"{code}",
                };
            }

            fn main(): void { println(kind(IoErrorKind.Closed)); }
            """);
        var (io, _, ioError) = Run("build", "-C", app);
        Assert.Equal(1, io);
        Assert.Contains("error[LYR-SEM0050]: match on 'IoErrorKind' is not exhaustive", ioError);
    }

    /// <summary><c>@Allow(code)</c> and <c>[lints] allow</c> (A10): a warning the declaration's
    /// attribute names is not reported, nor one the package's list names; one elsewhere is; an
    /// <c>@Allow</c> that suppresses nothing warns itself.</summary>
    [Fact]
    public void Allow_takes_back_a_warning_where_it_or_the_manifest_says()
    {
        const string main = """
            import std.io { println };

            @MustUse
            fn token(): int { return 7; }

            @Allow("LYR-SEM0071")
            fn quiet(): void {
                let unused = 1;
            }

            fn loud(): void {
                let unused = 2;
            }

            @Allow("LYR-SEM0144")
            fn stale(): void { }

            fn main(): void {
                token();
                quiet();
                loud();
                stale();
                println("ok");
            }
            """;
        var dir = Package(("lyric.toml", AppManifest + "\n[lints]\nallow = [\"LYR-SEM0178\"]\n"), ("src/main.lyr", main));
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.True(exit == 0, error);
        Assert.Contains(":12:9:", Assert.Single(Lines(error, "LYR-SEM0071")));
        Assert.Contains("this '@Allow' takes back nothing: no 'LYR-SEM0144'", Assert.Single(Lines(error, "LYR-SEM0180")));
        Assert.Empty(Lines(error, "LYR-SEM0178"));
        Assert.Equal(2, error.Split('\n').Count(l => l.Contains("warning[")));
    }
}
