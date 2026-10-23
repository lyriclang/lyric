using Lyric5.Site;

namespace Lyric5.Tests;

/// <summary>
/// The site's versions and the legacy site: the list the version switch reads and the root page it
/// leads from, the move of the 1.x–4.x documentation under <c>legacy/</c> over a small published
/// site made for the test, the refusals that keep the move off anything else, and where the
/// not-found page sends an old address.
/// </summary>
public class LegacyTests
{
    private static string Fixture()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lyric-legacy-" + Guid.NewGuid().ToString("N"));
        void Put(string path, string text)
        {
            var file = Path.Combine(dir, path);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, text);
        }
        Put("CNAME", "lyric.oil1i.de");
        Put(".nojekyll", "");
        Put("index.html", "<meta http-equiv=\"refresh\" content=\"0; url=v2.0.0/\" />");
        Put("versions.json", "[{ \"version\": \"nightly\", \"stable\": false }, { \"version\": \"v2.0.0\", \"stable\": true }, { \"version\": \"v1.0.0\", \"stable\": true }]");
        Put("v1.0.0/index.html", "<p data-versions=\"../versions.json\">one</p>");
        Put("v1.0.0/guide/functions/index.html", "<p data-versions=\"../../../versions.json\">functions</p>");
        Put("v2.0.0/index.html", "<p>two</p>");
        Put("v2.0.0/site.css", "body {}");
        Put("nightly/index.html", "<p>nightly</p>");
        Put("notes.txt", "kept");
        return dir;
    }

    private static Dictionary<string, byte[]> Files(string dir) =>
        Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(dir, f).Replace('\\', '/'), File.ReadAllBytes);

    [Fact]
    public void The_move_takes_every_old_page_under_legacy_with_its_bytes_and_nothing_else()
    {
        var dir = Fixture();
        try
        {
            var before = Files(dir);
            Legacy.Apply(dir);
            var after = Files(dir);

            foreach (var (path, bytes) in before)
            {
                if (path.StartsWith("nightly/", StringComparison.Ordinal)) Assert.False(after.ContainsKey(path), path);
                else if (path.StartsWith('v') || path == "versions.json")
                {
                    Assert.True(after.TryGetValue("legacy/" + path, out var moved), $"legacy/{path} is missing");
                    if (path != "versions.json") Assert.Equal(bytes, moved);
                    Assert.False(after.ContainsKey(path), $"{path} is still at the root");
                }
                else Assert.Equal(bytes, after[path]);
            }
            var movedTo = before.Keys.Where(p => p.StartsWith('v') || p == "versions.json").Select(p => "legacy/" + p).ToHashSet();
            var added = after.Keys.Where(k => !before.ContainsKey(k) && !movedTo.Contains(k));
            Assert.Equal(["404.html", "legacy/index.html"], added.Order(StringComparer.Ordinal));

            var list = File.ReadAllText(Path.Combine(dir, "legacy", "versions.json"));
            Assert.DoesNotContain("nightly", list);
            Assert.Contains("\"version\": \"v2.0.0\"", list);
            Assert.Contains("\"version\": \"v1.0.0\"", list);

            var overview = File.ReadAllText(Path.Combine(dir, "legacy", "index.html"));
            Assert.True(overview.IndexOf("href=\"v2.0.0/\"", StringComparison.Ordinal) < overview.IndexOf("href=\"v1.0.0/\"", StringComparison.Ordinal),
                "the overview lists the newest version first");
            Assert.Empty(Legacy.Plan(dir));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void A_dry_run_changes_nothing()
    {
        var dir = Fixture();
        try
        {
            var before = Files(dir);
            var steps = Legacy.Plan(dir);
            Assert.Equal(["move v1.0.0", "move v2.0.0", "move-file versions.json", "remove nightly", "write legacy/versions.json",
                    "write legacy/index.html", "write 404.html"],
                steps.Select(s => $"{s.Kind} {s.From}"));
            Assert.Equal(before.Keys.Order(), Files(dir).Keys.Order());
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Theory]
    [InlineData("source", "is the source repository")]
    [InlineData("main", "has the branch 'main' checked out")]
    [InlineData("bare", "not the published site")]
    [InlineData("twice", "both at the root and under legacy/")]
    public void The_move_refuses_anything_but_a_work_tree_of_the_published_site(string kind, string said)
    {
        var dir = Fixture();
        try
        {
            switch (kind)
            {
                case "source": File.WriteAllText(Path.Combine(dir, "Lyric.slnx"), ""); break;
                case "main":
                    Directory.CreateDirectory(Path.Combine(dir, ".git"));
                    File.WriteAllText(Path.Combine(dir, ".git", "HEAD"), "ref: refs/heads/main\n");
                    break;
                case "bare":
                    foreach (var d in Directory.EnumerateDirectories(dir)) Directory.Delete(d, recursive: true);
                    File.Delete(Path.Combine(dir, "CNAME"));
                    break;
                case "twice": Directory.CreateDirectory(Path.Combine(dir, "legacy", "v1.0.0")); break;
            }
            var e = Assert.Throws<InvalidOperationException>(() => Legacy.Plan(dir));
            Assert.Contains(said, e.Message);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Theory]
    [InlineData("ref: refs/heads/gh-pages\n")]
    [InlineData("2cb51ac78ca5c49899e684ae3e0c7cb9032f2a16\n")]
    public void The_move_runs_on_gh_pages_or_a_detached_copy(string head)
    {
        var dir = Fixture();
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, ".git"));
            File.WriteAllText(Path.Combine(dir, ".git", "HEAD"), head);
            Assert.NotEmpty(Legacy.Plan(dir));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Theory]
    [InlineData("/v4.5.0/", "/legacy/v4.5.0/")]
    [InlineData("/v4.5.0", "/legacy/v4.5.0/")]
    [InlineData("/v4.5.0/stdlib/std.time/", "/legacy/v4.5.0/stdlib/std.time/")]
    [InlineData("/v4.5.0/guide/building/", "/legacy/v4.5.0/guide/building/")]
    [InlineData("/v1.0.0/guide/functions/", "/legacy/v1.0.0/guide/functions/")]
    [InlineData("/v2.10.1/grammar/", "/legacy/v2.10.1/grammar/")]
    [InlineData("/v3.0.0/changelog/index.html", "/legacy/v3.0.0/changelog/index.html")]
    [InlineData("/nightly/guide/coroutines/", "/legacy/")]
    [InlineData("/nightly", "/legacy/")]
    [InlineData("/dev/book/", null)]
    [InlineData("/legacy/v9.9.9/", null)]
    [InlineData("/v4.5/guide/", null)]
    [InlineData("/v4.5.0x/", null)]
    [InlineData("/nightlyx/", null)]
    public void An_old_address_is_sent_to_its_page_under_legacy(string path, string? expected) =>
        Assert.Equal(expected, Legacy.RedirectOf(path));

    [Fact]
    public void The_not_found_page_redirects_by_the_same_pattern_and_asks_no_other_host()
    {
        var html = Legacy.NotFoundHtml();
        Assert.Contains("new RegExp(\"^/(v\\\\d+\\\\.\\\\d+\\\\.\\\\d+)(/.*)?$\")", html);
        Assert.Contains("\"/legacy/\" + old[1] + (old[2] || \"/\")", html);
        Assert.DoesNotContain("http", html);
        Assert.Contains("<a href=\"/legacy/\">", html);
    }

    [Fact]
    public void The_version_list_holds_stable_versions_newest_first_then_dev_then_legacy()
    {
        var dev = Versions.With([], "dev");
        Assert.Equal(["dev", Versions.Legacy.Name], dev.Select(e => e.Name));
        Assert.Equal(dev, Versions.With(dev, "dev"));
        Assert.Equal("dev/", Versions.Landing(dev).Path);

        var released = Versions.With(Versions.With(dev, "5.0"), "5.1");
        Assert.Equal(["5.1", "5.0", "dev", Versions.Legacy.Name], released.Select(e => e.Name));
        Assert.Equal(["5.1/", "5.0/", "dev/", "legacy/"], released.Select(e => e.Path));
        Assert.Equal("5.1/", Versions.Landing(released).Path);
        Assert.Equal(released, Versions.With(released, "5.0"));
    }

    [Theory]
    [InlineData("v5.0")]
    [InlineData("5")]
    [InlineData("5.0.1")]
    [InlineData("nightly")]
    public void Only_dev_and_major_minor_are_versions_of_the_site(string name)
    {
        Assert.False(Versions.IsVersionName(name));
        Assert.Throws<ArgumentException>(() => Versions.With([], name));
    }

    [Fact]
    public void The_root_takes_the_version_list_and_the_page_that_leads_on_and_a_second_update_changes_nothing()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lyric-versions-" + Guid.NewGuid().ToString("N"));
        try
        {
            Versions.Update(dir, "dev");
            var list = File.ReadAllText(Path.Combine(dir, "versions.json"));
            var root = File.ReadAllText(Path.Combine(dir, "index.html"));
            Assert.Equal("[\n  { \"name\": \"dev\", \"path\": \"dev/\" },\n  { \"name\": \"Lyric 1.0.0–4.5.0 (Legacy)\", \"path\": \"legacy/\" }\n]\n", list);
            Assert.Contains("<meta http-equiv=\"refresh\" content=\"0; url=dev/\" />", root);
            Assert.Equal(Versions.With([], "dev"), Versions.Read(dir));

            Versions.Update(dir, "dev");
            Assert.Equal(list, File.ReadAllText(Path.Combine(dir, "versions.json")));
            Assert.Equal(root, File.ReadAllText(Path.Combine(dir, "index.html")));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void The_old_sites_list_at_the_root_is_not_written_over()
    {
        var dir = Fixture();
        try
        {
            var e = Assert.Throws<InvalidOperationException>(() => Versions.Update(dir, "dev"));
            Assert.Contains("move the legacy site first", e.Message);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
