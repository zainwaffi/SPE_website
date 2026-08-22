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

    /// <summary>
    /// Unguessable key for this event's guest sign-up link (<c>/events/register/{token}</c>),
    /// which the committee hands out to people from outside the chapter.
    ///
    /// Null until the committee first asks for the link: an event becomes reachable that way
    /// only once someone deliberately shares it, and an event nobody shared has no anonymous
    /// route in at all. Regenerating the token retires every copy of the old link already out
    /// there, which is the only way to withdraw one once it has been sent.
    /// </summary>
    public Guid? PublicRegistrationToken { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<EventRating> Ratings { get; set; } = [];
    public ICollection<EventRegistration> Registrations { get; set; } = [];
}
