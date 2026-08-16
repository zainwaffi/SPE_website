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
                       .FirstOrDefaultAsync(u => u.Id == userId);
    }
}
