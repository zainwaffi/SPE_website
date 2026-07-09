using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SPE_website.Components;
using SPE_website.Data;
using SPE_website.Data.Models;
using SPE_website.Features.Events.Services;
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
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=spe.db"));

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.SignIn.RequireConfirmedAccount = false;
    options.Password.RequireDigit = true;
    options.Password.RequiredLength = 8;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = false;
})
.AddEntityFrameworkStores<AppDbContext>()
.AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/login";
    options.AccessDeniedPath = "/access-denied";
});

builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("EmailSettings"));

builder.Services.AddScoped<EventService>();
builder.Services.AddScoped<OpportunityService>();
builder.Services.AddScoped<TaskItemService>();
builder.Services.AddScoped<TutorialService>();
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

app.Reviews();

app.MapPost("/logout", async (SignInManager<ApplicationUser> signInManager) =>
{
    await signInManager.SignOutAsync();
    return TypedResults.LocalRedirect("/");
}).DisableAntiforgery();

using (var scope = app.Services.CreateScope())
{
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    foreach (var role in new[] { "President", "CommitteeMember" })
    {
        if (!await roleManager.RoleExistsAsync(role))
            await roleManager.CreateAsync(new IdentityRole(role));
    }

    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();

    // Seed default President (admin) account — change email/password before deploying
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    const string adminEmail = "zainaldinsabr@gmail.com";    // email of the admin
    if (await userManager.FindByEmailAsync(adminEmail) is null)
    {
        var admin = new ApplicationUser
        {
            UserName = adminEmail,
            Email = adminEmail,
            FullName = "SPE President",
            CommitteeRole = CommitteeRole.Member,
        };
        var result = await userManager.CreateAsync(admin, "Admin@1234");    // password
        if (result.Succeeded)
            await userManager.AddToRoleAsync(admin, "President");
    }
}

app.Run();
