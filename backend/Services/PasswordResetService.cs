using System.Security.Cryptography;
using MEval.Api.Configuration;
using MEval.Api.Data;
using MEval.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MEval.Api.Services;

public interface IPasswordResetService
{
    Task<bool> RequestPasswordResetAsync(string email, string ipAddress);
    Task<(bool Success, string? ErrorReason, IEnumerable<string>? ValidationErrors)> ResetPasswordAsync(string token, string newPassword, string ipAddress);
    Task<(bool Success, string? ErrorReason)> ForceResetPasswordAsync(int userId, string adminIpAddress);
}

public class PasswordResetService : IPasswordResetService
{
    private readonly AppDbContext _context;
    private readonly ITokenService _tokenService;
    private readonly IPasswordPolicyService _passwordPolicyService;
    private readonly IEmailService _emailService;
    private readonly JwtSettings _jwtSettings;
    private readonly SecuritySettings _securitySettings;

    public PasswordResetService(
        AppDbContext context,
        ITokenService tokenService,
        IPasswordPolicyService passwordPolicyService,
        IEmailService emailService,
        IOptions<JwtSettings> jwtSettings,
        IOptions<SecuritySettings> securitySettings)
    {
        _context = context;
        _tokenService = tokenService;
        _passwordPolicyService = passwordPolicyService;
        _emailService = emailService;
        _jwtSettings = jwtSettings.Value;
        _securitySettings = securitySettings.Value;
    }

    public async Task<bool> RequestPasswordResetAsync(string email, string ipAddress)
    {
        var normalizedEmail = email?.Trim().ToLowerInvariant() ?? string.Empty;

        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail && u.IsActive && u.SoftDeletedAtUtc == null);

        // Always return true to prevent user enumeration
        if (user == null)
        {
            return true;
        }

        // Invalidate any existing unused reset tokens for this user
        var existingTokens = await _context.PasswordResetTokens
            .Where(t => t.UserId == user.Id && t.UsedAtUtc == null)
            .ToListAsync();

        foreach (var existing in existingTokens)
        {
            existing.UsedAtUtc = DateTime.UtcNow;
        }

        // Generate 64-byte cryptographic raw token
        var rawBytes = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(rawBytes);
        var rawToken = Convert.ToBase64String(rawBytes);
        var tokenHash = _tokenService.HashToken(rawToken);

        var resetToken = new PasswordResetToken
        {
            UserId = user.Id,
            TokenHash = tokenHash,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(_jwtSettings.PasswordResetTokenExpirationMinutes),
            CreatedByIp = ipAddress
        };

        _context.PasswordResetTokens.Add(resetToken);
        await _context.SaveChangesAsync();

        await _emailService.SendPasswordResetEmailAsync(user.Email, rawToken);

        return true;
    }

    public async Task<(bool Success, string? ErrorReason, IEnumerable<string>? ValidationErrors)> ResetPasswordAsync(string token, string newPassword, string ipAddress)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return (false, "InvalidOrExpiredToken", null);
        }

        var tokenHash = _tokenService.HashToken(token);

        var resetToken = await _context.PasswordResetTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash);

        if (resetToken == null || resetToken.IsUsed || resetToken.IsExpired)
        {
            return (false, "InvalidOrExpiredToken", null);
        }

        var user = resetToken.User;
        if (user == null || !user.IsActive || user.SoftDeletedAtUtc != null)
        {
            return (false, "UserNotFoundOrInactive", null);
        }

        var policyResult = _passwordPolicyService.ValidatePassword(newPassword);
        if (!policyResult.IsValid)
        {
            return (false, "PasswordPolicyViolation", policyResult.ErrorMessage != null ? new[] { policyResult.ErrorMessage } : null);
        }

        // Mark token as used
        resetToken.UsedAtUtc = DateTime.UtcNow;

        // Update user password and clear lockout/failures
        user.PasswordHash = _passwordPolicyService.HashPassword(newPassword);
        user.MustChangePassword = false;
        user.PasswordChangedAtUtc = DateTime.UtcNow;
        user.FailedLoginAttempts = 0;
        user.LockoutEndUtc = null;

        // Revoke active session
        await _tokenService.RevokeActiveSessionAsync(user.Id, RevokeReasons.PasswordReset, ipAddress);

        await _context.SaveChangesAsync();

        await _emailService.SendPasswordChangedNotificationAsync(user.Email);

        return (true, null, null);
    }

    public async Task<(bool Success, string? ErrorReason)> ForceResetPasswordAsync(int userId, string adminIpAddress)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == userId && u.SoftDeletedAtUtc == null);

        if (user == null)
        {
            return (false, "UserNotFound");
        }

        var defaultPassword = _securitySettings.DefaultUserPassword ?? "Mina@123";

        // Reset to default temporary password, flag MustChangePassword = true, restart 14-day clock
        user.PasswordHash = _passwordPolicyService.HashPassword(defaultPassword);
        user.MustChangePassword = true;
        user.PasswordChangedAtUtc = DateTime.UtcNow;
        user.FailedLoginAttempts = 0;
        user.LockoutEndUtc = null;

        // Revoke user's active session
        await _tokenService.RevokeActiveSessionAsync(user.Id, RevokeReasons.AdminForceReset, adminIpAddress);

        await _context.SaveChangesAsync();

        await _emailService.SendTemporaryPasswordEmailAsync(user.Email, defaultPassword);

        return (true, null);
    }
}
