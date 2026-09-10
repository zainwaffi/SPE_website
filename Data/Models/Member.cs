namespace SPE_website.Data.Models;

/// <summary>
/// A row from the AUSA membership import (see "Uploading Members/" at the repo root).
/// This table's DDL is owned by that Python script, not EF Core — see
/// <see cref="AppDbContext.OnModelCreating"/>, which excludes it from migrations. The
/// app only ever reads it, for the student/card-number login path in
/// <c>MemberNumberAuthService</c>.
/// </summary>
public class Member
{
    /// <summary>The AUSA card number used as a sign-in identifier.</summary>
    public string CardNumber { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    /// <summary>"Student", "Non-Student", or "Committee" — drives the Identity role granted on login.</summary>
    public string MembershipType { get; set; } = string.Empty;

    /// <summary>When this membership was purchased. Login is refused once this is over a year old.</summary>
    public DateTime PurchasedAt { get; set; }

    /// <summary>False once a later import no longer lists this card number.</summary>
    public bool IsActive { get; set; }

    public DateTime FirstSeen { get; set; }
    public DateTime LastSeenImport { get; set; }
}
