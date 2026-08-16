using Microsoft.EntityFrameworkCore;
using SPE_website.Data;
using SPE_website.Features.Courses.Models;

namespace SPE_website.Features.Courses.Services;

/// <summary>
/// CRUD operations for the public learning-video <see cref="Course"/> library.
/// Takes a context factory rather than a scoped context: a Blazor Server circuit outlives
/// any single operation, and sharing one context across overlapping renders throws.
/// </summary>
public class CourseService(IDbContextFactory<AppDbContext> dbFactory)
{
    public async Task<List<Course>> GetAllAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Courses.AsNoTracking()
                       .OrderByDescending(c => c.CreatedAt)
                       .ThenBy(c => c.Title)
                       .ToListAsync();
    }

    public async Task<Course> CreateAsync(Course course)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        db.Courses.Add(course);
        await db.SaveChangesAsync();
        return course;
    }

    /// <summary>No-ops silently if the course no longer exists (idempotent delete).</summary>
    public async Task DeleteAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var course = await db.Courses.FindAsync(id);
        if (course is null)
        {
            return;
        }

        db.Courses.Remove(course);
        await db.SaveChangesAsync();
    }
}
