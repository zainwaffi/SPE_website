using Microsoft.EntityFrameworkCore;
using SPE_website.Data;
using SPE_website.Features.Tasks.Models;

namespace SPE_website.Features.Tasks.Services;

/// <summary>
/// Query and status operations for committee <see cref="TaskItem"/> assignments.
/// Takes a context factory rather than a scoped context: a Blazor Server circuit outlives
/// any single operation, and sharing one context across overlapping renders throws.
/// </summary>
public class TaskItemService(IDbContextFactory<AppDbContext> dbFactory)
{
    /// <summary>Tasks assigned to a specific member, soonest deadline first — used by the member's own "My Tasks" page.</summary>
    public async Task<List<TaskItem>> GetForUserAsync(string userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.TaskItems.AsNoTracking()
                       .Where(t => t.AssignedToUserId == userId)
                       .OrderBy(t => t.Deadline)
                       .ToListAsync();
    }

    /// <summary>Marks a task Completed/Failed/Processing. No-ops if the task no longer exists.</summary>
    public async Task UpdateStatusAsync(int id, AssignmentStatus status)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var task = await db.TaskItems.FindAsync(id);
        if (task is not null)
        {
            task.Status = status;
            await db.SaveChangesAsync();
        }
    }
}
