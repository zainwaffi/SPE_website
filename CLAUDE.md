# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Blazor Web App (.NET 10, **InteractiveServer render mode only**) + PostgreSQL via EF Core + Tailwind CSS v4, for the SPE University of Aberdeen student chapter. Public shopfront plus a members-only workspace. There is no HTTP API layer and **no test project** — pages inject scoped services that talk to `AppDbContext` directly. `README.md` (setup, content editing, deploy), `SUMMARY.md` (every model/page/service) and `PLAN.md` (deployment) hold more detail; `AGENTS.md` overlaps this file but is partly stale (e.g. it lists a `CommitteeRole` enum and `Enums.cs`, both gone).

## Commands

```bash
npm install                 # once: Tailwind CLI + sharp
dotnet run                  # http://localhost:5169 (https://localhost:7256); applies migrations on startup
dotnet build                # also runs `npm run build:css` if node_modules/ exists (incremental)
npm run watch:css           # Tailwind watcher while editing styles
npm run build:images        # assets/ (masters, gitignored) -> wwwroot/ as WebP
dotnet publish -c Release -o ./publish
```

Config lives outside the repo (`appsettings.json` and `appsettings.Development.json` are gitignored):

```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=…;Database=…;Username=…;Password=…"
dotnet user-secrets set "SeededAdmins:0:Email" "you@example.com"     # env form: SeededAdmins__0__Email
dotnet user-secrets set "EmailSettings:Password" "…"                  # optional; unsent email is reported, not fatal
```

There are no tests and no linter configured; `dotnet build` is the only check. `wwwroot/tailwind.css` is generated but **deliberately tracked** (the MSBuild target only runs when `node_modules/` exists, so CI/deploy machines rely on the committed copy) — rebuild it and commit it when markup classes change.

### Database safety

`DefaultConnection` points at a **live, shared, hosted Postgres**, and `Program.cs` runs `db.Database.MigrateAsync()` on every startup — so `dotnet run` against that connection applies any pending migration to production data. Generate migrations (`dotnet ef migrations add <Name>`) but do not apply them (`dotnet ef database update`, or running the app) without the user's explicit go-ahead. Do a full `dotnet build` before any `dotnet ef` command; never use `--no-build` on a possibly stale `bin/` (that once caused a live table drop).

## Architecture

**Vertical slices.** `Features/<Name>/{Pages,Models,Services,Components}` are self-contained; slices do not import each other's services. Shared coupling points are `Data/` (DbContext, `ApplicationUser`, `Team`, `MemberTeam`, `Member`), `Shared/` (`EmailService`, `MarkdownRenderer`, `YouTubeUrl`, `UkTime`, `AuthenticationStateExtensions`, `SeededAdmins`) and `Components/` (layout, `Routes.razor`, reusable UI). All feature services are registered `AddScoped` in `Program.cs` — register new ones there.

**DbContext pattern.** Services take `IDbContextFactory<AppDbContext>` and open a short-lived context per method (`await using var db = await factory.CreateDbContextAsync()`). A circuit-long scoped context throws on overlapping async renders. A scoped `AppDbContext` is also registered, but only because Identity's stores require one — don't inject it into feature services. EF calls are always async; read-only queries use `.AsNoTracking()`. Npgsql legacy timestamp behaviour is enabled globally.

**Render modes.** Pages are `@rendermode InteractiveServer` only when they need a circuit. Static SSR (no `@rendermode`) is required for anything that writes auth cookies (`LoginPage`; `/logout` is a minimal-API endpoint in `Program.cs` for the same reason) and is preferred for `Home` and `SiteHeader` — `SiteHeader` lives in `MainLayout`, so making it interactive would open a SignalR circuit on every route. Presentation behaviour (scroll reveal, header menus) is plain DOM code in `wwwroot/js/ui.js`, not JS interop, for the same reason. Circuit retention is deliberately trimmed in `Program.cs` for the 1.75 GB B1 App Service (also why the csproj uses Workstation GC).

**Auth and roles.** Login is password-less with two paths, chosen by the login field (`@` → email, else card number):
- `OpenWaterAuthService`: email verified against SPE's external OpenWater directory; also lets `SeededAdmins` in with no OpenWater record (the way back in if OpenWater is down).
- `MemberNumberAuthService`: AUSA card number checked against the read-only `members` table; valid one year from `PurchasedAt`; `MembershipType == "Committee"` → `CommitteeMember`, else `Member`. Card-number accounts start with no email; one added on the profile page (`ProfileService.UpdateEmailAsync`) also works for login — `LoginPage` tries `LoginWithAddedEmailAsync` when OpenWater has no record of the address, re-checking the membership's active/expiry status.

