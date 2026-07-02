# SPE Chapter Website

A full-stack member portal and administrative dashboard for a local chapter of the **Society of Petroleum Engineers (SPE)**. Built with Blazor Server, ASP.NET Core Identity, EF Core, and Tailwind CSS.

---

## Features

- **Event Management** — Public upcoming/past event calendar with ratings and Instagram embeds
- **Member Strike System** — President-initiated strike tracking with automated email notifications
- **Task Assignment** — Assign tasks to members with deadlines, status tracking, and email alerts
- **Role-Based Access** — Three access tiers: public, `CommitteeMember`, `President`
- **Tutorial Hub** — YouTube SOP videos filtered by each member's committee role
- **Opportunities Feed** — External opportunity links for members
- **Member Profiles** — Strike history, assigned tasks, and profile pictures
- **Admin Dashboard** — President oversees all members, strikes, and a visual task calendar

---

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Framework | ASP.NET Core / Blazor Server (.NET 10) |
| Database | SQLite + EF Core 10.0.9 |
| Authentication | ASP.NET Core Identity |
| Email | MailKit 4.17.0 |
| CSS | Tailwind CSS 4.0 |
| Build Tooling | Node.js / @tailwindcss/cli |

---

## Project Structure

```
SPE_website/
├── Components/             # Blazor layouts, error pages, root App
│   ├── App.razor
│   ├── Layout/             # MainLayout, NavMenu
│   └── Pages/              # Home, Error, NotFound
├── Data/                   # EF Core DbContext + models
│   ├── AppDbContext.cs
│   └── Models/
│       ├── ApplicationUser.cs   # Extended IdentityUser
│       └── Enums.cs             # CommitteeRole enum
├── Features/               # Vertical slice feature modules
│   ├── Authentication/     # Login, AccessDenied pages (SSR)
│   ├── Events/             # Event CRUD, past/upcoming pages, ratings
│   ├── MemberProfile/      # Profile page + service
│   ├── Opportunities/      # External opportunity links
│   ├── PresidentAdmin/     # Member dashboard, task calendar
│   ├── Tasks/              # Task list + service
│   └── Tutorials/          # Role-filtered YouTube tutorials
├── Shared/                 # Email service + settings model
├── Styles/                 # Tailwind input.css
├── Migrations/             # EF Core migration files
├── wwwroot/                # Static assets (tailwind.css, favicon)
├── Program.cs              # App startup, DI, role seeding
├── appsettings.json        # DB connection + email config
└── SPE_website.csproj
```

---

## Routes & Access

| Route | Access | Page |
|-------|--------|------|
| `/` | Public | Home |
| `/login` | Public | Login |
| `/events/upcoming` | Public | Upcoming Events |
| `/events/past` | Public | Past Events |
| `/opportunities` | Public | Opportunities |
| `/profile` | Authenticated | Member Profile |
| `/tasks` | Authenticated | My Tasks |
| `/tutorials` | Authenticated | Tutorial Hub |
| `/admin/members` | President only | Member Dashboard |
| `/admin/tasks` | President only | Task Calendar |

---

## Data Models

- **ApplicationUser** — Extends `IdentityUser` with `FullName`, `StrikeCount`, `ProfilePictureUrl`, `CommitteeRole`
- **Event** — Title, description, date, location, image; has many `EventRating`
- **EventRating** — Stars + comment, linked to an event
- **TaskItem** — Title, deadline, `AssignmentStatus` (Processing / Completed / Failed), assigned user
- **Opportunity** — Title, description, external URL
- **Tutorial** — Title, YouTube embed URL, filtered by `CommitteeRole`

**CommitteeRole enum:** `None`, `President`, `VP`, `Secretary`, `Treasurer`, `EventsCoordinator`, `TechnicalDirector`, `MediaCoordinator`, `OutreachCoordinator`

---

## Setup & Development

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js](https://nodejs.org/) (for Tailwind CSS build)

### Install & Run

```bash
# Install Tailwind CLI
npm install

# Build CSS
npm run build:css

# Apply database migrations
dotnet ef database update

# Run the application
dotnet run
```

App runs at:
- HTTP: `http://localhost:5169`
- HTTPS: `https://localhost:7256`

### CSS Watch Mode (Development)

```bash
npm run watch:css
```

### Database Migrations

```bash
dotnet ef migrations add <MigrationName>
dotnet ef database update
```

---

## Configuration

Edit `appsettings.json` to configure the database connection and email settings:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=spe.db"
  },
  "EmailSettings": {
    "SmtpHost": "<your-smtp-host>",
    "SmtpPort": 587,
    "Username": "<your-username>",
    "Password": "<your-password>",
    "From": "noreply@spe-chapter.com"
  }
}
```

Email notifications are sent via **MailKit** when:
- A strike is added to a member
- A task is assigned to a member

---

## Branding

Custom Tailwind tokens:

| Token | Value | Usage |
|-------|-------|-------|
| SPE Blue | `#003DA5` | Primary color |
| SPE Gold | `#F4A300` | Accent color |

---

## Architecture Notes

The project follows a **vertical slice architecture** — each feature (`Events`, `Tasks`, `Tutorials`, etc.) contains its own models, pages, and service, co-located under `Features/`. Shared infrastructure (email, auth) lives in `Shared/` and `Data/`.

See `AGENTS.md` for full architecture documentation, RBAC matrix, and coding conventions.


## Notes for me
remember to change the database later to postgressql or to find other than sqlite
