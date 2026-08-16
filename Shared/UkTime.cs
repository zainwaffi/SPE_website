namespace SPE_website.Shared;

/// <summary>
/// UK wall-clock time, used for everything date-related on the site.
///
/// The chapter and its members are all in the UK, so event times are stored and compared as
/// Europe/London wall-clock time — the time the committee typed in is the time the site shows
/// and the time a member's calendar displays.
///
/// This replaces an earlier inconsistency where events were created from the *server's* local
/// clock (<c>DateTime.Today</c>) but filtered against <c>DateTime.UtcNow</c>. On a UK dev
/// machine those agree; on a UTC production server they drift by an hour every summer, which
/// would have made events disappear from "Upcoming" up to an hour early during BST.
/// </summary>
public static class UkTime
{
    /// <summary>
    /// Europe/London. Resolved by IANA id first (Linux, and .NET 6+ on Windows), falling back
    /// to the Windows registry id so this works on either host without configuration.
    /// </summary>
    public static readonly TimeZoneInfo Zone = ResolveZone();

    /// <summary>The current UK wall-clock time. Use instead of <c>DateTime.Now</c>/<c>UtcNow</c>.</summary>
    public static DateTime Now => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Zone);

    /// <summary>Today's date in the UK.</summary>
    public static DateTime Today => Now.Date;

    /// <summary>True while British Summer Time is in effect for the given UK wall-clock time.</summary>
    public static bool IsSummerTime(DateTime ukTime) =>
        Zone.IsDaylightSavingTime(DateTime.SpecifyKind(ukTime, DateTimeKind.Unspecified));

    /// <summary>
    /// The UTC offset for a given UK wall-clock time, e.g. +00:00 in winter and +01:00 under BST.
    /// Needed when a UK time has to be expressed in absolute terms, such as an iCalendar feed.
    /// </summary>
    public static TimeSpan OffsetFor(DateTime ukTime) =>
        Zone.GetUtcOffset(DateTime.SpecifyKind(ukTime, DateTimeKind.Unspecified));

    private static TimeZoneInfo ResolveZone()
    {
        foreach (var id in new[] { "Europe/London", "GMT Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
                // Try the next spelling.
            }
            catch (InvalidTimeZoneException)
            {
                // Corrupt entry for this id; try the next spelling.
            }
        }

        // Neither id is available (a trimmed container with no tz database). UTC is wrong for
        // half the year, but it keeps the site running rather than failing at startup.
        return TimeZoneInfo.Utc;
    }
}
