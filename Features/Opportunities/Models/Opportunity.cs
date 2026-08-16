namespace SPE_website.Features.Opportunities.Models;

public class Opportunity
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Markdown body of the posting — headings, bullet points and links all render.
    /// Shown in full on the detail page and as a plain-text excerpt in the list.
    /// Rendered through the shared Markdown component, which escapes any raw HTML.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    public string? ExternalUrl { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
