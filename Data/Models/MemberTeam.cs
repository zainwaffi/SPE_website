namespace SPE_website.Data.Models;

/// <summary>
/// One member's allocation to one team. A member can be in several teams and a team has many
/// members, so this is the join row for that many-to-many relationship.
///
/// A plain entity rather than an EF skip-navigation, because <see cref="Team"/> is an enum and
/// has no table of its own to join through.
/// </summary>
public class MemberTeam
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    public Team Team { get; set; }
}
