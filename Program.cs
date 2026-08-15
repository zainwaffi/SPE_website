using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SPE_website.Components;
using SPE_website.Data;
using SPE_website.Data.Models;
using SPE_website.Features.Courses.Services;
using SPE_website.Features.Events.Services;
using SPE_website.Features.Authentication.Services;
using SPE_website.Features.MemberProfile.Services;
using SPE_website.Features.Opportunities.Services;
using SPE_website.Features.PresidentAdmin.Services;
using SPE_website.Features.Tasks.Services;
using SPE_website.Features.Tutorials.Services;
using SPE_website.Shared.Models;
using SPE_website.Shared.Services;

var builder = WebApplication.CreateBuilder(args);
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddCascadingAuthenticationState();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.SignIn.RequireConfirmedAccount = false;
})
.AddEntityFrameworkStores<AppDbContext>()
.AddDefaultTokenProviders();

builder.Services.AddHttpClient();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/login";
    options.AccessDeniedPath = "/";
});

builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("EmailSettings"));

builder.Services.AddScoped<EventService>();
builder.Services.AddScoped<OpportunityService>();
builder.Services.AddScoped<TaskItemService>();
builder.Services.AddScoped<TutorialService>();
builder.Services.AddScoped<CourseService>();
builder.Services.AddScoped<OpenWaterAuthService>();
builder.Services.AddScoped<ProfileService>();
builder.Services.AddScoped<AdminService>();
builder.Services.AddScoped<EmailService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();



app.MapPost("/logout", async (SignInManager<ApplicationUser> signInManager) =>
{
    await signInManager.SignOutAsync();
    return TypedResults.LocalRedirect("/");
}).DisableAntiforgery();

using (var scope = app.Services.CreateScope())
{
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    foreach (var role in new[] { "TeamLeader", "CommitteeMember", "Member" })
    {
        if (!await roleManager.RoleExistsAsync(role))
            await roleManager.CreateAsync(new IdentityRole(role));
    }

    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();

    // Seed a bootstrap Team Leader account (email can be changed from member management).
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    const string adminEmail = "zainaldinsabr@gmail.com";
    var admin = await userManager.FindByEmailAsync(adminEmail);
    var created = false;
    if (admin is null)
    {
        admin = new ApplicationUser
        {
            UserName = adminEmail,
            Email = adminEmail,
            FullName = "SPE Team Leader",
            IsStudentChapterOfficer = true
        };
        created = true;
    }

    admin.UserName = adminEmail;
    admin.Email = adminEmail;
    admin.FullName = "SPE Team Leader";
    admin.IsStudentChapterOfficer = true;
    admin.OpenWaterMemberId = admin.OpenWaterMemberId ?? "seeded";

    var saveResult = created
        ? await userManager.CreateAsync(admin)
        : await userManager.UpdateAsync(admin);

    if (saveResult.Succeeded)
    {
        if (!await userManager.IsInRoleAsync(admin, "TeamLeader"))
            await userManager.AddToRoleAsync(admin, "TeamLeader");

        if (!await userManager.IsInRoleAsync(admin, "CommitteeMember"))
            await userManager.AddToRoleAsync(admin, "CommitteeMember");
    }
}

app.Run();
