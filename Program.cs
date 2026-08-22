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

/* ---------- Host and data access ---------- */

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

/* ---------- Identity and authentication ---------- */

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

// Bootstrap admins are read once here rather than by each consumer: startup seeding creates the
// accounts and OpenWaterAuthService lets them in without a membership record, and those two
// previously held separate hard-coded copies of the same address.
builder.Services.AddSingleton(new SeededAdmins(
    [.. (builder.Configuration.GetSection("SeededAdmins").Get<SeededAdmin[]>() ?? [])
        // An entry with no address is what an unset environment variable looks like. Identity
        // would reject it anyway, so drop it rather than logging a failure that reads like a bug.
        .Where(a => !string.IsNullOrWhiteSpace(a.Email))]));

/* ---------- Feature services ---------- */

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

/* ---------- Request pipeline ---------- */

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


/* ---------- Endpoints ---------- */

app.MapPost("/logout", async (SignInManager<ApplicationUser> signInManager) =>
{
    await signInManager.SignOutAsync();
    return TypedResults.LocalRedirect("/");
}).DisableAntiforgery();

// /events/upcoming and /events/past used to be extra @page routes on the events page that
// rendered content identical to /events — duplicate URLs with no filtering behind them. The
// page is now the single route, and these redirect permanently to the matching section so any
// link that already exists still lands in the right place.
app.MapGet("/events/upcoming", () => Results.Redirect("/events#upcoming", permanent: true)).AllowAnonymous();
app.MapGet("/events/past", () => Results.Redirect("/events#past", permanent: true)).AllowAnonymous();

// Crawler files, generated from the request rather than written as static files in wwwroot, so
// they carry the right absolute URLs on localhost, on a staging host, and on the live domain
// without anyone remembering to edit a hard-coded address into them.
//
// #UpdateLink — the public route list below. Add an entry when a new public page is added; the
// members-only routes are deliberately absent.
static string[] PublicRoutes() => ["/", "/events", "/Scholarships", "/opportunities", "/courses"];

app.MapGet("/robots.txt", (HttpContext http) =>
{
    // The members-only areas sit behind [Authorize], so a crawler only ever reaches the login
    // redirect. Disallowing them keeps that redirect out of search results as well.
    var body = $"""
        User-agent: *
        Disallow: /admin/
        Disallow: /profile
        Disallow: /tasks
        Disallow: /tutorials
        Disallow: /login

        Sitemap: {http.Request.Scheme}://{http.Request.Host}/sitemap.xml
        """;

    return Results.Text(body, "text/plain", System.Text.Encoding.UTF8);
}).AllowAnonymous();

app.MapGet("/sitemap.xml", (HttpContext http) =>
{
    var baseUrl = $"{http.Request.Scheme}://{http.Request.Host}";
    var urls = string.Concat(PublicRoutes().Select(route =>
        $"  <url><loc>{baseUrl}{route}</loc></url>\n"));

    var body = $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
        {urls}</urlset>
        """;

    return Results.Text(body, "application/xml", System.Text.Encoding.UTF8);
}).AllowAnonymous();


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


/* ---------- Startup: migrations, roles, seeded admin ---------- */

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

    // The seeded Team Leader accounts, configured rather than hard-coded — see SeededAdmin for
    // the user-secrets and environment-variable forms. They exist so the app can never be locked
    // out of its own admin.
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var seededAdmins = scope.ServiceProvider.GetRequiredService<SeededAdmins>().Accounts;

    if (seededAdmins.Count == 0)
    {
        // Loud, because the failure is silent and delayed: the app starts perfectly well and only
        // turns out to have nobody who can sign in and grant roles once someone tries.
        app.Logger.LogWarning(
            "No seeded Team Leader is configured, so a new deployment has no admin account. Set " +
            "SeededAdmins__0__Email (and SeededAdmins__0__Name) in the environment, or " +
            "\"SeededAdmins:0:Email\" in user secrets.");
    }

    foreach (var seeded in seededAdmins)
    {
        var adminEmail = seeded.Email.Trim();
        var adminName = string.IsNullOrWhiteSpace(seeded.Name) ? "SPE Team Leader" : seeded.Name.Trim();

        var admin = await userManager.FindByEmailAsync(adminEmail);
        var created = admin is null;
        admin ??= new ApplicationUser();

        // Reapplied on every start, not just on creation, so changing the configured address is
        // enough to move a seeded account rather than leaving a stale one behind.
        admin.UserName = adminEmail;
        admin.Email = adminEmail;
        admin.FullName = adminName;
        admin.IsStudentChapterOfficer = true;
        admin.OpenWaterMemberId ??= "seeded";

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
}

app.Run();
