using System.Security.Cryptography;
using Lyric5.Toolchain;
using Lyric4.From4;

namespace Lyric5.Tests;

/// <summary>
/// Lyric 4.6's front end, frozen for the migration (<c>src/Lyric4.Frontend</c>): it reads 4.6's
/// own examples without a word, refuses what 4.6 refused, and speaks 4.6 where Lyric 5's shared
/// front end no longer does — <c>resume</c>, <c>xs.length</c>, a bare variant in a pattern, a
/// throwing call without <c>try</c>. The frozen files are the ones <c>tooling/from4/freeze.py</c>
/// wrote, byte for byte.
/// </summary>
public class From4Tests
{
    private static readonly string Root = RuntimeLayout.FindRoot(AppContext.BaseDirectory);
    private static readonly string Examples = Path.Combine(Root, "tests", "fix-from-4", "examples-4.6");

    private static string Said(Model model)
    {
        var text = new StringWriter();
        foreach (var d in model.Diagnostics.Diagnostics) text.WriteLine($"{d.Code} {d.Severity} {d.Message}");
        return text.ToString();
    }

    private static Model Program(string source)
    {
        var file = Path.Combine(TestDirectories.Fresh("lyric4-"), "main.lyr");
        File.WriteAllText(file, source);
        return From4.CheckFile(file);
    }

    [Fact]
    public void Every_example_of_4_6_checks_without_an_error()
    {
        var files = Directory.EnumerateFiles(Examples, "*.lyr", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(30, files.Count);
        foreach (var file in files)
        {
            var model = From4.CheckFile(file);
            Assert.False(model.HasErrors, $"{Path.GetRelativePath(Examples, file)}:\n{Said(model)}");
            Assert.Single(model.Roots);
        }
    }

    [Fact]
    public void It_speaks_4_6_where_the_shared_front_end_no_longer_does()
    {
        var model = Program("""
            import std.io.console { println };
            import std.core { Exception };

            enum Color { Red, Green }

            fn count(): Coroutine<int> {
                yield 1;
                yield 2;
            }

            fn risky(): int throws Exception {
                throw Exception { text = "no" };
            }

            fn safe(): int {
                try {
                    return risky();
                } catch (e) {
                    return 0;
                }
            }

            fn main(): int {
                let xs = [1, 2, 3];
                let c = count();
                let first = resume c;
                let name = match (Color.Red) { Red => "red", Green => "green" };
                let n = xs.length;
                println(name);
                return first + n + safe();
            }
            """);
        Assert.False(model.HasErrors, Said(model));
    }

    [Fact]
    public void What_4_6_refused_is_refused_with_its_words()
    {
        var model = Program("fn main(): int {\n    return \"no\";\n}\n");
        Assert.True(model.HasErrors);
        Assert.Contains(model.Diagnostics.Diagnostics, d => d.Code.StartsWith("LYR-SEM", StringComparison.Ordinal));
    }

    [Fact]
    public void The_frozen_files_are_the_ones_the_freeze_wrote()
    {
        var list = File.ReadAllLines(Path.Combine(Root, "src", "Lyric4.Frontend", "frozen.sha256"))
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .Select(l => (Sha: l[..64], Path: l[66..]))
            .ToList();
        Assert.True(list.Count > 100, $"{list.Count} files listed");
        foreach (var (sha, path) in list)
        {
            var bytes = File.ReadAllBytes(Path.Combine(Root, path));
            Assert.True(sha == Convert.ToHexStringLower(SHA256.HashData(bytes)), $"{path} is not the file the freeze wrote");
        }

        // nothing beside them in the frozen directories: a file of one's own goes under From4/
        var listed = list.Select(e => e.Path).ToHashSet(StringComparer.Ordinal);
        var frontend = Path.Combine(Root, "src", "Lyric4.Frontend");
        foreach (var dir in new[] { "Core", "Lexing", "Parsing", "AST", "Resolver", "Sema", "Ir", "stdlib" })
            foreach (var file in Directory.EnumerateFiles(Path.Combine(frontend, dir), "*", SearchOption.AllDirectories))
                Assert.Contains(Path.GetRelativePath(Root, file).Replace('\\', '/'), listed);
        foreach (var file in Directory.EnumerateFiles(Examples, "*", SearchOption.AllDirectories))
            Assert.Contains(Path.GetRelativePath(Root, file).Replace('\\', '/'), listed);
    }
}
