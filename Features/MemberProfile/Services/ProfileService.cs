using Microsoft.EntityFrameworkCore;
using SPE_website.Data;
using SPE_website.Data.Models;

namespace SPE_website.Features.MemberProfile.Services;

/// <summary>Read/update operations for a member's own profile data.</summary>
public class ProfileService(AppDbContext db)
{
    /// <summary>Fetches a user with their assigned tasks eager-loaded, for the profile dashboard.</summary>
    public Task<ApplicationUser?> GetByIdAsync(string userId) =>
        db.Users.Include(u => u.AssignedTasks).FirstOrDefaultAsync(u => u.Id == userId);

    public async Task UpdateProfileAsync(string userId, string fullName)
    {
        var user = await db.Users.FindAsync(userId);
        if (user is null) return;
        user.FullName = fullName;
        await db.SaveChangesAsync();
    }
}
