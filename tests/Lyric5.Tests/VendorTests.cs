using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Lyric5.Toolchain;
using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// Source trees (spec 15 §5.3): C a package does not hold, from a revision of a repository or
/// a file at a URL with its hash, named <c>@name/…</c> by the native part, read only where the
/// build's target needs it, locked as <c>kind = "native"</c>; and <c>lyric add --vendor</c>
/// (spec 14 §2). The repositories are local, the URLs a local server's or files: the tests reach
/// no network. Through <c>Main</c>, so in the console collection.
/// </summary>
[Collection("console")]
public class VendorTests
{
    /// <summary>The tree's C: a header and the function it declares, which adds <paramref name="bias"/>.</summary>
    private static (string Path, string Text)[] Clib(int bias = 0) =>
    [
        ("include/clib.h", "#include <stdint.h>\nint64_t clib_add(int64_t a, int64_t b);\n"),
        ("src/clib.c", $"#include \"clib.h\"\nint64_t clib_add(int64_t a, int64_t b) {{ return a + b + {bias}; }}\n"),
    ];

    /// <summary>A package whose native part compiles the tree <c>clib</c> and a source of its own
    /// that finds the tree's header — the program calls through the package's own function.</summary>
    private static string App(string tree, string native = "")
    {
        var manifest = AppManifest + $"\n[native.vendor]\nclib = {tree}\n\n[native]\nsources = [\"native/*.c\", \"@clib/src/*.c\"]\n"
                       + "include = [\"@clib/include\"]\n" + native;
        return Package(("lyric.toml", manifest),
            ("native/twice.c", "#include <clib.h>\nint64_t twice(int64_t x) { return clib_add(x, x); }\n"),
            ("src/main.lyr", "import std.io { println };\n\nextern \"C\" fn twice(x: int): int;\n\nfn main(): void {\n    println(f\"{twice(21)}\");\n}\n"));
    }

