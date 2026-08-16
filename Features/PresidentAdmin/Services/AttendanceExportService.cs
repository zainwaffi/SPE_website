using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using SPE_website.Data;
using SPE_website.Shared;

namespace SPE_website.Features.PresidentAdmin.Services;

/// <summary>
/// Builds the committee's attendance workbook: who signed up for which event, and what they
/// said about it afterwards.
///
/// Attendance and reviews are separate records — a member can sign up and never review, or
/// review an event they never signed up for — so the detail sheet is a full outer join of the
/// two, keyed on (member, event). That way neither is silently dropped.
/// </summary>
public class AttendanceExportService(IDbContextFactory<AppDbContext> dbFactory)
{
    private const string Anonymous = "Anonymous";

    /// <summary>Suggested download filename, dated so successive exports don't overwrite each other.</summary>
    public static string FileName() => $"spe-attendance-{UkTime.Now:yyyy-MM-dd}.xlsx";

    public async Task<byte[]> BuildWorkbookAsync()
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

        return BuildWorkbook(registrations, reviews);
    }

    /// <summary>
    /// The pure half: given the two record sets, produce the workbook bytes. Split out from
    /// <see cref="BuildWorkbookAsync"/> so the merge and sheet layout can be exercised without
    /// a database.
    /// </summary>
    public static byte[] BuildWorkbook(List<AttendanceRow> registrations, List<AttendanceRow> reviews)
    {
        var merged = Merge(registrations, reviews);

        using var workbook = new XLWorkbook();
        WriteDetailSheet(workbook, merged);
        WriteMemberSheet(workbook, merged);
        WriteEventSheet(workbook, merged);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
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

    private static void WriteDetailSheet(XLWorkbook workbook, List<AttendanceRow> rows)
    {
        var sheet = workbook.Worksheets.Add("Attendance & Reviews");
        var headers = new[]
        {
            "Member", "University", "Event", "Event Date",
            "Signed Up", "Attended", "Signed Up On", "Checked In On",
            "Rating", "Comment", "Reviewed On"
        };

        WriteHeader(sheet, headers);

        var r = 2;
        foreach (var row in rows)
        {
            sheet.Cell(r, 1).Value = row.FullName;
            sheet.Cell(r, 2).Value = row.University;
            sheet.Cell(r, 3).Value = row.EventName;
            sheet.Cell(r, 4).Value = row.EventDate;
            sheet.Cell(r, 4).Style.DateFormat.Format = "yyyy-mm-dd";
            sheet.Cell(r, 5).Value = row.RegisteredAt is null ? "No" : "Yes";
            sheet.Cell(r, 6).Value = row.Attended ? "Yes" : "No";

            if (row.RegisteredAt is DateTime signedUp)
            {
                sheet.Cell(r, 7).Value = signedUp;
                sheet.Cell(r, 7).Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
            }

            if (row.CheckedInAt is DateTime checkedIn)
            {
                sheet.Cell(r, 8).Value = checkedIn;
                sheet.Cell(r, 8).Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
            }

            if (row.Stars is int stars) sheet.Cell(r, 9).Value = stars;
            sheet.Cell(r, 10).Value = row.Comment ?? "";

            if (row.ReviewedAt is DateTime reviewed)
            {
                sheet.Cell(r, 11).Value = reviewed;
                sheet.Cell(r, 11).Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
            }

            r++;
        }

        Finish(sheet, headers.Length, r, commentColumn: 10);
    }

    private static void WriteMemberSheet(XLWorkbook workbook, List<AttendanceRow> rows)
    {
        var sheet = workbook.Worksheets.Add("By Member");
        var headers = new[] { "Member", "University", "Events Signed Up", "Events Attended", "Reviews Left", "Average Rating Given" };
        WriteHeader(sheet, headers);

        var members = rows
            .GroupBy(x => x.FullName)
            .Select(g => new
            {
                Name = g.Key,
                University = g.Select(x => x.University).FirstOrDefault(u => !string.IsNullOrWhiteSpace(u)) ?? "",
                SignedUp = g.Count(x => x.RegisteredAt is not null),
                Attended = g.Count(x => x.Attended),
                Reviews = g.Count(x => x.Stars is not null),
                Average = g.Where(x => x.Stars is not null).Select(x => (double)x.Stars!).DefaultIfEmpty().Average()
            })
            .OrderByDescending(x => x.Attended)
            .ThenByDescending(x => x.SignedUp)
            .ThenBy(x => x.Name);

        var r = 2;
        foreach (var m in members)
        {
            sheet.Cell(r, 1).Value = m.Name;
            sheet.Cell(r, 2).Value = m.University;
            sheet.Cell(r, 3).Value = m.SignedUp;
            sheet.Cell(r, 4).Value = m.Attended;
            sheet.Cell(r, 5).Value = m.Reviews;
            if (m.Reviews > 0)
            {
                sheet.Cell(r, 6).Value = Math.Round(m.Average, 2);
            }
            r++;
        }

        Finish(sheet, headers.Length, r);
    }

    private static void WriteEventSheet(XLWorkbook workbook, List<AttendanceRow> rows)
    {
        var sheet = workbook.Worksheets.Add("By Event");
        var headers = new[] { "Event", "Event Date", "Signed Up", "Attended", "Reviews", "Average Rating" };
        WriteHeader(sheet, headers);

        var events = rows
            .GroupBy(x => new { x.EventId, x.EventName, x.EventDate })
            .Select(g => new
            {
                g.Key.EventName,
                g.Key.EventDate,
                SignedUp = g.Count(x => x.RegisteredAt is not null),
                Attended = g.Count(x => x.Attended),
                Reviews = g.Count(x => x.Stars is not null),
                Average = g.Where(x => x.Stars is not null).Select(x => (double)x.Stars!).DefaultIfEmpty().Average()
            })
            .OrderByDescending(x => x.EventDate);

        var r = 2;
        foreach (var e in events)
        {
            sheet.Cell(r, 1).Value = e.EventName;
            sheet.Cell(r, 2).Value = e.EventDate;
            sheet.Cell(r, 2).Style.DateFormat.Format = "yyyy-mm-dd";
            sheet.Cell(r, 3).Value = e.SignedUp;
            sheet.Cell(r, 4).Value = e.Attended;
            sheet.Cell(r, 5).Value = e.Reviews;
            if (e.Reviews > 0)
            {
                sheet.Cell(r, 6).Value = Math.Round(e.Average, 2);
            }
            r++;
        }

        Finish(sheet, headers.Length, r);
    }

    private static void WriteHeader(IXLWorksheet sheet, string[] headers)
    {
        for (var i = 0; i < headers.Length; i++)
        {
            var cell = sheet.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#003DA5");
            cell.Style.Font.FontColor = XLColor.White;
        }
        sheet.SheetView.FreezeRows(1);
    }

    private static void Finish(IXLWorksheet sheet, int columns, int nextRow, int? commentColumn = null)
    {
        // AutoFilter needs at least one data row; on an empty sheet it throws.
        if (nextRow > 2)
        {
            sheet.Range(1, 1, nextRow - 1, columns).SetAutoFilter();
        }

        sheet.Columns().AdjustToContents();

        // Free-text comments would otherwise stretch a column across the screen.
        if (commentColumn is int c)
        {
            sheet.Column(c).Width = 60;
            sheet.Column(c).Style.Alignment.WrapText = true;
        }

        foreach (var column in sheet.Columns())
        {
            if (column.Width > 40 && column.ColumnNumber() != commentColumn)
            {
                column.Width = 40;
            }
        }
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
