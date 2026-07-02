using Microsoft.AspNetCore.Identity;
using SPE_website.Features.Tasks.Models;

namespace SPE_website.Data.Models;

public class ApplicationUser : IdentityUser
{
    public string FullName { get; set; } = string.Empty;
    public int StrikeCount { get; set; } = 0;
    public string? ProfilePictureUrl { get; set; }
    public CommitteeRole CommitteeRole { get; set; } = CommitteeRole.None;
    public ICollection<TaskItem> AssignedTasks { get; set; } = [];
}
