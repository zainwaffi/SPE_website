using Microsoft.EntityFrameworkCore;
using SPE_website.Data;
using SPE_website.Features.Tasks.Models;

namespace SPE_website.Features.Tasks.Services;

/// <summary>CRUD and query operations for committee <see cref="TaskItem"/> assignments.</summary>
public class TaskItemService(AppDbContext db)
{
    /// <summary>Tasks assigned to a specific member, soonest deadline first — used by the member's own "My Tasks" page.</summary>
    public Task<List<TaskItem>> GetForUserAsync(string userId) =>
        db.TaskItems
          .Where(t => t.AssignedToUserId == userId)
          .OrderBy(t => t.Deadline)
          .ToListAsync();

    /// <summary>All tasks across all members (with assignee eager-loaded) — used by the President's admin dashboard.</summary>
    public Task<List<TaskItem>> GetAllAsync() =>
        db.TaskItems
          .Include(t => t.AssignedTo)
          .OrderBy(t => t.Deadline)
          .ToListAsync();

    public async Task<TaskItem> CreateAsync(TaskItem task)
    {
        db.TaskItems.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    /// <summary>Marks a task Completed/Failed/Processing. No-ops if the task no longer exists.</summary>
    public async Task UpdateStatusAsync(int id, AssignmentStatus status)
    {
        var task = await db.TaskItems.FindAsync(id);
        if (task is not null)
        {
            task.Status = status;
            await db.SaveChangesAsync();
        }
    }

    public async Task DeleteAsync(int id)
    {
        var task = await db.TaskItems.FindAsync(id);
        if (task is not null)
        {
            db.TaskItems.Remove(task);
            await db.SaveChangesAsync();
        }
    }
}
