using System.ComponentModel.DataAnnotations;

namespace ZKDeviceManager.Data.Entities;

/// <summary>
/// A registered ZKTeco fingerprint / face terminal reachable over TCP-IP (port 4370).
/// </summary>
public class Device
{
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(45)]
    public string IpAddress { get; set; } = string.Empty;

    public int Port { get; set; } = 4370;

    /// <summary>Device communication key (0 when the terminal has no comm password).</summary>
    public int CommKey { get; set; }

    [MaxLength(60)]
    public string? SerialNumber { get; set; }

    [MaxLength(60)]
    public string? FirmwareVersion { get; set; }

    [MaxLength(60)]
    public string? Platform { get; set; }

    [MaxLength(100)]
    public string? Model { get; set; }

    [MaxLength(100)]
    public string? Location { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime? LastSeenUtc { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    // Last-read device capacity/counters (populated by "Read info"), for the health view.
    public DateTime? LastInfoUtc { get; set; }
    public int? DevUserCount { get; set; }
    public int? DevUserCapacity { get; set; }
    public int? DevFpCount { get; set; }
    public int? DevFpCapacity { get; set; }
    public int? DevFaceCount { get; set; }
    public int? DevFaceCapacity { get; set; }
    public int? DevLogCount { get; set; }

    public ICollection<EnrolledUser> Users { get; set; } = new List<EnrolledUser>();
}
