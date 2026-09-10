using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SPE_website.Data;
using SPE_website.Data.Models;

namespace SPE_website.Features.Authentication.Services;

/// <summary>
/// Handles the second login path: an AUSA card/student number checked against the
/// membership import (see "Uploading Members/" at the repo root) instead of OpenWater.
/// A matching, active, not-yet-expired row gets an <see cref="ApplicationUser"/>
/// found-or-created by <see cref="ApplicationUser.CardNumber"/> and is signed in directly —
/// there is no local password here either, same as <see cref="OpenWaterAuthService"/>.
/// </summary>
public class MemberNumberAuthService(
    IDbContextFactory<AppDbContext> dbContextFactory,
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager)
{
    /// <summary>A membership older than this is treated as expired, regardless of <see cref="Member.IsActive"/>.</summary>
    private static readonly TimeSpan MembershipLifetime = TimeSpan.FromDays(365);

    public async Task<(bool Succeeded, bool ShowJoinSpeOption, string? Error)> LoginWithCardNumberAsync(string cardNumber)
    {
        var normalizedCardNumber = cardNumber.Trim();

        await using var db = await dbContextFactory.CreateDbContextAsync();
        var member = await db.Members.SingleOrDefaultAsync(m => m.CardNumber == normalizedCardNumber);

        if (member is null)
            return (false, true, "No membership record was found for this student/card number.");

        if (!member.IsActive)
            return (false, false, "This membership is no longer active.");

        if (member.PurchasedAt < DateTime.UtcNow - MembershipLifetime)
            return (false, false, "This membership expired more than a year ago — please renew before signing in.");

        var user = await userManager.Users.SingleOrDefaultAsync(u => u.CardNumber == normalizedCardNumber);
        var isNewUser = user is null;
        user ??= new ApplicationUser
        {
            UserName = $"member-{normalizedCardNumber}",
            CardNumber = normalizedCardNumber,
        };
        user.FullName = member.FullName;

        var saveResult = isNewUser ? await userManager.CreateAsync(user) : await userManager.UpdateAsync(user);
        if (!saveResult.Succeeded)
            return (false, false, string.Join(" ", saveResult.Errors.Select(e => e.Description)));

        if (isNewUser)
        {
            var role = member.MembershipType == "Committee" ? "CommitteeMember" : "Member";
            var roleResult = await RoleSync.AddMissingRolesAsync(userManager, user, role);
            if (!roleResult.Succeeded)
                return (false, false, string.Join(" ", roleResult.Errors.Select(e => e.Description)));
        }

        await signInManager.SignInAsync(user, isPersistent: false);
        return (true, false, null);
    }
}
