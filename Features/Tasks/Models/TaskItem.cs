using SPE_website.Data.Models;

namespace SPE_website.Features.Tasks.Models;

public enum AssignmentStatus
{
    Processing,
    Completed,
    Failed
}

public class TaskItem
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime Deadline { get; set; }
    public AssignmentStatus Status { get; set; } = AssignmentStatus.Processing;
    public string? AssignedToUserId { get; set; }
    public ApplicationUser? AssignedTo { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