    /// <summary>A tar compressed with gzip holding <paramref name="files"/> under one top
    /// directory, written with <c>./</c> in front as tar often writes them.</summary>
    private static byte[] TarGz(string top, params (string Path, string Text)[] files)
    {
        using var bytes = new MemoryStream();
        using (var gzip = new GZipStream(bytes, CompressionLevel.Fastest, leaveOpen: true))
        using (var tar = new TarWriter(gzip, TarEntryFormat.Pax))
        {
            tar.WriteEntry(new PaxTarEntry(TarEntryType.Directory, $"./{top}/"));
            foreach (var (path, text) in files)
                tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, $"./{top}/{path}") { DataStream = new MemoryStream(Encoding.UTF8.GetBytes(text)) });
        }
        return bytes.ToArray();
    }

    private static byte[] Zip(params (string Path, string Text)[] files)
    {
        using var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (path, text) in files)
            {
                using var entry = zip.CreateEntry(path).Open();
                entry.Write(Encoding.UTF8.GetBytes(text));
            }
        return bytes.ToArray();
    }

    private static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    /// <summary>A file of its own: the test's bytes, unlike any other run's, so no cache answers
    /// for it but the one this test fills.</summary>
    private static (string Path, string Text)[] Unique(int bias = 0) =>
        [.. Clib(bias), ($"NOTE-{Guid.NewGuid():N}", "")];

    [Fact]
    public void A_tree_from_git_is_compiled_with_the_package_and_locked()
    {
        using var repo = new TestRepo();
        var commit = repo.Commit(Clib(bias: 1));
        repo.Tag("v1");
        var dir = App($"{{ git = \"{repo.Url}\", tag = \"v1\" }}");
        Assert.Equal("43\n", BuildAndRun(dir, "app", "build", "-C", dir));
        var locked = File.ReadAllText(Path.Combine(dir, "lyric.lock"));
        Assert.Contains($"name = \"clib\"\nkind = \"native\"\nof = \"app\"\ngit = \"{repo.Url}\"\ntag = \"v1\"\ncommit = \"{commit}\"\n", locked);
        // the tree lies in the user's cache, and the binary names it by its package and tree
        var tree = Path.Combine(UserCache.Root, "git", "trees", Lyric5.Build.GitCache.Id(repo.Url), commit);
        Assert.True(Directory.Exists(tree));
        var binary = File.ReadAllBytes(Path.Combine(dir, "out", "debug", Target.Host.Triple, "app" + Target.Host.ExecutableSuffix));
        Assert.False(binary.AsSpan().IndexOf(Encoding.UTF8.GetBytes(tree)) >= 0, $"the binary names {tree}");
    }

    /// <summary>A tree is read only where a table for the build's target names it: one named only
    /// for another system is never fetched — its URL does not exist.</summary>
    [Fact]
    public void A_tree_another_target_needs_is_not_read()
    {
        using var repo = new TestRepo();
        repo.Commit(Clib());
        var other = Target.Host.Os == TargetOs.Linux ? "windows" : "linux";
        var nowhere = new Uri(Path.Combine(Path.GetTempPath(), "lyric5-no-tree-" + Guid.NewGuid().ToString("N") + ".zip")).AbsoluteUri;
        var dir = App($"{{ git = \"{repo.Url}\" }}\nelsewhere = {{ url = \"{nowhere}\", hash = \"sha256:{new string('0', 64)}\" }}",
            $"\n[native.{other}]\nsources = [\"@elsewhere/x.c\"]\n");
        Assert.Equal("42\n", BuildAndRun(dir, "app", "build", "-C", dir));
        Assert.DoesNotContain("elsewhere", File.ReadAllText(Path.Combine(dir, "lyric.lock")));
    }

    /// <summary>A URL's file — a tar with gzip, one top directory and <c>./</c> in front — is
    /// loaded once by its hash and unpacked; a second build, the server gone, reads the cache.</summary>
    [Fact]
    public void A_tree_from_a_url_is_loaded_once_and_unpacked()
    {
        var archive = TarGz("clib-1.0", Unique(bias: 2));
        using var server = new FileServer(archive);
        var dir = App($"{{ url = \"{server.Url}\", hash = \"sha256:{Sha256(archive)}\" }}");
        Assert.Equal("44\n", BuildAndRun(dir, "app", "build", "-C", dir));
        Assert.Equal(1, server.Requests);
        server.Dispose();
        TestRepo.DeleteTree(Path.Combine(dir, "out"));
        Assert.Equal("44\n", BuildAndRun(dir, "app", "build", "-C", dir, "--offline"));
        Assert.False(File.Exists(Path.Combine(dir, "lyric.lock")), "a URL's tree is held by its hash, not by the lock");
    }

    /// <summary>A zip, told by its bytes — the URL's name says nothing —, read from a file.</summary>
    [Fact]
    public void A_zip_is_told_by_its_bytes()
    {
        var archive = Zip(Unique(bias: 3));
        var file = Path.Combine(TestDirectories.Fresh("lyric5-zip-"), "download");
        File.WriteAllBytes(file, archive);
        var dir = App($"{{ url = \"{new Uri(file).AbsoluteUri}\", hash = \"sha256:{Sha256(archive)}\" }}");
        Assert.Equal("45\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    [Fact]
    public void A_url_without_its_hash_is_refused_naming_it()
    {
        var archive = TarGz("clib", Unique());
        using var server = new FileServer(archive);
        var dir = App($"{{ url = \"{server.Url}\" }}");
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.Equal(1, exit);
        Assert.Contains($"error[LYR-PKG0002]: the source tree 'clib' is loaded from {server.Url} without a hash", error);
        Assert.Contains($"hash = \"sha256:{Sha256(archive)}\"", error);
    }

    [Fact]
    public void A_file_with_another_hash_is_refused()
    {
        var archive = TarGz("clib", Unique());
        using var server = new FileServer(archive);
        var pinned = new string('a', 64);
        var dir = App($"{{ url = \"{server.Url}\", hash = \"sha256:{pinned}\" }}");
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.Equal(1, exit);
        Assert.Contains($"error[LYR-PKG0008]: the file {server.Url} sends for the source tree 'clib' has the hash sha256:{Sha256(archive)}, "
                        + $"and the manifest pins sha256:{pinned}", error);
    }

    [Fact]
    public void Offline_a_tree_the_cache_lacks_is_refused()
    {
        var archive = TarGz("clib", Unique());
        using var server = new FileServer(archive);
        var dir = App($"{{ url = \"{server.Url}\", hash = \"sha256:{Sha256(archive)}\" }}");
        var (exit, _, error) = Run("build", "-C", dir, "--offline");
        Assert.Equal(2, exit);
        Assert.Contains($"error[LYR-PKG0007]: the source tree 'clib' from {server.Url} is not in the cache, and --offline loads nothing", error);
        Assert.Equal(0, server.Requests);
    }

    [Fact]
    public void A_url_that_cannot_be_loaded_is_the_environments_failure()
    {
        using var server = new FileServer([], status: HttpStatusCode.NotFound);
        var dir = App($"{{ url = \"{server.Url}\", hash = \"sha256:{new string('b', 64)}\" }}");
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.Equal(2, exit);
        Assert.Contains($"error[LYR-PKG0007]: the source tree 'clib' from {server.Url} cannot be loaded: the server answers 404", error);
    }

    /// <summary>Under <c>--locked</c> a tree the build needs and the lock lacks is refused, before it
    /// is fetched.</summary>
    [Fact]
    public void Locked_a_tree_the_lock_lacks_is_refused()
    {
        using var repo = new TestRepo();
        repo.Commit(Clib());
        var dir = App($"{{ git = \"{repo.Url}\" }}");
        var (exit, _, error) = Run("build", "-C", dir, "--locked");
        Assert.Equal(1, exit);
        Assert.Contains($"error[LYR-PKG0011]: the source tree 'clib' is read from {repo.Url} at its default branch, which lyric.lock does not hold", error);
        Assert.False(Directory.Exists(Path.Combine(UserCache.Root, "git", "db", Lyric5.Build.GitCache.Id(repo.Url))));
        // built once, the lock holds it, and --locked builds
        Assert.Equal("42\n", BuildAndRun(dir, "app", "build", "-C", dir));
        Assert.Equal("42\n", BuildAndRun(dir, "app", "build", "-C", dir, "--locked"));
    }

    /// <summary>A tree's content is held by the lock: content that is not what it holds is refused.</summary>
    [Fact]
    public void A_tree_whose_content_is_not_what_the_lock_holds_is_refused()
    {
        using var repo = new TestRepo();
        repo.Commit(Clib());
        var dir = App($"{{ git = \"{repo.Url}\" }}");
        Assert.Equal("42\n", BuildAndRun(dir, "app", "build", "-C", dir));
        var lockFile = Path.Combine(dir, "lyric.lock");
        var text = File.ReadAllText(lockFile);
        var at = text.IndexOf("hash = \"h1:", StringComparison.Ordinal) + "hash = \"h1:".Length;
        File.WriteAllText(lockFile, text[..at] + (text[at] == 'A' ? 'B' : 'A') + text[(at + 1)..]);
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.Equal(1, exit);
        Assert.Contains("error[LYR-PKG0008]: the content of 'clib' at commit", error);
    }

    /// <summary>A branch stays where the lock holds it; <c>lyric update</c> moves it to its head.</summary>
    [Fact]
    public void Update_moves_a_tree_to_its_branchs_head()
    {
        using var repo = new TestRepo();
        var first = repo.Commit(Clib());
        var dir = App($"{{ git = \"{repo.Url}\", branch = \"main\" }}");
        Assert.Equal("42\n", BuildAndRun(dir, "app", "build", "-C", dir));
        var second = repo.Commit(Clib(bias: 5));
        Assert.Equal("42\n", BuildAndRun(dir, "app", "build", "-C", dir));
        var (exit, output, error) = Run("update", "-C", dir, "clib");
        Assert.True(exit == 0, error);
        Assert.Contains($"  updated  clib (tree of app) ({repo.Url} at branch 'main') {first[..12]} -> {second[..12]}", output);
        Assert.Contains($"commit = \"{second}\"", File.ReadAllText(Path.Combine(dir, "lyric.lock")));
        Assert.Equal("47\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    /// <summary>A tree the manifest no longer names leaves the lock.</summary>
    [Fact]
    public void A_tree_no_manifest_names_leaves_the_lock()
    {
        using var repo = new TestRepo();
        repo.Commit(Clib());
        var dir = App($"{{ git = \"{repo.Url}\" }}");
        Assert.Equal("42\n", BuildAndRun(dir, "app", "build", "-C", dir));
        File.WriteAllText(Path.Combine(dir, "lyric.toml"), AppManifest);
        File.WriteAllText(Path.Combine(dir, "src", "main.lyr"), "fn main(): void {\n}\n");
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.True(exit == 0, error);
        Assert.DoesNotContain("clib", File.ReadAllText(Path.Combine(dir, "lyric.lock")));
    }

    [Fact]
    public void Add_vendor_writes_a_tree_from_git_and_one_from_a_url_with_its_hash()
    {
        using var repo = new TestRepo();
        repo.Commit(Clib());
        repo.Tag("v1");
        var archive = TarGz("z", Unique());
        using var server = new FileServer(archive);
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", "fn main(): void {\n}\n"));
        var (exit, output, error) = Run("add", "clib", "--vendor", "--git", repo.Url, "--tag", "v1", "-C", dir);
        Assert.True(exit == 0, error);
        Assert.Equal($"added clib = {{ git = \"{repo.Url}\", tag = \"v1\" }} to [native.vendor]\n", output);
        (exit, _, error) = Run("add", "z", "--vendor", "--url", server.Url, "-C", dir);
        Assert.True(exit == 0, error);
        Assert.Equal(AppManifest + $"\n[native.vendor]\nclib = {{ git = \"{repo.Url}\", tag = \"v1\" }}\n"
                     + $"z = {{ url = \"{server.Url}\", hash = \"sha256:{Sha256(archive)}\" }}\n",
            File.ReadAllText(Path.Combine(dir, "lyric.toml")));
        // no build needed the trees: the lock holds none
        Assert.False(File.Exists(Path.Combine(dir, "lyric.lock")));
        (exit, _, error) = Run("remove", "clib", "--vendor", "-C", dir);
        Assert.True(exit == 0, error);
        Assert.DoesNotContain("clib", File.ReadAllText(Path.Combine(dir, "lyric.toml")));
    }

    [Fact]
    public void Add_vendor_refuses_a_revision_the_repository_lacks_and_leaves_the_manifest()
    {
        using var repo = new TestRepo();
        repo.Commit(Clib());
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", "fn main(): void {\n}\n"));
        var (exit, _, error) = Run("add", "clib", "--vendor", "--git", repo.Url, "--tag", "v9", "-C", dir);
        Assert.Equal(1, exit);
        Assert.Contains($"error[LYR-PKG0006]: {repo.Url} has no tag 'v9' for 'clib'", error);
        Assert.Equal(AppManifest, File.ReadAllText(Path.Combine(dir, "lyric.toml")));
    }

    [Theory]
    [InlineData("--vendor --path ../x", "a source tree is read from a repository or a URL, not a directory")]
    [InlineData("--vendor", "'add --vendor' reads a source tree from a repository or from a URL")]
    [InlineData("--url https://example.org/x.zip", "'--url' names the file of a source tree: it goes with --vendor")]
    [InlineData("--vendor --url https://example.org/x.zip --tag v1", "'--tag' names a revision of a repository: it goes with --git")]
    public void Add_vendor_takes_one_source(string options, string message)
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", "fn main(): void {\n}\n"));
        var (exit, _, error) = Run(["add", "x", .. options.Split(' '), "-C", dir]);
        Assert.Equal(2, exit);
        Assert.Contains(message, error);
    }

    [Theory]
    [InlineData("[native.vendor]\nRay = { git = \"https://example.org/r\" }\n", "'Ray' is no name of a source tree")]
    [InlineData("[native.vendor]\nray = \"https://example.org/r\"\n", "the source tree 'ray' is written ray = { git = ")]
    [InlineData("[native.vendor]\nray = { url = \"ftp://example.org/r.zip\" }\n", "'ftp://example.org/r.zip' is no URL a tree is loaded from")]
    [InlineData("[native.vendor]\nray = { url = \"https://example.org/r.zip\", hash = \"md5:00\" }\n", "'md5:00' is no hash of a file")]
    [InlineData("[native.vendor]\nray = { git = \"https://example.org/r\", hash = \"sha256:00\" }\n", "'hash' is no key of the source tree 'ray', which is read from git")]
    [InlineData("[native.vendor]\nray = { tag = \"v1\" }\n", "the source tree 'ray' needs a repository or a URL")]
    [InlineData("[native]\nsources = [\"@ray/src/*.c\"]\n", "'@ray/src/*.c' in 'sources' of [native] names the source tree 'ray', which [native.vendor] does not hold")]
    [InlineData("[native]\nsources = [\"@ray\"]\n", "'@ray' in 'sources' of [native] is no pattern of the package's files or a tree's")]
    public void A_source_tree_is_written_so(string native, string message)
    {
        var dir = Package(("lyric.toml", AppManifest + "\n" + native), ("src/main.lyr", "fn main(): void {\n}\n"));
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.Equal(1, exit);
        Assert.Contains("error[LYR-PKG0002]: " + message, error);
    }

    /// <summary>A tree's file is no part of the package's content and is not refused for it — but
    /// it must be there.</summary>
    [Fact]
    public void A_trees_archive_must_be_there()
    {
        using var repo = new TestRepo();
        repo.Commit(Clib());
        var dir = App($"{{ git = \"{repo.Url}\" }}", "archives = [\"@clib/lib/libclib.a\"]\n");
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.Equal(1, exit);
        Assert.Contains("error[LYR-PKG0002]: app's manifest names '@clib/lib/libclib.a' in 'archives', and there is no such file", error);
    }

    /// <summary>A server on the loopback answering every request with one file, counting them.</summary>
    private sealed class FileServer : IDisposable
    {
        private HttpListener _listener;
        private readonly byte[] _bytes;
        private readonly HttpStatusCode _status;
        private int _requests;

        public FileServer(byte[] bytes, HttpStatusCode status = HttpStatusCode.OK)
        {
            _bytes = bytes;
            _status = status;
            // a free port can be taken between asking for it and binding it: ask again
            for (var attempt = 1; ; attempt++)
            {
                var port = FreePort();
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
                try
                {
                    _listener.Start();
                    Url = $"http://127.0.0.1:{port}/archive/v1";
                    break;
                }
                catch (HttpListenerException) when (attempt < 5) { _listener.Close(); }
            }
            _ = Serve();
        }

        public string Url { get; } = "";

        public int Requests => Volatile.Read(ref _requests);

        private async Task Serve()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;
                try { context = await _listener.GetContextAsync(); }
                catch (Exception e) when (e is HttpListenerException or ObjectDisposedException or InvalidOperationException) { return; }
                Interlocked.Increment(ref _requests);
                context.Response.StatusCode = (int)_status;
                if (_status == HttpStatusCode.OK) await context.Response.OutputStream.WriteAsync(_bytes);
                context.Response.Close();
            }
        }

        private static int FreePort()
        {
            using var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            return ((IPEndPoint)probe.LocalEndpoint).Port;
        }

        /// <summary><c>Close</c> alone: after <c>Stop</c>, the managed listener on Linux binds the
        /// port again to remove its prefix, and that can fail (seen in CI: address already in use).</summary>
        public void Dispose()
        {
            if (!_listener.IsListening) return;
            try { _listener.Close(); }
            catch (HttpListenerException) { }
        }
    }
}
