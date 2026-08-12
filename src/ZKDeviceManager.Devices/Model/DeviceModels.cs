using ZKDeviceManager.Data.Entities;

namespace ZKDeviceManager.Devices.Model;

/// <summary>Connection parameters for a ZK terminal.</summary>
public record DeviceConnection(string Ip, int Port = 4370, int CommKey = 0);

/// <summary>Live status / capacity snapshot read from a device.</summary>
public class DeviceInfo
{
    public string? SerialNumber { get; set; }
    public string? FirmwareVersion { get; set; }
    public string? Platform { get; set; }
    public string? ProductCode { get; set; }
    public string? SdkVersion { get; set; }
    public string? Mac { get; set; }

    public int AdminCount { get; set; }
    public int UserCount { get; set; }
    public int FingerprintCount { get; set; }
    public int FaceCount { get; set; }
    public int RecordCount { get; set; }

    public int UserCapacity { get; set; }
    public int FingerprintCapacity { get; set; }
    public int FaceCapacity { get; set; }
}

/// <summary>A user plus its biometric credentials as pulled from / pushed to a device.</summary>
public class UserRecord
{
    public string UserId { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? Password { get; set; }
    public int Privilege { get; set; }
    public bool Enabled { get; set; } = true;
    public string? CardNumber { get; set; }
    public List<TemplateRecord> Templates { get; set; } = new();
}

public class TemplateRecord
{
    public TemplateType Type { get; set; }
    public int FingerIndex { get; set; }
    public int Flag { get; set; } = 1;
    public string Data { get; set; } = string.Empty;
    public int Length { get; set; }
}

/// <summary>A full set of users (with templates) downloaded from a single device.</summary>
public class DeviceSnapshot
{
    public List<UserRecord> Users { get; set; } = new();
    public int TemplateCount => Users.Sum(u => u.Templates.Count);
}

/// <summary>Progress report emitted during long device operations.</summary>
public record JobProgress(int Current, int Total, string? Message = null);

public class UploadResult
{
    public int UsersWritten { get; set; }
    public int TemplatesWritten { get; set; }
    public int Failures { get; set; }
}

public enum DeviceControlAction
{
    EnableUi,
    DisableUi,
    Restart,
    PowerOff,
    ClearAllUsers
}

/// <summary>An attendance / transaction record read from a device.</summary>
public class AttendanceRecord
{
    public string UserId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public int VerifyMode { get; set; }
    public int InOutMode { get; set; }
    public int WorkCode { get; set; }
}

/// <summary>Which credential kinds to include when uploading/copying to a device.</summary>
public record TemplateSelection(bool Fingerprints = true, bool Faces = true, bool Cards = true)
{
    public static readonly TemplateSelection All = new(true, true, true);
}
