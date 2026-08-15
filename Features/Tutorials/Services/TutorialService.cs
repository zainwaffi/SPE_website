using Microsoft.EntityFrameworkCore;
using SPE_website.Data;
using SPE_website.Features.Tutorials.Models;

namespace SPE_website.Features.Tutorials.Services;

/// <summary>CRUD and query operations for role-gated SOP <see cref="Tutorial"/> videos.</summary>
public class TutorialService(AppDbContext db)
{
    public Task<List<Tutorial>> GetAllAsync() =>
        db.Tutorials.OrderBy(t => t.CategoryRole).ThenBy(t => t.Title).ToListAsync();

    /// <summary>Tutorials visible to a given role: those tagged for that exact role, plus the general "Member" tier.</summary>
    public Task<List<Tutorial>> GetForRoleAsync(string role) =>
        db.Tutorials.Where(t => t.CategoryRole == role || t.CategoryRole == "Member")
                    .OrderBy(t => t.Title)
                    .ToListAsync();

    public async Task<Tutorial> CreateAsync(Tutorial tutorial)
    {
        db.Tutorials.Add(tutorial);
        await db.SaveChangesAsync();
        return tutorial;
    }

    public async Task UpdateAsync(Tutorial tutorial)
    {
        db.Tutorials.Update(tutorial);
        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var t = await db.Tutorials.FindAsync(id);
        if (t is not null)
        {
            db.Tutorials.Remove(t);
            await db.SaveChangesAsync();
        }
    }
}
