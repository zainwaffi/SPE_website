# AGENTS.md — SPE Society Web App

## Architecture

Vertical Slice — features are self-contained in `Features/<Name>/`. No cross-feature service imports.

```
SPE_website/
├── Data/
│   ├── AppDbContext.cs          ← EF Core DbContext (extends IdentityDbContext<ApplicationUser>)
│   └── Models/
│       ├── ApplicationUser.cs   ← IdentityUser + StrikeCount, CommitteeRole
│       └── Enums.cs             ← CommitteeRole enum
├── Features/
│   ├── Events/                  ← Models, Services, Pages for upcoming + past events + ratings
│   ├── Opportunities/           ← SPE opportunity link list
│   ├── Authentication/          ← Login, Access Denied pages (Static SSR — no @rendermode)
│   ├── MemberProfile/           ← Profile dashboard showing strikes + tasks
│   ├── Tasks/                   ← Task management (view, update status)
│   ├── Tutorials/               ← Role-based YouTube SOP hub (folder/category structure)
│   └── PresidentAdmin/          ← Member dashboard, strike system, interactive task calendar
├── Shared/
│   ├── Services/EmailService.cs ← MailKit SMTP wrapper
│   └── Models/EmailSettings.cs
└── Components/
    ├── Layout/MainLayout.razor  ← Top navbar (no sidebar); SPE branding
    ├── Pages/                   ← Error.razor, NotFound.razor only
    ├── App.razor
    └── _Imports.razor           ← All feature namespaces imported here
```

## RBAC Roles

| Role | Access |
|------|--------|
| *(unauthenticated)* | Home, Events (read) |
| `Member` | baseline signed-in role |
| `CommitteeMember` | + Profile, Tasks, Tutorials, Opportunities, Courses, CRUD on Events/Opportunities |
| `TeamLeader` | + Member dashboard, strike system, task calendar, global edit rights |

Use `@attribute [Authorize(Roles = "CommitteeMember,TeamLeader")]` on the page, or
`<AuthorizeView Roles="...">` in markup to vary content within a page.

**Don't stack both plus a runtime role lookup for the same check.** `Routes.razor` already
renders a shared access-denied panel (and redirects anonymous users to `/login`), so a page
guarded by `[Authorize]` needs no `<AuthorizeView>` wrapper or "Access Denied" block of its own.

## Code Conventions

- **Blazor pages:** `@rendermode InteractiveServer` **only when the page actually needs a circuit.**
  A page with no event handlers and no mutable state should be static SSR — `Home`, `SiteHeader`
  and `LoginPage` are. `SiteHeader` especially: it sits in `MainLayout`, so making it interactive
  opens a SignalR circuit on *every* route.
- **Auth pages** (`/login`, `/logout`): NO `@rendermode` → Static SSR so `SignInManager` can write cookies via `HttpContext`
- **Logout endpoint:** `app.MapPost("/logout", ...)` minimal API in `Program.cs`, `.DisableAntiforgery()`
- **Services:** Primary constructor injection of `IDbContextFactory<AppDbContext>`, registered
  `Scoped` in `Program.cs`. Each method does `await using var db = await dbFactory.CreateDbContextAsync();`
  — a single scoped `DbContext` shared across a circuit throws on overlapping renders.
- **EF:** Always async — `ToListAsync`, `FirstOrDefaultAsync`, `SaveChangesAsync` — never synchronous.
  Read-only queries get `.AsNoTracking()`.
- **Lists:** every `@foreach` rendering a mutable collection needs `@key`
- **No JS interop for presentation.** Scroll reveals and the header menus are plain DOM work in
  `wwwroot/js/ui.js`; adding interop would force those components back onto a circuit.
- **Styling:** use the `font-heading` / `font-kicker` utilities and the `.spe-*` component classes
  in `Styles/input.css` — not inline `style="font-family:..."`.
- **Icons:** `<Icon Name="..." />` (`Components/Shared/Icon.razor`), inline SVG. No icon-font CDN.
- **Google Fonts:** loaded via `<link>` in `App.razor`. Do **not** move this to `@import` in
  `input.css` — Tailwind emits `@import` at the end of the generated file, which is spec-invalid,
  and browsers silently drop it (the fonts then never load, with no visible error).
- **`TaskItem` naming:** `AssignmentStatus` enum (not `TaskStatus`) to avoid conflict with `System.Threading.Tasks.TaskStatus`
- **Nullable:** Use `?` and null-coalescing. No `!` suppressors unless unavoidable.

## Build & Run

```bash
# First time — install Tailwind CLI + sharp
npm install

# Build Tailwind CSS (one-time)
npm run build:css

# Watch Tailwind during development
npm run watch:css

# Re-encode images: assets/ (masters) -> wwwroot/ (resized WebP)
npm run build:images

# Run the app
dotnet run

# EF Core migrations
dotnet ef migrations add <MigrationName>
dotnet ef database update
```

> MSBuild auto-runs `build:css` on `dotnet build` if `node_modules/` exists. The target is
> incremental — it only re-runs when `Styles/input.css` or a `.razor` file has changed.

## Images

Masters live in `assets/` and are **never served**. `npm run build:images` resizes and
re-encodes them into `wwwroot/` as WebP. Add a new image by dropping the original in
`assets/` (or `assets/members/`) and re-running that script — don't put large originals
in `wwwroot/`.

Every `<img>` needs `width`/`height` (prevents layout shift) and `loading="lazy"` unless
it is above the fold. Reference paths are **all-lowercase**: the app is developed on
Windows but deployed on Linux, where `/images/SPE-logo.png` and `/images/spe-logo.png`
are different files.

## SPE Branding (Tailwind v4 custom tokens)

Defined in `Styles/input.css` via `@theme`:
- `bg-spe-blue` / `text-spe-blue` → `#003DA5`
- `bg-spe-gold` / `text-spe-gold` → `#F4A300`

## Email Notifications (MailKit)

Triggered by:
- Strike added → email to affected member
- Task assigned → email to assigned member
- Task completed → email to team leaders
- Event updated → email to everyone signed up to attend it

Config keys in `appsettings.json` under `"EmailSettings"`: `SmtpHost`, `SmtpPort`, `Username`, `Password`, `From`.

`EmailService.SendAsync` sends to one person. `SendManyAsync` sends a personalised message to many
over a **single** SMTP connection — use it for anything addressing a whole list, because
`SendAsync` in a loop reconnects and re-authenticates per recipient and will stall the request
that triggered it.

## Seeded Admin Accounts

The Team Leader accounts created (and re-applied) at every startup, so a deployment always has
someone who can sign in and grant roles. They are also the only addresses that can log in when
the external OpenWater directory has no record of them, which makes them the way back in if that
service is down.

Configured under `"SeededAdmins"` — **never hard-coded**, and read in exactly one place
(`SeededAdmins`, registered in `Program.cs`) because both startup seeding and `OpenWaterAuthService`
need the same answer:

```bash
# development
dotnet user-secrets set "SeededAdmins:0:Email" "you@example.com"

# deployment — the default provider reads `__` as `:`
SeededAdmins__0__Email=you@example.com
SeededAdmins__0__Name=SPE Team Leader
```

With none configured the app still starts, logs a warning, and seeds nobody.
