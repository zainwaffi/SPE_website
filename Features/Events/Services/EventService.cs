using Microsoft.EntityFrameworkCore;
using SPE_website.Data;
using SPE_website.Features.Events.Models;
using SPE_website.Shared;

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
public class EventService(IDbContextFactory<AppDbContext> dbFactory)
{
    /// <summary>
    /// Both event lists in one round trip, upcoming soonest-first and past most-recent-first.
    /// The events page renders them together, so issuing two queries just doubled the
    /// latency of every load and every reload after an edit.
    /// </summary>
    public async Task<(List<Event> Upcoming, List<Event> Past)> GetUpcomingAndPastAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        // UK wall-clock, matching how event times are stored (see UkTime).
        var now = UkTime.Now;
        var all = await db.Events.AsNoTracking()
                          .Include(e => e.Ratings)
                          .OrderBy(e => e.Date)
                          .ToListAsync();

        var upcoming = all.Where(e => e.Date >= now).ToList();
        var past = all.Where(e => e.Date < now).OrderByDescending(e => e.Date).ToList();

        return (upcoming, past);
    }

    public async Task<Event> CreateAsync(Event evt)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        db.Events.Add(evt);
        await db.SaveChangesAsync();
        return evt;
    }

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
                           r.User != null ? r.User.Email : null,
                           r.RegisteredAt,
                           r.Attended,
                           r.CheckedInAt))
                       .ToListAsync();
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

/// <summary>One row of the attendees checklist.</summary>
public sealed record Attendee(
    int RegistrationId,
    string FullName,
    string University,
    string? Email,
    DateTime RegisteredAt,
    bool Attended,
    DateTime? CheckedInAt);
