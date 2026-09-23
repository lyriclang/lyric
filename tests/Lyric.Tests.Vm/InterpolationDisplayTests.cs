using System.Runtime.CompilerServices;
using Lyric.Bytecode;
using Lyric.Core;
using Lyric.Ir.Lowering;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;
using Lyric.Vm;

namespace Lyric.Tests.Vm;

/// <summary>
/// An f-string hole renders a <c>Display</c> value (§6.6): <c>{p}</c> runs <c>p.show()</c>, through
/// the conformance for a struct or an enum, through the constraint for a type parameter, and
/// through the vtable for an interface value. The scalar holes beside it keep their converters.
/// </summary>
public class InterpolationDisplayTests
{
    private static string RepoRoot([CallerFilePath] string thisFile = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));

    private static string Out(string source)
    {
        var sm = new SourceManager();
        var id = sm.AddVirtual("test.lyr", source);
        var de = new DiagnosticEngine(sm);
        var comp = new Compilation(sm, de)
        {
            ModuleLoader = StdlibLoader.ForRoot(Path.Combine(RepoRoot(), "stdlib"), sm, de),
        };
        comp.AddModule(new Parser(sm, id, de).ParseModule());
        var binding = comp.Resolve();
        var types = Semantics.Analyze(comp, binding, de);

        var diagnostics = new StringWriter();
        de.RenderText(diagnostics);
        Assert.False(de.HasErrors, "source did not compile: " + diagnostics);

        var ir = ModuleLowerer.Lower(comp, binding, types, de, verify: true);
        Assert.NotNull(ir);

        var output = new StringWriter();
        Interpreter.Run(BytecodeReader.ReadOrThrow(BytecodeWriter.Write(ir!)),
            NativeRegistry.CreateDefault(output, TextWriter.Null));
        return output.ToString().ReplaceLineEndings("\n");
    }

    private const string Prelude =
        """
        import std.io.console { println };
        import std.core { Display };

        struct P :: [Display] {
            x: int,
            y: int,
            fn show(): string { return f"({this.x}, {this.y})"; }
        }

        enum Color { Red, Blue }

        extend Color :: [Display] {
            fn show(): string {
                return match (this) { Color.Red => "red", Color.Blue => "blue" };
            }
        }

        """;

    [Fact]
    public void A_struct_and_an_enum_render_beside_scalars() =>
        Assert.Equal("p=(1, 2) c=blue n=42 s=x\n", Out(Prelude +
            """
            fn main(): int {
                let p = P { x = 1, y = 2 };
                println(f"p={p} c={Color.Blue} n={42} s={"x"}");
                return 0;
            }
            """));

    [Fact]
    public void A_type_parameter_renders_through_its_constraint() =>
        Assert.Equal("<(3, 4)> <red>\n", Out(Prelude +
            """
            fn tag<T :: [Display]>(v: T): string { return f"<{v}>"; }
            fn main(): int {
                println(f"{tag(P { x = 3, y = 4 })} {tag(Color.Red)}");
                return 0;
            }
            """));

    [Fact]
    public void An_interface_value_renders_through_its_vtable() =>
        Assert.Equal("(5, 6)\n", Out(Prelude +
            """
            fn main(): int {
                let d: Display = P { x = 5, y = 6 };
                println(f"{d}");
                return 0;
            }
            """));

    [Fact]
    public void The_hole_evaluates_its_expression_once() =>
        Assert.Equal("made\n(1, 1)\n", Out(Prelude +
            """
            fn make(): P { println("made"); return P { x = 1, y = 1 }; }
            fn main(): int {
                let s = f"{make()}";
                println(s);
                return 0;
            }
            """));
}
