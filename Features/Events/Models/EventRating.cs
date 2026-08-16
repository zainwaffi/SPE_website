using SPE_website.Data.Models;

namespace SPE_website.Features.Events.Models;

public class EventRating
{
    public int Id { get; set; }
    public int EventId { get; set; }
    public Event? Event { get; set; }
    public int Stars { get; set; }
    public string? Comment { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Who left the review. Recorded so the committee's attendance export can attribute
    /// feedback, but never surfaced in the public events UI — members see only the star
    /// average and the comment text, with no name attached.
    ///
    /// Null for reviews left before attribution existed, and for members whose account has
    /// since been deleted. Those export as "Anonymous".
    /// </summary>
    public string? UserId { get; set; }
    public ApplicationUser? User { get; set; }
}
