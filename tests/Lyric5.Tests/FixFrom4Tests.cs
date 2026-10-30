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
