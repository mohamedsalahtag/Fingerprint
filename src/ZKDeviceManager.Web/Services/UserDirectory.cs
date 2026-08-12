using Microsoft.EntityFrameworkCore;
using ZKDeviceManager.Data;

namespace ZKDeviceManager.Web.Services;

/// <summary>Helpers for resolving a user's unified default (display) name by PIN.</summary>
public static class UserDirectory
{
    /// <summary>Map of PIN → chosen default name (only PINs that have a non-empty DisplayName).</summary>
    public static async Task<Dictionary<string, string>> DisplayNamesAsync(AppDbContext db)
        => await db.UserProfiles
            .Where(p => p.DisplayName != null && p.DisplayName != "")
            .ToDictionaryAsync(p => p.Pin, p => p.DisplayName!);

    /// <summary>Map of PIN → best available name: the unified default if set, else the most common
    /// device-provided name (ignoring names that are just the PIN).</summary>
    public static async Task<Dictionary<string, string>> BestNamesAsync(AppDbContext db)
    {
        var euNames = await db.EnrolledUsers
            .Where(u => u.Name != null && u.Name != "")
            .Select(u => new { u.DeviceUserId, u.Name })
            .ToListAsync();

        var map = new Dictionary<string, string>();
        foreach (var g in euNames.GroupBy(x => x.DeviceUserId))
        {
            var best = g.Select(x => x.Name!).Where(n => n != g.Key)
                .GroupBy(n => n).OrderByDescending(z => z.Count()).Select(z => z.Key).FirstOrDefault();
            if (best != null) map[g.Key] = best;
        }
        foreach (var p in await DisplayNamesAsync(db)) map[p.Key] = p.Value; // unified default name wins over device name

        // The Employee (HR) full name is the highest-priority label.
        var emp = await db.Employees.Where(e => e.FullName != null && e.FullName != "")
            .Select(e => new { e.Pin, e.FullName }).ToListAsync();
        foreach (var e in emp) map[e.Pin] = e.FullName!;
        return map;
    }

    /// <summary>Best label for a user: unified default name, else the device-provided name, else the PIN.</summary>
    public static string Resolve(IReadOnlyDictionary<string, string> defaults, string pin, string? deviceName)
        => defaults.TryGetValue(pin, out var d) && !string.IsNullOrWhiteSpace(d) ? d
           : !string.IsNullOrWhiteSpace(deviceName) ? deviceName!
           : pin;
}
