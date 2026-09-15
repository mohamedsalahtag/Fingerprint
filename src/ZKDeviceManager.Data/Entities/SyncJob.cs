using System.ComponentModel.DataAnnotations;

namespace ZKDeviceManager.Data.Entities;

public enum SyncJobType
{
    // --- Legacy SDK-based types (0–6). No longer queued; kept so historical rows still label
    //     correctly. The runner no longer has handlers for these. ---
    DownloadInfo = 0,
    DownloadUsersAndTemplates = 1,
    UploadToDevice = 2,
    CopyDeviceToDevice = 3,
    DeviceControl = 4,
    DownloadLogs = 5,
    DownloadFaces = 6,

    // --- Current types (7+). Data ops go over PUSH/ADMS (only face-capable path on modern
    //     Windows); control ops go over the COM SDK on the STA thread. All are tracked jobs. ---
    /// <summary>PUSH: pull users + fingerprints + faces from a terminal (DATA QUERY).</summary>
    SyncDownload = 7,
    /// <summary>PUSH: copy this app's stored users + fingerprints + faces onto a terminal (DATA UPDATE).</summary>
    SyncCopy = 8,
    /// <summary>PUSH: pull attendance logs on demand (DATA QUERY ATTLOG).</summary>
    SyncDownloadLogs = 9,

    ControlRestart = 10,
    ControlPowerOff = 11,
    ControlClearUsers = 12,
    ControlSyncTime = 13,
    ControlEnableUi = 14,
    ControlDisableUi = 15,

    /// <summary>PUSH: push a user's default (display) name to one terminal (DATA UPDATE USERINFO, name only).</summary>
    ApplyName = 16,

    /// <summary>PUSH: create/update a user record on a terminal (no templates) so it can be enrolled there.</summary>
    CreateUser = 17,
    /// <summary>PUSH: send a remote fingerprint-enrollment command; the person enrolls at the terminal.</summary>
    EnrollFinger = 18,
    /// <summary>PUSH: send a remote face-enrollment command; the person enrolls at the terminal.</summary>
    EnrollFace = 19,

    /// <summary>PUSH: change the terminal's Cloud Server / ADMS address (SET OPTIONS), optionally rebooting.</summary>
    SetAdmsServer = 20,

    /// <summary>PUSH: remove ONE user (by PIN) from a single terminal (DATA DELETE USERINFO).</summary>
    DeleteUser = 21
}

public enum SyncJobStatus
{
    Queued = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3,
    Cancelled = 4
}

/// <summary>
/// A persisted, queued unit of device work. Progress is updated live while the
/// background runner executes it, and the row remains as an audit record afterwards.
/// </summary>
public class SyncJob
{
    public int Id { get; set; }

    public SyncJobType Type { get; set; }

    public int DeviceId { get; set; }
    public Device? Device { get; set; }

    /// <summary>Target device for copy/transfer jobs; null otherwise.</summary>
    public int? TargetDeviceId { get; set; }
    public Device? TargetDevice { get; set; }

    public SyncJobStatus Status { get; set; } = SyncJobStatus.Queued;

    public int Progress { get; set; }
    public int Total { get; set; }

    // For copy/upload jobs: which credential kinds to transfer.
    public bool IncludeFingerprints { get; set; } = true;
    public bool IncludeFaces { get; set; } = true;
    public bool IncludeCards { get; set; } = true;

    /// <summary>When set, the copy / apply-name job affects only this single user (PIN); null = all users.</summary>
    [MaxLength(24)]
    public string? UserFilter { get; set; }

    /// <summary>Free-form parameters for jobs that need extras (e.g. SetAdmsServer stores "server|port|reboot").</summary>
    [MaxLength(200)]
    public string? Payload { get; set; }

    [MaxLength(2000)]
    public string? Message { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? StartedUtc { get; set; }
    public DateTime? FinishedUtc { get; set; }
}
