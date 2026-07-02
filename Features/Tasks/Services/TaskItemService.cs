using Microsoft.EntityFrameworkCore;
using SPE_website.Data;
using SPE_website.Features.Tasks.Models;

namespace SPE_website.Features.Tasks.Services;

public class TaskItemService(AppDbContext db)
{
    public Task<List<TaskItem>> GetForUserAsync(string userId) =>
        db.TaskItems
          .Where(t => t.AssignedToUserId == userId)
          .OrderBy(t => t.Deadline)
          .ToListAsync();

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
