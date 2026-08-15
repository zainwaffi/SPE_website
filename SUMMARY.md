# SPE Chapter Website — Technical Summary

A reference for the codebase as it currently stands: every model, page, service, and the
authorization system that ties them together. For setup, deployment, and audience, see
[README.md](README.md).

---

## 1. Architecture at a glance

**Blazor Web App, server render mode only.** Not WebAssembly, not a hosted client/server
split. Interactivity runs over SignalR (`@rendermode InteractiveServer`), which is why pages
inject services that talk to `AppDbContext` directly — there is no HTTP API layer in between.

**Vertical slice structure.** Code is grouped by business capability rather than technical
layer. Each slice under `Features/` owns its `Models/`, `Pages/`, and `Services/`. No slice
imports another slice's service; the only shared coupling points are `Data/` (the DbContext)
and `Shared/` (email).

```
Razor page (@code, InteractiveServer circuit)
   └─> scoped feature service (e.g. EventService)
        └─> AppDbContext (EF Core)
             └─> PostgreSQL (Npgsql)
```

**Composition root** is `Program.cs`: registers Identity, EF Core, and nine scoped services;
then on startup seeds the three Identity roles and a bootstrap Team Leader account, and
applies pending migrations via `db.Database.MigrateAsync()`.

**Render-mode split.** Most pages are `InteractiveServer`. `LoginPage.razor` deliberately has
**no** `@rendermode` — it must run as static SSR so `SignInManager` can write the auth cookie
through `HttpContext`, which is impossible over a SignalR circuit. `/logout` is a minimal API
endpoint in `Program.cs` for the same reason.

---

## 2. Data models

### Core / Identity

| Model | File | Key fields | Notes |
|---|---|---|---|
| `ApplicationUser` | `Data/Models/ApplicationUser.cs` | `FullName`, `StrikeCount`, `CommitteeTitle`, `IsStudentChapterOfficer`, `OpenWaterMemberId`, `OpenWaterOrganization`, `OpenWaterProfileJson`, `AssignedTasks` | Extends `IdentityUser`. No local passwords — refreshed from OpenWater on every login. `CommitteeTitle` is a **free-form string** ("President", "Vice President"), separate from Identity roles. |
| `AppDbContext` | `Data/AppDbContext.cs` | `Events`, `EventRatings`, `Opportunities`, `TaskItems`, `Tutorials`, `Courses` | Extends `IdentityDbContext<ApplicationUser>`, so Identity and feature tables share one database. |

Two relationships are configured explicitly in `OnModelCreating`:

- `EventRating` → `Event` **cascade delete**: deleting an event removes its ratings.
- `TaskItem` → `ApplicationUser` **set null**: deleting a member clears the assignment but
  preserves task history rather than destroying it.

`Data/Models/Enums.cs` is now an empty namespace declaration. The old `CommitteeRole` enum was
replaced by the `CommitteeTitle` string in migration
`20260812142404_ReplaceCommitteeRoleWithTitleAndStringCategoryRole`.

### Feature models

