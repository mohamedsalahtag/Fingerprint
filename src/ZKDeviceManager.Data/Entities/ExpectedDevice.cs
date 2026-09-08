namespace ZKDeviceManager.Data.Entities;

/// <summary>
/// A terminal that is EXPECTED to exist (from the master site sheet), used to track migration/
/// commissioning progress: which terminals have been repointed to the new server and are pushing,
/// versus which are still pending. "Connected" is derived live by matching <see cref="Ip"/> against
/// the real device records (Devices / PushDevices) — this table is only the target roster.
/// </summary>
public class ExpectedDevice
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Ip { get; set; }
    public string Site { get; set; } = "";
    /// <summary>IP was transcribed from the sheet image and is uncertain / duplicated — flag to double-check.</summary>
    public bool VerifyIp { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
