# SPE University of Aberdeen — Chapter Website

The web portal for the Society of Petroleum Engineers student chapter at the University of
Aberdeen. It is two things at once: a public shopfront for the chapter, and a private
workspace for the committee that runs it.

For a technical breakdown of every model, page, and service, see [SUMMARY.md](SUMMARY.md).

---

## Who it is for

**Prospective students and the public.** Anyone can browse the home page and the events
listing — upcoming events with dates, locations, and Instagram posts, plus past events with
star ratings and comments. No account needed. This is the recruiting surface.

**SPE members.** Anyone who signs in gets the members-only content: the opportunities board
(jobs and internships from energy companies, with full detail pages and application links)
and the courses library (curated YouTube learning videos). Sign-in is verified against real
SPE membership, so this content stays inside the society.

**The committee.** Committee members get a working area on top of that: their own profile
with strike status, their assigned tasks with the ability to mark them complete or failed,
and a tutorials hub of standard-operating-procedure videos. They can also publish and remove
events, opportunities, and courses.

**The chapter leadership.** Team Leaders run the admin area: create and edit member records,
change roles and committee titles, issue and remove strikes, assign tasks with deadlines
through either a member table or a calendar view, and delete accounts. Members are emailed
automatically when they receive a strike, have one removed, or are assigned a task.

---

## Identity roles and authorization

Access is controlled by **three ASP.NET Identity roles**, seeded automatically on startup:

| Role | Who holds it | Can access |
|---|---|---|
| *(anonymous)* | Any visitor | Home, Events (view and rate) |
| `Member` | Any verified SPE member | The above, plus Opportunities and Courses |
| `CommitteeMember` | Chapter officers | The above, plus Profile, Tasks, Tutorials, and create/delete rights on events, opportunities, and courses |
| `TeamLeader` | Chapter leadership | Everything, plus the Member Dashboard and Task Calendar |

Identity roles do **not** inherit from one another. The tiers above are cumulative only
because every committee-level check names both roles explicitly
(`Roles="CommitteeMember,TeamLeader"`), so a Team Leader passes on its own. Holding
`TeamLeader` alone is enough for the whole site; the bootstrap account is granted both roles
as belt-and-braces, not because it is required.

### Committee titles are not permissions

Alongside Identity roles, each member has a free-form **committee title** — "President",
"Vice President", "Treasurer", and so on. This is a **display label only**. It grants no
access whatsoever. Someone titled "President" whose Identity role is `Member` has no admin
rights. All access decisions are made on the three Identity roles above.

### How roles are assigned

Login is **password-less**. A user enters their email, and the app verifies it against the
external **OpenWater** SPE membership directory. If OpenWater has no record, sign-in is
refused and the user is offered a link to join SPE.

On a member's **first** login, they are given `CommitteeMember` if OpenWater reports them as a
student chapter officer, otherwise `Member`. On every login after that, existing roles are
left alone — so role changes made by a Team Leader in the admin panel are never overwritten by
a later login. Promoting someone to `TeamLeader` is a manual action in the Member Dashboard.

### Where it is enforced

