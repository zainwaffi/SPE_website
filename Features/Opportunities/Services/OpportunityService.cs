using Microsoft.EntityFrameworkCore;
using SPE_website.Data;
using SPE_website.Features.Opportunities.Models;

namespace SPE_website.Features.Opportunities.Services;

/// <summary>CRUD operations for job/internship <see cref="Opportunity"/> postings.</summary>
public class OpportunityService(AppDbContext db)
{
    public Task<List<Opportunity>> GetAllAsync() =>
        db.Opportunities.OrderByDescending(o => o.CreatedAt).ToListAsync();

    public Task<Opportunity?> GetByIdAsync(int id) =>
        db.Opportunities.FirstOrDefaultAsync(o => o.Id == id);

    public async Task<Opportunity> CreateAsync(Opportunity opp)
    {
        db.Opportunities.Add(opp);
        await db.SaveChangesAsync();
        return opp;
    }

    public async Task UpdateAsync(Opportunity opp)
    {
        db.Opportunities.Update(opp);
        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var opp = await db.Opportunities.FindAsync(id);
        if (opp is not null)
        {
            db.Opportunities.Remove(opp);
            await db.SaveChangesAsync();
        }
    }
}
