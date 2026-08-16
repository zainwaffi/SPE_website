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

// A factory rather than a plain scoped DbContext: in Blazor Server a scoped context lives
// for the whole circuit, so two overlapping async renders sharing it throw "A second
// operation was started on this context". Services create a short-lived context per call.
builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Identity's stores still require a scoped AppDbContext, so hand them one from the factory.
builder.Services.AddScoped<AppDbContext>(sp =>
    sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext());

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
builder.Services.AddScoped<EventCalendarService>();
builder.Services.AddScoped<OpportunityService>();
builder.Services.AddScoped<TaskItemService>();
builder.Services.AddScoped<TutorialService>();
builder.Services.AddScoped<CourseService>();
builder.Services.AddScoped<OpenWaterAuthService>();
builder.Services.AddScoped<ProfileService>();
builder.Services.AddScoped<AdminService>();
builder.Services.AddScoped<AttendanceExportService>();
builder.Services.AddScoped<EmailService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

// Static assets are served before authentication/antiforgery so that CSS, JS and images
// don't pay for the full auth pipeline on every request.
app.MapStaticAssets();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();



app.MapPost("/logout", async (SignInManager<ApplicationUser> signInManager) =>
{
    await signInManager.SignOutAsync();
    return TypedResults.LocalRedirect("/");
}).DisableAntiforgery();

// Public iCalendar subscription feed. Deliberately anonymous: calendar apps poll this URL on a
// schedule with no way to sign in, so requiring auth would break subscriptions outright. It
// exposes only what the events page already shows publicly.
app.MapGet("/events/calendar.ics", async (EventCalendarService calendar, HttpContext http) =>
{
    var baseUrl = $"{http.Request.Scheme}://{http.Request.Host}";
    var feed = await calendar.BuildFeedAsync($"{baseUrl}/events");

    // Clients re-poll this URL, so a short cache keeps repeat hits cheap without making new
    // events wait long to show up.
    http.Response.Headers.CacheControl = "public, max-age=3600";

    return Results.Text(feed, "text/calendar", System.Text.Encoding.UTF8);
}).AllowAnonymous();

// Attendance export. A plain GET endpoint rather than JS interop, so the admin page can offer
// it as an ordinary link — the browser handles the download and no file is buffered over the
// SignalR circuit.
app.MapGet("/admin/export/attendance.xlsx", async (AttendanceExportService exportService) =>
{
    var workbook = await exportService.BuildWorkbookAsync();
    return Results.File(
        workbook,
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        AttendanceExportService.FileName());
})
.RequireAuthorization(policy => policy.RequireRole("TeamLeader"));


// Identity Role
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

    // #Updates: Seeded a Team Leader account (email can be changed from member management).\
    // the seeded account does not necessarily have to be SPE member, it can be any email for debugging or giving acess in order not to lock of the app
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
            FullName = "SPE Team Leader",   // seeded Team leader Name (President)
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
