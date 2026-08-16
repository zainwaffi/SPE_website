using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace SPE_website.Shared;

/// <summary>
/// Helpers for the one thing pages consistently need from the cascading authentication
/// state: the signed-in user's Identity id. Pulling the claim by hand was repeated in
/// four places (twice in the same file), so it lives here instead.
/// </summary>
public static class AuthenticationStateExtensions
{
    /// <summary>
    /// The signed-in user's Identity id, or <c>null</c> when nobody is signed in or the
    /// cascading <see cref="AuthenticationState"/> has not been supplied.
    /// </summary>
    public static async Task<string?> GetUserIdAsync(this Task<AuthenticationState>? authStateTask)
    {
        if (authStateTask is null) return null;
        var authState = await authStateTask;
        return authState.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    }
}