| Model | File | Key fields | Notes |
|---|---|---|---|
| `Event` | `Features/Events/Models/Event.cs` | `Title`, `Description`, `Date`, `Location`, `IsUpcoming`, `Category`, `InstagramEmbedUrl`, `ImageUrl`, `GoogleCalendarEventId`, `Ratings` | `IsUpcoming` and `GoogleCalendarEventId` are stored but **not used for filtering** — upcoming/past is computed live from `Date` vs `UtcNow`. `Location` is used as a URL (rendered as a "View Location" link). |
| `EventCategory` (enum) | `Features/Events/Models/EventCategory.cs` | `Talk`, `SiteVisit`, `Workshop`, `Other` | Persisted as `integer`. |
| `EventRating` | `Features/Events/Models/EventRating.cs` | `EventId`, `Stars`, `Comment` | Anonymous 1–5 star feedback on past events. |
| `Opportunity` | `Features/Opportunities/Models/Opportunity.cs` | `Title`, `Description`, `ExternalUrl` | `Description` is Markdown, rendered with Markdig. |
| `TaskItem` | `Features/Tasks/Models/TaskItem.cs` | `Title`, `Description`, `Deadline`, `Status`, `AssignedToUserId`, `AssignedTo` | Status enum is `AssignmentStatus` (`Processing`/`Completed`/`Failed`) — named to avoid clashing with `System.Threading.Tasks.TaskStatus`. |
| `Tutorial` | `Features/Tutorials/Models/Tutorial.cs` | `Title`, `Description`, `YoutubeEmbedUrl`, `CategoryRole` | `CategoryRole` is a free-form string defaulting to `"Member"`. |
| `Course` | `Features/Courses/Models/Course.cs` | `Title`, `Description`, `YoutubeEmbedUrl` | Same shape as `Tutorial` minus role gating. |
| `OpenWaterMemberProfile` | `Features/Authentication/Models/` | `Email`, `FullName`, `StudentId`, `Organization`, `DegreeProgramLevel`, `IsStudentOfficer`, `IsStudentMember`, `RawJson` | DTO only — never persisted directly; mapped onto `ApplicationUser`. |
| `EmailSettings` | `Shared/Models/EmailSettings.cs` | `SmtpHost`, `SmtpPort`, `Username`, `Password`, `From` | Bound from the `EmailSettings` config section. |
| `EmailResult` | `Shared/Models/EmailResult.cs` | `Sent`, `Error` | Record. Lets callers distinguish "delivered" from "email disabled" from "SMTP rejected it" without exceptions. |

---

## 3. Authorization system

### The two parallel role concepts

This is the single most important thing to understand about the codebase, and the easiest to
get wrong:

| | **Identity roles** | **Committee title** |
|---|---|---|
| Stored in | `AspNetRoles` / `AspNetUserRoles` | `ApplicationUser.CommitteeTitle` |
| Values | `TeamLeader`, `CommitteeMember`, `Member` | Free-form string, e.g. "President", "Vice President" |
| Purpose | **Drives all access control** | Display label only |
| Used by | `[Authorize]`, `<AuthorizeView>`, `CanManageRolesAsync` | Admin table, task-assignment dropdown |

`CommitteeTitle` grants **no permissions whatsoever**. A member titled "President" with the
Identity role `Member` has no admin access. Only the three Identity roles matter for security.

The three roles are seeded on every startup in `Program.cs`.

### Where authorization is enforced

**1. Router level.** `Components/Routes.razor` uses `AuthorizeRouteView`. This is what makes
`[Authorize]` attributes work at all — a plain `RouteView` silently ignores them. Anonymous
users hit `RedirectToLogin`; authenticated users lacking the role get an "Access denied" panel.

**2. Page level** via `@attribute`:

| Page | Attribute |
|---|---|
| `Home.razor`, `EventsPage.razor` | `[AllowAnonymous]` |
| `CoursesPage.razor`, `OpportunitiesPage.razor`, `OpportunityDetailPage.razor` | `[Authorize]` (any signed-in user) |
| `ProfilePage.razor`, `TasksPage.razor`, `TutorialsPage.razor` | `[Authorize(Roles = "CommitteeMember,TeamLeader")]` |
| `MemberDashboardPage.razor`, `TaskCalendarPage.razor` | `[Authorize(Roles = "TeamLeader")]` |

`LoginPage.razor` and `NotFound.razor` carry no attribute and are reachable by anyone.

**3. Markup level** via `<AuthorizeView>`, which hides UI inside otherwise-accessible pages —
the "+ Add Event" / "+ Add" / "+ Add Video" buttons and Delete links are all wrapped in
`<AuthorizeView Roles="CommitteeMember,TeamLeader">`. `SiteHeader.razor` and `MainLayout.razor`
use it to hide nav and footer links from users who cannot follow them.

