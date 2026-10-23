using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Lyric5.Doc;
using Lyric5.Site;
using Lyric5.Toolchain;

namespace Lyric5.Tests;

/// <summary>
/// The site's search: the pinned Pagefind and its check, the index a version carries, and what a
/// search finds in it. The index is read the way the browser's script reads it — the meta file
/// names the pages and the word chunks, a chunk lists each word's pages — so a word is found on a
/// page only where Pagefind would find it there.
///
/// <para>The index needs the Pagefind binary (<see cref="Search.Locate"/>). Where it is missing the
/// index tests pass without looking, unless <c>LYRIC_REQUIRE_PAGEFIND=1</c> says the run must
/// have it, as the site's own job does.</para>
/// </summary>
public class SearchTests
{
    private static readonly string Root = RuntimeLayout.FindRoot(AppContext.BaseDirectory);
    private const string LinuxSum = "afb824a9e7f64905a934900481cea5be679c03975e527329e0e5e6cc70f5feda";
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";

    [Fact]
    public void The_pin_names_the_version_and_a_sum_for_every_target()
    {
        var pin = PagefindPin.Of(Root);
        Assert.Equal("1.5.2", pin.Version);
        Assert.Equal(LinuxSum, pin.Sums["x86_64-unknown-linux-musl"]);
        foreach (var target in new[] { "aarch64-unknown-linux-musl", "x86_64-apple-darwin", "aarch64-apple-darwin", "x86_64-pc-windows-msvc" })
            Assert.Matches("^[0-9a-f]{64}$", pin.Sums[target]);
    }

    [Fact]
    public void A_line_the_pin_cannot_read_is_refused()
    {
        Assert.Throws<FormatException>(() => PagefindPin.Read("version = 1.5.2\nnonsense\n"));
        Assert.Throws<FormatException>(() => PagefindPin.Read("x86_64-unknown-linux-musl = abc\n"));
    }

    /// <summary>The install script unpacks an archive whose sum is the pinned one and refuses any
    /// other, before unpacking. Run on a copy of the script beside a pin made for a small archive,
    /// so nothing is downloaded.</summary>
    [Fact]
    public void The_install_script_takes_the_pinned_archive_and_refuses_another()
    {
        if (OperatingSystem.IsWindows()) return; // the script is the CI step's, run under bash
        var dir = Path.Combine(Path.GetTempPath(), "lyric5-pagefind-" + Guid.NewGuid().ToString("N"));
        try
        {
            var tooling = Path.Combine(dir, "tooling");
            Directory.CreateDirectory(Path.Combine(tooling, "pagefind"));
            File.Copy(Path.Combine(Root, "tooling", "pagefind", "install.sh"), Path.Combine(tooling, "pagefind", "install.sh"));
            var payload = Path.Combine(dir, "payload");
            Directory.CreateDirectory(payload);
            File.WriteAllText(Path.Combine(payload, "pagefind"), "not the real one\n");
            var archive = Path.Combine(dir, "pagefind.tar.gz");
            Run("tar", ["-czf", archive, "-C", payload, "pagefind"], null);
            var sum = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(archive)));

            File.WriteAllText(Path.Combine(tooling, "pagefind-version"), $"version = 1.5.2\nx86_64-unknown-linux-musl = {sum}\n");
            var into = Path.Combine(dir, "bin");
            var (ok, _) = Run("bash", [Path.Combine(tooling, "pagefind", "install.sh"), "x86_64-unknown-linux-musl", into], archive);
            Assert.Equal(0, ok);
            Assert.True(File.Exists(Path.Combine(into, "pagefind")));

