namespace SPE_website.Shared.Models;

/// <summary>
/// The configured bootstrap Team Leader accounts, read once at startup from the
/// <c>SeededAdmins</c> section (see <see cref="SeededAdmin"/> for the user-secrets and
/// environment-variable forms) and registered as a singleton.
///
/// One shared instance because two places need the same answer, and used to hold two
/// hard-coded copies of it that a comment asked the next person to keep in step by hand:
/// startup seeding creates these accounts, and login lets them in even when the external
/// OpenWater directory has no record of them — which is what makes them a recovery route when
/// that service is unreachable.
/// </summary>
public sealed class SeededAdmins(IReadOnlyList<SeededAdmin> accounts)
{
    /// <summary>The configured accounts, already filtered to those with an actual address.</summary>
    public IReadOnlyList<SeededAdmin> Accounts { get; } = accounts;

    /// <summary>
    /// Whether an address belongs to a bootstrap admin. Case- and whitespace-insensitive,
    /// because this is matched against whatever someone typed into the login box.
    /// </summary>
    public bool Includes(string? email) =>
        !string.IsNullOrWhiteSpace(email)
        && Accounts.Any(a => string.Equals(a.Email, email.Trim(), StringComparison.OrdinalIgnoreCase));
}
