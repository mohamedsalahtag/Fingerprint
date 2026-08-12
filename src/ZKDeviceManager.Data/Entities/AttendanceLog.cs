using System.ComponentModel.DataAnnotations;

namespace ZKDeviceManager.Data.Entities;

/// <summary>A single attendance / transaction punch downloaded from a device.</summary>
public class AttendanceLog
{
    public int Id { get; set; }

    public int DeviceId { get; set; }
    public Device? Device { get; set; }

    /// <summary>Device name captured at scan time (kept even if the device is later renamed/removed).</summary>
    [MaxLength(100)]
    public string? DeviceName { get; set; }

    /// <summary>Device IP captured at scan time.</summary>
    [MaxLength(45)]
    public string? DeviceIp { get; set; }

    [Required, MaxLength(24)]
    public string DeviceUserId { get; set; } = string.Empty;

    public DateTime Timestamp { get; set; }

    /// <summary>Verification mode (0=password,1=fingerprint,15=face, etc.).</summary>
    public int VerifyMode { get; set; }

    /// <summary>In/out/break state as configured on the device.</summary>
    public int InOutMode { get; set; }

    public int WorkCode { get; set; }

    public DateTime DownloadedUtc { get; set; } = DateTime.UtcNow;
}
