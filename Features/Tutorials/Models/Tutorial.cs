namespace SPE_website.Features.Tutorials.Models;

public class Tutorial
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string YoutubeEmbedUrl { get; set; } = string.Empty;
    public string CategoryRole { get; set; } = "Member";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
