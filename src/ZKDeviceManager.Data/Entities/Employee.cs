using System.ComponentModel.DataAnnotations;

namespace ZKDeviceManager.Data.Entities;

/// <summary>
/// An HR record for a person, keyed by their enroll number / PIN (<see cref="EnrolledUser.DeviceUserId"/>).
/// This is the org-side identity: a recognizable name, department, shift and employment status — kept in
/// THIS database only (not linked to any external HR system).
/// </summary>
public class Employee
{
    public int Id { get; set; }

    /// <summary>The enroll number / PIN shared with the terminals (unique).</summary>
    [Required, MaxLength(24)]
    public string Pin { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? FullName { get; set; }

    [MaxLength(100)]
    public string? JobTitle { get; set; }

    [MaxLength(30)]
    public string? CardNumber { get; set; }

    public int? DepartmentId { get; set; }
    public Department? Department { get; set; }

    public int? ShiftId { get; set; }
    public Shift? Shift { get; set; }

    public DateOnly? HireDate { get; set; }

    /// <summary>Whether the person is currently employed. Inactive staff are excluded from device pushes.</summary>
    public bool Active { get; set; } = true;

    /// <summary>When true, this person is loaded onto every site's devices (roaming staff / executives).</summary>
    public bool AllSites { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
