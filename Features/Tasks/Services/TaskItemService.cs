using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SPE_website.Data;
using SPE_website.Data.Models;
using SPE_website.Features.Tasks.Models;
using SPE_website.Shared.Models;
using SPE_website.Shared.Services;

namespace SPE_website.Features.Tasks.Services;

/// <summary>
/// Query and status operations for committee <see cref="TaskItem"/> assignments.
/// Takes a context factory rather than a scoped context: a Blazor Server circuit outlives
/// any single operation, and sharing one context across overlapping renders throws.
/// </summary>
public class TaskItemService(
    IDbContextFactory<AppDbContext> dbFactory,
    UserManager<ApplicationUser> userManager,
    EmailService emailService,
    ILogger<TaskItemService> logger)
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

    /// <summary>
    /// Marks a task Completed/Failed/Processing. No-ops if the task no longer exists.
    ///
    /// Completing a task notifies the team leaders by email. The return value reports how that
    /// went, and is <c>null</c> when no notification was due at all (any other status, or a task
    /// that has since been deleted) — the status change is saved regardless.
    /// </summary>
    public async Task<EmailResult?> UpdateStatusAsync(int id, AssignmentStatus status)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var task = await db.TaskItems.FindAsync(id);
        if (task is null) return null;

        task.Status = status;
        await db.SaveChangesAsync();

        if (status != AssignmentStatus.Completed) return null;

        var member = task.AssignedToUserId is null
            ? null
            : await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == task.AssignedToUserId);

        return await NotifyTeamLeadersOfCompletionAsync(task, member);
    }

    /// <summary>
    /// Emails every team leader that a member has completed a task. Leaders who have turned
    /// notification emails off in their profile are skipped, as is the member themselves when
    /// they are a leader completing their own task.
    ///
    /// One send per leader rather than a single multi-recipient message, so each of them gets a
    /// personally addressed mail and none of them sees the others' addresses. Succeeds if at
    /// least one leader was reached — a single bad address shouldn't report the whole thing failed.
    /// </summary>
    private async Task<EmailResult> NotifyTeamLeadersOfCompletionAsync(TaskItem task, ApplicationUser? member)
    {
        List<ApplicationUser> leaders;
        try
        {
            leaders = [.. await userManager.GetUsersInRoleAsync("TeamLeader")];
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not look up team leaders to notify about task {TaskId}", task.Id);
            return EmailResult.Failure(ex.Message);
        }

        var recipients = leaders
            .Where(l => l.Id != task.AssignedToUserId && l.EmailNotificationsEnabled)
            .ToList();

        if (recipients.Count == 0)
            return EmailResult.Failure("There is no team leader currently receiving email notifications.");

        var memberName = member?.FullName is { Length: > 0 } name ? name : "A member";
        var subject = $"Task Completed: {task.Title} — SPE Chapter";
        var lastError = "No team leader could be notified.";
        var anySent = false;

        foreach (var leader in recipients)
        {
            var body = $"""
                <p>Dear {Encode(leader.FullName)},</p>
                <p><strong>{Encode(memberName)}</strong> has marked the following task as completed:</p>
                <p><strong>{Encode(task.Title)}</strong></p>
                <p><em>{Encode(task.Description)}</em></p>
                <p>Deadline was: <strong>{task.Deadline:dddd, MMMM d, yyyy}</strong></p>
                <p>You can review it on the Member Dashboard of the chapter website.</p>
                <p>— SPE University of Aberdeen Chapter</p>
                """;

            try
            {
                var result = await emailService.SendAsync(leader.Email ?? string.Empty, leader.FullName, subject, body);
                if (result.Sent) anySent = true;
                else if (result.Error is not null) lastError = result.Error;
            }
            catch (Exception ex)
            {
                // EmailService swallows its own failures; this catches anything else so a broken
                // SMTP server can never undo a status change that is already committed.
                logger.LogError(ex, "Failed to notify team leader {Email} about task {TaskId}", leader.Email, task.Id);
                lastError = ex.Message;
            }
        }

        return anySent ? EmailResult.Success() : EmailResult.Failure(lastError);
    }

    /// <summary>Member-supplied text (names, task titles) goes into an HTML mail body, so it must be encoded.</summary>
    private static string Encode(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
