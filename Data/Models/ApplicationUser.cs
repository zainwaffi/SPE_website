using Microsoft.AspNetCore.Identity;
using SPE_website.Features.Tasks.Models;

namespace SPE_website.Data.Models;

/// <summary>
/// The chapter's member record, extending ASP.NET Identity's <see cref="IdentityUser"/>.
/// There are no local passwords: every field here except <see cref="IdentityUser.Id"/> and the
/// Identity-managed columns is populated/refreshed from the external OpenWater
/// membership system on each successful login (see OpenWaterAuthService).
/// </summary>
public class ApplicationUser : IdentityUser
{
    /// <summary>Display name shown throughout the UI (profile, task lists, admin table).</summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>Disciplinary strike count. 3+ strikes surfaces an "Action required" warning on the profile page.</summary>
    public int StrikeCount { get; set; } = 0;

    /// <summary>Committee title/position, assigned by a Team Leader (free-form string).</summary>
    public string? CommitteeTitle { get; set; }

    /// <summary>Whether OpenWater reports this user as a student chapter officer — drives Identity role sync.</summary>
    public bool IsStudentChapterOfficer { get; set; }

    /// <summary>External SPE/OpenWater member ID, shown on the profile page when present.</summary>
    public string? OpenWaterMemberId { get; set; }

    /// <summary>University/organization reported by OpenWater.</summary>
    public string? OpenWaterOrganization { get; set; }

    /// <summary>Raw JSON snapshot from the last OpenWater prefill response, kept for auditing/debugging.</summary>
    public string? OpenWaterProfileJson { get; set; }

    /// <summary>Tasks assigned to this member by the President (see <see cref="TaskItem"/>).</summary>
    public ICollection<TaskItem> AssignedTasks { get; set; } = [];
}
