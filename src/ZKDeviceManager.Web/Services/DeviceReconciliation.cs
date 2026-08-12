using Microsoft.EntityFrameworkCore;
using ZKDeviceManager.Data;
using ZKDeviceManager.Data.Entities;

namespace ZKDeviceManager.Web.Services;

/// <summary>
/// One-time (idempotent) cleanup that merges duplicate <see cref="Device"/> records for the same
/// physical terminal into a single identity, keyed by serial number.
///
/// The app used to auto-create a separate "PUSH {sn}" device when a terminal pushed data before it
/// was matched to a registered device — so the same terminal could exist twice (registered-by-IP and
/// pushed-by-serial), splitting its users/logs and making counts disagree between pages. This walks
/// every serial that maps to more than one device and folds the extras into the preferred keeper
/// (a manually-registered device wins over an auto-created "PUSH …" one).
///
/// Runs once at startup; after the first pass there are no duplicate serials so it becomes a no-op.
/// </summary>
public static class DeviceReconciliation
{
    public static async Task MergeDuplicatesBySerialAsync(AppDbContext db, ILogger logger)
    {
        var dupSerials = await db.Devices
            .Where(d => d.SerialNumber != null && d.SerialNumber != "")
            .GroupBy(d => d.SerialNumber!)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToListAsync();

        if (dupSerials.Count == 0) return;

        foreach (var serial in dupSerials)
        {
            var devices = await db.Devices.Where(d => d.SerialNumber == serial).ToListAsync();
            var keeper = devices
                .OrderBy(d => d.Name.StartsWith("PUSH ", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                .ThenBy(d => d.Id)
                .First();

            foreach (var orphan in devices.Where(d => d.Id != keeper.Id))
                await MergeAsync(db, orphan, keeper);

            logger.LogInformation("Reconciled {N} duplicate device(s) for serial {Serial} into #{Id} ({Name})",
                devices.Count - 1, serial, keeper.Id, keeper.Name);
        }
    }

    private static async Task MergeAsync(AppDbContext db, Device orphan, Device keeper)
    {
        await using var tx = await db.Database.BeginTransactionAsync();

        // --- Users: re-point non-colliding PINs; fold colliding PINs' templates into the keeper user. ---
        var keeperPins = await db.EnrolledUsers.Where(u => u.SourceDeviceId == keeper.Id)
            .Select(u => new { u.Id, u.DeviceUserId }).ToListAsync();
        var keeperByPin = keeperPins.ToDictionary(x => x.DeviceUserId, x => x.Id, StringComparer.Ordinal);

        var orphanUsers = await db.EnrolledUsers.Where(u => u.SourceDeviceId == orphan.Id)
            .Select(u => new { u.Id, u.DeviceUserId }).ToListAsync();

        foreach (var ou in orphanUsers)
        {
            if (keeperByPin.TryGetValue(ou.DeviceUserId, out var keeperUserId))
            {
                // Same PIN on both — move the orphan user's templates onto the keeper user, then drop it.
                await db.BiometricTemplates.Where(t => t.EnrolledUserId == ou.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.EnrolledUserId, keeperUserId));
                await db.EnrolledUsers.Where(u => u.Id == ou.Id).ExecuteDeleteAsync();
            }
            else
            {
                await db.EnrolledUsers.Where(u => u.Id == ou.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(u => u.SourceDeviceId, keeper.Id));
            }
        }

        // --- Attendance logs: drop collisions (unique on DeviceId+PIN+Timestamp) then re-point the rest. ---
        await db.AttendanceLogs
            .Where(l => l.DeviceId == orphan.Id && db.AttendanceLogs.Any(k =>
                k.DeviceId == keeper.Id && k.DeviceUserId == l.DeviceUserId && k.Timestamp == l.Timestamp))
            .ExecuteDeleteAsync();
        await db.AttendanceLogs.Where(l => l.DeviceId == orphan.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(l => l.DeviceId, keeper.Id)
                .SetProperty(l => l.DeviceName, keeper.Name)
                .SetProperty(l => l.DeviceIp, keeper.IpAddress));

        // --- Jobs reference devices with RESTRICT, so re-point before deleting the orphan. ---
        await db.SyncJobs.Where(j => j.DeviceId == orphan.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.DeviceId, keeper.Id));
        await db.SyncJobs.Where(j => j.TargetDeviceId == orphan.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.TargetDeviceId, (int?)keeper.Id));

        // --- Carry over any info the keeper is missing, then delete the orphan device. ---
        var keeperRow = await db.Devices.FirstAsync(d => d.Id == keeper.Id);
        var orphanRow = await db.Devices.FirstAsync(d => d.Id == orphan.Id);
        keeperRow.FirmwareVersion ??= orphanRow.FirmwareVersion;
        keeperRow.Platform ??= orphanRow.Platform;
        keeperRow.Model ??= orphanRow.Model;
        keeperRow.Location ??= orphanRow.Location;
        keeperRow.LastSeenUtc ??= orphanRow.LastSeenUtc;
        await db.SaveChangesAsync();

        await db.Devices.Where(d => d.Id == orphan.Id).ExecuteDeleteAsync();

        await tx.CommitAsync();
    }
}
