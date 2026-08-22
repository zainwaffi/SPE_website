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

    /// <summary>
    /// Rewrites an existing course, for the committee's Update action. Returns false if the
    /// course has since been deleted. <see cref="Course.CreatedAt"/> is deliberately left alone,
    /// so an edit does not reshuffle the library — the list is ordered by it.
    /// </summary>
    public async Task<bool> UpdateAsync(int id, string title, string description, string youtubeEmbedUrl)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var course = await db.Courses.FindAsync(id);
        if (course is null) return false;

        course.Title = title;
        course.Description = description;
        course.YoutubeEmbedUrl = youtubeEmbedUrl;

        await db.SaveChangesAsync();
        return true;
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
