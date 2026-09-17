using Microsoft.EntityFrameworkCore;
using ZKDeviceManager.Data;
using ZKDeviceManager.Data.Entities;

namespace ZKDeviceManager.Web.Services;

/// <summary>
/// Live, database-derived counts for a device. This is the SINGLE source of truth for counts across
/// the app (dashboard, devices list, device details, user templates) — replacing the old cumulative
/// PushDevice counters that never reset and diverged from reality.
/// <para><b>A face always counts as ONE per employee</b>, everywhere. A terminal physically stores ~12
/// face rows per enrolled person; those are internal parts of a single enrollment, never counted
/// individually. Both <see cref="UsersWithFaces"/> and <see cref="FaceTemplates"/> are therefore
/// per-person counts. Fingerprints are different: each enrolled finger is a real, separate template.</para>
/// </summary>
public readonly record struct DeviceStats(
    int Users,
    int UsersWithFaces,
    int UsersWithFingerprints,
    int UsersWithCards,
    int FingerprintTemplates,
    int FaceTemplates,
    int Logs)
{
    public static readonly DeviceStats Empty = new(0, 0, 0, 0, 0, 0, 0);
}

public static class DeviceStatsQuery
{
    public static async Task<DeviceStats> ForDeviceAsync(AppDbContext db, int deviceId)
    {
        var rows = await db.EnrolledUsers
            .Where(u => u.SourceDeviceId == deviceId)
            .Select(u => new UserFacts(
                u.Templates.Any(t => t.Type == TemplateType.Face),
                u.Templates.Any(t => t.Type == TemplateType.Fingerprint),
                u.CardNumber != null && u.CardNumber != "",
                u.Templates.Count(t => t.Type == TemplateType.Fingerprint),
                u.Templates.Count(t => t.Type == TemplateType.Face)))
            .ToListAsync();
        var logs = await db.AttendanceLogs.CountAsync(l => l.DeviceId == deviceId);
        return Aggregate(rows, logs);
    }

    public static async Task<Dictionary<int, DeviceStats>> ForAllAsync(AppDbContext db)
    {
        var perUser = await db.EnrolledUsers
            .Select(u => new
            {
                u.SourceDeviceId,
                Facts = new UserFacts(
                    u.Templates.Any(t => t.Type == TemplateType.Face),
                    u.Templates.Any(t => t.Type == TemplateType.Fingerprint),
                    u.CardNumber != null && u.CardNumber != "",
                    u.Templates.Count(t => t.Type == TemplateType.Fingerprint),
                    u.Templates.Count(t => t.Type == TemplateType.Face))
            })
            .ToListAsync();

        var logs = await db.AttendanceLogs
            .GroupBy(l => l.DeviceId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);

        return perUser
            .GroupBy(x => x.SourceDeviceId)
            .ToDictionary(g => g.Key, g => Aggregate(g.Select(x => x.Facts), logs.GetValueOrDefault(g.Key)));
    }

    private readonly record struct UserFacts(bool HasFace, bool HasFinger, bool HasCard, int Fp, int Fc);

    private static DeviceStats Aggregate(IEnumerable<UserFacts> facts, int logs)
    {
        var list = facts as ICollection<UserFacts> ?? facts.ToList();
        return new DeviceStats(
            list.Count,
            list.Count(x => x.HasFace),
            list.Count(x => x.HasFinger),
            list.Count(x => x.HasCard),
            list.Sum(x => x.Fp),
            list.Count(x => x.HasFace),   // one face per person, not the ~12 stored rows
            logs);
    }
}
