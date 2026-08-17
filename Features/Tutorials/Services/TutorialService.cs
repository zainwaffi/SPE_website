using Microsoft.EntityFrameworkCore;
using SPE_website.Data;
using SPE_website.Data.Models;
using SPE_website.Features.Tutorials.Models;

namespace SPE_website.Features.Tutorials.Services;

/// <summary>
/// CRUD and query operations for team-gated SOP <see cref="Tutorial"/> content.
/// Takes a context factory rather than a scoped context: a Blazor Server circuit outlives
/// any single operation, and sharing one context across overlapping renders throws.
/// </summary>
public class TutorialService(IDbContextFactory<AppDbContext> dbFactory)
{
    /// <summary>
    /// The tutorials a member is allowed to see, together with the teams they are in so the page
    /// can tell "you have no team yet" apart from "your teams have no tutorials yet".
    ///
    /// Visibility is enforced here rather than in the page, so the detail page and the list can't
    /// drift apart. Team Leaders see everything: they author the content, and a leader filing a
    /// tutorial for a team they aren't in would otherwise immediately lose sight of it.
    /// </summary>
    public async Task<MemberTutorials> GetVisibleToMemberAsync(string userId, bool seesAllTeams)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var memberTeams = await db.MemberTeams.AsNoTracking()
                                  .Where(m => m.UserId == userId)
                                  .Select(m => m.Team)
                                  .ToListAsync();

        var query = db.Tutorials.AsNoTracking().Include(t => t.Teams).AsQueryable();

        if (!seesAllTeams)
        {
            // No team means no tutorials at all — deliberately, not as a side effect of an
            // empty IN clause, so the intent survives anyone refactoring this query.
            if (memberTeams.Count == 0)
                return new MemberTutorials(memberTeams, []);

            query = query.Where(t => t.Teams.Any(tt => memberTeams.Contains(tt.Team)));
        }

        var tutorials = await query.OrderBy(t => t.Title).ToListAsync();
        return new MemberTutorials(memberTeams, tutorials);
    }

    /// <summary>
    /// A single tutorial for its detail page — but only if this member is allowed to see it, so a
    /// guessed URL can't reach another team's content. Null if it doesn't exist or isn't theirs.
    /// </summary>
    public async Task<Tutorial?> GetByIdForMemberAsync(int id, string userId, bool seesAllTeams)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var tutorial = await db.Tutorials.AsNoTracking()
                               .Include(t => t.Teams)
                               .FirstOrDefaultAsync(t => t.Id == id);

        if (tutorial is null || seesAllTeams) return tutorial;

        var memberTeams = await db.MemberTeams.AsNoTracking()
                                  .Where(m => m.UserId == userId)
                                  .Select(m => m.Team)
                                  .ToListAsync();

        return tutorial.Teams.Any(tt => memberTeams.Contains(tt.Team)) ? tutorial : null;
    }

    /// <summary>
    /// Creates a tutorial filed under the given teams. Duplicate teams in the input are collapsed
    /// — the unique index would reject them, and a caller passing the same box twice is a UI slip
    /// rather than something worth failing the whole save over.
    /// </summary>
    public async Task<Tutorial> CreateAsync(Tutorial tutorial, IEnumerable<Team> teams)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        tutorial.Teams = [.. teams.Distinct().Select(t => new TutorialTeam { Team = t })];

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

/// <summary>
/// What one member can see on the tutorials page. <paramref name="MemberTeams"/> is their own
/// allocation, which is empty for a Team Leader who sees everything without being in a team.
/// </summary>
public sealed record MemberTutorials(List<Team> MemberTeams, List<Tutorial> Tutorials);
