using System.ComponentModel.DataAnnotations;

namespace ZKDeviceManager.Data.Entities;

/// <summary>A non-working calendar day; days on this list are not counted as absences in reports.</summary>
public class Holiday
{
    public int Id { get; set; }

    public DateOnly Date { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;
}
