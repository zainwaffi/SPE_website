using System.ComponentModel.DataAnnotations;
using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SPE_website.Data;
using SPE_website.Data.Models;
using SPE_website.Features.Tasks.Models;
using SPE_website.Shared.Models;
using SPE_website.Shared.Services;

namespace SPE_website.Features.PresidentAdmin.Services;

/// <summary>
/// Administrative operations available to the President: member management,
/// strikes, task assignment, and role changes. Sends email notifications for
/// strikes and task assignments via <see cref="EmailService"/>.
/// </summary>
/// <remarks>
/// Uses a context factory rather than a scoped context: a Blazor Server circuit outlives any
/// single operation, and sharing one context across overlapping renders throws. Identity's
/// <see cref="UserManager{TUser}"/> keeps its own scoped context, which is fine — the two
/// never need to share a change tracker here.
/// </remarks>
public class AdminService(
    IDbContextFactory<AppDbContext> dbFactory,
    UserManager<ApplicationUser> userManager,
    EmailService emailService,
    ILogger<AdminService> logger)
{
    /// <summary>
    /// Every member plus their assigned tasks, for the admin table. Read-only projection —
    /// the dashboard updates its copy in place rather than re-querying after each action.
    /// </summary>
    public async Task<List<ApplicationUser>> GetAllMembersAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Users.AsNoTracking()
                       .Include(u => u.AssignedTasks)
                       .Include(u => u.Teams)
                       .OrderBy(u => u.FullName)
                       .ToListAsync();
    }

    /// <summary>
    /// Increments a member's strike count and emails them a notice. The strike is always saved;
    /// the returned <see cref="EmailResult"/> reports whether the notification actually went out.
    /// <c>StrikeCount</c> is the member's new total, or <c>null</c> if nothing was changed —
    /// callers use it to refresh their own copy without re-reading the whole member list.
    /// </summary>
    public async Task<(int? StrikeCount, EmailResult Notification)> AddStrikeAsync(string userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var user = await db.Users.FindAsync(userId);
        if (user is null) return (null, EmailResult.Failure("Member not found."));

        user.StrikeCount++;
        await db.SaveChangesAsync();

        // #UpdateLink — wording of the strike-added email (subject line, then the body).
        var notification = await SendNotificationAsync(
            user,
            "Strike Notice — SPE Chapter",
            $"""
             <p>Dear {Encode(user.FullName)},</p>
             <p>A strike has been added to your record. Your current strike count is <strong>{user.StrikeCount}</strong>.</p>
             <p>Please contact the President if you have any questions.</p>
             <p>— SPE University of Aberdeen Chapter</p>
             """);

        return (user.StrikeCount, notification);
    }

    /// <summary>
    /// Removes one strike from a member's record (never going below zero) and emails them the update.
    /// Returns a null count and a failed <see cref="EmailResult"/> if the member already had no strikes.
    /// </summary>
    public async Task<(int? StrikeCount, EmailResult Notification)> RemoveStrikeAsync(string userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var user = await db.Users.FindAsync(userId);
        if (user is null) return (null, EmailResult.Failure("Member not found."));
        if (user.StrikeCount <= 0) return (null, EmailResult.Failure("This member has no strikes to remove."));

        user.StrikeCount--;
        await db.SaveChangesAsync();

        // #UpdateLink — wording of the strike-removed email (subject line, then the body).
        var notification = await SendNotificationAsync(
            user,
            "Strike Removed — SPE Chapter",
            $"""
             <p>Dear {Encode(user.FullName)},</p>
             <p>A strike has been removed from your record. Your current strike count is now <strong>{user.StrikeCount}</strong>.</p>
             <p>— SPE University of Aberdeen Chapter</p>
             """);

        return (user.StrikeCount, notification);
    }

    /// <summary>
    /// Creates and assigns a new task to a member, emailing them the details. The task is always
    /// saved; the returned <see cref="EmailResult"/> reports whether the notification went out.
    ///
    /// <paramref name="assignedByUserId"/> is the leader doing the assigning, recorded so they can
    /// review what they handed out. It is nullable rather than required because the caller reads
    /// it from the cascading auth state, which can come back null — an unattributed task is a far
    /// better outcome there than refusing to create one.
    /// </summary>
    public async Task<(TaskItem Task, EmailResult Notification)> AssignTaskAsync(
        string userId, string title, string description, DateTime deadline, string? assignedByUserId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var user = await db.Users.FindAsync(userId);

        var task = new TaskItem
        {
            Title = title,
            Description = description,
            Deadline = deadline,
            AssignedToUserId = userId,
            AssignedByUserId = assignedByUserId,
            Status = AssignmentStatus.Processing
        };

        db.TaskItems.Add(task);
        await db.SaveChangesAsync();

        if (user is null)
            return (task, EmailResult.Failure("Member not found, so no notification was sent."));

        // #UpdateLink — wording of the task-assigned email (subject line, then the body).
        var notification = await SendNotificationAsync(
            user,
            $"New Task Assigned: {title} — SPE Chapter",
            $"""
             <p>Dear {Encode(user.FullName)},</p>
             <p>You have been assigned a new task: <strong>{Encode(title)}</strong></p>
             <p><em>{Encode(description)}</em></p>
             <p>Deadline: <strong>{deadline:dddd, MMMM d, yyyy}</strong></p>
             <p>You can view and update this task on the Tasks page of the chapter website.</p>
             <p>— SPE University of Aberdeen Chapter</p>
             """);

        return (task, notification);
    }

    /// <summary>Highest-priority Identity role for display purposes: TeamLeader > CommitteeMember > Member (default).</summary>
    public async Task<string> GetPrimaryRoleAsync(string userId)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null) return "Member";
        var roles = await userManager.GetRolesAsync(user);
        if (roles.Contains("TeamLeader"))
            return "TeamLeader";
        if (roles.Contains("CommitteeMember"))
            return "CommitteeMember";
        return "Member";
    }

    /// <summary>
    /// Updates a member's role, committee title and email, guarded by <see cref="CanManageRolesAsync"/>
    /// (TeamLeader-only).
    ///
    /// Name is still not editable here — it is owned by the external OpenWater membership record
    /// and re-synced on every login, so editing it in the admin panel would silently be undone at
    /// the member's next sign-in. Email is the same in principle (and doubles as the Identity
    /// username, updated alongside it), but members who sign in by card number
    /// (see <see cref="MemberNumberAuthService"/>) never get an email from anywhere else, so an
    /// admin override is the only way they get one. A member who signs in via OpenWater will have
    /// this overwritten again on their next login.
    /// </summary>
    public async Task<IdentityResult> UpdateMemberDetailsAsync(
        string actingUserId, string userId, string identityRole, string? committeeTitle, string? email, IEnumerable<Team> teams)
    {
        var canManageRoles = await CanManageRolesAsync(actingUserId);
        if (!canManageRoles)
            return IdentityResult.Failed(new IdentityError { Description = "Only a Team Leader can change member roles." });

        var user = await userManager.FindByIdAsync(userId);
        if (user is null) return IdentityResult.Failed(new IdentityError { Description = "Member not found." });

        if (!string.IsNullOrWhiteSpace(email))
        {
            var emailResult = await SetEmailIfChangedAsync(user, email);
            if (!emailResult.Succeeded) return emailResult;
        }

        user.CommitteeTitle = committeeTitle;
        user.IsStudentChapterOfficer = identityRole != "Member";

        var currentRoles = await userManager.GetRolesAsync(user);
        if (!currentRoles.Contains(identityRole))
        {
            await userManager.RemoveFromRolesAsync(user, currentRoles);
            await userManager.AddToRoleAsync(user, identityRole);
        }

        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded) return result;

        await SetTeamsAsync(userId, teams);
        return result;
    }

    /// <summary>
    /// Validates and applies a new email (and mirrors it onto the Identity username, same as
    /// <see cref="OpenWaterAuthService"/> does), unless it is unchanged. Shared by the admin
    /// override above and a member's own self-service edit on their profile.
    /// </summary>
    internal static async Task<IdentityResult> SetEmailIfChangedAsync(UserManager<ApplicationUser> userManager, ApplicationUser user, string email)
    {
        var trimmedEmail = email.Trim();
        if (!new EmailAddressAttribute().IsValid(trimmedEmail))
            return IdentityResult.Failed(new IdentityError { Description = "Enter a valid email address." });

        if (string.Equals(user.Email, trimmedEmail, StringComparison.OrdinalIgnoreCase))
            return IdentityResult.Success;

        var existing = await userManager.FindByEmailAsync(trimmedEmail);
        if (existing is not null && existing.Id != user.Id)
            return IdentityResult.Failed(new IdentityError { Description = "Another member already uses that email." });

        var emailResult = await userManager.SetEmailAsync(user, trimmedEmail);
        if (!emailResult.Succeeded) return emailResult;

        return await userManager.SetUserNameAsync(user, trimmedEmail);
    }

    private Task<IdentityResult> SetEmailIfChangedAsync(ApplicationUser user, string email) =>
        SetEmailIfChangedAsync(userManager, user, email);

    /// <summary>
    /// Replaces a member's team allocation with exactly the teams given. Only the difference is
    /// written, so re-saving the edit form without touching the checkboxes doesn't churn rows —
    /// which matters because the unique index makes a delete-then-reinsert a race with itself.
    /// </summary>
    private async Task SetTeamsAsync(string userId, IEnumerable<Team> teams)
    {
        var wanted = teams.Distinct().ToHashSet();

        await using var db = await dbFactory.CreateDbContextAsync();
        var existing = await db.MemberTeams.Where(m => m.UserId == userId).ToListAsync();

        var removed = existing.Where(m => !wanted.Contains(m.Team)).ToList();
        if (removed.Count > 0) db.MemberTeams.RemoveRange(removed);

        var alreadyThere = existing.Select(m => m.Team).ToHashSet();
        foreach (var team in wanted.Where(t => !alreadyThere.Contains(t)))
            db.MemberTeams.Add(new MemberTeam { UserId = userId, Team = team });

        await db.SaveChangesAsync();
    }

    /// <summary>Only a Team Leader (Identity role) may manage member roles.</summary>
    public async Task<bool> CanManageRolesAsync(string userId)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
            return false;

        var roles = await userManager.GetRolesAsync(user);
        return roles.Contains("TeamLeader");
    }

    public async Task<IdentityResult> DeleteMemberAsync(string userId)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null) return IdentityResult.Failed(new IdentityError { Description = "Member not found." });
        return await userManager.DeleteAsync(user);
    }

    /// <summary>
    /// Sends a notification email, unless the member has opted out on their profile. The action
    /// itself (strike, task) has already been committed either way — this only decides whether
    /// they hear about it by email.
    ///
    /// <see cref="EmailService"/> already swallows its own exceptions, but this guards against
    /// anything else going wrong so an unreachable SMTP server can never undo a strike or task
    /// assignment that is already saved.
    /// </summary>
    private async Task<EmailResult> SendNotificationAsync(ApplicationUser user, string subject, string htmlBody)
    {
        if (!user.EmailNotificationsEnabled)
        {
            logger.LogInformation("Skipped \"{Subject}\" — {Email} has notification emails turned off", subject, user.Email);
            return EmailResult.Failure("This member has turned off email notifications in their profile.");
        }

        try
        {
            return await emailService.SendAsync(user.Email ?? string.Empty, user.FullName, subject, htmlBody);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send notification email to {Email}", user.Email);
            return EmailResult.Failure(ex.Message);
        }
    }

    /// <summary>Member-supplied text (names, task titles) goes into an HTML mail body, so it must be encoded.</summary>
    private static string Encode(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
