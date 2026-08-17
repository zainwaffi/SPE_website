using SPE_website.Data.Models;

namespace SPE_website.Features.Tutorials.Models;

/// <summary>
/// Files one tutorial under one team. A tutorial can be tagged for several teams — onboarding
/// content that everyone needs is tagged for all of them — so it appears under each of those
/// headings on the tutorials page.
/// </summary>
public class TutorialTeam
{
    public int Id { get; set; }

    public int TutorialId { get; set; }
    public Tutorial? Tutorial { get; set; }

    public Team Team { get; set; }
}