**4. Service level (defense in depth).** `AdminService.UpdateMemberDetailsAsync` calls
`CanManageRolesAsync(actingUserId)` and refuses the write unless the *acting* user holds
`TeamLeader` — independent of any page attribute.

**5. Middleware.** Standard `UseAuthentication()` / `UseAuthorization()`, plus
`AddCascadingAuthenticationState()` so `AuthenticationStateProvider` can be injected anywhere.
There is no custom `AuthenticationStateProvider`; it is the default cookie-backed one.

### Login flow

Password-less, verified against the external OpenWater membership directory.

1. User submits an email on `LoginPage.razor` (static SSR `EditForm`, `FormName="login"`).
2. `OpenWaterAuthService.LoginWithEmailAsync` GETs the OpenWater prefill endpoint
   (`openwater-os.secure-platform.com/societypetroleumengineers/prefill?emailOrUserId=…`).
3. The response's top-level `success` flag decides whether a record exists. Fields arrive as
   alias/value pairs under `data.fields`; the service picks out `studentEmail`, `studentName`,
   `studentID`, `collegeUniversityName`, `studentDegProgLevel`, `isStudentChapterOfficer`,
   and `isStudentMember`.
4. No record → login refused, with a "Join SPE" call-to-action. The one exception is the
   hardcoded `FullAccessEmail` bootstrap admin, which is allowed through regardless.
5. The `ApplicationUser` is created or updated from the profile (`ApplyProfile`).
6. `SyncRolesAsync` reconciles Identity roles. **Roles are only assigned on first login** —
   `CommitteeMember` if OpenWater reports the user as a chapter officer, otherwise `Member`.
   On subsequent logins existing roles are left untouched, so a Team Leader's manual role
   changes are never overwritten. The bootstrap admin always gets `TeamLeader` +
   `CommitteeMember` re-applied.
7. `SignInManager.SignInAsync(user, isPersistent: false)` issues the cookie — session-scoped,
   so users are signed out when the browser closes.
8. Logout is `POST /logout`, a minimal API endpoint with `.DisableAntiforgery()` because it is
   posted from a plain HTML `<form>` outside Blazor's antiforgery flow.

After a successful login the user is returned to `returnUrl` if present, guarded by
`SafeReturnUrl()` which rejects anything that is not a site-relative path (open-redirect
protection).

---

## 4. Services

All nine are registered `Scoped` in `Program.cs`. In server-side Blazor, scope is the SignalR
circuit, not the HTTP request — a service instance lives as long as the user's tab.

| Service | Dependencies | Public methods | Injected into |
|---|---|---|---|
| `OpenWaterAuthService` | `IHttpClientFactory`, `UserManager`, `SignInManager` | `LoginWithEmailAsync` | `LoginPage` |
| `EventService` | `AppDbContext` | `GetUpcomingAsync`, `GetPastAsync`, `GetByIdAsync`, `CreateAsync`, `UpdateAsync`, `DeleteAsync`, `AddRatingAsync` | `EventsPage` |
| `OpportunityService` | `AppDbContext` | `GetAllAsync`, `GetByIdAsync`, `CreateAsync`, `UpdateAsync`, `DeleteAsync` | `OpportunitiesPage`, `OpportunityDetailPage` |
| `CourseService` | `AppDbContext` | `GetAllAsync`, `CreateAsync`, `DeleteAsync` | `CoursesPage` |
| `TaskItemService` | `AppDbContext` | `GetForUserAsync`, `GetAllAsync`, `CreateAsync`, `UpdateStatusAsync`, `DeleteAsync` | `TasksPage`, `MemberDashboardPage` |
| `TutorialService` | `AppDbContext` | `GetAllAsync`, `GetForRoleAsync`, `CreateAsync`, `UpdateAsync`, `DeleteAsync` | `TutorialsPage` |
| `ProfileService` | `AppDbContext` | `GetByIdAsync`, `UpdateProfileAsync` | `ProfilePage` |
| `AdminService` | `AppDbContext`, `UserManager`, `EmailService`, `ILogger` | `GetAllMembersAsync`, `AddStrikeAsync`, `RemoveStrikeAsync`, `AssignTaskAsync`, `UpdateMemberTitleAsync`, `CreateMemberAsync`, `GetPrimaryRoleAsync`, `UpdateMemberDetailsAsync`, `CanManageRolesAsync`, `DeleteMemberAsync` | `MemberDashboardPage`, `TaskCalendarPage` |
| `EmailService` | `IOptions<EmailSettings>`, `ILogger` | `SendAsync` | `AdminService` only (not injected into any page) |

