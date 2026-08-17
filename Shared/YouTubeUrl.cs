using Microsoft.AspNetCore.WebUtilities;

namespace SPE_website.Shared;

/// <summary>
/// Turns whatever YouTube URL someone pasted into the <c>/embed/</c> form an iframe needs.
///
/// Committee members copy links out of the browser address bar or the Share button, so what
/// arrives is a watch link, a youtu.be short link or a Shorts link — almost never the embed
/// URL, which is only reachable via "Embed" in the share dialog. Asking them for the embed
/// form was a reliable source of blank video cards.
///
/// Lives in Shared rather than in a feature slice because both Courses and Tutorials embed
/// video and must agree on what counts as a valid link.
/// </summary>
public static class YouTubeUrl
{
    /// <summary>
    /// The canonical embed URL for a pasted YouTube link, or <c>null</c> if it isn't one.
    /// Already-embed URLs pass through untouched, so links saved before this existed — and any
    /// hand-tuned ones carrying <c>?start=</c> — keep working.
    /// </summary>
    public static string? ToEmbedUrl(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;

        var trimmed = input.Trim();

        // People paste "youtube.com/watch?v=..." without the scheme; Uri needs one to parse
        // the link as absolute, and treats a scheme-less string as a relative path.
        if (!trimmed.Contains("://", StringComparison.Ordinal))
            trimmed = "https://" + trimmed;

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return null;

        // youtu.be/VIDEOID — the whole path is the id. Any ?t= start offset is in the query,
        // so it is dropped here rather than becoming part of the id.
        if (uri.Host.Contains("youtu.be", StringComparison.OrdinalIgnoreCase))
            return Embed(uri.AbsolutePath.Trim('/'));

        // Covers www., m. and music. subdomains.
        if (!uri.Host.Contains("youtube.com", StringComparison.OrdinalIgnoreCase)) return null;

        // Already an embed URL — leave it exactly as given.
        if (uri.AbsolutePath.Contains("/embed/", StringComparison.OrdinalIgnoreCase))
            return uri.ToString();

        // /shorts/VIDEOID and /live/VIDEOID carry the id in the path like youtu.be does.
        foreach (var prefix in new[] { "/shorts/", "/live/" })
        {
            if (uri.AbsolutePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return Embed(uri.AbsolutePath[prefix.Length..].Trim('/'));
        }

        // The ordinary watch link: /watch?v=VIDEOID, possibly with &list=, &t= and friends.
        if (QueryHelpers.ParseQuery(uri.Query).TryGetValue("v", out var videoId))
            return Embed(videoId.ToString());

        return null;
    }

    /// <summary>
    /// Builds the embed URL, rejecting an empty id. The id is path-segment-escaped: it lands in
    /// an iframe <c>src</c>, so a link with a slash or quote in that position must not be able
    /// to steer the resulting URL somewhere else.
    /// </summary>
    private static string? Embed(string videoId) =>
        string.IsNullOrWhiteSpace(videoId) ? null : $"https://www.youtube.com/embed/{Uri.EscapeDataString(videoId)}";
}
