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
    public DbSet<Opportunity> Opportunities => Set<Opportunity>();
    public DbSet<TaskItem> TaskItems => Set<TaskItem>();
    public DbSet<Tutorial> Tutorials => Set<Tutorial>();
    public DbSet<Course> Courses => Set<Course>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

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
    }
}
