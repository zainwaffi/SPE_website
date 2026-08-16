using Microsoft.EntityFrameworkCore;
using SPE_website.Data;
using SPE_website.Features.Tutorials.Models;

namespace SPE_website.Features.Tutorials.Services;

/// <summary>
/// CRUD and query operations for role-gated SOP <see cref="Tutorial"/> videos.
/// Takes a context factory rather than a scoped context: a Blazor Server circuit outlives
/// any single operation, and sharing one context across overlapping renders throws.
/// </summary>
public class TutorialService(IDbContextFactory<AppDbContext> dbFactory)
{
    public async Task<List<Tutorial>> GetAllAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Tutorials.AsNoTracking()
                       .OrderBy(t => t.CategoryRole)
                       .ThenBy(t => t.Title)
                       .ToListAsync();
    }

    /// <summary>
    /// Tutorials visible to a given role: those tagged for that exact role, plus the general
    /// "Member" tier.
    /// NOTE: TutorialsPage currently calls <see cref="GetAllAsync"/> and groups client-side,
    /// so this per-role filter is not actually applied anywhere yet.
    /// </summary>
    public async Task<List<Tutorial>> GetForRoleAsync(string role)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Tutorials.AsNoTracking()
                       .Where(t => t.CategoryRole == role || t.CategoryRole == "Member")
                       .OrderBy(t => t.Title)
                       .ToListAsync();
    }

    /// <summary>A single tutorial for its detail page, or null if it no longer exists.</summary>
    public async Task<Tutorial?> GetByIdAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Tutorials.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
    }

    public async Task<Tutorial> CreateAsync(Tutorial tutorial)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        db.Tutorials.Add(tutorial);
        await db.SaveChangesAsync();
        return tutorial;
    }

    /// <summary>No-ops silently if the tutorial no longer exists (idempotent delete).</summary>
    public async Task DeleteAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var tutorial = await db.Tutorials.FindAsync(id);
        if (tutorial is not null)
        {
            db.Tutorials.Remove(tutorial);
            await db.SaveChangesAsync();
        }
    }
}
