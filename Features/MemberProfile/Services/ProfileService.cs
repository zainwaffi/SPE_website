using Microsoft.EntityFrameworkCore;
using SPE_website.Data;
using SPE_website.Data.Models;

namespace SPE_website.Features.MemberProfile.Services;

/// <summary>
/// Read operations for a member's own profile data.
/// Takes a context factory rather than a scoped context: a Blazor Server circuit outlives
/// any single operation, and sharing one context across overlapping renders throws.
/// </summary>
public class ProfileService(IDbContextFactory<AppDbContext> dbFactory)
{
    /// <summary>Fetches a user with their assigned tasks eager-loaded, for the profile dashboard.</summary>
    public async Task<ApplicationUser?> GetByIdAsync(string userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Users.AsNoTracking()
                       .Include(u => u.AssignedTasks)
                       .Include(u => u.Teams)
                       .FirstOrDefaultAsync(u => u.Id == userId);
    }

    /// <summary>
    /// Turns the member's notification emails on or off. Returns false if the account no longer
    /// exists, so the page can tell "saved" apart from "silently did nothing".
    /// </summary>
    public async Task<bool> SetEmailNotificationsAsync(string userId, bool enabled)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var updated = await db.Users
                              .Where(u => u.Id == userId)
                              .ExecuteUpdateAsync(set => set.SetProperty(u => u.EmailNotificationsEnabled, enabled));
        return updated > 0;
    }
}
