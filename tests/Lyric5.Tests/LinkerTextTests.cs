using Lyric5.Build;
using Lyric5.Toolchain;

namespace Lyric5.Tests;

/// <summary>
/// The linker's text read for the findings of spec 15 §5.2 rule 12, each dialect as its linker
/// writes it: lld and zig's Mach-O linker (measured with zig 0.16), Apple's ld64 and GNU ld (as
/// they print it). What none of them says is left over — the build then says the link's failure as
/// it is.
/// </summary>
public class LinkerTextTests
{
    private static readonly Target Linux = Target.Parse("x86_64-linux-gnu");
    private static readonly Target Mac = Target.Parse("aarch64-macos");

    [Fact]
    public void Lld_says_an_undefined_symbol_and_what_references_it()
    {
        var (findings, leftOver) = LinkerText.Read("""
            ld.lld: error: undefined symbol: missing_fn
            >>> referenced by app/src/main.c:2
            >>>               /home/ada/.cache/zig/o/36d3/u.o:(main)
            """, Linux);
        var f = Assert.Single(findings);
        Assert.Equal(new LinkFinding(LinkFindingKind.Undefined, "missing_fn", []) with { Places = f.Places }, f);
        Assert.Equal(["app/src/main.c:2"], f.Places);
        Assert.False(leftOver);
    }

    [Fact]
    public void Lld_link_says_an_undefined_symbol_the_same()
    {
        var (findings, _) = LinkerText.Read("lld-link: error: undefined symbol: missing_fn\n>>> referenced by u.c:2\n>>>               u.obj:(main)\n",
            Target.Parse("x86_64-windows-gnu"));
        Assert.Equal("missing_fn", Assert.Single(findings).Name);
    }

    [Fact]
    public void Zigs_Mach_O_linker_names_the_symbol_with_its_underscore_which_goes()
    {
        var (findings, _) = LinkerText.Read("error: undefined symbol: _missing_fn\n    note: referenced by /x/u.o:_main\n", Mac);
        var f = Assert.Single(findings);
        Assert.Equal("missing_fn", f.Name);
        Assert.Equal(["/x/u.o:_main"], f.Places);
    }

    [Fact]
    public void A_duplicate_keeps_each_definer_with_the_lines_that_say_it_more_nearly()
    {
        var (findings, _) = LinkerText.Read("""
            ld.lld: error: duplicate symbol: dup
            >>> defined at b.c:1
            >>>            b.o:(dup)
            >>> defined at a.c:1
            >>>            a.o:(.text+0x0) in archive /p/liba.a
            """, Linux);
        var f = Assert.Single(findings);
        Assert.Equal(LinkFindingKind.Duplicate, f.Kind);
        Assert.Equal(["b.c:1\nb.o:(dup)", "a.c:1\na.o:(.text+0x0) in archive /p/liba.a"], f.Places);
    }

    [Fact]
    public void Zigs_Mach_O_linker_says_a_duplicate_with_its_definers()
    {
        var (findings, _) = LinkerText.Read("error: duplicate symbol definition: _dup\n    note: defined by /t/a.o\n    note: defined by /t/b.o\n", Mac);
        var f = Assert.Single(findings);
        Assert.Equal("dup", f.Name);
        Assert.Equal(["/t/a.o", "/t/b.o"], f.Places);
    }

    [Theory]
    [InlineData("error: unable to find dynamic system library 'nosuch' using strategy 'paths_first'. searched paths: none")]
    [InlineData("ld.lld: error: unable to find library -lnosuch")]
    [InlineData("ld: library 'nosuch' not found")]
    [InlineData("ld: library not found for -lnosuch")]
    [InlineData("/usr/bin/ld: cannot find -lnosuch: No such file or directory")]
    public void A_missing_library_is_read_from_each_linker(string line)
    {
        var (findings, leftOver) = LinkerText.Read(line + "\nclang: error: linker command failed with exit code 1 (use -v to see invocation)\n", Linux);
        Assert.Equal(new LinkFinding(LinkFindingKind.MissingLibrary, "nosuch", []), Assert.Single(findings) with { Places = [] });
        Assert.False(leftOver);
    }

    [Theory]
    [InlineData("ld: framework 'Cocoax' not found")]
    [InlineData("ld: framework not found Cocoax")]
    [InlineData("error: unable to find framework 'Cocoax'. searched paths: none")]
    public void A_missing_framework_is_read(string line)
    {
        var f = Assert.Single(LinkerText.Read(line, Mac).Findings);
        Assert.Equal((LinkFindingKind.MissingFramework, "Cocoax"), (f.Kind, f.Name));
    }

    [Fact]
    public void Ld64_says_its_undefined_symbols_in_a_block()
    {
        var (findings, leftOver) = LinkerText.Read("""
            Undefined symbols for architecture arm64:
              "_missing_fn", referenced from:
                  _main in main.o
              "_other_fn", referenced from:
                  _main in main.o
            ld: symbol(s) not found for architecture arm64
            clang: error: linker command failed with exit code 1 (use -v to see invocation)
            """, Mac);
        Assert.Equal(["missing_fn", "other_fn"], findings.Select(f => f.Name));
        Assert.Equal(["_main in main.o"], findings[0].Places);
        Assert.False(leftOver);
    }

    [Fact]
    public void Ld64_says_a_duplicate_with_its_objects_below()
    {
        var (findings, _) = LinkerText.Read("duplicate symbol '_dup' in:\n    /t/a.o\n    /t/b.o\nld: 1 duplicate symbols\n", Mac);
        var f = Assert.Single(findings);
        Assert.Equal("dup", f.Name);
        Assert.Equal(["/t/a.o", "/t/b.o"], f.Places);
    }

    [Fact]
    public void Gnu_ld_says_each_on_one_line()
    {
        var (findings, leftOver) = LinkerText.Read("""
            /usr/bin/ld: /tmp/u.o: in function `main':
            u.c:(.text+0x9): undefined reference to `missing_fn'
            /usr/bin/ld: b.o: in function `dup': b.c:(.text+0x0): multiple definition of `dup'; a.o:a.c:(.text+0x0): first defined here
            collect2: error: ld returned 1 exit status
            """, Linux);
        Assert.Equal([LinkFindingKind.Undefined, LinkFindingKind.Duplicate], findings.Select(f => f.Kind));
        Assert.Equal(["missing_fn", "dup"], findings.Select(f => f.Name));
        Assert.False(leftOver);
    }

    [Fact]
    public void A_symbol_said_twice_is_one_finding()
    {
        var (findings, _) = LinkerText.Read(
            "ld.lld: error: undefined symbol: f\n>>> referenced by a.c:1\nld.lld: error: undefined symbol: f\n>>> referenced by b.c:2\n", Linux);
        Assert.Equal(["a.c:1", "b.c:2"], Assert.Single(findings).Places);
    }

    [Fact]
    public void An_error_it_does_not_know_is_left_over()
    {
        var (findings, leftOver) = LinkerText.Read("ld.lld: error: /p/libbad.a: unknown file type\n", Linux);
        Assert.Empty(findings);
        Assert.True(leftOver);
    }
}
