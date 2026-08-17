namespace SPE_website.Data.Models;

/// <summary>
/// The chapter's working teams. Members are allocated to teams by a Team Leader, and tutorials
/// are filed against them — a member only sees tutorials belonging to a team they are in.
///
/// Persisted as <c>integer</c>, like <c>EventCategory</c>, so DO NOT reorder these: the numbers
/// are already in the database and reordering would silently re-file every member and tutorial.
/// Add new teams at the end.
/// </summary>
public enum Team
{
    SocialMediaAndProgramming,
    CoordinationAndOperations,
    EngagementAndOutreach
}

public static class Teams
{
    /// <summary>Every team, in declaration order — for rendering checkbox lists and section headings.</summary>
    public static readonly Team[] All = Enum.GetValues<Team>();

    /// <summary>Human-readable name. The enum members are identifiers, not display text.</summary>
    public static string DisplayName(this Team team) => team switch
    {
        Team.SocialMediaAndProgramming => "Social Media & Programming",
        Team.CoordinationAndOperations => "Coordination & Operations",
        Team.EngagementAndOutreach => "Engagement & Outreach",
        _ => team.ToString()
    };
}
