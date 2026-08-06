# SPE Chapter Website

A full-stack web portal for a local chapter of the Society of Petroleum Engineers (SPE). The site gives the public a place to discover chapter activity and gives committee members and presidents a secure workspace for managing events, members, tasks, learning material, and opportunities.

## What the website provides

### Public website

- A home page for the SPE chapter.
- Upcoming and past event listings, with event details, locations, images, and Instagram embeds where supplied.
- Event ratings and comments for completed events.
- An opportunities board with full detail pages and links to external applications or resources.

### Committee member portal

Authenticated committee members can:

- View their member profile, including committee role, strike count, and assigned tasks.
- Track and update the status of assigned tasks.
- Access a role-filtered tutorial hub containing YouTube standard-operating-procedure videos.
- Create and manage events and opportunities.
- Change their password securely after signing in.

### President administration

Presidents have all committee-member capabilities, plus an administration area for:

- Creating, editing, and removing member accounts.
- Assigning application roles (`President` or `CommitteeMember`) and committee positions.
- Issuing strikes and viewing each member's strike count.
- Assigning tasks with descriptions and deadlines.
- Reviewing task deadlines in an interactive calendar.
- Managing tutorials for committee roles.

The system sends email notifications when a member account is created, a task is assigned, or a strike is added.

## Access control

| Audience | Access |
|---|---|
| Visitors | Home, events, and opportunities |
| `CommitteeMember` | Member profile, tasks, tutorials, and content management |
| `President` | All member features plus member administration and task calendar |

Authentication and role-based authorisation are provided by ASP.NET Core Identity.

## Technology

| Area | Used technology |
|---|---|
| Application | ASP.NET Core 10 and Blazor Server |
| Database | PostgreSQL, EF Core, and Npgsql |
| Authentication | ASP.NET Core Identity |
| Styling | Tailwind CSS 4 |
| Email | MailKit SMTP |
| Front-end tooling | Node.js and the Tailwind CLI |

## Architecture

The application uses a vertical-slice structure: each business capability keeps its pages, models, and services together under `Features/`.

```text
Features/
  Authentication/     Sign-in, password management, and access-denied pages
  Events/             Event listings, ratings, and event management
  MemberProfile/      Member dashboard and profile data
  Opportunities/      Opportunity listings and detail pages
  PresidentAdmin/     Member management, strikes, task assignment, calendar
  Tasks/              Task tracking and status updates
  Tutorials/          Role-based video tutorial hub
Data/                 DbContext, Identity user model, and committee-role enum
Shared/               Reusable services, including email delivery
Components/           Application shell, layouts, navigation, and common pages
Styles/               Tailwind source styles and SPE design tokens
```

The SPE visual identity is implemented through Tailwind tokens for SPE Blue (`#003DA5`) and SPE Gold (`#F4A300`).

## Run locally

### Prerequisites

- .NET 10 SDK
- Node.js and npm
- A PostgreSQL database

### Setup

```bash
# Install the Tailwind CLI dependencies
npm install

# Create the production CSS
npm run build:css

# Configure a PostgreSQL connection and SMTP settings, then apply migrations
dotnet ef database update

# Run the application
dotnet run
```

For CSS development, keep Tailwind running in watch mode:

```bash
npm run watch:css
```

## Configuration

Configure the following settings outside source control (for example, with user secrets or environment variables):

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=<host>;Port=5432;Database=<database>;Username=<username>;Password=<password>"
  },
  "EmailSettings": {
    "SmtpHost": "<smtp-host>",
    "SmtpPort": 587,
    "Username": "<username>",
    "Password": "<password>",
    "From": "<sender-email>"
  }
}
```

Never commit real database credentials, SMTP passwords, or default administrator credentials.

## Database migrations

```bash
dotnet ef migrations add <MigrationName>
dotnet ef database update
```

On startup, the app applies pending migrations and ensures the `President` and `CommitteeMember` roles exist.