Four layers, described in full in [SUMMARY.md](SUMMARY.md#3-authorization-system):

1. **Router** — `AuthorizeRouteView` in `Components/Routes.razor`. This is what makes page
   attributes take effect; a plain `RouteView` ignores them silently.
2. **Page** — `@attribute [Authorize(Roles = "…")]` on each `.razor` page.
3. **Markup** — `<AuthorizeView>` hides buttons and nav links from users who cannot use them.
4. **Service** — `AdminService` re-checks the acting user's role before role changes.

---

## Tech stack

| Layer | Technology |
|---|---|
| Runtime | .NET 10 (`net10.0`) |
| Web framework | ASP.NET Core, Blazor Web App — **Interactive Server** render mode |
| UI transport | SignalR (Blazor Server circuits) |
| Database | PostgreSQL |
| ORM | Entity Framework Core 10 with `Npgsql.EntityFrameworkCore.PostgreSQL` |
| Authentication | ASP.NET Core Identity (cookie), email-only sign-in against the OpenWater API |
| Styling | Tailwind CSS v4, compiled by the Tailwind CLI |
| Markdown | Markdig (opportunity descriptions) |
| Email | MailKit over SMTP |
| Icons / fonts | Font Awesome 4.7, Google Fonts (Montserrat, Open Sans, Roboto Mono) |
| Front-end tooling | Node.js + npm (Tailwind CLI only — no bundler, no SPA framework) |

There is no JavaScript framework. The only custom JS is two small files: a lazy-loader for
Instagram embeds and a scroll-reveal animation helper.

## External services

| Service | Used for | Failure behaviour |
|---|---|---|
| **OpenWater** (`openwater-os.secure-platform.com`) | Verifying SPE membership at login and pulling profile data | Login fails for everyone except the bootstrap admin |
| **PostgreSQL** (currently hosted on Supabase) | All application data, including Identity tables | App fails to start — migrations run on startup |
| **SMTP** (currently Gmail) | Strike and task notification emails | Logged and reported in the UI; the underlying action still succeeds |
| **Instagram embed API** | Rendering event posts | Embed slots stay blank; the rest of the page is unaffected |

---

## Project layout

```text
Features/               One folder per business capability (vertical slices)
  Authentication/         Email sign-in via the OpenWater directory
  Courses/                Learning-video library (sign-in required)
  Events/                 Event listings, ratings, and management
  MemberProfile/          Member profile page
  Opportunities/          Jobs and internships board
  PresidentAdmin/         Member dashboard, strikes, task assignment, calendar
  Tasks/                  Personal task tracking
  Tutorials/              Committee SOP video hub
Data/                   AppDbContext and the ApplicationUser Identity model
Shared/                 Cross-cutting services and models (email)
Components/             App shell, layouts, router, shared components
Styles/input.css        Tailwind source and SPE design tokens
Migrations/             EF Core migration history
wwwroot/                Static assets, compiled CSS, custom JS
```

Each feature folder keeps its own `Models/`, `Pages/`, and `Services/` together. Features do
not reference each other — they share only `Data/` and `Shared/`.

---

## Writing content with Markdown

Two things are written in Markdown by the committee:

- **Tutorial articles** — when a tutorial's format is *Written article* rather than *YouTube video*
- **Opportunity postings** — the body of a job or internship listing

Both are rendered by `Shared/MarkdownRenderer.cs`. New to Markdown? The
[Markdown cheat sheet](https://www.markdownguide.org/cheat-sheet/) is the quickest reference.

### Supported syntax

| What you write | What you get |
|---|---|
| `# Heading` … `#### Heading` | Headings, four levels |
| `**bold**` · `*italic*` · `***both***` | **bold** · *italic* · ***both*** |
| `- item` or `* item` | Bullet list (indent two spaces to nest) |
| `1. item` | Numbered list |
| `a. item` · `i. item` | Lettered and roman-numeral lists |
| `[text](https://example.com)` | Link |
| `https://example.com` on its own | Turned into a link automatically |
| `[text](mailto:someone@example.com)` | Email link |
| `[text](/tutorials)` | Link to another page on this site |
| `![description](https://example.com/photo.png)` | Image |
| `> quoted text` | Block quote |
| `` `code` `` | Inline code |
| ` ```csharp ` … ` ``` ` | Fenced code block, optionally with a language |
| `\| a \| b \|` with a `\|---\|---\|` row under it | Table |
| `---` on its own line | Horizontal rule |
| `~~struck through~~` | ~~struck through~~ |
| `==highlighted==` | Highlighted text |
| `H~2~O` · `x^2^` | Subscript and superscript |
| `++inserted++` | Underlined/inserted text |
| Two spaces at the end of a line | Line break within a paragraph |

A blank line starts a new paragraph. A single newline does not — put two spaces at the end of
a line if you want a break without a new paragraph.

### Not supported

Task lists (`- [ ] item`), footnotes, definition lists, LaTeX maths, and Mermaid diagrams are
**not** enabled. They will show as plain text rather than rendering.

**Raw HTML is deliberately disabled.** Typing `<b>bold</b>` prints the tags rather than
applying them, and `javascript:` links are stripped. This is a security measure, not an
oversight: article authors are trusted committee members, but without it a committee member
could plant a script in an article and hijack a Team Leader's session the next time they
opened it. Use the Markdown syntax above instead.

To change what is supported, edit the pipeline in `Shared/MarkdownRenderer.cs` — and update
the tables above to match. Note that Markdig's `UseAdvancedExtensions()` must **not** be used:
it enables generic attributes, which would let an author attach an `onclick` handler to any
element and reopen the hole described above.

---

## Running locally

### Prerequisites

- .NET 10 SDK
- Node.js and npm
- A PostgreSQL database (local or hosted)

### Steps

```bash
# 1. Install the Tailwind CLI
npm install

# 2. Configure secrets (see below) — do not skip this
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=…;Database=…;Username=…;Password=…"

# 3. Run. Migrations are applied automatically on startup.
dotnet run
```

The app starts on <http://localhost:5169> (and <https://localhost:7256> with the `https`
profile). Tailwind compiles automatically as part of `dotnet build` via an MSBuild target, so
step 1 is all the CSS setup needed.

While working on styles, run the watcher in a second terminal:

```bash
npm run watch:css
```

---

## Configuration

Two settings groups are required. **Do not put real values in `appsettings.json`** — use user
secrets in development and environment variables in production.

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=<host>;Port=5432;Database=<db>;Username=<user>;Password=<pw>;SSL Mode=Require;Trust Server Certificate=true"
  },
  "EmailSettings": {
    "SmtpHost": "<smtp-host>",
    "SmtpPort": 587,
    "Username": "<smtp-user>",
    "Password": "<smtp-password>",
    "From": "<sender-address>"
  }
}
```

As environment variables, nested keys use a double underscore:

```bash
ConnectionStrings__DefaultConnection="Host=…"
EmailSettings__SmtpHost="smtp.gmail.com"
EmailSettings__Password="…"
```

If `EmailSettings` is left blank the app still runs — `EmailService` logs a warning and
reports the email as unsent, while strikes and task assignments still save correctly.

> **⚠️ Security notice.** The `appsettings.json` currently in this repository contains a live
> database password and a live Gmail app password in plain text. Anyone with repository access
> has full control of the production database. **Rotate both credentials and move them out of
> source control** before this repo is shared, made public, or handed to a new committee. See
> [SUMMARY.md](SUMMARY.md#7-known-issues-and-rough-edges).

### Bootstrap administrator

A Team Leader account is seeded on every startup, and the same address is hardcoded in
`OpenWaterAuthService` as a fallback that can sign in even when OpenWater has no record for
it. Change both to a chapter-owned address when the current holder hands over:

- `Program.cs` — the `adminEmail` constant
- `Features/Authentication/Services/OpenWaterAuthService.cs` — the `FullAccessEmail` constant

---

## Deployment

The app is a standard ASP.NET Core server application. It needs a host that can run a
long-lived .NET process — **not** static hosting, because Blazor Server keeps an open SignalR
connection per user.

### Build

```bash
npm install
npm run build:css
dotnet publish -c Release -o ./publish
```

Deploy the contents of `./publish` and start it with `dotnet SPE_website.dll`.

### Host requirements

- **WebSockets must be enabled.** Blazor Server needs them; without WebSockets it falls back
  to long polling, which is noticeably slower and less reliable.
- **Sticky sessions** if running more than one instance — a circuit is bound to the instance
  that created it.
- **HTTPS**, since `UseHttpsRedirection` and `UseHsts` are enabled outside development.
- Set `ASPNETCORE_ENVIRONMENT=Production`.

### Database migrations

Migrations are applied automatically at startup by `db.Database.MigrateAsync()`, so no manual
step is needed on deploy. To add one during development:

```bash
dotnet ef migrations add <Name>
dotnet ef database update
```

Startup also seeds the `TeamLeader`, `CommitteeMember`, and `Member` roles and the bootstrap
admin account, so a fresh empty database is enough to get a working deployment.

### Deployment checklist

- [ ] Connection string set via environment variables, not `appsettings.json`
- [ ] SMTP credentials set via environment variables
- [ ] Committed credentials rotated and removed from source control
- [ ] Bootstrap admin email changed to a chapter-owned address
- [ ] WebSockets enabled on the host
- [ ] HTTPS certificate in place
