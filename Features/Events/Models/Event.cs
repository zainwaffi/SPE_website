namespace SPE_website.Features.Events.Models;

public class Event
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string Location { get; set; } = string.Empty;
    public bool IsUpcoming { get; set; }
    public EventCategory Category { get; set; } = EventCategory.Other;
    public string? InstagramEmbedUrl { get; set; }
    public string? ImageUrl { get; set; }
    public string? GoogleCalendarEventId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<EventRating> Ratings { get; set; } = [];
    public ICollection<EventRegistration> Registrations { get; set; } = [];
}
