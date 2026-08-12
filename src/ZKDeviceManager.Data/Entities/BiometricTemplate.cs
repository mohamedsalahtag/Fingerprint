namespace ZKDeviceManager.Data.Entities;

/// <summary>Kind of biometric credential stored for a user.</summary>
public enum TemplateType
{
    Fingerprint = 0,
    Face = 1,
    Card = 2
}

/// <summary>
/// A single biometric template (fingerprint / face) or card credential belonging to an
/// <see cref="EnrolledUser"/>, stored in the SDK string form so it can be pushed back to a device.
/// </summary>
public class BiometricTemplate
{
    public int Id { get; set; }

    public int EnrolledUserId { get; set; }
    public EnrolledUser? EnrolledUser { get; set; }

    public TemplateType Type { get; set; }

    /// <summary>Finger index 0-9 for fingerprints; 50 for the face template set; 0 for cards.</summary>
    public int FingerIndex { get; set; }

    /// <summary>Template validity flag from the SDK: 0 = invalid, 1 = valid, 3 = duress.</summary>
    public int Flag { get; set; } = 1;

    /// <summary>Template payload as returned by the SDK (base64 or hex per the device BASE64 attribute).</summary>
    public string Data { get; set; } = string.Empty;

    public int Length { get; set; }

    public DateTime DownloadedUtc { get; set; } = DateTime.UtcNow;
}
