using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SPE_website.Data;
using SPE_website.Data.Models;

namespace SPE_website.Features.MemberProfile.Services;

public class ProfileService(AppDbContext db, UserManager<ApplicationUser> userManager)
{
    public Task<ApplicationUser?> GetByIdAsync(string userId) =>
        db.Users.Include(u => u.AssignedTasks).FirstOrDefaultAsync(u => u.Id == userId);

    public async Task UpdateProfileAsync(string userId, string fullName, string? profilePictureUrl)
    {
        var user = await db.Users.FindAsync(userId);
        if (user is null) return;
        user.FullName = fullName;
        user.ProfilePictureUrl = profilePictureUrl;
        await db.SaveChangesAsync();
    }
}
