using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SPE_website.Data;

namespace SPE_website.Features.Events.Services;

/// <summary>
/// Builds the chapter's iCalendar (RFC 5545) subscription feed, served at /events/calendar.ics.
///
/// This is a *subscription* feed, not a one-off download: members subscribe once and their
/// calendar app re-polls the URL, so events added later appear automatically.
/// </summary>
public class EventCalendarService(IDbContextFactory<AppDbContext> dbFactory)
{
    public const string CalendarName = "SPE Aberdeen Student Chapter";

    /// <summary>
    /// Events carry a start time but no end time, so each entry is given a nominal duration.
    /// Better than emitting a zero-length event, which some clients render as an all-day blob.
    /// </summary>
    private static readonly TimeSpan DefaultDuration = TimeSpan.FromHours(2);

    public async Task<string> BuildFeedAsync(string eventsPageUrl)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var events = await db.Events.AsNoTracking()
                             .OrderBy(e => e.Date)
                             .Select(e => new { e.Id, e.Title, e.Description, e.Date, e.Location, e.Category })
                             .ToListAsync();

        var ics = new StringBuilder();

        Line(ics, "BEGIN:VCALENDAR");
        Line(ics, "VERSION:2.0");
        Line(ics, "PRODID:-//SPE Aberdeen Student Chapter//Events//EN");
        Line(ics, "CALSCALE:GREGORIAN");
        Line(ics, "METHOD:PUBLISH");
        Line(ics, $"X-WR-CALNAME:{Escape(CalendarName)}");
        Line(ics, $"X-WR-CALDESC:{Escape("Events from the SPE University of Aberdeen Student Chapter.")}");

        // Ask subscribers to re-poll every 12 hours. X-PUBLISHED-TTL is the older Outlook
        // spelling of REFRESH-INTERVAL; both are emitted because clients differ on which they read.
        Line(ics, "REFRESH-INTERVAL;VALUE=DURATION:PT12H");
        Line(ics, "X-PUBLISHED-TTL:PT12H");

        WriteLondonTimeZone(ics);

        var stamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

        foreach (var e in events)
        {
            Line(ics, "BEGIN:VEVENT");

            // Stable per event, so re-polling updates the existing entry instead of duplicating it.
            Line(ics, $"UID:spe-event-{e.Id}@spe-aberdeen-chapter");
            Line(ics, $"DTSTAMP:{stamp}");

            // Anchored to Europe/London rather than left floating. The site is UK-oriented and
            // event times are UK wall-clock (see UkTime), so naming the zone means a member
            // abroad still sees the event at the correct UK moment instead of their own
            // local reading of the same clock face. BST is handled by the VTIMEZONE above.
            Line(ics, $"DTSTART;TZID={LondonTzId}:{Local(e.Date)}");
            Line(ics, $"DTEND;TZID={LondonTzId}:{Local(e.Date.Add(DefaultDuration))}");

            Line(ics, $"SUMMARY:{Escape(e.Title)}");

            var description = BuildDescription(e.Description, e.Category.ToString(), eventsPageUrl);
            if (description.Length > 0)
            {
                Line(ics, $"DESCRIPTION:{Escape(description)}");
            }

            // Location holds a maps/venue URL on this site rather than a postal address.
            if (!string.IsNullOrWhiteSpace(e.Location))
            {
                Line(ics, $"LOCATION:{Escape(e.Location)}");
            }

            Line(ics, $"URL:{Escape(eventsPageUrl)}");
            Line(ics, "END:VEVENT");
        }

        Line(ics, "END:VCALENDAR");
        return ics.ToString();
    }

    private static string BuildDescription(string? body, string category, string eventsPageUrl)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(body)) parts.Add(body.Trim());
        if (!string.IsNullOrWhiteSpace(category)) parts.Add($"Category: {category}");
        parts.Add(eventsPageUrl);
        return string.Join("\n\n", parts);
    }

    private const string LondonTzId = "Europe/London";

    /// <summary>
    /// A VTIMEZONE for Europe/London, so subscribers resolve BST correctly instead of trusting
    /// the client to know what the zone id means. The RRULEs encode the EU/UK rule: clocks go
    /// forward on the last Sunday of March and back on the last Sunday of October.
    /// </summary>
    private static void WriteLondonTimeZone(StringBuilder ics)
    {
        Line(ics, "BEGIN:VTIMEZONE");
        Line(ics, $"TZID:{LondonTzId}");
        Line(ics, "X-LIC-LOCATION:Europe/London");

        Line(ics, "BEGIN:DAYLIGHT");
        Line(ics, "TZOFFSETFROM:+0000");
        Line(ics, "TZOFFSETTO:+0100");
        Line(ics, "TZNAME:BST");
        Line(ics, "DTSTART:19700329T010000");
        Line(ics, "RRULE:FREQ=YEARLY;BYMONTH=3;BYDAY=-1SU");
        Line(ics, "END:DAYLIGHT");

        Line(ics, "BEGIN:STANDARD");
        Line(ics, "TZOFFSETFROM:+0100");
        Line(ics, "TZOFFSETTO:+0000");
        Line(ics, "TZNAME:GMT");
        Line(ics, "DTSTART:19701025T020000");
        Line(ics, "RRULE:FREQ=YEARLY;BYMONTH=10;BYDAY=-1SU");
        Line(ics, "END:STANDARD");

        Line(ics, "END:VTIMEZONE");
    }

    private static string Local(DateTime value) =>
        value.ToString("yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture);

    /// <summary>
    /// Escapes a TEXT value per RFC 5545 §3.3.11. Backslash must be handled first, otherwise
    /// the escapes introduced below would themselves be escaped again.
    /// </summary>
    private static string Escape(string value) =>
        value.Replace("\\", "\\\\")
             .Replace(";", "\\;")
             .Replace(",", "\\,")
             .Replace("\r\n", "\\n")
             .Replace("\n", "\\n")
             .Replace("\r", "\\n");

    /// <summary>
    /// Appends one content line, folded per RFC 5545 §3.1: no line may exceed 75 octets, and
    /// continuations begin with a single space. Folding counts UTF-8 *bytes*, not chars, and
    /// must never split a multi-byte character — otherwise a single accented letter in an
    /// event title corrupts the feed.
    /// </summary>
    private static void Line(StringBuilder builder, string content)
    {
        const int maxOctets = 75;

        var bytes = Encoding.UTF8.GetByteCount(content);
        if (bytes <= maxOctets)
        {
            builder.Append(content).Append("\r\n");
            return;
        }

        var start = 0;
        var isFirst = true;

        while (start < content.Length)
        {
            // Continuation lines spend one octet on the leading space.
            var budget = isFirst ? maxOctets : maxOctets - 1;
            var length = 0;
            var used = 0;

            while (start + length < content.Length)
            {
                // Keep surrogate pairs together.
                var charCount = char.IsHighSurrogate(content[start + length]) && start + length + 1 < content.Length ? 2 : 1;
                var size = Encoding.UTF8.GetByteCount(content.AsSpan(start + length, charCount));
                if (used + size > budget) break;

                used += size;
                length += charCount;
            }

            if (length == 0) break;

            if (!isFirst) builder.Append(' ');
            builder.Append(content, start, length).Append("\r\n");

            start += length;
            isFirst = false;
        }
    }
}
