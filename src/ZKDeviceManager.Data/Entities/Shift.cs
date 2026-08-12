using System.ComponentModel.DataAnnotations;

namespace ZKDeviceManager.Data.Entities;

/// <summary>
/// A working-time template: start/end, a grace window before "late", and which weekdays are working days.
/// <see cref="WorkDays"/> is a 7-char mask, index 0 = Sunday … 6 = Saturday ('1' = working day).
/// Default "1111100" = Sun–Thu working, Fri/Sat off (KSA week).
/// </summary>
public class Shift
{
    public int Id { get; set; }

    [Required, MaxLength(60)]
    public string Name { get; set; } = string.Empty;

    public TimeOnly Start { get; set; } = new(8, 0);
    public TimeOnly End { get; set; } = new(17, 0);

    /// <summary>Minutes after <see cref="Start"/> before a punch counts as late.</summary>
    public int GraceMinutes { get; set; } = 10;

    [Required, MaxLength(7)]
    public string WorkDays { get; set; } = "1111100";

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public ICollection<Employee> Employees { get; set; } = new List<Employee>();

    public bool IsWorkingDay(DayOfWeek d)
    {
        int i = (int)d; // Sunday = 0 … Saturday = 6
        return i >= 0 && i < WorkDays.Length && WorkDays[i] == '1';
    }
}
