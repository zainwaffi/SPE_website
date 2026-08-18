using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SPE_website.Data;
using SPE_website.Data.Models;
using SPE_website.Features.Tasks.Models;
using SPE_website.Shared;
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
    /// <summary>
    /// How long a <em>finished</em> task stays on screen after its deadline. Past that it ages off
    /// both the member's list and the leader's review table, so what is displayed is current work
    /// rather than a growing archive.
    ///
    /// Still-Processing tasks are exempt however old they get: ageing one out would quietly hide
    /// work nobody ever closed off from both the member who owes it and the leader chasing it.
    /// </summary>
    public static readonly TimeSpan VisibleAfterDeadline = TimeSpan.FromDays(7);

    /// <summary>
    /// Deadlines earlier than this have aged out. Read once per query into a local, so EF sends it
    /// as a parameter instead of trying to translate <see cref="UkTime"/> into SQL.
    /// </summary>
    private static DateTime AgeOutCutoff => UkTime.Today - VisibleAfterDeadline;

    /// <summary>
    /// Tasks assigned to a specific member — used by the member's own "My Tasks" page. Excludes
    /// anything the member has cleared, and any finished task whose deadline aged out. Both are
    /// filters rather than deletes, so the rows still count on the admin dashboard and the
    /// assigning leader's review page.
    /// </summary>
    /// <param name="soonestFirst">Deadline order: soonest first, or latest first when false.</param>
    public async Task<List<TaskItem>> GetForUserAsync(string userId, bool soonestFirst = true)
    {
        var cutoff = AgeOutCutoff;

        await using var db = await dbFactory.CreateDbContextAsync();

        var query = db.TaskItems.AsNoTracking()
                      .Where(t => t.AssignedToUserId == userId
                               && t.ClearedAt == null
                               && (t.Deadline >= cutoff || t.Status == AssignmentStatus.Processing));

        return await ByDeadline(query, soonestFirst).ToListAsync();
    }

    /// <summary>
    /// Tasks a given team leader handed out, with the assignee loaded for display. Passing
    /// <c>null</c> returns every task on record instead — which is how the review page reaches
    /// assignments made before authorship was recorded, and those by other leaders.
    ///
    /// Finished tasks age out here on the same terms as on the member's list, so the two agree on
    /// what still counts as live. Note this ignores <c>ClearedAt</c> — a member clearing a task off
    /// their own page must not take it off the leader's record.
    /// </summary>
    /// <param name="soonestFirst">Deadline order: soonest first, or latest first when false.</param>
    public async Task<List<TaskItem>> GetAssignedByAsync(string? leaderUserId, bool soonestFirst = true)
    {
        var cutoff = AgeOutCutoff;

        await using var db = await dbFactory.CreateDbContextAsync();

        var query = db.TaskItems.AsNoTracking()
                      .Include(t => t.AssignedTo)
                      .Include(t => t.AssignedBy)
                      .Where(t => t.Deadline >= cutoff || t.Status == AssignmentStatus.Processing);

        if (leaderUserId is not null)
            query = query.Where(t => t.AssignedByUserId == leaderUserId);

        return await ByDeadline(query, soonestFirst).ToListAsync();
    }

    /// <summary>
    /// Applies the deadline sort both task views share. Id breaks ties so tasks due the same day
    /// hold a stable order — without it the database is free to return them differently on each
    /// query, and the list would reshuffle under the reader on an unrelated refresh.
    /// </summary>
    private static IQueryable<TaskItem> ByDeadline(IQueryable<TaskItem> query, bool soonestFirst) =>
        soonestFirst
            ? query.OrderBy(t => t.Deadline).ThenBy(t => t.Id)
            : query.OrderByDescending(t => t.Deadline).ThenByDescending(t => t.Id);

    /// <summary>
    /// Archives a finished task off the member's own list. Returns false — changing nothing — if
    /// the task is gone, belongs to someone else, or is still Processing.
    ///
    /// The ownership check is here rather than only in the page because the page passes an id
    /// straight from the markup: without it, any signed-in member could clear another's task by
    /// invoking the handler with a different number.
    /// </summary>
    public async Task<bool> ClearAsync(int id, string userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var task = await db.TaskItems.FindAsync(id);

        if (task is null || task.AssignedToUserId != userId) return false;
        if (task.Status == AssignmentStatus.Processing) return false;
        if (task.ClearedAt is not null) return false;

        task.ClearedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return true;
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
        // #UpdateLink — wording of the task-completed email sent to team leaders.
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
