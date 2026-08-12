using Microsoft.EntityFrameworkCore;
using ZKDeviceManager.Data;
using ZKDeviceManager.Data.Entities;

namespace ZKDeviceManager.Devices.Jobs;

/// <summary>Creates persisted <see cref="SyncJob"/> rows and signals the background runner.</summary>
public class JobService
{
    private readonly AppDbContext _db;
    private readonly IJobQueue _queue;
    private readonly JobCancellation _cancellation;
    private readonly IJobNotifier _notifier;

    public JobService(AppDbContext db, IJobQueue queue, JobCancellation cancellation, IJobNotifier notifier)
    {
        _db = db;
        _queue = queue;
        _cancellation = cancellation;
        _notifier = notifier;
    }

    /// <summary>Cancel a queued or running job.</summary>
    public async Task<bool> CancelAsync(int jobId)
    {
        var job = await _db.SyncJobs.FirstOrDefaultAsync(j => j.Id == jobId);
        if (job == null) return false;

        if (job.Status == SyncJobStatus.Running)
        {
            // Signal the running job's token; the runner will mark it Cancelled when it unwinds.
            if (_cancellation.Cancel(jobId)) return true;
            // Not actually running in-process (e.g. after a restart) — fall through to force-cancel.
        }
        if (job.Status is SyncJobStatus.Queued or SyncJobStatus.Running)
        {
            job.Status = SyncJobStatus.Cancelled;
            job.Message = "Cancelled by user.";
            job.FinishedUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            _notifier.NotifyChanged(jobId);
            return true;
        }
        return false;
    }

    /// <summary>PUSH: pull users + fingerprints + faces from a terminal into the database.</summary>
    public Task<int> QueueSyncDownloadAsync(int deviceId)
        => CreateAsync(new SyncJob { Type = SyncJobType.SyncDownload, DeviceId = deviceId });

    /// <summary>PUSH: pull attendance logs from a terminal on demand.</summary>
    public Task<int> QueueSyncDownloadLogsAsync(int deviceId)
        => CreateAsync(new SyncJob { Type = SyncJobType.SyncDownloadLogs, DeviceId = deviceId });

    /// <summary>PUSH: copy the stored users + fingerprints + faces of the source device onto the target terminal.</summary>
    public Task<int> QueueSyncCopyAsync(int sourceDeviceId, int targetDeviceId,
        bool fingerprints = true, bool faces = true, bool cards = true)
        => CreateAsync(new SyncJob
        {
            Type = SyncJobType.SyncCopy,
            DeviceId = sourceDeviceId,
            TargetDeviceId = targetDeviceId,
            IncludeFingerprints = fingerprints,
            IncludeFaces = faces,
            IncludeCards = cards
        });

    /// <summary>PUSH: copy a single stored user (by PIN) from the source device onto the target terminal.</summary>
    public Task<int> QueueSyncCopyUserAsync(int sourceDeviceId, int targetDeviceId, string pin,
        bool fingerprints = true, bool faces = true, bool cards = true)
        => CreateAsync(new SyncJob
        {
            Type = SyncJobType.SyncCopy,
            DeviceId = sourceDeviceId,
            TargetDeviceId = targetDeviceId,
            UserFilter = pin,
            IncludeFingerprints = fingerprints,
            IncludeFaces = faces,
            IncludeCards = cards
        });

    /// <summary>PUSH: push a user's unified default name to one terminal (name only, no templates).</summary>
    public Task<int> QueueApplyNameAsync(int deviceId, string pin)
        => CreateAsync(new SyncJob { Type = SyncJobType.ApplyName, DeviceId = deviceId, UserFilter = pin });

    /// <summary>PUSH: create/update a user record on a terminal (uses the unified default name).</summary>
    public Task<int> QueueCreateUserAsync(int deviceId, string pin)
        => CreateAsync(new SyncJob { Type = SyncJobType.CreateUser, DeviceId = deviceId, UserFilter = pin });

    /// <summary>PUSH: remotely start fingerprint enrollment for a user on a terminal.</summary>
    public Task<int> QueueEnrollFingerAsync(int deviceId, string pin)
        => CreateAsync(new SyncJob { Type = SyncJobType.EnrollFinger, DeviceId = deviceId, UserFilter = pin });

    /// <summary>PUSH: remotely start face enrollment for a user on a terminal.</summary>
    public Task<int> QueueEnrollFaceAsync(int deviceId, string pin)
        => CreateAsync(new SyncJob { Type = SyncJobType.EnrollFace, DeviceId = deviceId, UserFilter = pin });

    /// <summary>SDK control action (restart/power off/clear users/sync time/enable/disable UI), tracked as a job.</summary>
    public Task<int> QueueControlAsync(int deviceId, SyncJobType controlType)
    {
        if (controlType is not (SyncJobType.ControlRestart or SyncJobType.ControlPowerOff
            or SyncJobType.ControlClearUsers or SyncJobType.ControlSyncTime
            or SyncJobType.ControlEnableUi or SyncJobType.ControlDisableUi))
            throw new ArgumentOutOfRangeException(nameof(controlType), controlType, "Not a control job type.");
        return CreateAsync(new SyncJob { Type = controlType, DeviceId = deviceId });
    }

    private async Task<int> CreateAsync(SyncJob job)
    {
        _db.SyncJobs.Add(job);
        await _db.SaveChangesAsync();
        await _queue.EnqueueAsync(job.Id);
        return job.Id;
    }
}
