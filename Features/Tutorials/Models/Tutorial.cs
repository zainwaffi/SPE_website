namespace SPE_website.Features.Tutorials.Models;

/// <summary>How a tutorial delivers its content.</summary>
public enum TutorialFormat
{
    /// <summary>An embedded YouTube video, played inline on the tutorials page.</summary>
    Video,

    /// <summary>A written article in markdown, opened on its own page.</summary>
    Article
}

public class Tutorial
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;

    /// <summary>Short summary shown on the tutorial card, for both formats.</summary>
    public string Description { get; set; } = string.Empty;

    public TutorialFormat Format { get; set; } = TutorialFormat.Video;

    /// <summary>Set when <see cref="Format"/> is <see cref="TutorialFormat.Video"/>.</summary>
    public string YoutubeEmbedUrl { get; set; } = string.Empty;

    /// <summary>
    /// Markdown body, set when <see cref="Format"/> is <see cref="TutorialFormat.Article"/>.
    /// Rendered through the shared Markdown component, which escapes any raw HTML.
    /// </summary>
    public string ArticleContent { get; set; } = string.Empty;

    public string CategoryRole { get; set; } = "Member";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
