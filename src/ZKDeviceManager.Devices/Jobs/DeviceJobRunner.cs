using System.Runtime.Versioning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ZKDeviceManager.Data;
using ZKDeviceManager.Data.Entities;
using ZKDeviceManager.Devices.Model;
using ZKDeviceManager.Devices.Sta;

namespace ZKDeviceManager.Devices.Jobs;

/// <summary>
/// Consumes queued <see cref="SyncJob"/> rows and executes them. Two families of work:
/// <list type="bullet">
///   <item><b>PUSH/ADMS data ops</b> (download / copy / logs) — queue <see cref="PushCommand"/>s for the
///   terminal and poll the database until the terminal has delivered/applied them. No COM SDK, so faces
///   work on modern Windows.</item>
///   <item><b>SDK control ops</b> (restart / power off / clear users / sync time / enable-disable UI) — run
///   on the shared STA thread via <see cref="IZkDeviceService"/>.</item>
/// </list>
/// One job runs at a time; live progress is reported to <see cref="IJobNotifier"/>.
/// </summary>
[SupportedOSPlatform("windows")]
public class DeviceJobRunner : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDbContextFactory<AppDbContext> _dbf;
    private readonly IJobQueue _queue;
    private readonly IJobNotifier _notifier;
    private readonly IZkDeviceService _zk;
    private readonly StaExecutor _sta;
    private readonly JobCancellation _cancellation;
    private readonly ILogger<DeviceJobRunner> _log;

    public DeviceJobRunner(IServiceScopeFactory scopeFactory, IDbContextFactory<AppDbContext> dbf,
                           IJobQueue queue, IJobNotifier notifier, IZkDeviceService zk, StaExecutor sta,
                           JobCancellation cancellation, ILogger<DeviceJobRunner> log)
    {
        _scopeFactory = scopeFactory;
        _dbf = dbf;
        _queue = queue;
        _notifier = notifier;
        _zk = zk;
        _sta = sta;
        _cancellation = cancellation;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverInterruptedJobsAsync(stoppingToken);

        await foreach (var jobId in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try { await RunJobAsync(jobId, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { _log.LogError(ex, "Job {JobId} crashed", jobId); }
        }
    }

    /// <summary>On startup, re-queue anything left Queued and fail anything left Running.</summary>
    private async Task RecoverInterruptedJobsAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var running = await db.SyncJobs.Where(j => j.Status == SyncJobStatus.Running).ToListAsync(ct);
            foreach (var j in running)
            {
                j.Status = SyncJobStatus.Failed;
                j.Message = "Interrupted by application restart.";
                j.FinishedUtc = DateTime.UtcNow;
            }

            var queued = await db.SyncJobs.Where(j => j.Status == SyncJobStatus.Queued)
                                          .Select(j => j.Id).ToListAsync(ct);
            await db.SaveChangesAsync(ct);
            foreach (var id in queued) await _queue.EnqueueAsync(id, ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Job recovery skipped (database not reachable yet?)");
        }
    }

    private async Task RunJobAsync(int jobId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var job = await db.SyncJobs.FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job == null || job.Status != SyncJobStatus.Queued) return;

        job.Status = SyncJobStatus.Running;
        job.StartedUtc = DateTime.UtcNow;
        job.Message = "Running...";
        await db.SaveChangesAsync(ct);
        _notifier.NotifyChanged(jobId);

        // Per-job cancellation: a linked source the UI can trip to cancel THIS job.
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var jobCt = linkedCts.Token;
        _cancellation.Register(jobId, linkedCts);

        try
        {
            switch (job.Type)
            {
                // ---- PUSH/ADMS data ops ----
                case SyncJobType.SyncDownload:
                    await PushDownloadAsync(job, jobCt);
                    break;
                case SyncJobType.SyncDownloadLogs:
                    await PushDownloadLogsAsync(job, jobCt);
                    break;
                case SyncJobType.SyncCopy:
                    await PushCopyAsync(job, jobCt);
                    break;
                case SyncJobType.ApplyName:
                    await PushApplyNameAsync(job, jobCt);
                    break;
                case SyncJobType.CreateUser:
                    await PushCreateUserAsync(job, jobCt);
                    break;
                case SyncJobType.DeleteUser:
                    await PushDeleteUserAsync(job, jobCt);
                    break;
                case SyncJobType.EnrollFinger:
                    await PushEnrollAsync(job, $"ENROLL_FP PIN={job.UserFilter}\tFID=0\tRETRY=3\tOVERWRITE=1",
                        "fingerprint", jobCt);
                    break;
                case SyncJobType.EnrollFace:
                    await PushEnrollAsync(job, $"ENROLL_BIO TYPE=2\tPIN={job.UserFilter}\tNO=0\tRETRY=3\tOVERWRITE=1",
                        "face", jobCt);
                    break;

                // ---- SDK control ops (STA thread) ----
                case SyncJobType.ControlRestart:
                    await ControlAsync(db, job, DeviceControlAction.Restart, jobCt);
                    break;
                case SyncJobType.ControlPowerOff:
                    await ControlAsync(db, job, DeviceControlAction.PowerOff, jobCt);
                    break;
                case SyncJobType.ControlClearUsers:
                    await ControlAsync(db, job, DeviceControlAction.ClearAllUsers, jobCt);
                    break;
                case SyncJobType.ControlEnableUi:
                    await ControlAsync(db, job, DeviceControlAction.EnableUi, jobCt);
                    break;
                case SyncJobType.ControlDisableUi:
                    await ControlAsync(db, job, DeviceControlAction.DisableUi, jobCt);
                    break;
                case SyncJobType.ControlSyncTime:
                    await SyncTimeAsync(db, job, jobCt);
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Job type {job.Type} is no longer supported (legacy SDK data op). " +
                        "Use Sync data / Copy instead.");
            }

            if (_zk is FakeZkDeviceService && !string.IsNullOrEmpty(job.Message))
                job.Message = "[SIMULATED — not from a real device] " + job.Message;

            job.Status = SyncJobStatus.Succeeded;
            job.FinishedUtc = DateTime.UtcNow;
        }
        catch (OperationCanceledException) when (linkedCts.IsCancellationRequested)
        {
            job.Status = SyncJobStatus.Cancelled;
            job.Message = ct.IsCancellationRequested ? "Cancelled (application stopping)." : "Cancelled by user.";
            job.FinishedUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Job {JobId} failed", jobId);
            job.Status = SyncJobStatus.Failed;
            job.Message = ex.Message.Length > 1900 ? ex.Message[..1900] : ex.Message;
            job.FinishedUtc = DateTime.UtcNow;
        }
        finally
        {
            _cancellation.Unregister(jobId);
        }

        await db.SaveChangesAsync(CancellationToken.None);
        _notifier.NotifyChanged(jobId);
    }

    private static DeviceConnection Conn(Device d) => new(d.IpAddress, d.Port, d.CommKey);

    // ======================= PUSH/ADMS data ops =======================

    private async Task PushDownloadAsync(SyncJob job, CancellationToken ct)
    {
        var (device, serial) = await ResolvePushTargetAsync(job.DeviceId, "this device", ct);
        var ids = await QueueCommandsAsync(serial,
            new[] { "DATA QUERY USERINFO", "DATA QUERY FINGERTMP", "DATA QUERY BIODATA" }, ct);

        var final = await PollReceiveAsync(job, device.Id, ids, "Downloading users, fingerprints & faces", ct);
        job.Total = final.Users;
        job.Progress = final.Users;
        job.Message = $"Synced from {device.Name}: {final.Users} users, {final.Fingerprints} fingerprint " +
                      $"templates, {final.Faces} face(s) — one per enrolled person.";
    }

    private async Task PushDownloadLogsAsync(SyncJob job, CancellationToken ct)
    {
        var (device, serial) = await ResolvePushTargetAsync(job.DeviceId, "this device", ct);
        var before = await ReadCountsAsync(device.Id, ct);
        var ids = await QueueCommandsAsync(serial, new[] { "DATA QUERY ATTLOG" }, ct);

        var final = await PollReceiveAsync(job, device.Id, ids, "Downloading attendance logs", ct);
        var added = final.Logs - before.Logs;
        job.Total = final.Logs;
        job.Progress = final.Logs;
        job.Message = $"Attendance sync from {device.Name}: {added} new log(s) ({final.Logs} stored in total).";
    }

    private async Task PushCopyAsync(SyncJob job, CancellationToken ct)
    {
        if (job.TargetDeviceId is null)
            throw new InvalidOperationException("Copy job has no target device.");

        Device source, target;
        await using (var db = _dbf.CreateDbContext())
        {
            source = await db.Devices.FirstAsync(d => d.Id == job.DeviceId, ct);
            target = await db.Devices.FirstAsync(d => d.Id == job.TargetDeviceId!.Value, ct);
        }
        var (targetDev, targetSerial) = await ResolvePushTargetAsync(target.Id, "the target terminal", ct);

        var commands = await BuildCopyCommandsAsync(source.Id, targetSerial,
            job.IncludeFingerprints, job.IncludeFaces, job.IncludeCards, job.UserFilter, ct);
        if (commands.Count == 0)
            throw new InvalidOperationException(job.UserFilter is null
                ? $"{source.Name} has no stored users to copy. Sync it (Sync data) first."
                : $"User {job.UserFilter} is not stored for {source.Name}. Sync that device first.");

        var who = job.UserFilter is null ? "all users" : $"user {job.UserFilter}";
        int userCmds = commands.Count(c => c.StartsWith("DATA UPDATE USERINFO", StringComparison.Ordinal));
        int fpCmds = commands.Count(c => c.StartsWith("DATA UPDATE FINGERTMP", StringComparison.Ordinal));
        // A face is ONE per person even though the terminal stores it as ~12 rows/commands.
        int faceCount = commands.Where(c => c.StartsWith("DATA UPDATE FACE", StringComparison.Ordinal))
            .Select(PinOf).Distinct().Count();
        var ids = await QueueCommandsAsync(targetSerial, commands, ct);
        job.Total = ids.Count;

        var (delivered, completed) = await PollDeliverAsync(job, ids, $"Copying {who} to {targetDev.Name}", ct);
        job.Progress = delivered;
        var templateCmds = ids.Count - userCmds;
        job.Message = $"Copy {who} to {targetDev.Name}: {userCmds} user record(s) + {fpCmds} fingerprint(s) + " +
                      $"{faceCount} face(s); " +
                      $"{delivered} delivered, {completed} acknowledged by the terminal. " +
                      (templateCmds > 0 && completed <= userCmds
                          ? "WARNING: the terminal acknowledged no template — the fingerprints/face may NOT have "
                            + "transferred. Re-sync the target and check its biometric counts."
                          : delivered >= ids.Count
                              ? "Re-sync the target to confirm the stored counts."
                              : "Delivery continues as the terminal polls; check the target shortly.");
    }

    private async Task PushCreateUserAsync(SyncJob job, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(job.UserFilter))
            throw new InvalidOperationException("Create-user job has no user.");
        var (device, serial) = await ResolvePushTargetAsync(job.DeviceId, "this device", ct);

        string? name; EnrolledUser? existing;
        await using (var db = _dbf.CreateDbContext())
        {
            name = (await db.UserProfiles.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Pin == job.UserFilter, ct))?.DisplayName;
            existing = await db.EnrolledUsers.AsNoTracking()
                .FirstOrDefaultAsync(u => u.SourceDeviceId == device.Id && u.DeviceUserId == job.UserFilter, ct);
        }
        var displayName = string.IsNullOrWhiteSpace(name) ? job.UserFilter : name;
        var pri = existing?.Privilege ?? 0;

        var cmd = $"DATA UPDATE USERINFO PIN={job.UserFilter}\tName={displayName}\tPri={pri}\t" +
                  $"Passwd={existing?.Password}\tCard={existing?.CardNumber}\tGrp=1\tTZ=0000000000000000\tVerify=-1\tViceCard=";
        var ids = await QueueCommandsAsync(serial, new[] { cmd }, ct);
        job.Total = 1;
        var (delivered, completed) = await PollDeliverAsync(job, ids, $"Creating user on {device.Name}", ct);
        job.Progress = delivered;
        job.Message = $"Created/updated user {job.UserFilter} (\"{displayName}\") on {device.Name} " +
                      $"({(delivered > 0 ? "delivered" : "queued")}).";
    }

    private async Task PushEnrollAsync(SyncJob job, string command, string kind, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(job.UserFilter))
            throw new InvalidOperationException("Enrollment job has no user.");
        var (device, serial) = await ResolvePushTargetAsync(job.DeviceId, "this device", ct);

        var ids = await QueueCommandsAsync(serial, new[] { command }, ct);
        job.Total = 1;
        var (delivered, completed) = await PollDeliverAsync(job, ids, $"Sending {kind} enrollment to {device.Name}", ct);
        job.Progress = delivered;
        job.Message = $"{kind} enrollment sent to {device.Name} for user {job.UserFilter} " +
                      $"({(delivered > 0 ? "delivered — ask the person to enroll at the terminal now" : "queued")}). " +
                      "The captured template uploads automatically; re-sync the device to confirm it saved.";
    }

    private async Task PushApplyNameAsync(SyncJob job, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(job.UserFilter))
            throw new InvalidOperationException("Apply-name job has no user.");
        var (device, serial) = await ResolvePushTargetAsync(job.DeviceId, "this device", ct);

        EnrolledUser? user; string? displayName;
        await using (var db = _dbf.CreateDbContext())
        {
            user = await db.EnrolledUsers.AsNoTracking()
                .FirstOrDefaultAsync(u => u.SourceDeviceId == device.Id && u.DeviceUserId == job.UserFilter, ct);
            displayName = (await db.UserProfiles.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Pin == job.UserFilter, ct))?.DisplayName;
        }
        if (string.IsNullOrWhiteSpace(displayName))
            throw new InvalidOperationException("No default name is set for this user yet.");
        if (user is null)
            throw new InvalidOperationException(
                $"User {job.UserFilter} is not stored for {device.Name}. Sync that device first.");

        var cmd = $"DATA UPDATE USERINFO PIN={user.DeviceUserId}\tName={displayName}\tPri={user.Privilege}\t" +
                  $"Passwd={user.Password}\tCard={user.CardNumber}\tGrp=1\tTZ=0000000000000000\tVerify=-1\tViceCard=";
        var ids = await QueueCommandsAsync(serial, new[] { cmd }, ct);
        job.Total = 1;

        var (delivered, completed) = await PollDeliverAsync(job, ids, $"Applying name to {device.Name}", ct);
        job.Progress = delivered;

        // Mirror the new name onto our stored record for THIS device straight away. Without this the
        // Users page keeps showing the terminal's old name until that device next uploads its user list,
        // which looks like the unify "didn't work".
        if (delivered > 0)
        {
            await using var db2 = _dbf.CreateDbContext();
            await db2.EnrolledUsers
                .Where(u => u.SourceDeviceId == device.Id && u.DeviceUserId == job.UserFilter)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.Name, displayName), ct);
        }

        job.Message = $"Pushed name \"{displayName}\" for user {user.DeviceUserId} to {device.Name} " +
                      $"({(delivered > 0 ? "delivered" : "queued")}" + (completed > 0 ? ", confirmed" : "") + ").";
    }

    /// <summary>
    /// Remove one user from one terminal (DATA DELETE USERINFO). The terminal drops the user and all of
    /// their templates; we mirror that by deleting the local EnrolledUser row for THIS device only, so the
    /// stored counts stay truthful. Attendance history is deliberately kept.
    /// </summary>
    private async Task PushDeleteUserAsync(SyncJob job, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(job.UserFilter))
            throw new InvalidOperationException("Delete-user job has no user.");
        var (device, serial) = await ResolvePushTargetAsync(job.DeviceId, "this device", ct);

        var cmd = $"DATA DELETE USERINFO PIN={job.UserFilter}";
        var ids = await QueueCommandsAsync(serial, new[] { cmd }, ct);
        job.Total = 1;

        var (delivered, completed) = await PollDeliverAsync(job, ids, $"Removing user from {device.Name}", ct);
        job.Progress = delivered;

        int removed = 0;
        if (delivered > 0)
        {
            await using var db = _dbf.CreateDbContext();
            removed = await db.EnrolledUsers
                .Where(u => u.SourceDeviceId == device.Id && u.DeviceUserId == job.UserFilter)
                .ExecuteDeleteAsync(ct);   // cascades to that user's templates on this device
        }

        job.Message = $"Removed user {job.UserFilter} from {device.Name} " +
                      $"({(delivered > 0 ? "delivered" : "queued")}" + (completed > 0 ? ", confirmed" : "") + ")." +
                      (removed > 0 ? " Local record cleared; attendance history kept." : "");
    }

    /// <summary>Resolve a device to a PUSH-addressable (serial, connected) target or throw a clear reason.</summary>
    private async Task<(Device device, string serial)> ResolvePushTargetAsync(int deviceId, string who, CancellationToken ct)
    {
        await using var db = _dbf.CreateDbContext();
        var device = await db.Devices.FirstAsync(d => d.Id == deviceId, ct);
        if (string.IsNullOrWhiteSpace(device.SerialNumber))
            throw new InvalidOperationException(
                $"{device.Name} has no serial number yet, so PUSH can't address it. Click \"Read info\" on " +
                "the device once (or let it connect via ADMS) so its serial is known, then retry.");
        var connected = await db.PushDevices.AnyAsync(p => p.SerialNumber == device.SerialNumber, ct);
        if (!connected)
            throw new InvalidOperationException(
                $"{who} ({device.Name}) has not connected via PUSH/ADMS yet. On the terminal set " +
                "Comm → Cloud Server/ADMS to this app's address and port, then retry.");
        return (device, device.SerialNumber!);
    }

    /// <summary>Pull the PIN out of a "DATA UPDATE &lt;kind&gt; PIN=x\t..." command (for per-person counting).</summary>
    private static string PinOf(string command)
    {
        const string marker = "PIN=";
        var i = command.IndexOf(marker, StringComparison.Ordinal);
        if (i < 0) return "";
        i += marker.Length;
        var end = command.IndexOf('\t', i);
        return end < 0 ? command[i..] : command[i..end];
    }

    private async Task<List<int>> QueueCommandsAsync(string serial, IEnumerable<string> commands, CancellationToken ct)
    {
        await using var db = _dbf.CreateDbContext();
        var rows = commands.Select(c => new PushCommand { SerialNumber = serial, CommandText = c }).ToList();
        db.PushCommands.AddRange(rows);
        await db.SaveChangesAsync(ct);
        return rows.Select(c => c.Id).ToList();
    }

    /// <summary>Poll until a data query has been picked up and the incoming records settle (or time out).</summary>
    private async Task<Counts> PollReceiveAsync(SyncJob job, int deviceId, List<int> commandIds, string label, CancellationToken ct)
    {
        var start = DateTime.UtcNow;
        var deliveryDeadline = start + TimeSpan.FromMinutes(2);
        var hardDeadline = start + TimeSpan.FromMinutes(10);
        DateTime? deliveredAt = null;
        var lastChange = start;
        var last = await ReadCountsAsync(deviceId, ct);

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(TimeSpan.FromSeconds(3), ct);

            var (delivered, completed) = await CommandStatusAsync(commandIds, ct);
            if (delivered && deliveredAt is null) deliveredAt = DateTime.UtcNow;

            var now = await ReadCountsAsync(deviceId, ct);
            if (!now.Equals(last)) { lastChange = DateTime.UtcNow; last = now; }

            _notifier.Report(job.Id, new JobProgress(now.Users, 0,
                $"{label}: {now.Users} users, {now.Fingerprints} fp, {now.Faces} faces…"));

            if (deliveredAt is null)
            {
                if (DateTime.UtcNow > deliveryDeadline)
                    throw new InvalidOperationException(
                        "The terminal did not pick up the request within 2 minutes. Is it online and still " +
                        "configured to push to this app (Comm → Cloud Server/ADMS)?");
                continue;
            }

            var settled = DateTime.UtcNow > deliveredAt.Value + TimeSpan.FromSeconds(15);
            var quiet = DateTime.UtcNow > lastChange + TimeSpan.FromSeconds(20);
            if (completed || (settled && quiet)) return now;
            if (DateTime.UtcNow > hardDeadline) return now; // partial — the message reports what actually arrived
        }
    }

    /// <summary>Poll until copy commands have been delivered/applied to the target (or time out).</summary>
    private async Task<(int delivered, int completed)> PollDeliverAsync(SyncJob job, List<int> ids, string label, CancellationToken ct)
    {
        var start = DateTime.UtcNow;
        var hardDeadline = start + TimeSpan.FromMinutes(15);
        DateTime? allDeliveredAt = null;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(TimeSpan.FromSeconds(3), ct);

            var (dc, cc) = await CommandCountsAsync(ids, ct);
            _notifier.Report(job.Id, new JobProgress(dc, ids.Count, $"{label}: {dc}/{ids.Count} delivered…"));

            if (dc >= ids.Count && allDeliveredAt is null) allDeliveredAt = DateTime.UtcNow;

            if (cc >= ids.Count) return (dc, cc);
            if (allDeliveredAt is not null && DateTime.UtcNow > allDeliveredAt.Value + TimeSpan.FromSeconds(20))
                return (dc, cc);
            if (dc == 0 && DateTime.UtcNow > start + TimeSpan.FromMinutes(2))
                throw new InvalidOperationException(
                    "The target terminal did not pick up any commands within 2 minutes. Is it online and " +
                    "configured to push to this app (Comm → Cloud Server/ADMS)?");
            if (DateTime.UtcNow > hardDeadline) return (dc, cc);
        }
    }

    private async Task<(bool delivered, bool completed)> CommandStatusAsync(List<int> ids, CancellationToken ct)
    {
        await using var db = _dbf.CreateDbContext();
        var cmds = await db.PushCommands.Where(c => ids.Contains(c.Id))
            .Select(c => new { c.DeliveredUtc, c.CompletedUtc }).ToListAsync(ct);
        return (cmds.Count > 0 && cmds.All(c => c.DeliveredUtc != null),
                cmds.Count > 0 && cmds.All(c => c.CompletedUtc != null));
    }

    private async Task<(int delivered, int completed)> CommandCountsAsync(List<int> ids, CancellationToken ct)
    {
        await using var db = _dbf.CreateDbContext();
        var cmds = await db.PushCommands.Where(c => ids.Contains(c.Id))
            .Select(c => new { c.DeliveredUtc, c.CompletedUtc }).ToListAsync(ct);
        return (cmds.Count(c => c.DeliveredUtc != null), cmds.Count(c => c.CompletedUtc != null));
    }

    private readonly record struct Counts(int Users, int Fingerprints, int Faces, int UsersWithFaces, int Logs);

    private async Task<Counts> ReadCountsAsync(int deviceId, CancellationToken ct)
    {
        await using var db = _dbf.CreateDbContext();
        var users = await db.EnrolledUsers.CountAsync(u => u.SourceDeviceId == deviceId, ct);
        var fp = await db.EnrolledUsers.Where(u => u.SourceDeviceId == deviceId)
            .SelectMany(u => u.Templates).CountAsync(t => t.Type == TemplateType.Fingerprint, ct);
        // A face counts ONCE per person (a terminal stores ~12 rows per enrollment — internal parts).
        var fc = await db.EnrolledUsers.CountAsync(
            u => u.SourceDeviceId == deviceId && u.Templates.Any(t => t.Type == TemplateType.Face), ct);
        var uwf = await db.EnrolledUsers.CountAsync(
            u => u.SourceDeviceId == deviceId && u.Templates.Any(t => t.Type == TemplateType.Face), ct);
        var logs = await db.AttendanceLogs.CountAsync(l => l.DeviceId == deviceId, ct);
        return new Counts(users, fp, fc, uwf, logs);
    }

    /// <summary>
    /// Build the device-to-device copy commands (users + fingerprints + faces) from the source device's
    /// stored templates. NOTE: the exact DATA UPDATE field layout is firmware-sensitive and must be
    /// verified against a live terminal (raw log) — this is the highest-risk piece of the copy path.
    /// </summary>
    private async Task<List<string>> BuildCopyCommandsAsync(int sourceDeviceId, string targetSn,
        bool fingerprints, bool faces, bool cards, string? pinFilter, CancellationToken ct)
    {
        await using var db = _dbf.CreateDbContext();
        var q = db.EnrolledUsers.Where(u => u.SourceDeviceId == sourceDeviceId);
        if (!string.IsNullOrWhiteSpace(pinFilter)) q = q.Where(u => u.DeviceUserId == pinFilter);
        var users = await q.Include(u => u.Templates).AsNoTracking().OrderBy(u => u.DeviceUserId).ToListAsync(ct);

        // Prefer the unified default name so copies propagate the recognizable name too.
        var names = await db.UserProfiles.AsNoTracking()
            .Where(p => p.DisplayName != null && p.DisplayName != "")
            .ToDictionaryAsync(p => p.Pin, p => p.DisplayName!, ct);

        var cmds = new List<string>();
        foreach (var u in users)
        {
            var name = names.GetValueOrDefault(u.DeviceUserId) ?? u.Name;
            var card = cards ? u.CardNumber : null;
            cmds.Add($"DATA UPDATE USERINFO PIN={u.DeviceUserId}\tName={name}\tPri={u.Privilege}\t" +
                     $"Passwd={u.Password}\tCard={card}\tGrp=1\tTZ=0000000000000000\tVerify=-1\tViceCard=");

            if (fingerprints)
                foreach (var f in u.Templates.Where(t => t.Type == TemplateType.Fingerprint))
                    cmds.Add($"DATA UPDATE FINGERTMP PIN={u.DeviceUserId}\tFID={f.FingerIndex}\t" +
                             $"Size={f.Length}\tValid={(f.Flag == 0 ? 1 : f.Flag)}\tTMP={f.Data}");

            if (faces)
                foreach (var f in u.Templates.Where(t => t.Type == TemplateType.Face))
                    cmds.Add($"DATA UPDATE FACE PIN={u.DeviceUserId}\tFID={f.FingerIndex}\t" +
                             $"SIZE={f.Length}\tVALID={(f.Flag == 0 ? 1 : f.Flag)}\tTMP={f.Data}");
        }
        return cmds;
    }

    // ======================= SDK control ops =======================

    private async Task ControlAsync(AppDbContext db, SyncJob job, DeviceControlAction action, CancellationToken ct)
    {
        var device = await db.Devices.FirstAsync(d => d.Id == job.DeviceId, ct);
        await _sta.RunAsync(() => _zk.Control(Conn(device), action), ct);
        job.Total = 1;
        job.Progress = 1;

        if (action == DeviceControlAction.ClearAllUsers)
        {
            // The terminal is now empty — mirror that in the app so the counts stay accurate.
            // The FK cascade removes each user's fingerprint/face templates. Attendance history is kept.
            var removed = await db.EnrolledUsers.Where(u => u.SourceDeviceId == device.Id).ExecuteDeleteAsync(ct);
            job.Message = $"All users deleted from {device.Name}; also removed {removed} stored user(s) " +
                          "from this app so the counts match. Attendance history was kept.";
        }
        else
        {
            job.Message = $"{action} succeeded on {device.Name}.";
        }
    }

    private async Task SyncTimeAsync(AppDbContext db, SyncJob job, CancellationToken ct)
    {
        var device = await db.Devices.FirstAsync(d => d.Id == job.DeviceId, ct);
        var now = DateTime.Now;
        var readBack = await _sta.RunAsync(() => _zk.SyncTime(Conn(device), now), ct);
        device.LastSeenUtc = DateTime.UtcNow;
        job.Total = 1;
        job.Progress = 1;
        if (readBack is null)
            job.Message = $"Sent {now:g} to {device.Name} (device did not report its clock back to confirm).";
        else if (Math.Abs((readBack.Value - now).TotalMinutes) <= 2)
            job.Message = $"Clock on {device.Name} set — it now reads {readBack:g}.";
        else
            job.Message = $"Set attempt sent {now:g} but {device.Name} still reads {readBack:g} " +
                          $"(off by {(readBack.Value - now).TotalMinutes:F0} min) — the device did not accept the change.";
    }
}
