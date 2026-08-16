using Markdig;
using Markdig.Renderers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace SPE_website.Shared;

/// <summary>
/// Renders committee-authored markdown for tutorial articles and opportunity postings.
///
/// The set of supported syntax is documented for authors in README.md — keep the two in step.
///
/// SECURITY, three separate holes are closed here:
///
/// 1. <c>DisableHtml()</c> — raw HTML in the markdown is escaped and shown as text rather
///    than executed.
/// 2. The pipeline is assembled extension by extension rather than via
///    <c>UseAdvancedExtensions()</c>, because that bundle includes generic attributes, which
///    lets an author attach arbitrary attributes such as <c>onclick</c> to an element and
///    would reopen exactly the hole <c>DisableHtml()</c> closes.
/// 3. Link URLs are scheme-checked. <c>DisableHtml()</c> does NOT cover this: a plain
///    markdown link <c>[text](javascript:...)</c> is valid markdown and Markdig will happily
///    emit it as a working href.
///
/// Authors are trusted committee members, but any of these would let a CommitteeMember plant
/// script in an article and capture a Team Leader's session — privilege escalation, not just
/// defacement.
/// </summary>
public static class MarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .DisableHtml()
        .UseAutoLinks()        // bare URLs become links
        .UseEmphasisExtras()   // ~~strikethrough~~, ==marked==, super^script^, sub~script~
        .UsePipeTables()       // | a | b | tables
        .UseListExtras()       // a. and i. ordered lists
        .Build();

    /// <summary>Schemes a link or image is allowed to use. Everything else is neutralised.</summary>
    private static readonly HashSet<string> AllowedSchemes =
        new(StringComparer.OrdinalIgnoreCase) { "http", "https", "mailto", "tel" };

    public static string ToHtml(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return "";

        // Parse first so link URLs can be inspected before they reach the HTML writer.
        var document = Markdown.Parse(markdown, Pipeline);

        foreach (var link in document.Descendants<LinkInline>())
        {
            if (!IsSafeUrl(link.Url)) link.Url = null;
        }

        foreach (var autolink in document.Descendants<AutolinkInline>())
        {
            if (!IsSafeUrl(autolink.Url)) autolink.Url = "";
        }

        using var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer);
        Pipeline.Setup(renderer);
        renderer.Render(document);
        writer.Flush();

        return writer.ToString();
    }

    /// <summary>
    /// True for relative URLs and for the handful of schemes that cannot execute script.
    ///
    /// Whitespace and control characters are stripped before the scheme is read, because
    /// browsers ignore them when parsing one — <c>"java\tscript:alert(1)"</c> and
    /// <c>" javascript:alert(1)"</c> both run, and both would slip past a naive prefix check.
    /// </summary>
    private static bool IsSafeUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return true;

        var cleaned = new string([.. url.Where(c => !char.IsWhiteSpace(c) && !char.IsControl(c))]);

        var colon = cleaned.IndexOf(':');
        if (colon < 0) return true;   // relative link — no scheme to abuse

        // A colon appearing after a path or fragment separator is part of the path, not a
        // scheme: "/a:b" and "#a:b" are relative and safe.
        var separator = cleaned.IndexOfAny(['/', '#', '?']);
        if (separator >= 0 && separator < colon) return true;

        return AllowedSchemes.Contains(cleaned[..colon]);
    }
}