            File.WriteAllText(Path.Combine(tooling, "pagefind-version"), $"version = 1.5.2\nx86_64-unknown-linux-musl = {LinuxSum}\n");
            var refusedInto = Path.Combine(dir, "refused");
            var (code, errors) = Run("bash", [Path.Combine(tooling, "pagefind", "install.sh"), "x86_64-unknown-linux-musl", refusedInto], archive);
            Assert.NotEqual(0, code);
            Assert.Contains("refused", errors);
            Assert.False(Directory.Exists(refusedInto));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static (int Code, string Errors) Run(string file, string[] args, string? archive)
    {
        var start = new ProcessStartInfo(file) { RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var a in args) start.ArgumentList.Add(a);
        if (archive is not null) start.Environment["PAGEFIND_ARCHIVE"] = archive;
        using var process = Process.Start(start)!;
        process.StandardOutput.ReadToEnd();
        var errors = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, errors);
    }

    private static readonly SpecChapter[] Chapters =
    [
        new("01-lists.md", "# 1 Lists\n\nA list grows at its end: `List.push` appends one value.\n"),
    ];

    /// <summary>A version of the site with the specification's fixture and the repository's Book
    /// and catalog, indexed; <c>null</c> where there is no Pagefind to index it.</summary>
    private static string? IndexedSite(Func<List<Page>, List<Page>>? change = null)
    {
        if (Search.Locate(Root, null) is not { } binary)
        {
            Assert.False(Environment.GetEnvironmentVariable("LYRIC_REQUIRE_PAGEFIND") == "1",
                $"no Pagefind {PagefindPin.Of(Root).Version}: set LYRIC_PAGEFIND or install it with tooling/pagefind/install.sh");
            return null;
        }
        var spec = Path.Combine(Path.GetTempPath(), "lyric5-spec-" + Guid.NewGuid().ToString("N"));
        var output = Path.Combine(Path.GetTempPath(), "lyric5-search-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(spec, SpecPages.Directory));
            foreach (var chapter in Chapters)
                File.WriteAllText(Path.Combine(spec, SpecPages.Directory, chapter.File), chapter.Text);
            var pages = SiteBuilder.Build(Root, "dev", "main", new SpecSource(spec, Commit));
            var version = Path.Combine(output, "dev");
            SiteWriter.Write(version, change is null ? pages : change(pages));
            Search.Index(binary, version);
            return output;
        }
        finally
        {
            Directory.Delete(spec, recursive: true);
        }
    }

    [Fact]
    public void A_search_finds_a_std_name_a_code_and_a_word_of_the_Book()
    {
        if (IndexedSite() is not { } output) return;
        try
        {
            var index = PagefindIndex.Open(Path.Combine(output, "dev", Search.IndexDirectory));
            Assert.Contains("/spec/01-lists/", index.Find("List.push"));
            Assert.Contains("/diagnostics/LYR-SEM0001/", index.Find("LYR-SEM0001"));
            Assert.Contains("/book/getting-started/", index.Find("toolchain"));
            Assert.DoesNotContain("/", index.Find("toolchain"));
            Assert.Equal(["Book", "Diagnostics", "Releases", "Spec"], index.Areas());
        }
        finally
        {
            Directory.Delete(output, recursive: true);
        }
    }

    [Fact]
    public void The_index_asks_no_other_host_for_anything()
    {
        if (IndexedSite() is not { } output) return;
        try
        {
            var offenders = Directory.EnumerateFiles(Path.Combine(output, "dev", Search.IndexDirectory), "*.css")
                .Where(f => System.Text.RegularExpressions.Regex.IsMatch(File.ReadAllText(f), "url\\(\\s*[\"']?(?:https?:)?//|@import"))
                .ToList();
            Assert.Empty(offenders);
        }
        finally
        {
            Directory.Delete(output, recursive: true);
        }
    }

    [Fact]
    public void Every_page_of_text_is_marked_for_the_index_and_the_start_page_is_not()
    {
        var pages = SiteBuilder.Build(Root, "dev", "main");
        Assert.All(pages.Where(p => p.Path != SiteBuilder.HomePath),
            p => Assert.Contains("data-pagefind-body=\"\"", p.Html));
        Assert.DoesNotContain("data-pagefind-body", pages.Single(p => p.Path == SiteBuilder.HomePath).Html);
        Assert.Contains("data-pagefind-filter=\"area:Book\"", pages.Single(p => p.Path == "book/getting-started/index.html").Html);
    }
}

/// <summary>
/// A version's Pagefind index, read as the browser's script reads it: each file is gzip, after the
/// signature <c>pagefind_dcd</c> CBOR. The meta file lists the pages (a fragment's hash each) and
/// the word chunks (first word, last word, hash); a chunk lists its words, each with the pages it
/// is on, the page numbers as differences to the one before.
/// </summary>
internal sealed class PagefindIndex
{
    private readonly string _dir;
    private readonly List<object?> _meta;

