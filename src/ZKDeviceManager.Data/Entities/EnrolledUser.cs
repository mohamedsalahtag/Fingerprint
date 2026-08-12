using System.ComponentModel.DataAnnotations;

namespace ZKDeviceManager.Data.Entities;

/// <summary>
/// A user snapshot downloaded from a device. Keyed by the device enroll number
/// (<see cref="DeviceUserId"/>, up to 24 chars) plus the source device.
/// </summary>
public class EnrolledUser
{
    public int Id { get; set; }

    /// <summary>The device enroll number / PIN (string form, up to 24 chars).</summary>
    [Required, MaxLength(24)]
    public string DeviceUserId { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? Name { get; set; }

    [MaxLength(50)]
    public string? Password { get; set; }

    /// <summary>0 = common user, 1/2 = enroller/manager, 3 = super administrator.</summary>
    public int Privilege { get; set; }

    public bool Enabled { get; set; } = true;

    [MaxLength(30)]
    public string? CardNumber { get; set; }

    public int SourceDeviceId { get; set; }
    public Device? SourceDevice { get; set; }

    public DateTime LastDownloadedUtc { get; set; } = DateTime.UtcNow;

    public ICollection<BiometricTemplate> Templates { get; set; } = new List<BiometricTemplate>();
}
