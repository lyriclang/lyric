using Lyric4.From4;
using Lyric5.Toolchain;
using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// The migration's rules, a case each (<c>tests/fix-from-4/&lt;case&gt;/</c>): <c>before/</c> a
/// Lyric 4 program that 4.6 accepts, <c>after/</c> what the rules make of it, <c>notes.txt</c> what
/// they say. The case is migrated in a copy and compared file by file; then the result is checked
/// by Lyric 5 — clean, unless <c>check.txt</c> names the exit and the codes it expects. So each case
/// proves both sides: what 4.6 read, what Lyric 5 reads.
/// </summary>
[Collection("console")]
public class FixFrom4Tests
{
    private static readonly string Cases = Path.Combine(RuntimeLayout.FindRoot(AppContext.BaseDirectory), "tests", "fix-from-4");

    /// <summary>A line of the table with a typing error would do nothing, silently.</summary>
    [Fact]
    public void Every_line_of_the_table_names_a_function_of_4_6s_library() => Assert.Empty(Migrator.TableFaults());

    /// <summary>A function of 4.6 that neither the table nor another rule speaks for, and that Lyric 5 has no
    /// function of that name for, would reach the user as a bare <c>SEM0012</c>.</summary>
    [Fact]
    public void Every_public_function_of_4_6_is_spoken_for_or_stands_in_Lyric_5()
    {
        var error = new StringWriter();
        var surface = Lyric5.Build.DocRun.StdSurface("0", error);
        Assert.True(surface is not null, error.ToString());
        var lines = Lyric5.Doc.Api.ApiSurface.Text(Lyric5.Doc.Api.ApiSurface.Entries(surface!)).Split('\n');
        Assert.True(lines.Length > 1000, $"the surface has {lines.Length} lines");
        Assert.Empty(Migrator.TableGaps(lines));
    }

    /// <summary>The test above says nothing if it looks at nothing: against a surface with no functions, every
    /// function without a line comes up, a free one, a method and one of a string.</summary>
    [Fact]
    public void Against_an_empty_surface_the_functions_without_a_line_come_up()
    {
        var gaps = Migrator.TableGaps([]);
        Assert.Contains("std.io.console.println", gaps);
        Assert.Contains("std.io.path.fileName", gaps);
        Assert.Contains("string.toUpper", gaps);
        Assert.DoesNotContain("std.fmt.padLeft", gaps);
        Assert.DoesNotContain("string.indexOf", gaps);
    }

    public static TheoryData<string> Names() => [.. Directory.EnumerateDirectories(Cases)
        .Select(Path.GetFileName).OfType<string>()
        .Where(name => name != "examples-4.6")
        .Order(StringComparer.Ordinal)];

    /// <summary>The case's <c>before/</c> in a fresh directory, migrated; the migration and the directory.</summary>
    internal static (Migration Migration, string Dir) Migrate(string name)
    {
        var dir = TestDirectories.Fresh($"lyric-from4-{name}-");
        Copy(Path.Combine(Cases, name, "before"), dir);
        var json = Path.Combine(dir, "lyric.json");
        var model = File.Exists(json) ? From4.CheckProject(json) : From4.CheckFile(Directory.EnumerateFiles(dir, "*.lyr").Single());
        Assert.False(model.HasErrors, string.Join("\n", model.Diagnostics.Diagnostics.Select(d => $"{d.Code} {d.Message}")));
        var migration = Migrator.Run(model, File.Exists(json) ? json : null);
        migration.Apply();
        return (migration, dir);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void A_case_migrates_to_what_its_after_holds_and_Lyric_5_reads_it(string name)
    {
        var (migration, dir) = Migrate(name);
        var expected = Path.Combine(Cases, name, "after");
        var got = Tree(dir);
        var want = Tree(expected);
        Assert.Equal(want.Keys.Order(StringComparer.Ordinal), got.Keys.Order(StringComparer.Ordinal));
        static string Shown(string text) => text.Replace("\n", "⏎\n");
        foreach (var (path, text) in want) Assert.True(text == got[path], $"{name}/{path}:\n--- expected\n{Shown(text)}\n--- migrated\n{Shown(got[path])}");

        var notes = migration.Ordered().Select(n => Migration.Line(n, dir)).ToList();
        var said = File.Exists(Path.Combine(Cases, name, "notes.txt"))
            ? File.ReadAllLines(Path.Combine(Cases, name, "notes.txt")).Where(l => l.Length > 0).ToList()
            : [];
        Assert.Equal(said, notes);

        // Lyric 5 reads the result: a package by its manifest, a file on its own
        var checkFile = Path.Combine(Cases, name, "check.txt");
        var (exit, _, error) = File.Exists(Path.Combine(dir, "lyric.toml"))
            ? Run("check", "-C", dir)
            : Run("check", Directory.EnumerateFiles(dir, "*.lyr").Single());
        if (!File.Exists(checkFile)) Assert.True(exit == 0, $"{name}: lyric check said\n{error}");
        else
        {
            var lines = File.ReadAllLines(checkFile).Where(l => l.Length > 0).ToList();
            Assert.True(exit == int.Parse(lines[0]), $"{name}: lyric check exited {exit}:\n{error}");
            foreach (var code in lines.Skip(1)) Assert.Contains(code, error);
        }
    }

    private static Dictionary<string, string> Tree(string dir) =>
        Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
            .Where(f => !Path.GetRelativePath(dir, f).Split(Path.DirectorySeparatorChar).Any(p => p is "out" or ".lyric"))
            .ToDictionary(f => Path.GetRelativePath(dir, f).Replace('\\', '/'), f => File.ReadAllText(f).Replace("\r\n", "\n"), StringComparer.Ordinal);

    private static void Copy(string from, string to)
    {
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }
}
