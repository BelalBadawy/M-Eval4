using System.ComponentModel.DataAnnotations;

namespace MEval.Api.Models;

public class PasswordResetToken
{
    public int Id { get; set; }

    public int UserId { get; set; }

    [Required]
    [MaxLength(255)]
    public string TokenHash { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime ExpiresAtUtc { get; set; }

    public DateTime? UsedAtUtc { get; set; }

    [MaxLength(50)]
    public string CreatedByIp { get; set; } = string.Empty;

    public bool IsExpired => DateTime.UtcNow >= ExpiresAtUtc;
    public bool IsUsed => UsedAtUtc.HasValue;
    public bool IsValid => !IsUsed && !IsExpired;

    // Navigation property
    public User User { get; set; } = null!;
}
