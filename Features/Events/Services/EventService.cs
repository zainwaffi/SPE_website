using Microsoft.EntityFrameworkCore;
using SPE_website.Data;
using SPE_website.Features.Events.Models;

namespace SPE_website.Features.Events.Services;

public class EventService(AppDbContext db)
{
    public Task<List<Event>> GetUpcomingAsync() =>
        db.Events
          .Where(e => e.IsUpcoming && e.Date >= DateTime.UtcNow)
          .OrderBy(e => e.Date)
          .ToListAsync();

    public Task<List<Event>> GetPastAsync() =>
        db.Events
          .Where(e => !e.IsUpcoming || e.Date < DateTime.UtcNow)
          .Include(e => e.Ratings)
          .OrderByDescending(e => e.Date)
          .ToListAsync();

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

    public async Task DeleteAsync(int id)
    {
        var evt = await db.Events.FindAsync(id);
        if (evt is not null)
        {
            db.Events.Remove(evt);
            await db.SaveChangesAsync();
        }
    }

    public async Task AddRatingAsync(int eventId, int stars, string? comment)
    {
        db.EventRatings.Add(new EventRating { EventId = eventId, Stars = stars, Comment = comment });
        await db.SaveChangesAsync();
    }
}
