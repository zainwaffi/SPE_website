using Microsoft.EntityFrameworkCore;
using SPE_website.Data;
using SPE_website.Features.Events.Models;

namespace SPE_website.Features.Events.Services;

/// <summary>
/// CRUD and query operations for chapter <see cref="Event"/> records and their ratings.
/// "Upcoming" vs "past" is always computed live from <see cref="Event.Date"/> against
/// <see cref="DateTime.UtcNow"/> — the <see cref="Event.IsUpcoming"/> flag is not used
/// for filtering, only for informational display.
/// </summary>
public class EventService(AppDbContext db)
{
    /// <summary>Events scheduled at or after now, soonest first.</summary>
    public Task<List<Event>> GetUpcomingAsync() =>
        db.Events
          .Where(e => e.Date >= DateTime.UtcNow)
          .OrderBy(e => e.Date)
          .ToListAsync();

    /// <summary>Events that already happened, most recent first, with ratings eager-loaded for the review UI.</summary>
    public Task<List<Event>> GetPastAsync() =>
        db.Events
          .Where(e => e.Date < DateTime.UtcNow)
          .Include(e => e.Ratings)
          .OrderByDescending(e => e.Date)
          .ToListAsync();

    /// <summary>Fetches a single event with its ratings, or null if it doesn't exist.</summary>
    public Task<Event?> GetByIdAsync(int id) =>
        db.Events.Include(e => e.Ratings).FirstOrDefaultAsync(e => e.Id == id);

    public async Task<Event> CreateAsync(Event evt)
    {
        db.Events.Add(evt);
        await db.SaveChangesAsync();
        return evt;
    }

    public async Task UpdateAsync(Event evt)
    {
        db.Events.Update(evt);
        await db.SaveChangesAsync();
    }

    /// <summary>No-ops silently if the event no longer exists (idempotent delete).</summary>
    public async Task DeleteAsync(int id)
    {
        var evt = await db.Events.FindAsync(id);
        if (evt is null) return;

        db.Events.Remove(evt);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Adds an anonymous star rating (1-5) with optional comment for a past event.
    /// No user/duplicate check is performed — ratings are anonymous by design.
    /// </summary>
    public async Task AddRatingAsync(int eventId, int stars, string? comment)
    {
        db.EventRatings.Add(new EventRating { EventId = eventId, Stars = stars, Comment = comment });
        await db.SaveChangesAsync();
    }
}
