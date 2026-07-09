using SPE_website.Data;

public static class Review
{
    public static void Reviews(this IEndpointRouteBuilder app)
    {
        app.MapGet("/reviews", (AppDbContext db) => (
            db.Events
                .Select(e => new
                {
                    e.Id,
                    e.Title,
                    AverageRating = e.Ratings.Any() ? e.Ratings.Average(r => r.Stars) : 0,
                    List = e.Ratings.Select(r => new
                    {

                        r.Stars,
                        r.Comment,
                        r.CreatedAt

                    })
                .ToList()
                }

        )
        ));
    }
}