using System.ComponentModel.DataAnnotations;

namespace ZKDeviceManager.Data.Entities;

/// <summary>A terminal that has connected to us via the ZK PUSH/ADMS protocol (device-initiated HTTP).</summary>
public class PushDevice
{
    public int Id { get; set; }

    [Required, MaxLength(60)]
    public string SerialNumber { get; set; } = string.Empty;

    [MaxLength(45)]
    public string? LastIp { get; set; }

    [MaxLength(200)]
    public string? Info { get; set; }

    public DateTime FirstSeenUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;

    public int UsersReceived { get; set; }
    public int FacesReceived { get; set; }
    public int FingersReceived { get; set; }
    public int LogsReceived { get; set; }
}

/// <summary>A command queued for a PUSH device to execute on its next poll (getrequest).</summary>
public class PushCommand
{
    public int Id { get; set; }

    [Required, MaxLength(60)]
    public string SerialNumber { get; set; } = string.Empty;

    /// <summary>The command body (without the "C:id:" prefix, which is added when delivered).
    /// Uses nvarchar(max) because DATA UPDATE FACE/FINGERTMP commands embed a base64 template.</summary>
    [Required]
    public string CommandText { get; set; } = string.Empty;

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? DeliveredUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }

    [MaxLength(200)]
    public string? Result { get; set; }
}
