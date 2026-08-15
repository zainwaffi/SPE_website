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
public class AdminService(
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    EmailService emailService,
    ILogger<AdminService> logger)
{
    public Task<List<ApplicationUser>> GetAllMembersAsync() =>
        db.Users.Include(u => u.AssignedTasks).OrderBy(u => u.FullName).ToListAsync();

    /// <summary>
    /// Increments a member's strike count and emails them a notice. The strike is always saved;
    /// the returned <see cref="EmailResult"/> reports whether the notification actually went out.
    /// </summary>
    public async Task<EmailResult> AddStrikeAsync(string userId)
    {
        var user = await db.Users.FindAsync(userId);
        if (user is null) return EmailResult.Failure("Member not found.");

        user.StrikeCount++;
        await db.SaveChangesAsync();

        return await SendNotificationAsync(
            user,
            "Strike Notice — SPE Chapter",
            $"""
             <p>Dear {Encode(user.FullName)},</p>
             <p>A strike has been added to your record. Your current strike count is <strong>{user.StrikeCount}</strong>.</p>
             <p>Please contact the President if you have any questions.</p>
             <p>— SPE University of Aberdeen Chapter</p>
             """);
    }

    /// <summary>
    /// Removes one strike from a member's record (never going below zero) and emails them the update.
    /// Returns a failed <see cref="EmailResult"/> if the member already had no strikes.
    /// </summary>
    public async Task<EmailResult> RemoveStrikeAsync(string userId)
    {
        var user = await db.Users.FindAsync(userId);
        if (user is null) return EmailResult.Failure("Member not found.");
        if (user.StrikeCount <= 0) return EmailResult.Failure("This member has no strikes to remove.");

        user.StrikeCount--;
        await db.SaveChangesAsync();

        return await SendNotificationAsync(
            user,
            "Strike Removed — SPE Chapter",
            $"""
             <p>Dear {Encode(user.FullName)},</p>
             <p>A strike has been removed from your record. Your current strike count is now <strong>{user.StrikeCount}</strong>.</p>
             <p>— SPE University of Aberdeen Chapter</p>
             """);
    }

    /// <summary>
    /// Creates and assigns a new task to a member, emailing them the details. The task is always
    /// saved; the returned <see cref="EmailResult"/> reports whether the notification went out.
    /// </summary>
    public async Task<(TaskItem Task, EmailResult Notification)> AssignTaskAsync(string userId, string title, string description, DateTime deadline)
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

        if (user is null)
            return (task, EmailResult.Failure("Member not found, so no notification was sent."));

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

    public async Task UpdateMemberTitleAsync(string userId, string? title)
    {
        var user = await db.Users.FindAsync(userId);
        if (user is null) return;
        user.CommitteeTitle = title;
        await db.SaveChangesAsync();
    }

    public async Task<IdentityResult> CreateMemberAsync(string fullName, string email, string identityRole, string? committeeTitle)
    {
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FullName = fullName,
            CommitteeTitle = committeeTitle,
            IsStudentChapterOfficer = identityRole != "Member"
        };

        var result = await userManager.CreateAsync(user);
        if (!result.Succeeded) return result;

        await userManager.AddToRoleAsync(user, identityRole);

        return result;
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

    /// <summary>Updates a member's profile and role, guarded by <see cref="CanManageRolesAsync"/> (TeamLeader-only).</summary>
    public async Task<IdentityResult> UpdateMemberDetailsAsync(string actingUserId, string userId, string fullName, string email, string identityRole, string? committeeTitle)
    {
        var canManageRoles = await CanManageRolesAsync(actingUserId);
        if (!canManageRoles)
            return IdentityResult.Failed(new IdentityError { Description = "Only a Team Leader can change member roles." });

        var user = await userManager.FindByIdAsync(userId);
        if (user is null) return IdentityResult.Failed(new IdentityError { Description = "Member not found." });

        user.FullName = fullName;
        user.CommitteeTitle = committeeTitle;
        user.IsStudentChapterOfficer = identityRole != "Member";

        if (!string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase))
        {
            var setEmailResult = await userManager.SetEmailAsync(user, email);
            if (!setEmailResult.Succeeded) return setEmailResult;

            var setUserNameResult = await userManager.SetUserNameAsync(user, email);
            if (!setUserNameResult.Succeeded) return setUserNameResult;
        }

        var currentRoles = await userManager.GetRolesAsync(user);
        if (!currentRoles.Contains(identityRole))
        {
            await userManager.RemoveFromRolesAsync(user, currentRoles);
            await userManager.AddToRoleAsync(user, identityRole);
        }

        return await userManager.UpdateAsync(user);
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
    /// Sends a notification email. <see cref="EmailService"/> already swallows its own exceptions,
    /// but this guards against anything else going wrong so an unreachable SMTP server can never
    /// undo a strike or task assignment that is already committed to the database.
    /// </summary>
    private async Task<EmailResult> SendNotificationAsync(ApplicationUser user, string subject, string htmlBody)
    {
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