    private PagefindIndex(string dir, List<object?> meta) => (_dir, _meta) = (dir, meta);

    public static PagefindIndex Open(string dir)
    {
        var meta = Directory.EnumerateFiles(dir, "*.pf_meta").Single();
        return new PagefindIndex(dir, (List<object?>)Cbor.Read(Unpack(meta)));
    }

    /// <summary>The URLs of the pages the term is on, the term spelled as Pagefind keeps a word:
    /// lowercase, without its punctuation.</summary>
    public HashSet<string> Find(string term)
    {
        var word = new string(term.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        var pages = (List<object?>)_meta[1]!;
        var urls = new HashSet<string>();
        foreach (List<object?> chunk in (List<object?>)_meta[2]!)
        {
            if (string.CompareOrdinal(word, (string)chunk[0]!) < 0 || string.CompareOrdinal(word, (string)chunk[1]!) > 0) continue;
            var words = (List<object?>)((List<object?>)Cbor.Read(Unpack(Path.Combine(_dir, "index", chunk[2] + ".pf_index"))))[0]!;
            foreach (List<object?> entry in words)
            {
                if ((string)entry[0]! != word) continue;
                long page = 0;
                foreach (List<object?> on in (List<object?>)entry[1]!)
                {
                    page += (long)on[0]!;
                    var hash = (string)((List<object?>)pages[(int)page]!)[0]!;
                    using var fragment = JsonDocument.Parse(Unpack(Path.Combine(_dir, "fragment", hash + ".pf_fragment")));
                    urls.Add(fragment.RootElement.GetProperty("url").GetString()!);
                }
            }
        }
        return urls;
    }

    /// <summary>The values of the <c>area</c> filter, in order: the filter file is the filter's
    /// name and its values, each with the pages that carry it.</summary>
    public List<string> Areas()
    {
        var filter = Directory.EnumerateFiles(Path.Combine(_dir, "filter"), "*.pf_filter").Single();
        var root = (List<object?>)Cbor.Read(Unpack(filter))!;
        Assert.Equal("area", root[0]);
        return [.. ((List<object?>)root[1]!).Select(v => (string)((List<object?>)v!)[0]!).Order(StringComparer.Ordinal)];
    }

    private static byte[] Unpack(string path)
    {
        using var gzip = new GZipStream(File.OpenRead(path), CompressionMode.Decompress);
        using var bytes = new MemoryStream();
        gzip.CopyTo(bytes);
        var all = bytes.ToArray();
        var signature = "pagefind_dcd"u8;
        if (!all.AsSpan().StartsWith(signature)) throw new InvalidDataException($"{path}: no Pagefind signature");
        return all[signature.Length..];
    }
}

/// <summary>The part of CBOR Pagefind writes: integers, strings, arrays and maps.</summary>
internal static class Cbor
{
    public static object? Read(byte[] data)
    {
        var at = 0;
        return Item(data, ref at);
    }

    private static object? Item(byte[] data, ref int at)
    {
        var head = data[at++];
        var (major, info) = (head >> 5, head & 31);
        ulong n = info switch
        {
            < 24 => (ulong)info,
            24 => data[at++],
            25 => (ulong)(data[at++] << 8 | data[at++]),
            26 => Big(data, ref at, 4),
            27 => Big(data, ref at, 8),
            _ => throw new InvalidDataException($"CBOR: length form {info} at {at - 1}"),
        };
        switch (major)
        {
            case 0: return (long)n;
            case 1: return -1 - (long)n;
            case 2: { var b = data[at..(at + (int)n)]; at += (int)n; return b; }
            case 3: { var s = Encoding.UTF8.GetString(data, at, (int)n); at += (int)n; return s; }
            case 4:
            {
                var list = new List<object?>((int)n);
                for (ulong i = 0; i < n; i++) list.Add(Item(data, ref at));
                return list;
            }
            case 5:
            {
                var map = new Dictionary<object, object?>();
                for (ulong i = 0; i < n; i++) map[Item(data, ref at)!] = Item(data, ref at);
                return map;
            }
            default: throw new InvalidDataException($"CBOR: major type {major} at {at - 1}");
        }
    }

    private static ulong Big(byte[] data, ref int at, int count)
    {
        ulong n = 0;
        for (var i = 0; i < count; i++) n = n << 8 | data[at++];
        return n;
    }
}
