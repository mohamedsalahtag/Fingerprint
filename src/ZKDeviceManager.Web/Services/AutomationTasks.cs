using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ZKDeviceManager.Data;
using ZKDeviceManager.Data.Entities;
using ZKDeviceManager.Devices.Jobs;

namespace ZKDeviceManager.Web.Services;

/// <summary>
/// The actual work behind each scheduled automation. Shared by the background scheduler and the
/// "Run now" buttons so both do exactly the same thing.
/// </summary>
public static class AutomationTasks
{
    public static string BackupDir => Path.Combine(AppContext.BaseDirectory, "backups");

    public static async Task<string> RunAsync(ScheduledTaskType type, AppDbContext db, JobService jobs) => type switch
    {
        ScheduledTaskType.NightlySync => $"Queued sync for {await QueueForConnectedAsync(db, jobs, sync: true)} device(s).",
        ScheduledTaskType.NightlyLogPull => $"Queued log pull for {await QueueForConnectedAsync(db, jobs, sync: false)} device(s).",
        ScheduledTaskType.NightlyBackup => await RunBackupAsync(db),
        _ => "Unknown task."
    };

    private static async Task<int> QueueForConnectedAsync(AppDbContext db, JobService jobs, bool sync)
    {
        var serials = await db.PushDevices.Select(p => p.SerialNumber).ToListAsync();
        var ids = await db.Devices
            .Where(d => d.SerialNumber != null && serials.Contains(d.SerialNumber!))
            .Select(d => d.Id).ToListAsync();
        foreach (var id in ids)
            if (sync) await jobs.QueueSyncDownloadAsync(id);
            else await jobs.QueueSyncDownloadLogsAsync(id);
        return ids.Count;
    }

    public static async Task<string> RunBackupAsync(AppDbContext db)
    {
        Directory.CreateDirectory(BackupDir);
        var deviceNames = await db.Devices.ToDictionaryAsync(d => d.Id, d => d.Name);
        var users = await db.EnrolledUsers.Include(u => u.Templates).AsNoTracking().ToListAsync();

        var snapshot = new
        {
            createdUtc = DateTime.UtcNow,
            userCount = users.Count,
            templateCount = users.Sum(u => u.Templates.Count),
            users = users.Select(u => new
            {
                device = deviceNames.GetValueOrDefault(u.SourceDeviceId, u.SourceDeviceId.ToString()),
                u.SourceDeviceId, pin = u.DeviceUserId, u.Name, u.Privilege, u.Enabled, u.CardNumber,
                templates = u.Templates.Select(t => new { type = t.Type.ToString(), t.FingerIndex, t.Flag, t.Length, t.Data })
            })
        };

        var name = $"zkdm_backup_{DateTime.Now:yyyyMMdd_HHmmss}.json";
        var path = Path.Combine(BackupDir, name);
        await using (var fs = File.Create(path))
            await JsonSerializer.SerializeAsync(fs, snapshot);

        // Retention: keep the 14 most recent backups.
        foreach (var old in new DirectoryInfo(BackupDir).GetFiles("zkdm_backup_*.json")
                     .OrderByDescending(f => f.Name).Skip(14))
            try { old.Delete(); } catch { /* ignore */ }

        var kb = new FileInfo(path).Length / 1024;
        return $"{users.Count} users, {snapshot.templateCount} templates → {name} ({kb} KB).";
    }
}
