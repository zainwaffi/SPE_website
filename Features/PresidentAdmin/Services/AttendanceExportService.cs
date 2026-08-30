using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SPE_website.Data;
using SPE_website.Shared;

namespace SPE_website.Features.PresidentAdmin.Services;

/// <summary>
/// Builds the committee's attendance export: who signed up for which event, and what they
/// said about it afterwards.
///
/// Attendance and reviews are separate records — a member can sign up and never review, or
/// review an event they never signed up for — so the export is a full outer join of the two,
/// keyed on (member, event). That way neither is silently dropped.
///
/// CSV rather than .xlsx: the workbook writer (ClosedXML + DocumentFormat.OpenXml + its font
/// stack) was ~9.5 MB of the deployed app — a third of its total size — for this one download.
/// The per-member and per-event summary sheets it used to pre-compute are all derivable from
/// these rows with a pivot table, so no information is lost, only the pre-aggregation.
/// </summary>
public class AttendanceExportService(IDbContextFactory<AppDbContext> dbFactory)
{
    private const string Anonymous = "Anonymous";

    private static readonly string[] Headers =
    [
        "Member", "University", "Event", "Event Date",
        "Signed Up", "Attended", "Signed Up On", "Checked In On",
        "Rating", "Comment", "Reviewed On"
    ];

    /// <summary>Suggested download filename, dated so successive exports don't overwrite each other.</summary>
    public static string FileName() => $"spe-attendance-{UkTime.Now:yyyy-MM-dd}.csv";

    public async Task<byte[]> BuildCsvAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var registrations = await db.EventRegistrations.AsNoTracking()
            .Select(r => new AttendanceRow(
                r.UserId,
                r.FullName,
                r.University,
                r.EventId,
                r.EventName,
                r.Event!.Date,
                r.RegisteredAt,
                null, null, null,
                r.Attended,
                r.CheckedInAt))
            .ToListAsync();

        var reviews = await db.EventRatings.AsNoTracking()
            .Select(r => new AttendanceRow(
                r.UserId,
                r.User != null ? r.User.FullName : Anonymous,
                r.User != null ? (r.User.OpenWaterOrganization ?? "") : "",
                r.EventId,
                r.Event!.Title,
                r.Event.Date,
                null,
                r.Stars,
                r.Comment,
                r.CreatedAt))
            .ToListAsync();

        return BuildCsv(registrations, reviews);
    }

    /// <summary>
    /// The pure half: given the two record sets, produce the file bytes. Split out from
    /// <see cref="BuildCsvAsync"/> so the merge and layout can be exercised without a database.
    /// </summary>
    public static byte[] BuildCsv(List<AttendanceRow> registrations, List<AttendanceRow> reviews)
    {
        var merged = Merge(registrations, reviews);

        using var stream = new MemoryStream();

        // Excel only reads a CSV as UTF-8 when the file opens with a byte-order mark. Without
        // one it falls back to the machine's ANSI codepage and every accented name in the
        // export comes out as mojibake.
        using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)))
        {
            writer.WriteLine(string.Join(',', Headers));

            foreach (var row in merged)
            {
                writer.WriteLine(string.Join(',',
                [
                    Text(row.FullName),
                    Text(row.University),
                    Text(row.EventName),
                    Day(row.EventDate),
                    row.RegisteredAt is null ? "No" : "Yes",
                    row.Attended ? "Yes" : "No",
                    Moment(row.RegisteredAt),
                    Moment(row.CheckedInAt),
                    row.Stars?.ToString(CultureInfo.InvariantCulture) ?? "",
                    Text(row.Comment ?? ""),
                    Moment(row.ReviewedAt)
                ]));
            }
        }

        return stream.ToArray();
    }

    /// <summary>
    /// Combines the two record types into one row per (member, event). A member identified only
    /// by a deleted account falls back to their snapshotted name so history isn't lost.
    /// </summary>
    private static List<AttendanceRow> Merge(List<AttendanceRow> registrations, List<AttendanceRow> reviews)
    {
        var byKey = new Dictionary<(string, int), AttendanceRow>();

        foreach (var row in registrations.Concat(reviews))
        {
            var key = (row.UserId ?? row.FullName, row.EventId);

            byKey[key] = byKey.TryGetValue(key, out var existing)
                ? existing with
                {
                    // Whichever side carried each value wins; neither overwrites the other with null.
                    RegisteredAt = existing.RegisteredAt ?? row.RegisteredAt,
                    Stars = existing.Stars ?? row.Stars,
                    Comment = existing.Comment ?? row.Comment,
                    ReviewedAt = existing.ReviewedAt ?? row.ReviewedAt,
                    Attended = existing.Attended || row.Attended,
                    CheckedInAt = existing.CheckedInAt ?? row.CheckedInAt,
                    University = string.IsNullOrWhiteSpace(existing.University) ? row.University : existing.University,
                }
                : row;
        }

        return [.. byKey.Values.OrderBy(r => r.FullName).ThenByDescending(r => r.EventDate)];
    }

    /* ---------- CSV field formatting ---------- */

    private static string Day(DateTime value) =>
        value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Moment(DateTime? value) =>
        value is DateTime moment ? moment.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : "";

    private static readonly char[] NeedsQuoting = [',', '"', '\n', '\r'];

    /// <summary>
    /// RFC 4180 quoting for a text field, plus a guard against spreadsheet formula injection:
    /// event comments are free text typed by members, and Excel evaluates any cell opening with
    /// =, +, - or @ as a formula. A leading apostrophe forces it back to literal text. Only text
    /// columns go through here — the numeric ones are written unquoted, so a rating is still a
    /// number to the spreadsheet.
    /// </summary>
    private static string Text(string value)
    {
        if (value.Length > 0 && "=+-@\t\r".Contains(value[0]))
        {
            value = "'" + value;
        }

        return value.IndexOfAny(NeedsQuoting) >= 0
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }
}

/// <summary>One member's relationship to one event: signed up, reviewed, or both.</summary>
public sealed record AttendanceRow(
    string? UserId,
    string FullName,
    string University,
    int EventId,
    string EventName,
    DateTime EventDate,
    DateTime? RegisteredAt,
    int? Stars,
    string? Comment,
    DateTime? ReviewedAt,
    bool Attended = false,
    DateTime? CheckedInAt = null);
