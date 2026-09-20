using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SPE_website.Data;
using SPE_website.Data.Models;
using SPE_website.Features.PresidentAdmin.Services;

namespace SPE_website.Features.MemberProfile.Services;

/// <summary>
/// Read operations for a member's own profile data.
/// Takes a context factory rather than a scoped context: a Blazor Server circuit outlives
/// any single operation, and sharing one context across overlapping renders throws.
/// </summary>
public class ProfileService(IDbContextFactory<AppDbContext> dbFactory, UserManager<ApplicationUser> userManager)
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

    /// <summary>
    /// Lets a member change their own contact email, validated and applied the same way as
    /// <see cref="AdminService.UpdateMemberDetailsAsync"/>. A member who signs in via OpenWater
    /// will have this overwritten again from their membership record on their next login.
    /// </summary>
    public async Task<IdentityResult> UpdateEmailAsync(string userId, string email)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null) return IdentityResult.Failed(new IdentityError { Description = "Account not found." });

        return await AdminService.SetEmailIfChangedAsync(userManager, user, email);
    }
}
