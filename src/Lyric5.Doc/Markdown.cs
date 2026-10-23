using System.Text;
using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Lyric5.Doc;

/// <summary>One heading of a page, for its contents and the navigation.</summary>
/// <param name="Anchor">The heading's id, without the leading <c>#</c>.</param>
public sealed record Heading(int Level, string Text, string Anchor);

/// <summary>A rendered Markdown document.</summary>
public sealed record RenderedMarkdown(string Html, IReadOnlyList<Heading> Headings);

/// <summary>
/// Markdown to HTML, with pipe tables and an id on every heading. A code block fenced as
/// <c>lyric</c> — its info string's first word — is highlighted by <see cref="Highlighter"/>;
/// every other block is written as escaped text.
/// </summary>
public static class Markdown
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseAutoIdentifiers()
        .Build();

    /// <summary>Renders <paramref name="markdown"/>.</summary>
    /// <param name="link">Rewrites each link's target; <c>null</c> leaves the targets as written.</param>
    /// <param name="dropTitle">Leaves the first level-1 heading out of the HTML, for a page form
    /// that writes the title itself; it is still in <see cref="RenderedMarkdown.Headings"/>.</param>
    public static RenderedMarkdown Render(string markdown, Func<string, string>? link = null, bool dropTitle = false)
    {
        var document = Markdig.Markdown.Parse(markdown, Pipeline);

        if (link is not null)
        {
            foreach (var inline in document.Descendants<LinkInline>())
                if (!string.IsNullOrEmpty(inline.Url))
                    inline.Url = link(inline.Url);
        }

        var headings = document.Descendants<HeadingBlock>()
            .Select(h => new Heading(h.Level, PlainText(h.Inline), h.GetAttributes().Id ?? ""))
            .ToArray();

        if (dropTitle && document.OfType<HeadingBlock>().FirstOrDefault(h => h.Level == 1) is { } title)
            document.Remove(title);

        var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer);
        Pipeline.Setup(renderer);
        renderer.ObjectRenderers.ReplaceOrAdd<CodeBlockRenderer>(new HighlightingCodeBlockRenderer());
        renderer.Render(document);
        writer.Flush();
        return new RenderedMarkdown(writer.ToString().ReplaceLineEndings("\n"), headings);
    }

    /// <summary>The text of an inline sequence without its markup: emphasis and code give their
    /// content, so a heading reads the same in the navigation as on the page.</summary>
    private static string PlainText(ContainerInline? inline)
    {
        if (inline is null) return "";
        var sb = new StringBuilder();
        foreach (var child in inline.Descendants())
            sb.Append(child switch
            {
                LiteralInline literal => literal.Content.ToString(),
                CodeInline code => code.Content,
                _ => "",
            });
        return sb.ToString();
    }

    /// <summary>The first word of a fence's info string: <c>lyric</c> in <c>```lyric,no_run</c>.</summary>
    internal static string FenceLanguage(string? info)
    {
        if (string.IsNullOrEmpty(info)) return "";
        var end = info.AsSpan().IndexOfAny(", \t");
        return end < 0 ? info : info[..end];
    }

    private sealed class HighlightingCodeBlockRenderer : HtmlObjectRenderer<CodeBlock>
    {
        protected override void Write(HtmlRenderer renderer, CodeBlock block)
        {
            var language = block is FencedCodeBlock fenced ? FenceLanguage(fenced.Info) : "";
            var lines = block.Lines.Lines;
            var text = new StringBuilder();
            for (var i = 0; i < block.Lines.Count; i++)
            {
                if (i > 0) text.Append('\n');
                text.Append(lines[i].Slice.AsSpan());
            }

            renderer.EnsureLine();
            if (language == "lyric")
                renderer.Write("<pre><code class=\"language-lyric\">").Write(Highlighter.Highlight(text.ToString()));
            else if (language.Length > 0)
                renderer.Write("<pre><code class=\"language-").Write(Html.Attr(language)).Write("\">").Write(Html.Text(text.ToString()));
            else
                renderer.Write("<pre><code>").Write(Html.Text(text.ToString()));
            renderer.WriteLine("</code></pre>");
        }
    }
}