### Notable service behaviour

- **`EmailService` never throws.** If SMTP is unconfigured it logs a warning and returns a
  failed `EmailResult`. `AdminService.SendNotificationAsync` wraps calls in a further
  try/catch, so an unreachable mail server can never roll back a strike or task assignment
  that is already committed to the database. The UI reports the difference.
- **HTML encoding.** `AdminService.Encode` runs member-supplied names and task titles through
  `WebUtility.HtmlEncode` before interpolating them into HTML mail bodies.
- **Emails are sent** on strike added, strike removed, and task assigned. Member creation does
  *not* send an email in the current code.

### Dead code (defined, never called)

Four public service methods have no callers anywhere in the solution:

| Method | Note |
|---|---|
| `TutorialService.GetForRoleAsync` | `TutorialsPage` calls `GetAllAsync()` and groups by `CategoryRole` client-side, showing **every** tutorial to any committee member. The per-role filtering this method implements is not actually applied. |
| `ProfileService.UpdateProfileAsync` | `ProfilePage` is read-only; there is no edit form. |
| `AdminService.CreateMemberAsync` | The "add member" UI is present in the page's `@code` as unused fields but not wired up. |
| `AdminService.UpdateMemberTitleAsync` | Superseded by `UpdateMemberDetailsAsync`. |

---

## 5. Pages

| Page | Route(s) | Access | What it does | Services used |
|---|---|---|---|---|
| `Home.razor` | `/` | Anonymous | Hero, About Us, Why Join Us, Meet the Committee, Chapter Achievements. All content is hardcoded markup. | — |
| `LoginPage.razor` | `/login` | Anonymous, **static SSR** | Email-only sign-in form; shows a "Join SPE" link when the email is unknown; honours `returnUrl`. | `OpenWaterAuthService` |
| `EventsPage.razor` | `/events`, `/events/upcoming`, `/events/past` | Anonymous (create/delete gated) | Custom month calendar picker, upcoming/past grids, lazy Instagram embeds, star ratings with comments, live search. | `EventService`, `IJSRuntime` |
| `OpportunitiesPage.razor` | `/opportunities` | `[Authorize]` | Searchable job/internship list. | `OpportunityService` |
| `OpportunityDetailPage.razor` | `/opportunities/{id:int}` | `[Authorize]` | Markdown detail view with external link. | `OpportunityService` |
| `CoursesPage.razor` | `/courses` | `[Authorize]` | Searchable YouTube course grid. | `CourseService` |
| `TutorialsPage.razor` | `/tutorials` | `CommitteeMember,TeamLeader` | SOP videos grouped by `CategoryRole`. | `TutorialService` |
| `ProfilePage.razor` | `/profile` | `CommitteeMember,TeamLeader` | Read-only profile: title, strike status, assigned tasks. | `ProfileService`, `AuthenticationStateProvider` |
| `TasksPage.razor` | `/tasks` | `CommitteeMember,TeamLeader` | The signed-in member's own tasks, with complete/fail actions. | `TaskItemService`, `AuthenticationStateProvider` |
| `MemberDashboardPage.razor` | `/admin/members` | `TeamLeader` | Member table with task counts, strike add/remove, task assignment, edit and delete. | `AdminService`, `TaskItemService`, `AuthenticationStateProvider` |
| `TaskCalendarPage.razor` | `/admin/calendar` | `TeamLeader` | Month calendar; click a date to assign a task to a member. | `AdminService` |
| `NotFound.razor` | `/not-found` | Anonymous | 404 target for `UseStatusCodePagesWithReExecute`. | — |

