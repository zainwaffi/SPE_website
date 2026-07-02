using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SPE_website.Data;
using SPE_website.Data.Models;
using SPE_website.Features.Tasks.Models;
using SPE_website.Shared.Services;

namespace SPE_website.Features.PresidentAdmin.Services;

public class AdminService(
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    EmailService emailService)
{
    public Task<List<ApplicationUser>> GetAllMembersAsync() =>
        db.Users.Include(u => u.AssignedTasks).OrderBy(u => u.FullName).ToListAsync();

    public async Task AddStrikeAsync(string userId)
    {
        var user = await db.Users.FindAsync(userId);
        if (user is null) return;

        user.StrikeCount++;
        await db.SaveChangesAsync();

        await emailService.SendAsync(
            user.Email ?? string.Empty,
            user.FullName,
            "Strike Notice — SPE Chapter",
            $"<p>Dear {user.FullName},</p><p>A strike has been added to your record. Your current strike count is <strong>{user.StrikeCount}</strong>.</p><p>Please contact the President if you have questions.</p>"
        );
    }

    public async Task<TaskItem> AssignTaskAsync(string userId, string title, string description, DateTime deadline)
    {
        var user = await db.Users.FindAsync(userId);

        var task = new TaskItem
        {
            Title = title,
            Description = description,
            Deadline = deadline,
            AssignedToUserId = userId,
            Status = AssignmentStatus.Processing
        };

        db.TaskItems.Add(task);
        await db.SaveChangesAsync();

        if (user is not null)
        {
            await emailService.SendAsync(
                user.Email ?? string.Empty,
                user.FullName,
                "New Task Assigned — SPE Chapter",
                $"<p>Dear {user.FullName},</p><p>You have been assigned a new task: <strong>{title}</strong></p><p><em>{description}</em></p><p>Deadline: {deadline:MMMM d, yyyy}</p>"
            );
        }

        return task;
    }

    public async Task UpdateMemberRoleAsync(string userId, CommitteeRole role)
    {
        var user = await db.Users.FindAsync(userId);
        if (user is null) return;
        user.CommitteeRole = role;
        await db.SaveChangesAsync();
    }
}
