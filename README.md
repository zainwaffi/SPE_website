# SPE University of Aberdeen — Chapter Website

The web portal for the Society of Petroleum Engineers student chapter at the University of
Aberdeen. It is two things at once: a public shopfront for the chapter, and a private
workspace for the committee that runs it.

Built with **Blazor Server (.NET 10)**, **PostgreSQL** via Entity Framework Core, and
**Tailwind CSS v4**.

- Full technical breakdown of every model, page and service: **[SUMMARY.md](SUMMARY.md)**
- Known gaps and the deployment plan: **[PLAN.md](PLAN.md)**

> **⚠️ Credentials.** `appsettings.json` is a template with no secrets in it and is untracked.
> Real values come from user-secrets locally and environment variables in production — see
> Configuration below. The **original** credentials are still in git history from earlier
> commits, which is harmless only because both have been rotated. Do not put a password back
> into this file.

---

## Who can see what

Access runs on three ASP.NET Identity roles, seeded automatically at startup. They do **not**
inherit from one another — every committee-level check names both roles explicitly, which is
what makes the tiers cumulative in practice.

| Role | Who holds it | Gets |
|---|---|---|
| *(anonymous)* | Any visitor | Home, Events, Scholarships, the written parts of Opportunities and Courses |
| `Member` | Any verified SPE member | The above, plus the opportunity board and the video library |
| `CommitteeMember` | Chapter officers | The above, plus Profile, Tasks, Tutorials, and publish/delete rights |
| `TeamLeader` | Chapter leadership | Everything, plus the Member Dashboard and Task Calendar |

A member's **committee title** ("President", "Treasurer", …) is a display label only. It
grants nothing. Login is password-less: the email is verified against SPE's OpenWater
membership system, which decides the role.

---

## Running it locally

Prerequisites: the **.NET 10 SDK**, **Node.js**, and a **PostgreSQL** database.

```bash
npm install                                   # Tailwind CLI + the image optimiser

# Credentials live outside the repo. Note the colon separator — environment variables use __
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=…;Database=…;Username=…;Password=…"
dotnet user-secrets set "EmailSettings:Password" "…"        # optional; email is skipped if blank

dotnet run                                    # migrations run automatically on startup
```

The `<UserSecretsId>` is already in the csproj, so `init` is not needed. Secrets are stored per
machine at `%APPDATA%\Microsoft\UserSecrets\<id>\secrets.json` and are not shared — each
committee member runs the two `set` commands once on their own machine.

The site comes up on <http://localhost:5169>. Tailwind rebuilds as part of `dotnet build`, so
there is no separate CSS step — but while working on styles it is quicker to run the watcher
in a second terminal:

```bash
npm run watch:css
```

---

## Where things live

```
Components/           Shared shell — App, Routes, MainLayout, SiteHeader, Home
  Shared/             Reusable pieces: Icon, FadeUp, Markdown, PageMeta, DesktopOnly, SortToggleButton
Features/             One folder per feature, with Pages/ Models/ Services/ and, where a page
                      needs a circuit for only part of itself, Components/
  Authentication/     Password-less login against SPE OpenWater
  Bursaries/          Scholarships page (static copy)
  Courses/            Public intro + members-only video library
  Events/             Events listing, sign-ups, ratings, attendee check-in
  MemberProfile/      A committee member's own profile and strikes
  Opportunities/      Public volunteering/careers copy + members-only job board
  PresidentAdmin/     Member dashboard, task calendar, attendance export
  Tasks/              Task list and assigned-task views
  Tutorials/          Internal SOP video library
Data/                 AppDbContext and the Identity user model
Shared/               Cross-feature helpers: UkTime, YouTubeUrl, MarkdownRenderer, EmailService
Styles/input.css      The only hand-written CSS. Compiles to wwwroot/tailwind.css
assets/               Image masters. Never served — the optimiser reads from here
wwwroot/              Served files: compiled CSS, JS, and optimised WebP images
scripts/              optimize-images.mjs
Migrations/           EF Core migrations, applied automatically at startup
```

