using Microsoft.EntityFrameworkCore;
using ZKDeviceManager.Data;
using ZKDeviceManager.Data.Entities;

namespace ZKDeviceManager.Web.Services;

public record AttendanceDay(
    string Pin, string Name, string Department, DateOnly Date, string DayName, string Shift,
    TimeOnly? In, TimeOnly? Out, double Hours, int Punches, string Status);

public record AttendanceSummary(
    string Pin, string Name, string Department,
    int Present, int Late, int Absent, int Incomplete, int OffOrHoliday, double TotalHours);

public class AttendanceReportResult
{
    public List<AttendanceDay> Days { get; } = new();
    public List<AttendanceSummary> Summaries { get; } = new();
}

/// <summary>
/// Turns raw attendance punches into per-employee-per-day results: first-in/last-out pairing, hours,
/// and Present / Late / Absent / Incomplete / Off / Holiday status using the employee's shift and the
/// holiday calendar. Pure computation over data already in this database.
/// </summary>
public static class AttendanceReportBuilder
{
    private const string DefaultWorkDays = "1111100"; // Sun–Thu working (used when an employee has no shift)

    public static async Task<AttendanceReportResult> BuildAsync(
        AppDbContext db, DateOnly from, DateOnly to, int? departmentId, string? search)
    {
        if (to < from) (from, to) = (to, from);

        var empQuery = db.Employees.Include(e => e.Department).Include(e => e.Shift)
            .Where(e => e.Active);
        if (departmentId is > 0) empQuery = empQuery.Where(e => e.DepartmentId == departmentId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            empQuery = empQuery.Where(e => e.Pin.Contains(s) || (e.FullName != null && e.FullName.Contains(s)));
        }
        var employees = await empQuery.AsNoTracking().ToListAsync();

        var result = new AttendanceReportResult();
        if (employees.Count == 0) return result;

        var pins = employees.Select(e => e.Pin).ToHashSet();
        var fromDt = from.ToDateTime(TimeOnly.MinValue);
        var toDt = to.AddDays(1).ToDateTime(TimeOnly.MinValue);

        var logs = await db.AttendanceLogs
            .Where(l => l.Timestamp >= fromDt && l.Timestamp < toDt && pins.Contains(l.DeviceUserId))
            .Select(l => new { l.DeviceUserId, l.Timestamp })
            .ToListAsync();

        // (pin, date) -> ordered punch times
        var byUserDay = logs
            .GroupBy(l => (l.DeviceUserId, DateOnly.FromDateTime(l.Timestamp)))
            .ToDictionary(g => g.Key, g => g.Select(x => x.Timestamp).OrderBy(t => t).ToList());

        var holidays = (await db.Holidays.Where(h => h.Date >= from && h.Date <= to).Select(h => h.Date).ToListAsync())
            .ToHashSet();

        foreach (var emp in employees.OrderBy(e => e.Department?.Name).ThenBy(e => e.FullName ?? e.Pin))
        {
            var name = string.IsNullOrWhiteSpace(emp.FullName) ? emp.Pin : emp.FullName!;
            var dept = emp.Department?.Name ?? "—";
            var shiftName = emp.Shift?.Name ?? "—";
            int present = 0, late = 0, absent = 0, incomplete = 0, offHol = 0;
            double totalHours = 0;

            for (var d = from; d <= to; d = d.AddDays(1))
            {
                bool isHoliday = holidays.Contains(d);
                bool isWorkday = IsWorkday(emp.Shift, d.DayOfWeek);
                byUserDay.TryGetValue((emp.Pin, d), out var punches);
                punches ??= new List<DateTime>();

                TimeOnly? inT = punches.Count > 0 ? TimeOnly.FromDateTime(punches[0]) : null;
                TimeOnly? outT = punches.Count > 1 ? TimeOnly.FromDateTime(punches[^1]) : null;
                double hours = (inT is not null && outT is not null && outT > inT)
                    ? (outT.Value - inT.Value).TotalHours : 0;

                string status;
                if (punches.Count == 0)
                {
                    if (isHoliday) { status = "Holiday"; offHol++; }
                    else if (!isWorkday) { status = "Off"; offHol++; }
                    else { status = "Absent"; absent++; }
                }
                else if (punches.Count == 1)
                {
                    status = "Incomplete"; incomplete++;
                }
                else
                {
                    bool isLate = emp.Shift is not null &&
                                  inT!.Value > emp.Shift.Start.AddMinutes(emp.Shift.GraceMinutes);
                    status = isLate ? "Late" : "Present";
                    if (isLate) late++; else present++;
                    totalHours += hours;
                }

                // Only emit rows that carry information (skip clean Off/Holiday days to keep the sheet readable).
                if (status is not ("Off" or "Holiday"))
                    result.Days.Add(new AttendanceDay(emp.Pin, name, dept, d, d.DayOfWeek.ToString(),
                        shiftName, inT, outT, Math.Round(hours, 2), punches.Count, status));
            }

            result.Summaries.Add(new AttendanceSummary(emp.Pin, name, dept,
                present, late, absent, incomplete, offHol, Math.Round(totalHours, 2)));
        }

        return result;
    }

    private static bool IsWorkday(Shift? shift, DayOfWeek day)
    {
        var mask = shift?.WorkDays ?? DefaultWorkDays;
        int i = (int)day; // Sunday = 0
        return i >= 0 && i < mask.Length && mask[i] == '1';
    }
}
