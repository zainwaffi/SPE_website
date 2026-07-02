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

    public async Task<(IdentityResult result, string? tempPassword)> CreateMemberAsync(string fullName, string email, string identityRole, CommitteeRole committeeRole)
    {
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FullName = fullName,
            CommitteeRole = committeeRole
        };

        var tempPassword = GenerateTempPassword();
        var result = await userManager.CreateAsync(user, tempPassword);
        if (!result.Succeeded) return (result, null);

        await userManager.AddToRoleAsync(user, identityRole);

        await emailService.SendAsync(
            email,
            fullName,
            "Welcome to SPE Chapter — Your Account",
            $"<p>Dear {fullName},</p><p>An account has been created for you on the SPE Chapter portal.</p>" +
            $"<p>Email: <strong>{email}</strong><br>Temporary password: <strong>{tempPassword}</strong></p>" +
            $"<p>Please sign in at the chapter website and change your password after logging in.</p>"
        );

        return (result, tempPassword);
    }

    private static string GenerateTempPassword()
    {
        const string letters = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz";
        const string digits = "23456789";
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(10);

        var password = new char[10];
        password[0] = digits[bytes[0] % digits.Length];
        for (int i = 1; i < 10; i++)
        {
            var allChars = letters + digits;
            password[i] = allChars[bytes[i] % allChars.Length];
        }

        return new string(password);
    }

    public async Task<string> GetPrimaryRoleAsync(string userId)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null) return "CommitteeMember";
        var roles = await userManager.GetRolesAsync(user);
        return roles.Contains("President") ? "President" : "CommitteeMember";
    }

    public async Task<IdentityResult> UpdateMemberDetailsAsync(string userId, string fullName, string email, string identityRole, CommitteeRole committeeRole)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null) return IdentityResult.Failed(new IdentityError { Description = "Member not found." });

        user.FullName = fullName;
        user.CommitteeRole = committeeRole;

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

    public async Task<IdentityResult> DeleteMemberAsync(string userId)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null) return IdentityResult.Failed(new IdentityError { Description = "Member not found." });
        return await userManager.DeleteAsync(user);
    }
}