Every `.razor`, `.cs` and `.css` file is divided into labelled sections that follow the order
things appear on the page:

```
@* ---------- Volunteering ---------- *@      in markup
/* ---------- Volunteering ---------- */      in C# and CSS
```

---

## Editing the content

### Find what you need with `#UpdateLink`

Anything a non-developer would realistically want to change — external URLs, contact
addresses, image paths, statistics, button labels, hard-coded body copy — is tagged with a
single marker. Search the project for:

```
#UpdateLink
```

VS Code: `Ctrl+Shift+F`, type `#UpdateLink`. There are around 44 of them, and each one names
what it covers. A marker sitting above a block covers everything in that block, so you will
not find one on every individual line.

Most page copy is **not** in the markup. Text that repeats as cards or tiles lives in arrays
in the `@code` block at the bottom of the page — edit it there and the markup follows.

### Page titles and link previews

Each public page passes its own title, search description and preview image to `<PageMeta>` at
the top of the file — that is what a link shows when it is pasted into Instagram, WhatsApp or
LinkedIn. That block is tagged `#UpdateLink`. Keep descriptions to roughly 150 characters; past
that, search results and previews truncate.

`/robots.txt` and `/sitemap.xml` are generated by the app from the incoming request, so they
need no editing when the domain changes. The list of public routes they advertise lives in
`Program.cs`, also tagged `#UpdateLink` — add to it when you add a public page.

### Swapping a photo

1. Drop the new picture into `assets/` under the **same filename** as the one it replaces.
2. Run `npm run build:images`.
3. If it is an `<img>` rather than a background, update its `alt` text to describe the new
   picture.

The optimiser resizes and re-encodes every master into WebP in `wwwroot/images/`. It skips
anything already up to date, so re-running it is cheap. To add a new image, copy an existing
entry in the `jobs` list in `scripts/optimize-images.mjs`.

### Tutorials and opportunity postings

These are written in **Markdown** by committee members through the site itself, not in code.
Supported: headings, **bold**, *italic*, `code`, lists, links, tables, blockquotes,
strikethrough. Raw HTML is deliberately stripped, and links are restricted to
`http`/`https`/`mailto`/`tel` — a pasted `javascript:` link is neutralised rather than
rendered.

---

## Configuration

Two groups of settings are required. **Do not put real values in `appsettings.json`** — use
user secrets locally and environment variables in production. Nested keys use a double
underscore as environment variables:

```bash
ConnectionStrings__DefaultConnection="Host=…;Database=…;Username=…;Password=…;SSL Mode=Require"
EmailSettings__SmtpHost="smtp.gmail.com"
EmailSettings__SmtpPort="587"
EmailSettings__Username="…"
EmailSettings__Password="…"
EmailSettings__From="…"
EmailSettings__FromName="SPE Aberdeen Student Chapter"
EmailSettings__ReplyTo=""          # optional; falls back to From
```

If `EmailSettings` is left blank the app still runs. `EmailService` reports the email as
unsent and strikes and task assignments still save correctly.

### The bootstrap administrator

One Team Leader account is seeded on every startup so the site can never lock itself out of
its own admin. The same address is also hardcoded as a fallback that can sign in even when
OpenWater has no record for it. When the current holder hands over, change **both** (each is
tagged `#UpdateLink`):

- `Program.cs` — the `adminEmail` constant
- `Features/Authentication/Services/OpenWaterAuthService.cs` — the `FullAccessEmail` constant

---

## Deploying

This is a long-running ASP.NET Core server app, not a static site — Blazor Server holds an
open SignalR connection per visitor, so static hosts will not work.

```bash
npm install && npm run build:css
dotnet publish -c Release -o ./publish
```

Deploy `./publish` and start it with `dotnet SPE_website.dll`. The host needs WebSockets
enabled, HTTPS, `ASPNETCORE_ENVIRONMENT=Production`, and sticky sessions if you ever run more
than one instance. Migrations apply themselves on startup, so a fresh empty database is
enough.

Step-by-step instructions, hosting options and a pre-flight checklist are in
**[PLAN.md](PLAN.md)**.
