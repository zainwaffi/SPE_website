# AGENTS.md — SPE Society Web App

## Architecture

Vertical Slice — features are self-contained in `Features/<Name>/`. No cross-feature service imports.

```
SPE_website/
├── Data/
│   ├── AppDbContext.cs          ← EF Core DbContext (extends IdentityDbContext<ApplicationUser>)
│   └── Models/
│       ├── ApplicationUser.cs   ← IdentityUser + StrikeCount, CommitteeRole, ProfilePictureUrl
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
| *(unauthenticated)* | Home, Events (read), Opportunities (read) |
| `CommitteeMember` | + Profile, Tasks, Tutorials, CRUD on Events/Opportunities |
| `President` | + Member dashboard, strike system, task calendar, global edit rights |

Use `[Authorize(Roles = "CommitteeMember,President")]` on page `@code` blocks or `<AuthorizeView Roles="...">` in markup.

## Code Conventions

- **Blazor pages:** `@page "/route"` + `@rendermode InteractiveServer` — except auth pages (see below)
- **Auth pages** (`/login`, `/logout`): NO `@rendermode` directive → Static SSR so `SignInManager` can write cookies via `HttpContext`
- **Logout endpoint:** `app.MapPost("/logout", ...)` minimal API in `Program.cs`, `.DisableAntiforgery()`
- **Services:** Primary constructor injection `(AppDbContext db)`, registered as `Scoped` in `Program.cs`
- **EF:** Always async — `ToListAsync`, `FirstOrDefaultAsync`, `SaveChangesAsync` — never synchronous
- **`TaskItem` naming:** `AssignmentStatus` enum (not `TaskStatus`) to avoid conflict with `System.Threading.Tasks.TaskStatus`
- **Nullable:** Use `?` and null-coalescing. No `!` suppressors unless unavoidable.

## Build & Run

```bash
# First time — install Tailwind CLI
npm install

# Build Tailwind CSS (one-time)
npm run build:css

# Watch Tailwind during development
npm run watch:css

# Run the app
dotnet run

# EF Core migrations
dotnet ef migrations add <MigrationName>
dotnet ef database update
```

> MSBuild auto-runs `build:css` on every `dotnet build` if `node_modules/` exists.

## SPE Branding (Tailwind v4 custom tokens)

Defined in `Styles/input.css` via `@theme`:
- `bg-spe-blue` / `text-spe-blue` → `#003DA5`
- `bg-spe-gold` / `text-spe-gold` → `#F4A300`

## Email Notifications (MailKit)

Triggered by:
- Strike added → email to affected member
- Task assigned → email to assigned member

Config keys in `appsettings.json` under `"EmailSettings"`: `SmtpHost`, `SmtpPort`, `Username`, `Password`, `From`.
