using Microsoft.EntityFrameworkCore;
using SPE_website.Data;
using SPE_website.Data.Models;
using SPE_website.Features.Tutorials.Models;

namespace SPE_website.Features.Tutorials.Services;

public class TutorialService(AppDbContext db)
{
    public Task<List<Tutorial>> GetAllAsync() =>
        db.Tutorials.OrderBy(t => t.CategoryRole).ThenBy(t => t.Title).ToListAsync();

    public Task<List<Tutorial>> GetForRoleAsync(CommitteeRole role) =>
        db.Tutorials.Where(t => t.CategoryRole == role || t.CategoryRole == CommitteeRole.Member)
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
