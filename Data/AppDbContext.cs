using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SPE_website.Data.Models;
using SPE_website.Features.Courses.Models;
using SPE_website.Features.Events.Models;
using SPE_website.Features.Opportunities.Models;
using SPE_website.Features.Tasks.Models;
using SPE_website.Features.Tutorials.Models;

namespace SPE_website.Data;

/// <summary>
/// EF Core context for the whole application. Extends <see cref="IdentityDbContext{TUser}"/>
/// so Identity tables (Users, Roles, Claims) live alongside feature tables in one PostgreSQL database.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Event> Events => Set<Event>();
    public DbSet<EventRating> EventRatings => Set<EventRating>();
    public DbSet<EventRegistration> EventRegistrations => Set<EventRegistration>();
    public DbSet<Opportunity> Opportunities => Set<Opportunity>();
    public DbSet<TaskItem> TaskItems => Set<TaskItem>();
    public DbSet<Tutorial> Tutorials => Set<Tutorial>();
    public DbSet<TutorialTeam> TutorialTeams => Set<TutorialTeam>();
    public DbSet<MemberTeam> MemberTeams => Set<MemberTeam>();
    public DbSet<Course> Courses => Set<Course>();
    public DbSet<Member> Members => Set<Member>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Notification emails are opt-out. The database default matters as much as the CLR one:
        // without it the migration backfills every existing member to false and silently opts
        // the whole committee out.
        builder.Entity<ApplicationUser>()
            .Property(u => u.EmailNotificationsEnabled)
            .HasDefaultValue(true);

        // A second sign-in identifier for members who came in through the AUSA card-number
        // import rather than OpenWater. Nullable and unique — Postgres treats multiple nulls
        // as distinct, so this never collides with the many users who only have an email.
        builder.Entity<ApplicationUser>()
            .HasIndex(u => u.CardNumber)
            .IsUnique();

        // public.members is created and maintained entirely by "Uploading Members/MembersList.py"
        // — EF must never generate migrations for it, only read it. Its columns are the raw
        // snake_case names the Python script's CREATE TABLE uses, not EF's PascalCase default,
        // so every one of them needs an explicit HasColumnName or Npgsql looks for a column
        // ("CardNumber", quoted) that was never created.
        builder.Entity<Member>(member =>
        {
            member.HasKey(m => m.CardNumber);
            member.ToTable("members", t => t.ExcludeFromMigrations());

            member.Property(m => m.CardNumber).HasColumnName("card_number");
            member.Property(m => m.FullName).HasColumnName("full_name");
            member.Property(m => m.MembershipType).HasColumnName("membership_type");
            member.Property(m => m.PurchasedAt).HasColumnName("purchased_at");
            member.Property(m => m.IsActive).HasColumnName("is_active");
            member.Property(m => m.FirstSeen).HasColumnName("first_seen");
            member.Property(m => m.LastSeenImport).HasColumnName("last_seen_import");
        });

        // A rating always belongs to exactly one event; deleting the event removes its ratings too.
        builder.Entity<EventRating>()
            .HasOne(r => r.Event)
            .WithMany(e => e.Ratings)
            .HasForeignKey(r => r.EventId)
            .OnDelete(DeleteBehavior.Cascade);

        // Tasks survive user deletion (e.g. member leaves) — the assignment is just cleared,
        // preserving task history for reporting rather than losing it.
        builder.Entity<TaskItem>()
            .HasOne(t => t.AssignedTo)
            .WithMany(u => u.AssignedTasks)
            .HasForeignKey(t => t.AssignedToUserId)
            .OnDelete(DeleteBehavior.SetNull);

        // The leader who handed the task out. No inverse collection on ApplicationUser: nothing
        // loads a user in order to walk their authored tasks, and a second navigation to the same
        // principal would only make the two relationships easier to confuse. Set null on delete
        // for the same reason as the assignee — the task is the record, not the people on it.
        builder.Entity<TaskItem>()
            .HasOne(t => t.AssignedBy)
            .WithMany()
            .HasForeignKey(t => t.AssignedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        // Reviews outlive their author for the same reason attendance does: the export is a
        // historical record. Clearing the link leaves the review in place, exporting as anonymous.
        builder.Entity<EventRating>()
            .HasOne(r => r.User)
            .WithMany()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        // Team allocations are pure join rows — they carry no history worth keeping once the
        // member or the tutorial is gone, so both cascade rather than lingering as orphans.
        // The unique indexes stop a double-submitted form filing the same pair twice.
        builder.Entity<MemberTeam>(membership =>
        {
            membership.HasOne(m => m.User)
                      .WithMany(u => u.Teams)
                      .HasForeignKey(m => m.UserId)
                      .OnDelete(DeleteBehavior.Cascade);

            membership.HasIndex(m => new { m.UserId, m.Team }).IsUnique();
        });

        builder.Entity<TutorialTeam>(filing =>
        {
            filing.HasOne(t => t.Tutorial)
                  .WithMany(t => t.Teams)
                  .HasForeignKey(t => t.TutorialId)
                  .OnDelete(DeleteBehavior.Cascade);

            filing.HasIndex(t => new { t.TutorialId, t.Team }).IsUnique();
        });

        builder.Entity<EventRegistration>(registration =>
        {
            registration.HasOne(r => r.Event)
                        .WithMany(e => e.Registrations)
                        .HasForeignKey(r => r.EventId)
                        .OnDelete(DeleteBehavior.Cascade);

            registration.HasOne(r => r.User)
                        .WithMany()
                        .HasForeignKey(r => r.UserId)
                        .OnDelete(DeleteBehavior.SetNull);

            // One sign-up per member per event. Enforced in the database rather than only in
            // the service, so a double-submitted button can't create a duplicate attendee.
            registration.HasIndex(r => new { r.EventId, r.UserId }).IsUnique();
        });
    }
}
