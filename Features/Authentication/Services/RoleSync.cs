using Microsoft.AspNetCore.Identity;
using SPE_website.Data.Models;

namespace SPE_website.Features.Authentication.Services;

/// <summary>
/// Shared by both login paths (OpenWater email, AUSA card number): grants whichever of the
/// given roles a user doesn't already hold, and never removes one. Role changes made by a
/// Team Leader through the Member Dashboard are a one-way ratchet from here — a later login
/// only ever adds roles, so it can't silently undo an upgrade.
/// </summary>
public static class RoleSync
{
    public static async Task<IdentityResult> AddMissingRolesAsync(
        UserManager<ApplicationUser> userManager, ApplicationUser user, params string[] roles)
    {
        var currentRoles = await userManager.GetRolesAsync(user);
        var missingRoles = roles.Where(r => !currentRoles.Contains(r, StringComparer.OrdinalIgnoreCase)).ToArray();
        return missingRoles.Length > 0
            ? await userManager.AddToRolesAsync(user, missingRoles)
            : IdentityResult.Success;
    }
}
