using System.ComponentModel.DataAnnotations;

namespace ZKDeviceManager.Data.Entities;

/// <summary>An organizational unit / site used to group employees (and filter reports).</summary>
public class Department
{
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(20)]
    public string? Code { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public ICollection<Employee> Employees { get; set; } = new List<Employee>();
}
