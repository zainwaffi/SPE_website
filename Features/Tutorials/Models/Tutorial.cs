using SPE_website.Data.Models;

namespace SPE_website.Features.Tutorials.Models;

public class Tutorial
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string YoutubeEmbedUrl { get; set; } = string.Empty;
    public CommitteeRole CategoryRole { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
