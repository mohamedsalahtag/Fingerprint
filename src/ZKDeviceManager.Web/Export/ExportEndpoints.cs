using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using ZKDeviceManager.Data;
using ZKDeviceManager.Data.Entities;
using ZKDeviceManager.Web.Services;

namespace ZKDeviceManager.Web.Export;

/// <summary>Downloadable Excel (.xlsx) reports for users and attendance, styled as filterable tables.</summary>
public static class ExportEndpoints
{
    private const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static void MapExportEndpoints(this WebApplication app)
    {
        // Users — optionally filtered to one machine (?device=<id>; 0 or omitted = all machines).
        app.MapGet("/export/users", async (int? device, IDbContextFactory<AppDbContext> dbf) =>
        {
            await using var db = dbf.CreateDbContext();
            var names = await UserDirectory.BestNamesAsync(db);
            var deviceNames = await db.Devices.ToDictionaryAsync(d => d.Id, d => d.Name);

            var q = db.EnrolledUsers.AsQueryable();
            if (device is > 0) q = q.Where(u => u.SourceDeviceId == device);
            var rows = await q.Select(u => new
            {
                u.DeviceUserId, u.SourceDeviceId, u.Name, u.Privilege, u.Enabled, u.CardNumber,
                Fp = u.Templates.Count(t => t.Type == TemplateType.Fingerprint),
                HasFace = u.Templates.Any(t => t.Type == TemplateType.Face),
                u.LastDownloadedUtc
            }).ToListAsync();

            using var wb = new XLWorkbook();
            var ws = wb.AddWorksheet("Users");
            string[] headers = { "User ID", "Name", "Machine", "Privilege", "Enabled", "Card", "Fingerprints", "Face", "Last updated" };
            for (int c = 0; c < headers.Length; c++) ws.Cell(1, c + 1).Value = headers[c];

            int r = 2;
            foreach (var u in rows.OrderBy(x => deviceNames.GetValueOrDefault(x.SourceDeviceId, ""))
                                  .ThenBy(x => x.DeviceUserId.Length).ThenBy(x => x.DeviceUserId))
            {
                ws.Cell(r, 1).Value = u.DeviceUserId;
                ws.Cell(r, 2).Value = names.GetValueOrDefault(u.DeviceUserId) ?? u.Name ?? "";
                ws.Cell(r, 3).Value = deviceNames.GetValueOrDefault(u.SourceDeviceId, "");
                ws.Cell(r, 4).Value = u.Privilege == 3 ? "Admin" : u.Privilege.ToString();
                ws.Cell(r, 5).Value = u.Enabled ? "Yes" : "No";
                ws.Cell(r, 6).Value = u.CardNumber ?? "";
                ws.Cell(r, 7).Value = u.Fp;
                ws.Cell(r, 8).Value = u.HasFace ? "Yes" : "No";
                ws.Cell(r, 9).Value = u.LastDownloadedUtc.ToLocalTime();
                ws.Cell(r, 9).Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
                r++;
            }
            Finish(ws, r - 1, headers.Length);

            var suffix = device is > 0 ? deviceNames.GetValueOrDefault(device.Value, "device") : "all";
            return Results.File(ToBytes(wb), Xlsx, $"users_{Safe(suffix)}_{Stamp()}.xlsx");
        });

        // Attendance — same filters as the Attendance page.
        app.MapGet("/export/attendance", async (int? device, string? from, string? to, string? user,
                                                 IDbContextFactory<AppDbContext> dbf) =>
        {
            await using var db = dbf.CreateDbContext();
            var names = await UserDirectory.BestNamesAsync(db);

            IQueryable<AttendanceLog> q = db.AttendanceLogs;
            if (device is > 0) q = q.Where(l => l.DeviceId == device);
            if (DateTime.TryParse(from, out var f)) q = q.Where(l => l.Timestamp >= f.Date);
            if (DateTime.TryParse(to, out var t)) q = q.Where(l => l.Timestamp < t.Date.AddDays(1));
            if (!string.IsNullOrWhiteSpace(user))
            {
                var uf = user.Trim();
                var pins = names.Where(kv => kv.Value.Contains(uf, StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Key).ToList();
                q = q.Where(l => l.DeviceUserId.Contains(uf) || pins.Contains(l.DeviceUserId));
            }
            var rows = await q.OrderByDescending(l => l.Timestamp).Take(100000).ToListAsync();

            using var wb = new XLWorkbook();
            var ws = wb.AddWorksheet("Attendance");
            string[] headers = { "Date", "Time", "User ID", "Name", "Machine", "Verify", "In/Out", "Work code" };
            for (int c = 0; c < headers.Length; c++) ws.Cell(1, c + 1).Value = headers[c];

            int r = 2;
            foreach (var l in rows)
            {
                ws.Cell(r, 1).Value = l.Timestamp.Date;
                ws.Cell(r, 1).Style.DateFormat.Format = "yyyy-mm-dd";
                ws.Cell(r, 2).Value = l.Timestamp;
                ws.Cell(r, 2).Style.DateFormat.Format = "hh:mm:ss";
                ws.Cell(r, 3).Value = l.DeviceUserId;
                ws.Cell(r, 4).Value = names.GetValueOrDefault(l.DeviceUserId) ?? "";
                ws.Cell(r, 5).Value = l.DeviceName ?? "";
                ws.Cell(r, 6).Value = VerifyName(l.VerifyMode);
                ws.Cell(r, 7).Value = l.InOutMode == 0 ? "In" : l.InOutMode == 1 ? "Out" : l.InOutMode.ToString();
                ws.Cell(r, 8).Value = l.WorkCode;
                r++;
            }
            Finish(ws, r - 1, headers.Length);
            return Results.File(ToBytes(wb), Xlsx, $"attendance_{Stamp()}.xlsx");
        });

        // Enrollment coverage.
        app.MapGet("/export/coverage", async (IDbContextFactory<AppDbContext> dbf) =>
        {
            await using var db = dbf.CreateDbContext();
            var names = await UserDirectory.BestNamesAsync(db);
            var deptByPin = await db.Employees.Where(e => e.DepartmentId != null)
                .Select(e => new { e.Pin, Dept = e.Department!.Name }).ToDictionaryAsync(x => x.Pin, x => x.Dept);
            var raw = await db.EnrolledUsers.Select(u => new
            {
                u.DeviceUserId, u.SourceDeviceId,
                HasCard = u.CardNumber != null && u.CardNumber != "",
                HasFinger = u.Templates.Any(t => t.Type == TemplateType.Fingerprint),
                HasFace = u.Templates.Any(t => t.Type == TemplateType.Face)
            }).ToListAsync();
            var rows = raw.GroupBy(x => x.DeviceUserId).Select(g => new
            {
                Pin = g.Key, Name = names.GetValueOrDefault(g.Key, g.Key),
                Dept = deptByPin.GetValueOrDefault(g.Key, ""),
                Devices = g.Select(x => x.SourceDeviceId).Distinct().Count(),
                Finger = g.Any(x => x.HasFinger), Face = g.Any(x => x.HasFace), Card = g.Any(x => x.HasCard)
            }).OrderBy(r => r.Pin.Length).ThenBy(r => r.Pin).ToList();

            using var wb = new XLWorkbook();
            var ws = wb.AddWorksheet("Coverage");
            string[] headers = { "User ID", "Name", "Department", "Machines", "Fingerprint", "Face", "Card", "Status" };
            for (int c = 0; c < headers.Length; c++) ws.Cell(1, c + 1).Value = headers[c];
            int r = 2;
            foreach (var x in rows)
            {
                ws.Cell(r, 1).Value = x.Pin; ws.Cell(r, 2).Value = x.Name; ws.Cell(r, 3).Value = x.Dept;
                ws.Cell(r, 4).Value = x.Devices;
                ws.Cell(r, 5).Value = x.Finger ? "Yes" : "No"; ws.Cell(r, 6).Value = x.Face ? "Yes" : "No"; ws.Cell(r, 7).Value = x.Card ? "Yes" : "No";
                ws.Cell(r, 8).Value = !x.Finger && !x.Face ? "No biometric" : !x.Face ? "No face" : !x.Finger ? "No fingerprint" : "OK";
                r++;
            }
            Finish(ws, r - 1, headers.Length);
            return Results.File(ToBytes(wb), Xlsx, $"coverage_{Stamp()}.xlsx");
        });

        // Employee directory.
        app.MapGet("/export/employees", async (IDbContextFactory<AppDbContext> dbf) =>
        {
            await using var db = dbf.CreateDbContext();
            var rows = await db.Employees.Include(e => e.Department).Include(e => e.Shift)
                .OrderBy(e => e.Department!.Name).ThenBy(e => e.FullName).ToListAsync();

            using var wb = new XLWorkbook();
            var ws = wb.AddWorksheet("Employees");
            string[] headers = { "User ID", "Full name", "Department", "Job title", "Card", "Shift", "Hire date", "Active", "All sites" };
            for (int c = 0; c < headers.Length; c++) ws.Cell(1, c + 1).Value = headers[c];
            int r = 2;
            foreach (var e in rows)
            {
                ws.Cell(r, 1).Value = e.Pin;
                ws.Cell(r, 2).Value = e.FullName ?? "";
                ws.Cell(r, 3).Value = e.Department?.Name ?? "";
                ws.Cell(r, 4).Value = e.JobTitle ?? "";
                ws.Cell(r, 5).Value = e.CardNumber ?? "";
                ws.Cell(r, 6).Value = e.Shift?.Name ?? "";
                if (e.HireDate is { } hd) { ws.Cell(r, 7).Value = hd.ToDateTime(TimeOnly.MinValue); ws.Cell(r, 7).Style.DateFormat.Format = "yyyy-mm-dd"; }
                ws.Cell(r, 8).Value = e.Active ? "Yes" : "No";
                ws.Cell(r, 9).Value = e.AllSites ? "Yes" : "";
                r++;
            }
            Finish(ws, r - 1, headers.Length);
            return Results.File(ToBytes(wb), Xlsx, $"employees_{Stamp()}.xlsx");
        });

        // Computed attendance report (Summary + Daily sheets).
        app.MapGet("/export/attendance-report", async (string? from, string? to, int? dept, string? search,
                                                        IDbContextFactory<AppDbContext> dbf) =>
        {
            await using var db = dbf.CreateDbContext();
            var f = DateTime.TryParse(from, out var fd) ? DateOnly.FromDateTime(fd) : DateOnly.FromDateTime(DateTime.Today.AddDays(-7));
            var t = DateTime.TryParse(to, out var td) ? DateOnly.FromDateTime(td) : DateOnly.FromDateTime(DateTime.Today);
            var report = await AttendanceReportBuilder.BuildAsync(db, f, t, dept, search);

            using var wb = new XLWorkbook();
            var ws1 = wb.AddWorksheet("Summary");
            string[] h1 = { "User ID", "Name", "Department", "Present", "Late", "Absent", "Incomplete", "Off/Holiday", "Total hours" };
            for (int c = 0; c < h1.Length; c++) ws1.Cell(1, c + 1).Value = h1[c];
            int r = 2;
            foreach (var s in report.Summaries)
            {
                ws1.Cell(r, 1).Value = s.Pin; ws1.Cell(r, 2).Value = s.Name; ws1.Cell(r, 3).Value = s.Department;
                ws1.Cell(r, 4).Value = s.Present; ws1.Cell(r, 5).Value = s.Late; ws1.Cell(r, 6).Value = s.Absent;
                ws1.Cell(r, 7).Value = s.Incomplete; ws1.Cell(r, 8).Value = s.OffOrHoliday; ws1.Cell(r, 9).Value = s.TotalHours;
                r++;
            }
            Finish(ws1, r - 1, h1.Length);

            var ws2 = wb.AddWorksheet("Daily");
            string[] h2 = { "Date", "Day", "User ID", "Name", "Department", "Shift", "In", "Out", "Hours", "Punches", "Status" };
            for (int c = 0; c < h2.Length; c++) ws2.Cell(1, c + 1).Value = h2[c];
            r = 2;
            foreach (var d in report.Days)
            {
                ws2.Cell(r, 1).Value = d.Date.ToDateTime(TimeOnly.MinValue); ws2.Cell(r, 1).Style.DateFormat.Format = "yyyy-mm-dd";
                ws2.Cell(r, 2).Value = d.DayName; ws2.Cell(r, 3).Value = d.Pin; ws2.Cell(r, 4).Value = d.Name; ws2.Cell(r, 5).Value = d.Department;
                ws2.Cell(r, 6).Value = d.Shift;
                ws2.Cell(r, 7).Value = d.In?.ToString("HH:mm") ?? ""; ws2.Cell(r, 8).Value = d.Out?.ToString("HH:mm") ?? "";
                ws2.Cell(r, 9).Value = d.Hours; ws2.Cell(r, 10).Value = d.Punches; ws2.Cell(r, 11).Value = d.Status;
                r++;
            }
            Finish(ws2, r - 1, h2.Length);

            return Results.File(ToBytes(wb), Xlsx, $"attendance_report_{Stamp()}.xlsx");
        });
    }

    private static void Finish(IXLWorksheet ws, int lastRow, int cols)
    {
        var table = ws.Range(1, 1, Math.Max(lastRow, 1), cols).CreateTable();
        table.Theme = XLTableTheme.TableStyleMedium2;
        table.HeadersRow().Style.Font.Bold = true;
        ws.SheetView.FreezeRows(1);
        // AdjustToContents relies on SixLabors.Fonts + system fonts; degrade gracefully if unavailable.
        try
        {
            ws.Columns().AdjustToContents();
            foreach (var col in ws.ColumnsUsed())
                if (col.Width > 55) col.Width = 55;
        }
        catch { for (int c = 1; c <= cols; c++) ws.Column(c).Width = 18; }
    }

    private static byte[] ToBytes(XLWorkbook wb)
    {
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static string Stamp() => DateTime.Now.ToString("yyyyMMdd_HHmm");
    private static string Safe(string s) => string.Concat(s.Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_'));

    private static string VerifyName(int v) => v switch
    {
        0 => "Password/Auto", 1 => "Fingerprint", 2 => "Fingerprint", 3 => "Password",
        4 => "Card", 15 => "Face", 25 => "Palm", _ => $"Mode {v}"
    };
}
