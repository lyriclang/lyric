using System.Text;
using Lyric.Core;
using Lyric.Lsp.Analysis;
using Lyric.Lsp.Documents;
using Lyric.Lsp.Protocol;

namespace Lyric.Tests.Lsp;

/// <summary>
/// Binary search <see cref="SourceManager.Locate"/> tests and UTF-16 incremental edit fuzzing.
/// </summary>
public sealed class PerformanceAndFuzzTests
{
    [Fact]
    public void SourceManager_Locate_binary_search_matches_expected_lines_across_10k_lines()
    {
        const int lineCount = 10_000;
        var sb = new StringBuilder(lineCount * 14);
        for (var i = 0; i < lineCount; i++)
            sb.Append("line ").Append(i).Append('\n');
        var text = sb.ToString();

        var sm = new SourceManager();
        var id = sm.AddVirtual("large.lyr", text);

        Assert.Equal(new LinePosition(1, 1), sm.Locate(id, 0));
        var midOffset = text.IndexOf("line 5432\n", StringComparison.Ordinal);
        Assert.Equal(new LinePosition(5433, 4), sm.Locate(id, midOffset + 3));
        Assert.Equal(new LinePosition(lineCount + 1, 1), sm.Locate(id, text.Length));
    }

    [Fact]
    public void Incremental_utf16_edit_fuzz_keeps_store_offsets_and_completion_sound()
    {
        var rng = new Random(1337);
        var store = new DocumentStore();
        var uri = "file:///fuzz/doc.lyr";
        var text = "fn main(): int {\n    let x = \"hi 😀\";\n    return 0;\n}\n";
        var doc = store.Set(uri, 1, text)!;

        string[] snippets = ["let a = 1;\n", "😀", "\r\n", "  ", "p.\nreturn 0;\n"];

        for (var step = 2; step <= 80; step++)
        {
            var start = rng.Next(0, text.Length + 1);
            var end = rng.Next(start, text.Length + 1);

            // Avoid splitting UTF-16 surrogate pairs in randomly picked offsets when slicing string
            if (start > 0 && start < text.Length && char.IsLowSurrogate(text[start])) start--;
            if (end > start && end < text.Length && char.IsLowSurrogate(text[end])) end++;

            var insert = snippets[rng.Next(snippets.Length)];
            text = string.Concat(text.AsSpan(0, start), insert, text.AsSpan(end));
            doc = store.Set(uri, step, text)!;

            var sm = new SourceManager();
            var fid = sm.AddVirtual(doc.Path, doc.Text);
            var endPos = sm.Locate(fid, doc.Text.Length);
            var roundTripOffset = TextOffsets.ToOffset(doc.Text, new Position { Line = endPos.Line - 1, Character = endPos.Column - 1 });
            Assert.Equal(doc.Text.Length, roundTripOffset);
        }

        // Verify CompletionProvider never crashes or leaks its marker on fuzzed text
        var res = CompletionProvider.At(doc.Path, doc.Text, doc.Text.Length, Single.Options(doc.Path));
        if (res is not null)
            Assert.DoesNotContain(res, item => item.Label.Contains("__lyric_completion__", StringComparison.Ordinal));
    }
}
