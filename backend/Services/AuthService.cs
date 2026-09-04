using BCrypt.Net;
using MEval.Api.Configuration;
using MEval.Api.Data;
using MEval.Api.DTOs;
using MEval.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MEval.Api.Services;

public interface IAuthService
{
    Task<(bool Success, LoginResponse? Response, string? ErrorReason, int? LockoutMinutes)> LoginAsync(string email, string password, string ipAddress);
    Task<(bool Success, LoginResponse? Response, string? ErrorReason, IEnumerable<string>? ValidationErrors)> ChangePasswordAsync(int userId, string currentPassword, string newPassword, string ipAddress);
    Task<bool> LogoutAsync(int userId, string ipAddress);
    Task<SessionResponse?> GetSessionAsync(int userId, string ipAddress);
}

public class AuthService : IAuthService
{
    private readonly AppDbContext _context;
    private readonly ITokenService _tokenService;
    private readonly IPasswordPolicyService _passwordPolicyService;
    private readonly SecuritySettings _securitySettings;

    // Dummy hash for constant-time comparison when user does not exist (prevents user enumeration via timing)
    private const string DummyHash = "$2a$11$uB0oPz7v0P2H5Q8qVbL.0OmXmG4YfFqj3LdM8Vn7L.3X4Qz2B6rSe";

    public AuthService(
        AppDbContext context,
        ITokenService tokenService,
        IPasswordPolicyService passwordPolicyService,
        IOptions<SecuritySettings> securitySettings)
    {
        _context = context;
        _tokenService = tokenService;
        _passwordPolicyService = passwordPolicyService;
        _securitySettings = securitySettings.Value;
    }

    public async Task<(bool Success, LoginResponse? Response, string? ErrorReason, int? LockoutMinutes)> LoginAsync(string email, string password, string ipAddress)
    {
        var normalizedEmail = email?.Trim().ToLowerInvariant() ?? string.Empty;

        var user = await _context.Users
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
                    .ThenInclude(r => r.RolePermissions)
                        .ThenInclude(rp => rp.Permission)
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail);

        if (user == null)
        {
            // Uniform timing execution
            BCrypt.Net.BCrypt.Verify(password, DummyHash);
            return (false, null, "InvalidCredentials", null);
        }

        if (!user.IsActive || user.SoftDeletedAtUtc != null)
        {
            BCrypt.Net.BCrypt.Verify(password, DummyHash);
            return (false, null, "AccountDeactivated", null);
        }

        var now = DateTime.UtcNow;

        // Check Lockout
        if (user.LockoutEndUtc.HasValue)
        {
            if (user.LockoutEndUtc.Value > now)
            {
                var remainingMinutes = (int)Math.Ceiling((user.LockoutEndUtc.Value - now).TotalMinutes);
                return (false, null, "AccountLocked", Math.Max(1, remainingMinutes));
            }

            // Lockout expired, reset counters
            user.LockoutEndUtc = null;
            user.FailedLoginAttempts = 0;
        }

        // Verify password
        var passwordValid = BCrypt.Net.BCrypt.Verify(password, user.PasswordHash);
        if (!passwordValid)
        {
            user.FailedLoginAttempts++;
            if (user.FailedLoginAttempts >= _securitySettings.LockoutThreshold)
            {
                user.LockoutEndUtc = now.AddMinutes(_securitySettings.LockoutDurationMinutes);
                await _context.SaveChangesAsync();
                return (false, null, "AccountLocked", _securitySettings.LockoutDurationMinutes);
            }

            await _context.SaveChangesAsync();
            return (false, null, "InvalidCredentials", null);
        }

        // Password is correct, reset lockout and failed attempts
        user.FailedLoginAttempts = 0;
        user.LockoutEndUtc = null;
        await _context.SaveChangesAsync();

        // Single active session: issue new refresh token (revokes any existing token with SupersededByNewLogin)
        var (_, rawRefreshToken) = await _tokenService.IssueRefreshTokenAsync(user.Id, ipAddress);

        var roles = user.UserRoles.Select(ur => ur.Role.Name).Distinct().ToList();
        var permissions = user.UserRoles
            .SelectMany(ur => ur.Role.RolePermissions)
            .Select(rp => rp.Permission.Code)
            .Distinct()
            .ToList();

        var accessToken = _tokenService.CreateAccessToken(user, roles, permissions);

        var summary = new UserSummaryDto(
            user.Id,
            user.FullName,
            user.Email,
            user.MustChangePassword,
            roles,
            permissions
        );

        return (true, new LoginResponse(accessToken, rawRefreshToken, summary), null, null);
    }

    public async Task<(bool Success, LoginResponse? Response, string? ErrorReason, IEnumerable<string>? ValidationErrors)> ChangePasswordAsync(int userId, string currentPassword, string newPassword, string ipAddress)
    {
        var user = await _context.Users
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
                    .ThenInclude(r => r.RolePermissions)
                        .ThenInclude(rp => rp.Permission)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null || !user.IsActive || user.SoftDeletedAtUtc != null)
        {
            return (false, null, "UserNotFound", null);
        }

        var isCurrentValid = BCrypt.Net.BCrypt.Verify(currentPassword, user.PasswordHash);
        if (!isCurrentValid)
        {
            return (false, null, "InvalidCurrentPassword", null);
        }

        var policyResult = _passwordPolicyService.ValidatePassword(newPassword);
        if (!policyResult.IsValid)
        {
            return (false, null, "PasswordPolicyViolation", policyResult.ErrorMessage != null ? new[] { policyResult.ErrorMessage } : null);
        }

        user.PasswordHash = _passwordPolicyService.HashPassword(newPassword);
        user.MustChangePassword = false;
        user.PasswordChangedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        // Revoke current session with PasswordChanged
        await _tokenService.RevokeActiveSessionAsync(user.Id, RevokeReasons.PasswordChanged, ipAddress);

        // Issue fresh session with must_change_password = false
        var (_, rawRefreshToken) = await _tokenService.IssueRefreshTokenAsync(user.Id, ipAddress);

        var roles = user.UserRoles.Select(ur => ur.Role.Name).Distinct().ToList();
        var permissions = user.UserRoles
            .SelectMany(ur => ur.Role.RolePermissions)
            .Select(rp => rp.Permission.Code)
            .Distinct()
            .ToList();

        var accessToken = _tokenService.CreateAccessToken(user, roles, permissions);

        var summary = new UserSummaryDto(
            user.Id,
            user.FullName,
            user.Email,
            user.MustChangePassword,
            roles,
            permissions
        );

        return (true, new LoginResponse(accessToken, rawRefreshToken, summary), null, null);
    }

    public async Task<bool> LogoutAsync(int userId, string ipAddress)
    {
        return await _tokenService.RevokeActiveSessionAsync(userId, RevokeReasons.UserLogout, ipAddress);
    }

    public async Task<SessionResponse?> GetSessionAsync(int userId, string ipAddress)
    {
        var user = await _context.Users
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
                    .ThenInclude(r => r.RolePermissions)
                        .ThenInclude(rp => rp.Permission)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null || !user.IsActive || user.SoftDeletedAtUtc != null)
        {
            return null;
        }

        var roles = user.UserRoles.Select(ur => ur.Role.Name).Distinct().ToList();
        var permissions = user.UserRoles
            .SelectMany(ur => ur.Role.RolePermissions)
            .Select(rp => rp.Permission.Code)
            .Distinct()
            .ToList();

        return new SessionResponse(
            user.Id,
            user.FullName,
            user.Email,
            user.MustChangePassword,
            roles,
            permissions,
            ipAddress
        );
    }
}
