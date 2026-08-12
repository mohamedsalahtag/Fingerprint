namespace ZKDeviceManager.Data.Entities;

/// <summary>
/// Audit trail of biometric-template changes: whenever an ingested template actually differs from the
/// stored one, the PREVIOUS value is archived here before it's overwritten. Only real changes are recorded
/// (routine re-syncs of identical data don't create rows), so it doubles as a re-enrollment history.
/// </summary>
public class TemplateChange
{
    public int Id { get; set; }

    public int DeviceId { get; set; }

    public string Pin { get; set; } = string.Empty;

    public TemplateType Type { get; set; }

    public int FingerIndex { get; set; }

    /// <summary>The template payload that was replaced (kept so a prior enrollment could be restored).</summary>
    public string? OldData { get; set; }

    public int OldLength { get; set; }

    public DateTime ChangedUtc { get; set; } = DateTime.UtcNow;
}
