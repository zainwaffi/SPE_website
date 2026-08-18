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

    /// <summary>
    /// The team leader who created this assignment, so a leader can review what they handed out
    /// and to whom. Null for tasks assigned before attribution was recorded, and for any whose
    /// author has since been deleted — the assignment itself outlives them.
    /// </summary>
    public string? AssignedByUserId { get; set; }
    public ApplicationUser? AssignedBy { get; set; }

    /// <summary>
    /// When the member archived this task off their own "My Tasks" list, or null while it is
    /// still showing. Deliberately a soft clear rather than a delete: the row stays in the
    /// database, so clearing a Failed task cannot wipe it from the leader's record.
    /// </summary>
    public DateTime? ClearedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
