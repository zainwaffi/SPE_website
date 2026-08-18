using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using SPE_website.Data.Models;
using SPE_website.Features.Authentication.Models;

namespace SPE_website.Features.Authentication.Services;

/// <summary>
/// Handles password-less login by verifying an email address against the external
/// OpenWater membership directory, upserting a matching <see cref="ApplicationUser"/>,
/// syncing Identity roles from the member's officer status, and signing them in.
/// </summary>
public class OpenWaterAuthService(
    IHttpClientFactory httpClientFactory,
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager)
{
    /* ---------- Configuration constants ---------- */

    // #UpdateLink — the OpenWater lookup URL, the bootstrap admin address, and the join-SPE
    // link below. The admin address must be kept in step with adminEmail in Program.cs.
    private const string PrefillUrlTemplate = "https://openwater-os.secure-platform.com/societypetroleumengineers/prefill?emailOrUserId={0}";

    /// <summary>
    /// Bootstrap admin email that is always granted full (President) access even if
    /// no OpenWater record is found — used to recover access if the external service
    /// is unreachable or the account isn't yet registered there.
    /// </summary>
    private const string FullAccessEmail = "zainaldinsabr@gmail.com";

    /// <summary>Public join-SPE link shown to users whose email isn't found in OpenWater.</summary>
    public const string JoinSpeUrl = "https://www.spe.org/en/membership/join/";

    /// <summary>
    /// Attempts to sign the given email in. Returns whether it succeeded, whether the
    /// "Join SPE" call-to-action should be shown (only relevant on failure), and an
    /// optional user-facing error message.
    /// </summary>
    public async Task<(bool Succeeded, bool ShowJoinSpeOption, string? Error)> LoginWithEmailAsync(string email)
    {
        var normalizedEmail = email.Trim();
        var isFullAccessEmail = string.Equals(normalizedEmail, FullAccessEmail, StringComparison.OrdinalIgnoreCase);
        var profile = await GetMemberProfileAsync(normalizedEmail);

        if (profile is null)
        {
            // Only the bootstrap admin can log in without an OpenWater record.
            if (!isFullAccessEmail)
                return (false, true, "No SPE membership record was found for this email.");

            profile = new OpenWaterMemberProfile
            {
                Email = normalizedEmail,
                FullName = "SPE President",
                IsStudentOfficer = true,
                RawJson = "{}"
            };
        }

        var user = await userManager.FindByEmailAsync(profile.Email);
        var isNewUser = user is null;
        user ??= new ApplicationUser { UserName = profile.Email, Email = profile.Email };

        ApplyProfile(user, profile, isNewUser);

        var saveResult = isNewUser ? await userManager.CreateAsync(user) : await userManager.UpdateAsync(user);
        if (!saveResult.Succeeded)
            return (false, false, string.Join(" ", saveResult.Errors.Select(e => e.Description)));

        var roleResult = await SyncRolesAsync(user, profile.IsStudentOfficer, isNewUser);
        if (!roleResult.Succeeded)
            return (false, false, string.Join(" ", roleResult.Errors.Select(e => e.Description)));

        await signInManager.SignInAsync(user, isPersistent: false);
        return (true, false, null);
    }

    /// <summary>
    /// Calls the OpenWater prefill endpoint and parses its response. The top-level
    /// "success" flag indicates whether a matching record was found; when true, the
    /// member's fields live in "data.fields" as alias/value pairs. Only the fields we
    /// care about are picked out — collegeUniversityName, studentName, studentID,
    /// studentEmail, isStudentChapterOfficer, studentDegProgLevel, and isStudentMember.
    /// Returns null if "success" is false (e.g. no record found for the email).
    /// </summary>
    private async Task<OpenWaterMemberProfile?> GetMemberProfileAsync(string email)
    {
        var httpClient = httpClientFactory.CreateClient();
        var response = await httpClient.GetAsync(string.Format(PrefillUrlTemplate, Uri.EscapeDataString(email)));
        var json = await response.Content.ReadAsStringAsync();

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (root.GetProperty("success").ValueKind != JsonValueKind.True)
            return null;

        var fields = root.GetProperty("data").GetProperty("fields")
            .EnumerateArray()
            .ToDictionary(
                field => field.GetProperty("alias").GetString()!,
                field => field.GetProperty("value").GetString() ?? string.Empty,
                StringComparer.OrdinalIgnoreCase);

        return new OpenWaterMemberProfile
        {
            Email = fields.GetValueOrDefault("studentEmail", email),
            FullName = fields.GetValueOrDefault("studentName", email),
            StudentId = fields.GetValueOrDefault("studentID"),
            Organization = fields.GetValueOrDefault("collegeUniversityName"),
            DegreeProgramLevel = fields.GetValueOrDefault("studentDegProgLevel"),
            IsStudentOfficer = IsYes(fields.GetValueOrDefault("isStudentChapterOfficer")),
            IsStudentMember = IsYes(fields.GetValueOrDefault("isStudentMember")),
            RawJson = json
        };
    }

    /// <summary>
    /// Reconciles the user's Identity roles on login. The bootstrap admin always ends
    /// up with both "President" and "CommitteeMember" on every login. For everyone
    /// else, a default role ("CommitteeMember" or "Member" based on OpenWater's officer
    /// flag) is only assigned the first time the account is created — after that, role
    /// changes are the President's responsibility via the Member Dashboard, and logins
    /// never overwrite them.
    /// </summary>
    private async Task<IdentityResult> SyncRolesAsync(ApplicationUser user, bool isStudentOfficer, bool isNewUser)
    {
        var isFullAccessEmail = string.Equals(user.Email, FullAccessEmail, StringComparison.OrdinalIgnoreCase);

        if (isFullAccessEmail)
        {
            user.IsStudentChapterOfficer = true;
            return await AddMissingRolesAsync(user, "TeamLeader", "CommitteeMember");
        }

        if (!isNewUser)
            return IdentityResult.Success;

        return await AddMissingRolesAsync(user, isStudentOfficer ? "CommitteeMember" : "Member");
    }

    private async Task<IdentityResult> AddMissingRolesAsync(ApplicationUser user, params string[] roles)
    {
        var currentRoles = await userManager.GetRolesAsync(user);
        var missingRoles = roles.Where(r => !currentRoles.Contains(r, StringComparer.OrdinalIgnoreCase)).ToArray();
        return missingRoles.Length > 0
            ? await userManager.AddToRolesAsync(user, missingRoles)
            : IdentityResult.Success;
    }

    /// <summary>Copies parsed OpenWater fields onto the tracked <see cref="ApplicationUser"/> entity.</summary>
    private static void ApplyProfile(ApplicationUser user, OpenWaterMemberProfile profile, bool isNewUser)
    {
        user.Email = profile.Email;
        user.UserName = profile.Email;
        user.FullName = profile.FullName;
        user.IsStudentChapterOfficer = profile.IsStudentOfficer;
        user.OpenWaterMemberId = profile.StudentId;
        user.OpenWaterOrganization = profile.Organization;
        user.OpenWaterProfileJson = profile.RawJson;

        if (!isNewUser)
            return;
    }

    private static bool IsYes(string? value) =>
        string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
}
