using System.ComponentModel.DataAnnotations;

namespace ZKDeviceManager.Data.Entities;

/// <summary>
/// A person's unified identity across terminals, keyed by the enroll number / PIN
/// (<see cref="EnrolledUser.DeviceUserId"/>). The same PIN on every machine is treated as the same
/// person. <see cref="DisplayName"/> is the chosen "default" name shown everywhere in the app and
/// pushed to terminals (via the Apply-name job) so they gradually converge on one recognizable name.
/// </summary>
public class UserProfile
{
    public int Id { get; set; }

    /// <summary>The enroll number / PIN this profile applies to (unique).</summary>
    [Required, MaxLength(24)]
    public string Pin { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? DisplayName { get; set; }

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
