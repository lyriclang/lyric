using System.Runtime.CompilerServices;
using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;
using Xunit;

namespace Lyric.Tests.Sema;

/// <summary>
/// The number tower of Lyric 5 (design/v5/spec/03 T1, T2; 08 Y4), each rule pinned red and green:
/// the 64-bit names are aliases (T1a), an integer widens losslessly at a coercion site and
/// nowhere else (T1c), <c>as</c> is the one bit-near conversion and knows what it refuses (T1d),
/// <c>char</c> and <c>bool</c> are not numbers (T1e), and the wrap operators are for integers
/// only (Y4). The rules of Lyric 4 these replace — distinct <c>int64</c>, <c>char</c> as an
/// integer, no implicit conversion at all — had their pins retired with them.
/// </summary>
public class NumberTowerTests
{
    private static string RepoRoot([CallerFilePath] string thisFile = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));

    private static DiagnosticEngine Check(string source)
    {
        var sm = new SourceManager();
        var id = sm.AddVirtual("test.lyr", source);
        var de = new DiagnosticEngine(sm);
        var comp = new Compilation(sm, de)
        {
            ModuleLoader = StdlibLoader.ForRoot(Path.Combine(RepoRoot(), "stdlib"), sm, de),
        };
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

    private static string AssertOneError(string source, string code)
    {
        var de = Check(source);
        var errors = de.Diagnostics.Where(d => d.Severity == Severity.Error).ToList();
        Assert.True(errors.Count == 1 && errors[0].Code == code,
            $"expected exactly one {code}, got:\n" + string.Join("\n", errors.Select(d => $"{d.Code}: {d.Message}")));
        return errors[0].Message;
    }

    // ------------------------------------------------------------------ T1a aliases

    [Fact]
    public void The_64_bit_names_are_the_same_types()
    {
        AssertClean("""
            fn takes(x: int64, y: uint64, z: float64): int { return x; }
            fn main(): int {
                let a: int = 1;
                let b: int64 = a;
                let u: uint = 2;
                let v: uint64 = u;
                let f: float = 1.5;
                let g: float64 = f;
                return takes(a, u, f) + takes(b, v, g);
            }
            """);
    }

    [Fact]
    public void An_alias_displays_as_the_type_it_names()
    {
        var message = AssertOneError("fn main(): int { let x: int64 = 1; let s: string = x; return 0; }", "LYR-SEM0001");
        Assert.Contains("'int'", message);
        Assert.DoesNotContain("int64", message);
    }

    // ------------------------------------------------------------------ T1c widening

    [Theory]
    [InlineData("int8", "int16")]
    [InlineData("int8", "int32")]
    [InlineData("int16", "int")]
    [InlineData("int32", "int")]
    [InlineData("uint8", "uint16")]
    [InlineData("uint16", "uint32")]
    [InlineData("uint32", "uint")]
    [InlineData("uint8", "int16")]
    [InlineData("uint16", "int32")]
    [InlineData("uint32", "int")]
    [InlineData("float32", "float")]
    public void A_lossless_widening_is_implicit_at_every_coercion_site(string from, string to)
    {
        AssertClean($$"""
            fn takes(x: {{to}}): {{to}} { return x; }
            fn gives(x: {{from}}): {{to}} { return x; }
            fn main(): int {
                let small: {{from}} = 1;
                let wide: {{to}} = small;
                var slot: {{to}} = 0;
                slot = small;
                let _ = takes(small) == wide && gives(small) == slot;
                return 0;
            }
            """);
    }

    [Theory]
    [InlineData("int", "int8")]       // a narrowing
    [InlineData("int16", "int8")]
    [InlineData("uint", "int")]       // the top bit has nowhere to go
    [InlineData("int", "uint")]       // a sign has nowhere to go
    [InlineData("int32", "uint32")]
    [InlineData("int8", "uint16")]
    [InlineData("int", "float")]      // an integer stays an integer
    [InlineData("int8", "float32")]
    [InlineData("float", "int")]
    [InlineData("float", "float32")]
    [InlineData("uint32", "char")]    // a char is not a number
    [InlineData("char", "uint32")]
    [InlineData("bool", "int")]
    public void Everything_else_stays_an_assignment_error(string from, string to)
    {
        var start = from == "char" ? "'a'" : from == "bool" ? "true" : "1";
        AssertOneError($"fn main(): int {{ let x: {from} = {start}; let y: {to} = x; return 0; }}", "LYR-SEM0001");
    }

    [Fact]
    public void A_widening_never_happens_inside_an_operator()
    {
        // 'a + b' has one operand type (T8: coercion after the unification, never in it): the
        // int8 does not widen to meet the int, and the diagnostic names the explicit way.
        var message = AssertOneError("""
            fn main(): int {
                let a: int8 = 1;
                let b: int = 2;
                let _ = a + b;
                return 0;
            }
            """, "LYR-SEM0003");
        Assert.Contains("'+' is not applicable to 'int8' and 'int'", message);
        Assert.Contains("'as'", message);
    }

    [Fact]
    public void A_widening_never_happens_between_arms()
    {
        // Without a context the arms unify exactly; the widening is a coercion-site rule.
        var de = Check("""
            fn main(): int {
                let a: int8 = 1;
                let b: int = 2;
                let c = 1 < 2;
                let _ = if (c) a else b;
                return 0;
            }
            """);
        Assert.Contains(de.Diagnostics, d => d.Code == "LYR-SEM0016");
    }

    [Fact]
    public void A_context_widens_both_arms()
    {
        // With a context, each arm is checked against it — and each may widen to it.
        AssertClean("""
            fn main(): int {
                let a: int8 = 1;
                let b: int16 = 2;
                let c = 1 < 2;
                let w: int = if (c) a else b;
                return w;
            }
            """);
    }

    // ------------------------------------------------------------------ T1d 'as'

    [Fact]
    public void As_converts_between_all_numbers_in_every_direction()
    {
        AssertClean("""
            fn main(): int {
                let i: int = 300;
                let f: float = 1e30;
                let _ = i as int8;
                let _ = i as uint8;
                let _ = i as uint;
                let _ = i as float;
                let _ = i as float32;
                let _ = f as int;
                let _ = f as uint8;
                let _ = f as float32;
                let _ = (i as uint32) as int16;
                return 0;
            }
            """);
    }

    [Fact]
    public void A_char_converts_to_and_from_uint32_only()
    {
        AssertClean("""
            fn main(): int {
                let c = 'A';
                let code: uint32 = c as uint32;
                let back: char = (code + 1) as char;
                let wide: int = c as uint32 as int;
                return if (back == 'B') wide else 0;
            }
            """);
        var message = AssertOneError("fn main(): int { let c = 'A'; let _ = c as int; return 0; }", "LYR-SEM0006");
        Assert.Contains("'as uint32'", message);
        AssertOneError("fn main(): int { let n: int = 65; let _ = n as char; return 0; }", "LYR-SEM0006");
        AssertOneError("fn main(): int { let n: uint8 = 65; let _ = n as char; return 0; }", "LYR-SEM0006");
    }

    [Fact]
    public void A_bool_is_never_cast()
    {
        var message = AssertOneError("fn main(): int { let b = true; let _ = b as int; return 0; }", "LYR-SEM0006");
        Assert.Contains("'bool' is not a number", message);
        AssertOneError("fn main(): int { let n = 1; let _ = n as bool; return 0; }", "LYR-SEM0006");
        AssertOneError("fn main(): int { let c = 'a'; let _ = c as bool; return 0; }", "LYR-SEM0006");
    }

    // ------------------------------------------------------------------ T1e char and bool

    [Fact]
    public void An_integer_literal_is_not_a_char()
    {
        // 'let c: char = 65' names a number where a scalar value belongs (Lyric 4 took it).
        AssertOneError("fn main(): int { let c: char = 65; return 0; }", "LYR-SEM0001");
    }

    [Fact]
    public void A_char_has_no_arithmetic()
    {
        var message = AssertOneError("fn main(): int { let c = 'a'; let _ = c + 1; return 0; }", "LYR-SEM0003");
        Assert.Contains("'as uint32'", message);
        AssertOneError("fn main(): int { let c = 'a'; let _ = c + 'b'; return 0; }", "LYR-SEM0003");
        AssertOneError("fn main(): int { let c = 'a'; let _ = -c; return 0; }", "LYR-SEM0003");
        AssertOneError("fn main(): int { let c = 'a'; let _ = ~c; return 0; }", "LYR-SEM0003");
        AssertOneError("fn main(): int { let c = 'a'; let _ = c << 1; return 0; }", "LYR-SEM0003");
        AssertOneError("fn main(): int { var c = 'a'; c++; return 0; }", "LYR-SEM0003");
    }

    [Fact]
    public void A_char_compares_with_a_char_and_not_with_a_number()
    {
        AssertClean("""
            fn main(): int {
                let c = 'b';
                let inRange = c >= 'a' && c <= 'z';
                let same = c == 'b' && c != 'c';
                return if (inRange && same) 0 else 1;
            }
            """);
        AssertOneError("fn main(): int { let c = 'a'; let _ = c < 98; return 0; }", "LYR-SEM0003");
        AssertOneError("fn main(): int { let c = 'a'; let _ = c == 97; return 0; }", "LYR-SEM0003");
    }

    // ------------------------------------------------------------------ Y4 wrap operators, T2 shifts

    [Fact]
    public void The_wrap_operators_take_integers_of_one_type()
    {
        AssertClean("""
            fn main(): int {
                let a: uint8 = 255;
                var b: uint8 = a +% 1;
                b = b -% 1;
                b = b *% 3;
                b +%= 1;
                b -%= 1;
                b *%= 2;
                let i: int = 9223372036854775807;
                let wrapped = i +% 1;
                let _ = (i -% 1) *% 2;
                return if (wrapped < 0) 0 else 1;
            }
            """);
    }

    [Fact]
    public void The_wrap_operators_refuse_floats_and_chars()
    {
        var message = AssertOneError("fn main(): int { let f = 1.5; let _ = f +% 1.0; return 0; }", "LYR-SEM0003");
        Assert.Contains("'+%' is not applicable to 'float' and 'float'", message);
        Assert.Contains("integer types only", message);
        AssertOneError("fn main(): int { let c = 'a'; let _ = c *% 'b'; return 0; }", "LYR-SEM0003");
        AssertOneError("fn main(): int { let a: int8 = 1; let b: int = 2; let _ = a -% b; return 0; }", "LYR-SEM0003");
    }

    [Fact]
    public void Shifts_and_bit_operators_are_for_integers()
    {
        AssertClean("""
            fn main(): int {
                let a: uint32 = 1;
                let b: int = 1;
                let _ = (a << 3) >> 1 | (a & 7) ^ ~a;
                let _ = (b << 62) >> 3;
                return 0;
            }
            """);
        AssertOneError("fn main(): int { let f = 1.5; let _ = f << 1; return 0; }", "LYR-SEM0003");
        AssertOneError("fn main(): int { let f = 1.5; let _ = f & 1.0; return 0; }", "LYR-SEM0003");
    }

    [Fact]
    public void A_float_keeps_its_arithmetic_and_ordering()
    {
        AssertClean("""
            fn main(): int {
                let a: float = 1.5;
                let b: float32 = 0.5;
                let c = a * 2.0 - a / 3.0 + a % 1.0;
                let d = b + 1.0;
                let e = a < c && -a <= 0.0;
                return if (e && d > 0.0) 0 else 1;
            }
            """);
        // float32 and float meet like any two types: through 'as', or the widening at a site.
        AssertOneError("fn main(): int { let a: float = 1.5; let b: float32 = 0.5; let _ = a + b; return 0; }", "LYR-SEM0003");
    }
}