Roles are `Member`, `CommitteeMember`, `TeamLeader` (created at startup). They **do not inherit** — committee-level checks name both, `Roles = "CommitteeMember,TeamLeader"`. `RoleSync.AddMissingRolesAsync` only ever adds roles at login, never removes, so a Team Leader's upgrades stick. `ApplicationUser.CommitteeTitle` ("President", …) is a display label and grants nothing. Guard a page with `@attribute [Authorize(...)]` alone: `Routes.razor` already redirects anonymous users to `/login` and renders the access-denied panel, so don't add an `<AuthorizeView>` or manual role lookup for the same check. Use `<AuthorizeView Roles=…>` or `AuthenticationStateExtensions` only to vary content or behaviour within a page.

**Seeded admins.** Team Leader accounts come from the `SeededAdmins` config section, read once into the `SeededAdmins` singleton (used by startup seeding *and* `OpenWaterAuthService`). Never hard-code addresses. (README's "bootstrap administrator" section describing `adminEmail`/`FullAccessEmail` constants is outdated.)

**The `members` table is not owned by EF.** It is created and upserted by the offline Python tool in `Uploading Members/` (`MembersList.py`, uses `ConnectionStrings:MembersImport` in `appsettings.json`) and is `ExcludeFromMigrations()` in `AppDbContext`. The app only reads it — don't add migrations for it or write to it.

**`Program.cs` endpoints** beyond Razor routes: `/logout`, `/robots.txt` and `/sitemap.xml` (generated from the request; the public-route list is tagged `#UpdateLink` — extend it when adding a public page, and add members-only paths to the robots `Disallow` list), `/events/calendar.ics` (anonymous iCal feed), `/admin/export/attendance.csv` (TeamLeader only, plain GET so the browser handles the download), and permanent redirects for `/events/upcoming|past`. Guest event sign-up (`/events/register/{Token:guid}`) is anonymous by design; the unguessable token is its only protection.

**Email.** `EmailService` (MailKit) is only the transport; wording is written by the callers (`AdminService`, `TaskItemService`, event update notifications) as raw-string HTML. HTML-encode every interpolated member-supplied value (local `Encode(...)` helper), don't write a plain-text body (it's derived automatically), and inline any CSS. Use `SendManyAsync` (one SMTP connection) for lists, never `SendAsync` in a loop. See `note.md`.

**Markdown/URLs.** Tutorials and opportunities are member-authored Markdown rendered by `Shared/MarkdownRenderer.cs` (raw HTML stripped, links restricted by `IsSafeUrl`); `Shared/YouTubeUrl.cs` turns pasted links into embed URLs. Both handle untrusted input — keep them strict.

**Time.** Use `Shared/UkTime.cs` for event times (BST boundary bugs make events vanish an hour early).

## Conventions

- `@foreach` over mutable collections needs `@key`. No `!` null suppressors unless unavoidable.
- `TaskItem` uses `AssignmentStatus`, not `TaskStatus` (clashes with `System.Threading.Tasks`).
- Styling: `font-heading`/`font-kicker` utilities and `.spe-*` component classes in `Styles/input.css` (the only hand-written CSS); brand tokens `spe-blue #003DA5`, `spe-gold #F4A300`. Icons via `<Icon Name="…" />` (inline SVG, no icon-font CDN).
- Google Fonts stay as `<link>` in `App.razor`; don't move to `@import` in `input.css` (Tailwind emits it last, invalid, and browsers silently drop it).
- Images: originals go in `assets/` and are never served; run `npm run build:images`. Every `<img>` needs `width`/`height` and `loading="lazy"` (unless above the fold). Reference paths **all-lowercase** — dev is Windows, deploy is Linux.
- Files are split into labelled sections: `@* ---------- Name ---------- *@` in markup, `/* ---------- Name ---------- */` in C#/CSS.
- Editable copy (URLs, contact details, stats, hard-coded text, image paths) is tagged `#UpdateLink`; keep tagging new ones and grep for it to find content.
- Migrations are timestamp-named in `Migrations/`; give new columns sensible DB defaults so existing rows aren't silently changed (see `EmailNotificationsEnabled`).

## Deployment

Pushes to `master` trigger GitHub Actions (`.github/workflows/`) that build and deploy to Azure App Service (two workflows: `SPE-UoA` on windows-latest, `SPE-UoA-website` on ubuntu-latest). It needs WebSockets, HTTPS, and sticky sessions if scaled beyond one instance. Migrations apply themselves at startup.