### Shared components

| Component | Purpose |
|---|---|
| `Components/App.razor` | HTML document root: stylesheets, Instagram preconnect hints, script tags. |
| `Components/Routes.razor` | Router + `AuthorizeRouteView` + `NotAuthorized`/`Authorizing` templates. |
| `Components/Layout/MainLayout.razor` | Fixed background image, header, `@Body`, footer with role-filtered quick links and social icons. |
| `Components/Layout/SiteHeader.razor` | Sticky header, hamburger nav, role-gated Tools submenu, logout form. |
| `Components/Layout/ReconnectModal.razor` | Custom SignalR reconnection UI (`.razor.css` + `.razor.js`). |
| `Components/Layout/NavMenu.razor` | Present in the tree but not referenced by the current layout. |
| `Components/Shared/FadeUp.razor` | Scroll-reveal animation wrapper (`IJSRuntime`). |
| `Components/Shared/RedirectToLogin.razor` | Sends anonymous users to `/login?returnUrl=…`. Covers in-circuit navigation, where no HTTP challenge can fire. |

---

## 6. Front-end assets

- **Tailwind CSS v4**, compiled `Styles/input.css` → `wwwroot/tailwind.css`. An MSBuild target
  (`BuildTailwind`) runs `npm run build:css` before every `dotnet build`, conditional on
  `node_modules` existing.
- **Design tokens** in the `@theme` block: `--color-spe-blue: #003DA5`, `--color-spe-gold:
  #F4A300`, `--color-spe-cobalt: #0046AD`, `--color-spe-endeavor: #0067B1`, `--color-spe-ink`.
- **Component classes**: `.spe-page-shell`, `.spe-page-kicker`, `.spe-page-title`,
  `.spe-primary-btn`, `.spe-gold-btn`, `.spe-card`, plus `.ig-embed-slot` / `.ig-lazy` for the
  Instagram placeholder.
- **Fonts**: Montserrat (headings), Open Sans (body), Roboto Mono (kickers), via Google Fonts.
- **Font Awesome 4.7** from cdnjs, used for the footer social icons.
- `wwwroot/js/instagram-embed.js` — lazy-loads Instagram embeds via `IntersectionObserver`,
  and only fetches Instagram's `embed.js` when the first post approaches the viewport.
- `wwwroot/js/animations.js` — scroll-reveal helper backing `FadeUp`.

---

## 7. Known issues and rough edges

Recorded as-is; none are addressed here.

1. **Secrets are committed.** `appsettings.json` contains a live Supabase PostgreSQL password
   and a Gmail app password in plain text, in version control. Both should be rotated and
   moved to user secrets or environment variables.
2. **Hardcoded bootstrap admin.** `OpenWaterAuthService.FullAccessEmail` and the seeding block
   in `Program.cs` both hardcode a personal Gmail address that always receives `TeamLeader`.
3. **Tutorials are not actually role-filtered** — see `GetForRoleAsync` above.
4. **`MemberDashboardPage` has a redundant `isAuthorized` check** in `@code` that predates the
   `[Authorize]` attribute and the `AuthorizeView` guard now wrapping the page.
5. **Unused `Event` fields**: `IsUpcoming` and `GoogleCalendarEventId` are persisted but never
   drive behaviour.
6. **Unused warnings**: `MemberDashboardPage` still declares `createError` for the add-member
   flow that was never wired up.
7. **Live search is a round trip per keystroke.** The three search bars bind on `oninput` over
   the SignalR circuit. Fine at current data volumes; would need debouncing if lists grow.
