# SPE Chapter Website

A full-stack member portal and administrative dashboard for a local chapter of the **Society of Petroleum Engineers (SPE)**. Built with Blazor Server, ASP.NET Core Identity, EF Core, and Tailwind CSS.

---

## Features

- **Account Management** — Presidents create member accounts with auto-generated temp passwords; members change passwords on first login via dedicated Change Password page
- **Event Management** — Public upcoming/past event calendars with Instagram embeds (official `embed.js` widget), clickable Google Maps location links, member ratings, and delete functionality
- **Member Strike System** — President-initiated strike tracking with automated email notifications
- **Task Assignment** — Assign tasks to members with deadlines, status tracking, and email alerts
- **Role-Based Access** — Two access tiers: `CommitteeMember` (regular user), `President` (admin)
- **Tutorial Hub** — YouTube SOP videos filtered by each member's committee role
- **Opportunities Feed** — Searchable opportunity links with dedicated detail pages for each opportunity
- **Member Profiles** — Strike history, assigned tasks, and profile pictures
- **Admin Dashboard** — President creates accounts, manages members (edit/delete), assigns tasks, tracks strikes, views task calendar

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
| `/change-password` | Authenticated | Change Password |
| `/events/upcoming` | Public | Upcoming Events |
| `/events/past` | Public | Past Events |
| `/opportunities` | Public | Opportunities List |
| `/opportunities/{id}` | Public | Opportunity Details |
| `/profile` | Authenticated | Member Profile |
| `/tasks` | Authenticated | My Tasks |
| `/tutorials` | Authenticated | Tutorial Hub |
| `/admin/members` | President only | Member Dashboard (create/edit/delete members, assign tasks, add strikes) |
| `/admin/tasks` | President only | Task Calendar |

---

## Event Management

### Creating Events (Members & Admins)

Members and admins navigate to `/events/upcoming` and click **"+ Add Event"** to create a new event by providing:
1. **Date & Time** — When the event occurs
2. **Google Maps Link** — Clickable location link that opens in a new tab
3. **Instagram Post URL** — Direct link to the Instagram post (e.g., `https://www.instagram.com/p/XXXX/`) for embedding

Events are auto-titled based on the date and displayed in a grid view. Instagram embeds are rendered using Instagram's official `embed.js` widget.

### Viewing Events

- **Upcoming Events** (`/events/upcoming`) — Shows future events with full Instagram embed and clickable location links
- **Past Events** (`/events/past`) — Shows completed events, also with embeds and location links; members can rate events with stars and comments

### Managing Events (Members & Admins)

Both members and admins can:
- **Delete events** — Delete button appears in the top-right corner of each event card (red text, hover enabled)
- **View location** — Click the 📍 location badge to open the Google Maps link in a new tab
- **See Instagram embed** — Full Instagram post embed displays on each event card

---

## Opportunities Management

### Viewing Opportunities

Members navigate to `/opportunities` to browse a list of SPE-related opportunities. Each opportunity card shows:
- Title and description preview (truncated to 3 lines)
- "View Details →" link to navigate to the full opportunity page

### Opportunity Details (`/opportunities/{id}`)

Clicking an opportunity card navigates to its dedicated detail page showing:
- Full title and description
- "Visit External Link →" button (if an external URL is configured)
- "Back to Opportunities" navigation link

### Managing Opportunities (Members & Admins)

Both members and admins can:
- **Create opportunities** — Click "+ Add" on `/opportunities` to add new opportunity links
- **Delete opportunities** — Delete button (red text) appears on each card; uses `@onclick:stopPropagation` to prevent navigation to detail page
- **Edit opportunity details** — Full description visible on dedicated detail page

---

## Member Account Management

### Creating Member Accounts (Admin Only)

Presidents navigate to `/admin/members` and click **"+ Add Member"** to:
1. Enter member name, email, and role (`President` or `CommitteeMember`)
2. Assign a committee position (optional)
3. The system generates a secure temporary password
4. **On first use**: A success modal displays the email & temp password for the admin to share
5. Member logs in at `/login` with email + temp password

**Password Generation:** Temporary passwords are 10 characters (letters + digits), guaranteed to satisfy the policy: 8+ characters, at least one digit.

**Email Configuration:** If SMTP is configured in `appsettings.json`, the temp password is also sent via email. If SMTP is not configured, admins must manually share the password displayed in the success modal.

### Managing Members (Admin Only)

From the member table on `/admin/members`, admins can:

| Action | Button | Effect |
|--------|--------|--------|
| **Add Strike** | `+ Strike` | Increment strike count, email member notification |
| **Assign Task** | `Assign Task` | Create task with title, description, deadline; email member |
| **Edit Member** | `Edit` | Update name, email, committee position, identity role; prevents self-deletion |
| **Delete Member** | `Delete` | Permanently remove member account; hidden for current logged-in admin (self-protection) |

### Password Management (All Users)

Members who receive temporary passwords (or anyone logged in) can change their password by:
1. Clicking **"Change Password"** in the navigation bar
2. Entering current password + new password (with confirmation)
3. Password must be 8+ characters with at least one digit
4. On success, they're redirected to home

---

## Data Models

- **ApplicationUser** — Extends `IdentityUser` with `FullName`, `StrikeCount`, `ProfilePictureUrl`, `CommitteeRole`
- **Event** — Title, description, date, location (Google Maps link), `InstagramEmbedUrl`, `ImageUrl`, `IsUpcoming` flag; has many `EventRating`; supports ratings with star scores and comments
- **EventRating** — Stars (1-5) + optional comment, linked to an event
- **TaskItem** — Title, description, deadline, `AssignmentStatus` (Processing / Completed / Failed), assigned user
- **Opportunity** — Title, description, optional external URL; each opportunity has a dedicated detail page
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
