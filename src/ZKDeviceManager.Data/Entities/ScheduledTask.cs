using System.ComponentModel.DataAnnotations;

namespace ZKDeviceManager.Data.Entities;

public enum ScheduledTaskType
{
    /// <summary>Pull users + fingerprints + faces from every connected terminal.</summary>
    NightlySync = 0,
    /// <summary>Pull attendance logs from every connected terminal.</summary>
    NightlyLogPull = 1,
    /// <summary>Write a JSON snapshot of all users + templates to the server's backups folder.</summary>
    NightlyBackup = 2
}

/// <summary>A recurring automation that runs once per day at <see cref="TimeOfDay"/> (server local time).</summary>
public class ScheduledTask
{
    public int Id { get; set; }

    public ScheduledTaskType Type { get; set; }

    public bool Enabled { get; set; }

    public TimeOnly TimeOfDay { get; set; } = new(2, 0);

    public DateTime? LastRunUtc { get; set; }

    [MaxLength(500)]
    public string? LastResult { get; set; }
}
