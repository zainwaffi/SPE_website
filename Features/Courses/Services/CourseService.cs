using Microsoft.EntityFrameworkCore;
using SPE_website.Data;
using SPE_website.Features.Courses.Models;

namespace SPE_website.Features.Courses.Services;

/// <summary>CRUD operations for the public learning-video <see cref="Course"/> library.</summary>
public class CourseService(AppDbContext db)
{
    public Task<List<Course>> GetAllAsync() =>
        db.Courses.OrderByDescending(c => c.CreatedAt).ThenBy(c => c.Title).ToListAsync();

    public async Task<Course> CreateAsync(Course course)
    {
        db.Courses.Add(course);
        await db.SaveChangesAsync();
        return course;
    }

    public async Task DeleteAsync(int id)
    {
        var course = await db.Courses.FindAsync(id);
        if (course is null)
        {
            return;
        }

        db.Courses.Remove(course);
        await db.SaveChangesAsync();
    }
}
