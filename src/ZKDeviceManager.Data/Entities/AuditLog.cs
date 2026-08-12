using System.ComponentModel.DataAnnotations;

namespace ZKDeviceManager.Data.Entities;

/// <summary>A record of a security-relevant action (sign-in/out and key operations) for accountability.</summary>
public class AuditLog
{
    public int Id { get; set; }

    public DateTime WhenUtc { get; set; } = DateTime.UtcNow;

    [MaxLength(100)]
    public string User { get; set; } = string.Empty;

    [MaxLength(60)]
    public string Action { get; set; } = string.Empty;

    [MaxLength(400)]
    public string? Detail { get; set; }

    [MaxLength(45)]
    public string? Ip { get; set; }
}
