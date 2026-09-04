using System.ComponentModel.DataAnnotations;

namespace MEval.Api.Models;

public class User
{
    public int Id { get; set; }

    [Required]
    [MaxLength(150)]
    public string FullName { get; set; } = string.Empty;

    [Required]
    [MaxLength(255)]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [MaxLength(30)]
    public string? PhoneNumber { get; set; }

    [Required]
    [MaxLength(255)]
    public string PasswordHash { get; set; } = string.Empty;

    public bool MustChangePassword { get; set; } = true;

    public DateTime? PasswordChangedAtUtc { get; set; }

    public DateTime? LockoutEndUtc { get; set; }

    public int FailedLoginAttempts { get; set; } = 0;

    public bool IsActive { get; set; } = true;

    public UserSource Source { get; set; } = UserSource.Manual;

    public int? ImportBatchId { get; set; }

    public bool IsRolledBack { get; set; } = false;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAtUtc { get; set; }

    public DateTime? SoftDeletedAtUtc { get; set; }

    // Helper property for lockout state
    public bool IsLockedOut => LockoutEndUtc.HasValue && LockoutEndUtc.Value > DateTime.UtcNow;

    // Navigation properties
    public ImportBatch? ImportBatch { get; set; }
    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
    public ICollection<PasswordResetToken> PasswordResetTokens { get; set; } = new List<PasswordResetToken>();
}
