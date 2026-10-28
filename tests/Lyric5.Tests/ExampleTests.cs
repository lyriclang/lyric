using Lyric5.Build;
using Lyric5.Toolchain;
using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// The examples: inventory, stats and stack, 4.x's examples/ in Lyric 5, each built as a package
/// and run. The multi-package example — <c>programs/three_packages</c>, a
/// path dependency and one from git — builds offline once the cache holds it, and builds for the
/// other operating system; a dependency's globals are ready before the importer's.
/// The file tool, <c>programs/filetool.lyr</c>, over a tree the test makes. The word counter,
/// <c>programs/wordcount.lyr</c>, over a text in four scripts. The log
/// filter, <c>programs/logfilter.lyr</c>, over a log; the JSON server, <c>programs/jsonserver.lyr</c>, with its
/// client; the gzip tool, <c>programs/gzipfile.lyr</c>, against .NET's GZipStream and the platform's gzip.
/// Through <c>Main</c>, so in the console collection.
/// </summary>
[Collection("console")]
public class ExampleTests
{
    private static string Examples([System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "programs", "three_packages"));

    internal static void Copy(string from, string to)
    {
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    private const string Report = "bed: 120000 cm2\npath: 40000 cm2\ntotal: 160000 cm2\n";

    private static string ProgramText(string name, [System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
        File.ReadAllText(Path.Combine(Path.GetDirectoryName(here)!, "programs", name + ".lyr"));

    /// <summary>As a user meets them: <c>src/main.lyr</c> of a package, built and run — the prelude
    /// and every module of the standard library at hand.</summary>
    [Theory]
    [InlineData("inventory", "Bread (0 gold)\nSword (15 gold)\nAmulet (80 gold)\nGesamtwert: 95 gold\n"
        + "Erstes im Budget (20): Bread\nBread gratis? true\n")]
    [InlineData("stats", "Summe:        24\nMaximum:      9\nDurchschnitt: 4.80\n")]
    [InlineData("stack", "Groesse: 3\nSpitze:  3\npop -> 3\npop -> 2\npop -> 1\n")]
    public void An_example_of_the_standard_library_runs(string name, string expected)
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", ProgramText(name)));
        Assert.Equal(expected, BuildAndRun(dir, "app", "build", "-C", dir));
    }

    /// <summary>As the example's README says: <c>units</c> made a repository, added by git, built
    /// online once — then offline with the repository gone.</summary>
    [Fact]
    public void The_example_of_three_packages_builds_offline()
    {
        var dir = Package(("x", ""));
        Copy(Examples(), dir);
        using var units = new TestRepo();
        var commit = units.Commit(
            ("lyric.toml", File.ReadAllText(Path.Combine(dir, "units", "lyric.toml"))),
            ("src/convert.lyr", File.ReadAllText(Path.Combine(dir, "units", "src", "convert.lyr"))));
        units.Tag("v1.0.0");
        var app = Path.Combine(dir, "app");

        var (added, _, why) = Run("add", "units", "--git", units.Url, "--tag", "v1.0.0", "-C", app);
        Assert.True(added == 0, why);
        Assert.Equal(commit, Assert.Single(LockFile.Read(Path.Combine(app, "lyric.lock"))).Commit);
        Assert.Equal(Report, BuildAndRun(app, "app", "build", "-C", app));

        TestRepo.DeleteTree(units.Dir);
        TestRepo.DeleteTree(Path.Combine(app, "out"));
        Assert.Equal(Report, BuildAndRun(app, "app", "build", "-C", app, "--offline"));

        // The other system's binary, from the same cache: a PE for Windows, an ELF for Linux —
        // where the run builds for other targets.
        if (!CrossTargets.Enabled) return;
        var other = Target.Host.Os == TargetOs.Windows ? Target.Parse("x86_64-linux-gnu") : Target.Parse("x86_64-windows-gnu");
        var (crossed, _, error) = Run("build", "-C", app, "--offline", "--target", other.Triple);
        Assert.True(crossed == 0, error);
        var binary = File.ReadAllBytes(Path.Combine(app, "out", "debug", other.Triple, "app" + other.ExecutableSuffix));
        // "MZ", and 0x7F "ELF" — byte by byte: C#'s \x takes up to four hex digits, and E is one.
        byte[] magic = other.Os == TargetOs.Windows ? [0x4D, 0x5A] : [0x7F, 0x45, 0x4C, 0x46];
        Assert.Equal(magic, binary[..magic.Length]);
    }

    /// <summary>Module globals are made eagerly at the start, a module's imports first —
    /// across packages as within one.</summary>
    [Fact]
    public void A_dependency_s_globals_are_ready_before_the_importer_s()
    {
        var dir = Package(
            ("lyric.toml", AppManifest + "\n[dependencies]\ngeo = { path = \"deps/geo\" }\n"),
            ("src/main.lyr", "import std.io { println };\nimport app.config { scale };\n\nfn main(): void {\n    println(f\"{scale}\");\n}\n"),
            ("src/config.lyr", "import geo.base { unit };\n\npub let scale: int = unit * 3;\n"),
            ("deps/geo/lyric.toml", "[package]\nname = \"geo\"\nversion = \"0.1.0\"\n"),
            ("deps/geo/src/base.lyr", "fn seven(): int {\n    return 7;\n}\n\npub let unit: int = seven();\n"));
        Assert.Equal("21\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    private const string TreeStats = "5 files, 3 directories, 2325 bytes\n  1000 sub/c.txt\n  1000 sub/e.bin\n  300 b.log\n";

    /// <summary>The file tool built as a package — its three commands over
    /// a tree with an empty directory and two files of one size (the walk's order between them
    /// kept), the copy byte for byte the original, a missing directory and an unknown command.</summary>
    [Fact]
    public void The_file_tool_reports_copies_and_finds()
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", ProgramText("filetool")));
        var (built, _, why) = Run("build", "-C", dir);
        Assert.True(built == 0, why);
        var host = Target.Host;
        var exe = Path.Combine(dir, "out", "debug", host.Triple, "app" + host.ExecutableSuffix);

        var tree = Path.Combine(dir, "tree");
        void Put(string rel, int size) =>
            File.WriteAllBytes(Path.Combine(tree, rel), [.. Enumerable.Range(0, size).Select(i => (byte)(i * 7 + rel.Length))]);
        Directory.CreateDirectory(Path.Combine(tree, "empty"));
        Directory.CreateDirectory(Path.Combine(tree, "sub", "deeper"));
        Put("a.txt", 5);
        Put("b.log", 300);
        Put(Path.Combine("sub", "c.txt"), 1000);
        Put(Path.Combine("sub", "deeper", "d.txt"), 20);
        Put(Path.Combine("sub", "e.bin"), 1000);

        (int Exit, string Out, string Err) Tool(params string[] args)
        {
            var ran = ProcessRunner.Run(exe, args, TimeSpan.FromMinutes(1));
            return (ran.ExitCode, ran.Stdout.Replace("\r\n", "\n"), ran.Stderr.Replace("\r\n", "\n"));
        }

        Assert.Equal((0, TreeStats, ""), Tool("stats", tree));
        Assert.Equal((0, "a.txt\nsub/c.txt\nsub/deeper/d.txt\n", ""), Tool("find", tree, ".txt"));

        var copy = Path.Combine(dir, "copy");
        Assert.Equal((0, "copied 5 files, 2325 bytes\n", ""), Tool("copy", tree, copy));
        Assert.True(Directory.Exists(Path.Combine(copy, "empty")));
        foreach (var file in Directory.EnumerateFiles(tree, "*", SearchOption.AllDirectories))
            Assert.Equal(File.ReadAllBytes(file), File.ReadAllBytes(Path.Combine(copy, Path.GetRelativePath(tree, file))));
        Assert.Equal((0, TreeStats, ""), Tool("stats", copy));

        var missing = Path.Combine(dir, "missing");
        var (failed, nothing, said) = Tool("stats", missing);
        Assert.Equal((1, ""), (failed, nothing));
        Assert.StartsWith($"filetool: not found: {missing}", said);

        var (unknown, _, usage) = Tool();
        Assert.Equal(2, unknown);
        Assert.StartsWith("usage: filetool", usage);
    }

    /// <summary>Three lines in German, Greek, Japanese and Portuguese — letters beyond ASCII, an
    /// upper-case word, a dash between words, punctuation at both ends.</summary>
    private const string UnicodeText =
        "Grüße aus Zürich! Zürich ist groß, ZÜRICH ist schön.\nΕλλάδα, ελλάδα — Ωραία μέρα.\n"
        + "東京 und São Paulo: São Paulo ist groß.\n";

    /// <summary>The compile-time example: constants a module computes before its entry, and ones
    /// computed while compiling — <c>fib(30)</c> is data in the program, and <c>fib</c> is not built.</summary>
    [Fact]
    public void Constants_are_filled_before_the_entry_or_computed_while_compiling()
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", ProgramText("constants")));
        Assert.Equal("lyric 1.0\n832040\n49 8\n0 2\n64 bits\n", BuildAndRun(dir, "app", "build", "-C", dir));
        var (exit, ir, error) = Run("build", "-C", dir, "--emit", "ir");
        Assert.True(exit == 0, error);
        Assert.DoesNotContain("fn app.main.fib ", ir);
    }

    /// <summary>The word counter built as a package, over a text in four
    /// scripts — the counts and the ranking checked against Python's reading of the same text
    /// (<c>str.split</c>, <c>isalnum</c>, <c>lower</c>), the CRC-32 against zlib's.</summary>
    [Fact]
    public void The_word_counter_counts_a_text_in_four_scripts()
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", ProgramText("wordcount")));
        var (built, _, why) = Run("build", "-C", dir);
        Assert.True(built == 0, why);
        var host = Target.Host;
        var exe = Path.Combine(dir, "out", "debug", host.Triple, "app" + host.ExecutableSuffix);
        var text = Path.Combine(dir, "text.txt");
        File.WriteAllBytes(text, System.Text.Encoding.UTF8.GetBytes(UnicodeText));

        var ran = ProcessRunner.Run(exe, [text], TimeSpan.FromMinutes(1));
        Assert.True(ran.ExitCode == 0, ran.Stderr);
        Assert.Equal("3 lines, 21 words, 120 characters, 157 bytes\n  3  ist\n  3  zürich\n  2  groß\n  2  paulo\n"
                     + "  2  são\ncrc32 cd8316ea\n", ran.Stdout.Replace("\r\n", "\n"));

        var usage = ProcessRunner.Run(exe, [], TimeSpan.FromMinutes(1));
        Assert.Equal(2, usage.ExitCode);
        Assert.StartsWith("usage: wordcount", usage.Stderr);
    }

    private const string LogText =
        "2026-10-14T08:00:01Z INFO  server started on :8080\n"
        + "2026-10-14T08:00:05Z WARN  slow request GET /api/users 1203ms\n"
        + "2026-10-14T08:00:07Z ERROR db: connection refused (attempt 3)\n"
        + "2026-10-14T08:00:09Z INFO  request GET /health 2ms\n"
        + "2026-10-14T08:00:11Z ERROR db: timeout after 5000ms\n";

    /// <summary>The log filter built as a package, over a log — the lines a
    /// regular expression selects, inverted, counted, numbered; a pattern RE2 refuses is reported
    /// with its offset.</summary>
    [Fact]
    public void The_log_filter_selects_the_lines_a_regex_matches()
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", ProgramText("logfilter")));
        var (built, _, why) = Run("build", "-C", dir);
        Assert.True(built == 0, why);
        var host = Target.Host;
        var exe = Path.Combine(dir, "out", "debug", host.Triple, "app" + host.ExecutableSuffix);
        var log = Path.Combine(dir, "server.log");
        File.WriteAllText(log, LogText);

        var errors = ProcessRunner.Run(exe, [@"\b(ERROR|WARN)\b", log], TimeSpan.FromMinutes(1));
        Assert.True(errors.ExitCode == 0, errors.Stderr);
        Assert.Equal("2026-10-14T08:00:05Z WARN  slow request GET /api/users 1203ms\n"
                     + "2026-10-14T08:00:07Z ERROR db: connection refused (attempt 3)\n"
                     + "2026-10-14T08:00:11Z ERROR db: timeout after 5000ms\n", errors.Stdout.Replace("\r\n", "\n"));

        var slow = ProcessRunner.Run(exe, ["-n", @"\s[1-9]\d{3,}ms$", log], TimeSpan.FromMinutes(1));
        Assert.Equal("2:2026-10-14T08:00:05Z WARN  slow request GET /api/users 1203ms\n"
                     + "5:2026-10-14T08:00:11Z ERROR db: timeout after 5000ms\n", slow.Stdout.Replace("\r\n", "\n"));

        var count = ProcessRunner.Run(exe, ["-c", "-v", "INFO", log], TimeSpan.FromMinutes(1));
        Assert.Equal("3\n", count.Stdout.Replace("\r\n", "\n"));

        var none = ProcessRunner.Run(exe, ["(?i)fatal", log], TimeSpan.FromMinutes(1));
        Assert.Equal(1, none.ExitCode);
        Assert.Equal("", none.Stdout);

        var bad = ProcessRunner.Run(exe, ["(ERROR", log], TimeSpan.FromMinutes(1));
        Assert.Equal(2, bad.ExitCode);
        Assert.Equal("logfilter: the group is not closed at offset 0\n", bad.Stderr.Replace("\r\n", "\n"));

        var usage = ProcessRunner.Run(exe, ["-x", "a", log], TimeSpan.FromMinutes(1));
        Assert.Equal(2, usage.ExitCode);
        Assert.StartsWith("usage: logfilter", usage.Stderr);

        // The matches in color where asked (the output here is a pipe: plain unless --color)
        var colored = ProcessRunner.Run(exe, ["--color", "-n", "ERROR", log], TimeSpan.FromMinutes(1));
        Assert.Equal("\u001b[32m3\u001b[0m:2026-10-14T08:00:07Z \u001b[1;31mERROR\u001b[0m db: connection refused (attempt 3)\n"
                     + "\u001b[32m5\u001b[0m:2026-10-14T08:00:11Z \u001b[1;31mERROR\u001b[0m db: timeout after 5000ms\n", colored.Stdout.Replace("\r\n", "\n"));
        var plain = ProcessRunner.Run(exe, ["-n", "ERROR", log], TimeSpan.FromMinutes(1));
        Assert.DoesNotContain("\u001b", plain.Stdout);
    }

