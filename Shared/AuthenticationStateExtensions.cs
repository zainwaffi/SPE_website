using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace SPE_website.Shared;

/// <summary>
/// Helpers for what pages consistently need from the cascading authentication state: the
/// signed-in user's Identity id, and whether they hold any of a set of roles. Pulling the id claim by
/// hand was repeated in four places (twice in the same file), so it lives here instead.
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

    /// <summary>
    /// Whether the signed-in user holds any of the given Identity roles, for the cases a page has
    /// to vary behaviour rather than markup — an <c>&lt;AuthorizeView&gt;</c> can only do the
    /// latter, and an event handler that must not fire for the wrong role needs the answer in C#.
    /// Returns <c>false</c> when nobody is signed in or no state was supplied.
    ///
    /// Takes a list so a call reads like the <c>Roles="A,B"</c> it is mirroring — the pages
    /// guarding a control with <c>&lt;AuthorizeView Roles="CommitteeMember,TeamLeader"&gt;</c>
    /// need the same either/or test in code, and two chained single-role calls said it worse.
    /// </summary>
    public static async Task<bool> IsInAnyRoleAsync(this Task<AuthenticationState>? authStateTask, params string[] roles)
    {
        if (authStateTask is null) return false;
        var authState = await authStateTask;
        return roles.Any(authState.User.IsInRole);
    }
}
