using Microsoft.EntityFrameworkCore;
using SPE_website.Data;
using SPE_website.Features.Opportunities.Models;

namespace SPE_website.Features.Opportunities.Services;

/// <summary>
/// CRUD operations for job/internship <see cref="Opportunity"/> postings.
/// Takes a context factory rather than a scoped context: a Blazor Server circuit outlives
/// any single operation, and sharing one context across overlapping renders throws.
/// </summary>
public class OpportunityService(IDbContextFactory<AppDbContext> dbFactory)
{
    public async Task<List<Opportunity>> GetAllAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Opportunities.AsNoTracking()
                       .OrderByDescending(o => o.CreatedAt)
                       .ToListAsync();
    }

    public async Task<Opportunity?> GetByIdAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Opportunities.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id);
    }

    public async Task<Opportunity> CreateAsync(Opportunity opp)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        db.Opportunities.Add(opp);
        await db.SaveChangesAsync();
        return opp;
    }

    /// <summary>No-ops silently if the posting no longer exists (idempotent delete).</summary>
    public async Task DeleteAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var opp = await db.Opportunities.FindAsync(id);
        if (opp is not null)
        {
            db.Opportunities.Remove(opp);
            await db.SaveChangesAsync();
        }
    }
}
