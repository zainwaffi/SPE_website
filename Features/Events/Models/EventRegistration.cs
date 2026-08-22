using SPE_website.Data.Models;

namespace SPE_website.Features.Events.Models;

/// <summary>
/// A member signing up to attend an <see cref="Event"/>.
///
/// <see cref="FullName"/>, <see cref="University"/> and <see cref="EventName"/> are deliberately
/// snapshotted at sign-up rather than always read through the navigation properties: the
/// attendance export is a historical record, and it should still say who attended under the name
/// and university they had at the time, even after they update their profile, transfer, or have
/// their account deleted.
/// </summary>
public class EventRegistration
{
    public int Id { get; set; }

    public int EventId { get; set; }
    public Event? Event { get; set; }

    /// <summary>Null once the member's account has been deleted; the snapshot fields survive.</summary>
    public string? UserId { get; set; }
    public ApplicationUser? User { get; set; }

    public string FullName { get; set; } = string.Empty;

    /// <summary>
    /// Where they are from — their university for a member, or their company for a guest who
    /// signed up through a shared link. One field rather than two because it is one idea, and
    /// the attendance export has a single column for it.
    /// </summary>
    public string University { get; set; } = string.Empty;

    public string EventName { get; set; } = string.Empty;

    /// <summary>
    /// Contact address for a guest who signed up through a shared link and has no account to
    /// read one from. Null for ordinary member sign-ups: their address lives on the Identity
    /// record and is re-synced at every login, so copying it here would only let the two disagree.
    /// </summary>
    public string? Email { get; set; }

    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Whether they actually turned up, ticked off by a committee member on the attendees
    /// checklist. Distinct from simply having a registration row: signing up is the member's
    /// own intent, this is the committee confirming it, so a no-show stays visible as
    /// signed-up-but-not-attended rather than disappearing.
    /// </summary>
    public bool Attended { get; set; }

    /// <summary>When they were ticked off. Null while <see cref="Attended"/> is false.</summary>
    public DateTime? CheckedInAt { get; set; }
}