    /// <summary>The JSON server built as a package — the server answers
    /// JSON, the client of the same program fetches it, posts one, reads it back, misses one.</summary>
    [Fact]
    public void The_json_server_serves_and_its_client_fetches()
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", ProgramText("jsonserver")));
        var (built, _, why) = Run("build", "-C", dir);
        Assert.True(built == 0, why);
        var host = Target.Host;
        var exe = Path.Combine(dir, "out", "debug", host.Triple, "app" + host.ExecutableSuffix);
        var ran = ProcessRunner.Run(exe, [], TimeSpan.FromMinutes(1));
        Assert.True(ran.ExitCode == 0, ran.Stderr);
        Assert.Equal("1 Brot 2.5\n2 Käse 7.0\n201 Created /items/3 Milch\n200 {\"id\":3,\"name\":\"Milch\",\"price\":1.2}\n"
                     + "404 {\"error\":\"no item 9\"}\n", ran.Stdout.Replace("\r\n", "\n"));
    }

    /// <summary>CRC-32 (IEEE), as gzip's trailer and the tool's output carry it.</summary>
    private static uint Crc32Of(byte[] bytes)
    {
        uint crc = 0xFFFFFFFF;
        foreach (var b in bytes)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
        }
        return crc ^ 0xFFFFFFFF;
    }

    /// <summary>The gzip file tool built as a package — a file compressed
    /// here is read by .NET's GZipStream and, where the platform has it, by <c>gzip -d</c>; a file
    /// compressed by GZipStream is read here; the CRC-32 is the same on both sides.</summary>
    [Fact]
    public void The_gzip_tool_agrees_with_the_platform()
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", ProgramText("gzipfile")));
        var (built, _, why) = Run("build", "-C", dir);
        Assert.True(built == 0, why);
        var host = Target.Host;
        var exe = Path.Combine(dir, "out", "debug", host.Triple, "app" + host.ExecutableSuffix);
        var plain = Path.Combine(dir, "plain.txt");
        var text = ProgramText("gzipfile") + ProgramText("wordcount");
        File.WriteAllText(plain, text + text + text);
        var bytes = File.ReadAllBytes(plain);
        var crc = Crc32Of(bytes);

        // ours, read by .NET
        var ours = Path.Combine(dir, "ours.gz");
        var packed = ProcessRunner.Run(exe, ["-9", plain, ours], TimeSpan.FromMinutes(1));
        Assert.True(packed.ExitCode == 0, packed.Stderr);
        Assert.Equal($"{bytes.Length} -> {new FileInfo(ours).Length} bytes, crc32 {crc:x8}\n", packed.Stdout.Replace("\r\n", "\n"));
        Assert.True(new FileInfo(ours).Length < bytes.Length / 3, "it shrinks");
        using (var unpacked = new MemoryStream())
        {
            using (var gz = new System.IO.Compression.GZipStream(File.OpenRead(ours), System.IO.Compression.CompressionMode.Decompress))
                gz.CopyTo(unpacked);
            Assert.Equal(bytes, unpacked.ToArray());
        }

        // theirs, read by ours
        var theirs = Path.Combine(dir, "theirs.gz");
        using (var gz = new System.IO.Compression.GZipStream(File.Create(theirs), System.IO.Compression.CompressionLevel.SmallestSize))
            gz.Write(bytes);
        var back = Path.Combine(dir, "back.txt");
        var unpackedRun = ProcessRunner.Run(exe, ["-d", theirs, back], TimeSpan.FromMinutes(1));
        Assert.True(unpackedRun.ExitCode == 0, unpackedRun.Stderr);
        Assert.Equal($"{new FileInfo(theirs).Length} -> {bytes.Length} bytes, crc32 {crc:x8}\n", unpackedRun.Stdout.Replace("\r\n", "\n"));
        Assert.Equal(bytes, File.ReadAllBytes(back));

        // the platform's gzip, where there is one
        var gzip = ProcessRunner.Run("gzip", ["--version"], TimeSpan.FromSeconds(10));
        if (gzip.ExitCode == 0)
        {
            var listed = ProcessRunner.Run("gzip", ["-t", ours], TimeSpan.FromSeconds(30));
            Assert.True(listed.ExitCode == 0, listed.Stderr);
        }

        // not gzip
        var bad = ProcessRunner.Run(exe, ["-d", plain, back], TimeSpan.FromMinutes(1));
        Assert.Equal(1, bad.ExitCode);
        Assert.Equal("gzipfile: not a gzip stream: the magic bytes are missing\n", bad.Stderr.Replace("\r\n", "\n"));
        var usage = ProcessRunner.Run(exe, ["-x", plain], TimeSpan.FromMinutes(1));
        Assert.Equal(2, usage.ExitCode);
    }

    private static string MacrosExample([System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "examples", "macros"));

    /// <summary>The macros example, <c>examples/macros</c> — <c>retry!</c> from a
    /// quote, <c>@Builder</c> from the constructors, <c>formula!</c> over <c>parseExpr</c> — built
    /// and run; and <c>lyric expand</c> shows the three sites replaced.</summary>
    [Fact]
    public void The_macro_examples_run_and_expand()
    {
        var dir = Package(("x", ""));
        Copy(MacrosExample(), dir);
        Assert.Equal("fetched 30 after 3 calls; attempts is still 7\npoint 3 4\n66\n", BuildAndRun(dir, "macros", "build", "-C", dir));
        var (exit, expanded, error) = Run("expand", "-C", dir);
        Assert.True(exit == 0, error);
        Assert.Contains("struct PointBuilder {", expanded);
        Assert.Contains("println(2 * x.pow(2) + 3 * x + 1);", expanded);
        Assert.Contains("if (attempts__", expanded);
        Assert.DoesNotContain("retry!", expanded);
    }
}
