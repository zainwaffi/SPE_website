using System.Net;
using Microsoft.EntityFrameworkCore;
using SPE_website.Data;
using SPE_website.Features.Events.Models;
using SPE_website.Shared;
using SPE_website.Shared.Models;
using SPE_website.Shared.Services;

namespace SPE_website.Features.Events.Services;

/// <summary>
/// CRUD and query operations for chapter <see cref="Event"/> records and their ratings.
/// "Upcoming" vs "past" is always computed live from <see cref="Event.Date"/> against the
/// current UK wall-clock time (see <see cref="UkTime"/>) — the <see cref="Event.IsUpcoming"/>
/// flag is not used for filtering, only for informational display.
///
/// Takes a context factory rather than a scoped context: a Blazor Server circuit outlives
/// any single operation, and sharing one context across overlapping renders throws.
/// </summary>
public class EventService(
    IDbContextFactory<AppDbContext> dbFactory,
    EmailService emailService,
    ILogger<EventService> logger)
{
    /// <summary>
    /// Grace period after an event's start time before it counts as past. Only the start time
    /// is stored, so without this an event dropped out of "Upcoming" the moment it began —
    /// while it was still running and members were still arriving.
    /// </summary>
    private static readonly TimeSpan PastEventGrace = TimeSpan.FromHours(2);

    /// <summary>
    /// Both event lists in one round trip, upcoming soonest-first and past most-recent-first.
    /// The events page renders them together, so issuing two queries just doubled the
    /// latency of every load and every reload after an edit.
    ///
    /// An event stays in <c>Upcoming</c> until <see cref="PastEventGrace"/> has elapsed past
    /// its start time.
    /// </summary>
    public async Task<(List<Event> Upcoming, List<Event> Past)> GetUpcomingAndPastAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        // UK wall-clock, matching how event times are stored (see UkTime).
        var cutoff = UkTime.Now - PastEventGrace;
        var all = await db.Events.AsNoTracking()
                          .Include(e => e.Ratings)
                          .OrderBy(e => e.Date)
                          .ToListAsync();

        var upcoming = all.Where(e => e.Date >= cutoff).ToList();
        var past = all.Where(e => e.Date < cutoff).OrderByDescending(e => e.Date).ToList();

        return (upcoming, past);
    }

    public async Task<Event> CreateAsync(Event evt)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        db.Events.Add(evt);
        await db.SaveChangesAsync();
        return evt;
    }

    /// <summary>
    /// Rewrites the editable fields of an existing event, for the committee's Update action, and
    /// tells anyone signed up to attend what changed.
    ///
    /// Takes the fields one by one rather than a whole <see cref="Event"/> so an edit cannot
    /// touch what the form does not offer: <see cref="Event.Ratings"/> and
    /// <see cref="Event.Registrations"/> stay attached, and <see cref="Event.CreatedAt"/> keeps
    /// its original value — handing in a detached instance would blank all three.
    ///
    /// The edit is always saved. The return value reports the attendee notification, and is
    /// <c>null</c> when none was due at all — see <see cref="NotifyAttendeesAsync"/> for when
    /// that is.
    /// </summary>
    public async Task<AttendeeNotification?> UpdateAsync(
        int id, string title, string location, string? instagramEmbedUrl, EventCategory category, DateTime date)
    {
        Event evt;
        (string Title, DateTime Date, string Location) previous;

        // Scoped to a block, not the method: notifying is an SMTP conversation that can run for
        // seconds, and there is no reason to hold a pooled database connection open across it.
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var found = await db.Events.FindAsync(id);
            if (found is null) return null;

            // Captured before the overwrite so the notification can say what actually moved,
            // rather than just that "something changed".
            previous = (found.Title, found.Date, found.Location);

            found.Title = title;
            found.Location = location;
            found.InstagramEmbedUrl = instagramEmbedUrl;
            found.Category = category;
            found.Date = date;

            // Upcoming vs past is computed live from Date on every read (see
            // GetUpcomingAndPastAsync), so this flag is display-only — but leaving it stale after
            // a date edit would have the card contradict the section it is sitting in.
            found.IsUpcoming = date >= UkTime.Now - PastEventGrace;

            await db.SaveChangesAsync();
            evt = found;
        }

        return await NotifyAttendeesAsync(evt, previous);
    }

    /// <summary>
    /// Emails everyone signed up for an event that its details have changed, listing what moved.
    ///
    /// Returns <c>null</c> — sending nothing — in three cases:
    /// <list type="bullet">
    /// <item>Nothing attendance-relevant changed. Only the title, date and location are compared:
    /// a member decides whether to turn up on what it is, when, and where. Re-categorising an
    /// event or adding its Instagram post afterwards changes neither, and mailing forty people
    /// about it would train them to ignore the mail that does matter.</item>
    /// <item>The event is over. Announcing a correction to something that already happened is
    /// noise — though a past event moved to a future date is a reschedule, and does go out, which
    /// is why both the old and the new date are checked.</item>
    /// <item>Nobody is left to tell: no sign-ups, or nobody on the list has a usable address —
    /// every member has turned notification emails off or deleted their account, and no
    /// hand-added guest left one.</item>
    /// </list>
    /// </summary>
    private async Task<AttendeeNotification?> NotifyAttendeesAsync(
        Event evt, (string Title, DateTime Date, string Location) previous)
    {
        var changes = DescribeChanges(previous, evt);
        if (changes.Count == 0) return null;

        var cutoff = UkTime.Now - PastEventGrace;
        if (evt.Date < cutoff && previous.Date < cutoff) return null;

        // Two kinds of attendee are reachable, and they are reached differently:
        //
        //  - A member, through their live account. Preferred over the sign-up snapshot, which
        //    exists to keep the attendance record historically accurate — the opposite of what is
        //    wanted for contacting someone today — and skipped if they have turned notification
        //    emails off in their profile.
        //  - A guest who signed up through a shared link, through the address stored on the
        //    registration. They have no account and so no preference to honour; they gave that
        //    address when they took their place for exactly this.
        //
        // Anyone else — a deleted account, or a guest row left without an address — has nowhere
        // to be written to and drops out here.
        List<AttendeeContact> attendees;
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            attendees = await db.EventRegistrations.AsNoTracking()
                                .Where(r => r.EventId == evt.Id
                                         && ((r.User != null && r.User.Email != null && r.User.EmailNotificationsEnabled)
                                          || (r.User == null && r.Email != null)))
                                .Select(r => new AttendeeContact(
                                    r.User != null && r.User.Email != null ? r.User.Email : (r.Email ?? string.Empty),
                                    r.User != null ? r.User.FullName : r.FullName))
                                .ToListAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not look up attendees to notify about event {EventId}", evt.Id);
            return new AttendeeNotification(0, EmailResult.Failure(ex.Message));
        }

        if (attendees.Count == 0) return null;

        // #UpdateLink — wording of the event-updated email sent to attendees.
        var subject = $"Event Updated: {evt.Title} — SPE Chapter";
        var changeList = string.Join("", changes.Select(c => $"<li>{c}</li>"));

        var recipients = attendees
            .Select(a => new MailRecipient(a.Email, a.Name, $"""
                <p>Dear {Encode(a.Name)},</p>
                <p>An event you signed up for has been updated: <strong>{Encode(evt.Title)}</strong></p>
                <p>What changed:</p>
                <ul>{changeList}</ul>
                <p>The event is now on <strong>{evt.Date:dddd, MMMM d, yyyy}</strong> at
                   <strong>{evt.Date:h:mm tt}</strong>.</p>
                {LocationParagraph(evt.Location)}
                <p>If you can no longer make it, you can withdraw your sign-up on the Events page
                   of the chapter website.</p>
                <p>— SPE University of Aberdeen Chapter</p>
                """))
            .ToList();

        var result = await emailService.SendManyAsync(recipients, subject);
        return new AttendeeNotification(recipients.Count, result);
    }

    /// <summary>
    /// The attendance-relevant differences between an event's old and new details, as finished
    /// sentences for the notification email. Empty when nothing a member would act on has moved.
    /// </summary>
    private static List<string> DescribeChanges((string Title, DateTime Date, string Location) before, Event after)
    {
        var changes = new List<string>();

        if (!string.Equals(before.Title, after.Title, StringComparison.Ordinal))
            changes.Add($"The event is now called <strong>{Encode(after.Title)}</strong> (was &ldquo;{Encode(before.Title)}&rdquo;).");

        if (before.Date != after.Date)
        {
            changes.Add($"""
                Date and time: <strong>{after.Date:dddd, MMMM d, yyyy 'at' h:mm tt}</strong>
                (was {before.Date:dddd, MMMM d, yyyy 'at' h:mm tt}).
                """);
        }

        if (!string.Equals(before.Location, after.Location, StringComparison.Ordinal))
        {
            changes.Add(string.IsNullOrWhiteSpace(after.Location)
                ? "The location link has been removed — check the Events page for details."
                : "The location has changed; the new one is linked below.");
        }

        return changes;
    }

    /// <summary>The "where" line of the notification, omitted entirely when no location is set.</summary>
    private static string LocationParagraph(string? location) =>
        string.IsNullOrWhiteSpace(location)
            ? string.Empty
            : $"""<p>Location: <a href="{Encode(location)}">View on the map</a></p>""";

    /// <summary>Committee-supplied text (titles, links) goes into an HTML mail body, so it must be encoded.</summary>
    private static string Encode(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    /// <summary>Where one attendee's notification goes, read from their live account.</summary>
    private sealed record AttendeeContact(string Email, string Name);

    /// <summary>No-ops silently if the event no longer exists (idempotent delete).</summary>
    public async Task DeleteAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var evt = await db.Events.FindAsync(id);
        if (evt is null) return;

        db.Events.Remove(evt);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Adds a star rating (1-5) with optional comment for a past event.
    /// <paramref name="userId"/> is recorded so the committee's attendance export can attribute
    /// feedback; it is never shown in the public events UI. Stars are clamped to 1-5 so a
    /// malformed request can't skew the average.
    /// </summary>
    public async Task AddRatingAsync(int eventId, string userId, int stars, string? comment)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        db.EventRatings.Add(new EventRating
        {
            EventId = eventId,
            UserId = userId,
            Stars = Math.Clamp(stars, 1, 5),
            Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim()
        });

        await db.SaveChangesAsync();
    }

    /// <summary>The member's current name and university, used to prefill the sign-up form.</summary>
    public async Task<(string FullName, string University)> GetMemberDetailsAsync(string userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var member = await db.Users.AsNoTracking()
                             .Where(u => u.Id == userId)
                             .Select(u => new { u.FullName, u.OpenWaterOrganization })
                             .FirstOrDefaultAsync();

        return (member?.FullName ?? "", member?.OpenWaterOrganization ?? "");
    }

    /// <summary>
    /// Attendee count per event, as one grouped query. Kept separate from the event list so the
    /// public page never has to materialise every registration row just to print a number.
    /// </summary>
    public async Task<Dictionary<int, int>> GetRegistrationCountsAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.EventRegistrations.AsNoTracking()
                       .GroupBy(r => r.EventId)
                       .Select(g => new { EventId = g.Key, Count = g.Count() })
                       .ToDictionaryAsync(x => x.EventId, x => x.Count);
    }

    /// <summary>Event ids this member has already signed up for, so the UI can show sign-up state.</summary>
    public async Task<HashSet<int>> GetRegisteredEventIdsAsync(string userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var ids = await db.EventRegistrations.AsNoTracking()
                          .Where(r => r.UserId == userId)
                          .Select(r => r.EventId)
                          .ToListAsync();
        return [.. ids];
    }

    /// <summary>
    /// Signs a member up to attend an event, snapshotting the name and university they gave.
    /// Returns false if the event no longer exists or they were already signed up — the unique
    /// index on (EventId, UserId) is the real guard, so a double-click can't double-register.
    /// </summary>
    public async Task<bool> RegisterAsync(int eventId, string userId, string fullName, string university)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var eventName = await db.Events.AsNoTracking()
                                .Where(e => e.Id == eventId)
                                .Select(e => e.Title)
                                .FirstOrDefaultAsync();
        if (eventName is null) return false;

        if (await db.EventRegistrations.AnyAsync(r => r.EventId == eventId && r.UserId == userId))
        {
            return false;
        }

        db.EventRegistrations.Add(new EventRegistration
        {
            EventId = eventId,
            UserId = userId,
            EventName = eventName,
            FullName = fullName.Trim(),
            University = university.Trim()
        });

        try
        {
            await db.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateException)
        {
            // Lost a race against the unique index — they are signed up either way.
            return false;
        }
    }

    /// <summary>Withdraws a member's sign-up. No-ops if they weren't signed up.</summary>
    public async Task CancelRegistrationAsync(int eventId, string userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.EventRegistrations
                .Where(r => r.EventId == eventId && r.UserId == userId)
                .ExecuteDeleteAsync();
    }

    /// <summary>The event itself, for the attendees page header. Null if it no longer exists.</summary>
    public async Task<Event?> GetByIdAsync(int eventId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Events.AsNoTracking().FirstOrDefaultAsync(e => e.Id == eventId);
    }

    /// <summary>
    /// Everyone signed up for an event, alphabetically, for the check-in checklist.
    /// Email comes from the live account rather than the snapshot so the committee can still
    /// contact them; it is null once the account is deleted.
    /// </summary>
    public async Task<List<Attendee>> GetAttendeesAsync(int eventId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.EventRegistrations.AsNoTracking()
                       .Where(r => r.EventId == eventId)
                       .OrderBy(r => r.FullName)
                       .Select(r => new Attendee(
                           r.Id,
                           r.FullName,
                           r.University,
                           // The account is authoritative while it exists — it is re-synced at
                           // every login. The stored address is the fallback for a guest who
                           // signed up through a link, and all that is left once an account is
                           // deleted.
                           r.User != null ? r.User.Email : r.Email,
                           r.RegisteredAt,
                           r.Attended,
                           r.CheckedInAt))
                       .ToListAsync();
    }

    /* ---------- Guest sign-up link ---------- */

    /// <summary>
    /// This event's guest sign-up token, minting one on first use. The committee shares the
    /// resulting link with people outside the chapter, who sign themselves up through it
    /// instead of being typed onto the list by hand.
    ///
    /// Created lazily rather than at event creation so an event has no anonymous way in until
    /// someone deliberately asks for the link. Returns null if the event no longer exists.
    /// </summary>
    public async Task<Guid?> GetOrCreateShareTokenAsync(int eventId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var evt = await db.Events.FindAsync(eventId);
        if (evt is null) return null;

        if (evt.PublicRegistrationToken is { } existing) return existing;

        evt.PublicRegistrationToken = Guid.NewGuid();
        await db.SaveChangesAsync();
        return evt.PublicRegistrationToken;
    }

    /// <summary>
    /// Replaces the guest sign-up token, breaking every copy of the old link. The only way to
    /// withdraw a link once it has been forwarded further than intended — sign-ups already made
    /// through the old one are untouched, since they are attendees now, not links.
    ///
    /// Returns the new token, or null if the event no longer exists.
    /// </summary>
    public async Task<Guid?> RegenerateShareTokenAsync(int eventId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var evt = await db.Events.FindAsync(eventId);
        if (evt is null) return null;

        evt.PublicRegistrationToken = Guid.NewGuid();
        await db.SaveChangesAsync();
        return evt.PublicRegistrationToken;
    }

    /// <summary>
    /// The event a guest sign-up link points at, for the anonymous registration page. Null when
    /// the token matches nothing — a mistyped link, or one retired by
    /// <see cref="RegenerateShareTokenAsync"/>.
    /// </summary>
    public async Task<Event?> GetByShareTokenAsync(Guid token)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Events.AsNoTracking()
                       .FirstOrDefaultAsync(e => e.PublicRegistrationToken == token);
    }

    /// <summary>
    /// Signs a guest up through a shared link. The token is the whole authorisation: holding it
    /// is what proves the committee invited them, so nothing here reads the signed-in user.
    ///
    /// The row is an ordinary registration with <see cref="EventRegistration.UserId"/> left null
    /// and the address stored on the registration itself, so it flows through the check-in list,
    /// the attendance export and the event-changed notification with no special case anywhere.
    /// </summary>
    public async Task<GuestSignUpResult> RegisterGuestAsync(
        int eventId, string fullName, string organisation, string email)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var evt = await db.Events.AsNoTracking()
                          .Where(e => e.Id == eventId)
                          .Select(e => new { e.Title, e.Date })
                          .FirstOrDefaultAsync();
        if (evt is null) return GuestSignUpResult.LinkNotValid;

        // Same grace period the events page uses to decide "upcoming", so a link stays usable
        // for latecomers right up to the point the event drops off the front of the site.
        if (evt.Date < UkTime.Now - PastEventGrace) return GuestSignUpResult.EventOver;

        var trimmedEmail = email.Trim();

        // Best-effort, not a constraint: the unique index is on (EventId, UserId), and Postgres
        // treats the null UserIds of guest rows as distinct, so nothing at the database level
        // stops two. This catches what actually happens — someone opening the link twice, or
        // being sent it twice — and a genuine race only leaves a duplicate on the door list,
        // which the committee can see and ignore.
        var already = await db.EventRegistrations
                              .AnyAsync(r => r.EventId == eventId
                                          && r.UserId == null
                                          && r.Email != null
                                          && r.Email.ToLower() == trimmedEmail.ToLower());
        if (already) return GuestSignUpResult.AlreadySignedUp;

        db.EventRegistrations.Add(new EventRegistration
        {
            EventId = eventId,
            UserId = null,
            EventName = evt.Title,
            FullName = fullName.Trim(),
            University = organisation.Trim(),
            Email = trimmedEmail
        });

        await db.SaveChangesAsync();
        return GuestSignUpResult.SignedUp;
    }

    /// <summary>
    /// Ticks one attendee on or off the checklist. Returns the check-in timestamp so the page
    /// can show it without re-reading the whole list.
    /// </summary>
    public async Task<DateTime?> SetAttendanceAsync(int registrationId, bool attended)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var checkedInAt = attended ? UkTime.Now : (DateTime?)null;

        await db.EventRegistrations
                .Where(r => r.Id == registrationId)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(r => r.Attended, attended)
                    .SetProperty(r => r.CheckedInAt, checkedInAt));

        return checkedInAt;
    }

    /// <summary>
    /// Ticks every attendee of an event on or off at once — for the common case where the
    /// whole signed-up list turned up, or for undoing a mistaken "mark all".
    /// </summary>
    public async Task<DateTime?> SetAllAttendanceAsync(int eventId, bool attended)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var checkedInAt = attended ? UkTime.Now : (DateTime?)null;

        await db.EventRegistrations
                .Where(r => r.EventId == eventId)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(r => r.Attended, attended)
                    .SetProperty(r => r.CheckedInAt, checkedInAt));

        return checkedInAt;
    }
}

/// <summary>
/// Outcome of telling an event's attendees that it changed. <see cref="Recipients"/> is how many
/// members the notification was addressed to, so the UI can say "12 attendees notified" rather
/// than just that mail went out.
/// </summary>
public sealed record AttendeeNotification(int Recipients, EmailResult Result);

/// <summary>Outcome of a guest signing themselves up through a shared link.</summary>
public enum GuestSignUpResult
{
    SignedUp,

    /// <summary>The link matches no event — mistyped, or retired by regenerating the token.</summary>
    LinkNotValid,

    /// <summary>The event has already happened, so there is nothing left to sign up for.</summary>
    EventOver,

    /// <summary>That address is already on this event's list.</summary>
    AlreadySignedUp
}

/// <summary>One row of the attendees checklist.</summary>
public sealed record Attendee(
    int RegistrationId,
    string FullName,
    string University,
    string? Email,
    DateTime RegisteredAt,
    bool Attended,
    DateTime? CheckedInAt);
