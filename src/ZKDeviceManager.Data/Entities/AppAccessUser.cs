using System.ComponentModel.DataAnnotations;

namespace ZKDeviceManager.Data.Entities;

/// <summary>
/// An AD account explicitly granted permission to sign in to the app. When access enforcement is on
/// (config "Auth:RestrictToGranted" = true), only accounts listed here may log in.
/// </summary>
public class AppAccessUser
{
    public int Id { get; set; }

    /// <summary>The AD sAMAccountName (login), lower-cased for matching.</summary>
    [Required, MaxLength(100)]
    public string Login { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? DisplayName { get; set; }

    public DateTime GrantedUtc { get; set; } = DateTime.UtcNow;
}
