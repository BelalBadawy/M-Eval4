using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using MEval.Api.Configuration;
using MEval.Api.Data;
using MEval.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace MEval.Api.Services;

public interface ITokenService
{
    string CreateAccessToken(User user, IEnumerable<string> roles, IEnumerable<string> permissions);
    Task<(RefreshToken RefreshToken, string RawToken)> IssueRefreshTokenAsync(Guid userId, string ipAddress);
    Task<(bool Success, string? AccessToken, string? RawRefreshToken, string? ErrorReason)> RotateRefreshTokenAsync(string rawRefreshToken, string ipAddress);
    Task<bool> RevokeActiveSessionAsync(Guid userId, string reason, string? ipAddress = null);
    string HashToken(string token);
}

public class TokenService : ITokenService
{
    private readonly AppDbContext _context;
    private readonly JwtSettings _jwtSettings;

    public TokenService(AppDbContext context, IOptions<JwtSettings> jwtSettings)
    {
        _context = context;
        _jwtSettings = jwtSettings.Value;
    }

    public string CreateAccessToken(User user, IEnumerable<string> roles, IEnumerable<string> permissions)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.SecretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Name, user.FullName),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("must_change_password", user.MustChangePassword.ToString().ToLowerInvariant())
        };

        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        foreach (var perm in permissions)
        {
            claims.Add(new Claim("permission", perm));
        }

        var token = new JwtSecurityToken(
            issuer: _jwtSettings.Issuer,
            audience: _jwtSettings.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddMinutes(_jwtSettings.AccessTokenExpirationMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string HashToken(string token)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(token);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }

    public async Task<(RefreshToken RefreshToken, string RawToken)> IssueRefreshTokenAsync(Guid userId, string ipAddress)
    {
        // 1. Single Active Session Per User: Revoke any existing active token
        var existingActiveTokens = await _context.RefreshTokens
            .Where(r => r.UserId == userId && r.RevokedAtUtc == null)
            .ToListAsync();

        foreach (var token in existingActiveTokens)
        {
            token.RevokedAtUtc = DateTime.UtcNow;
            token.RevokedReason = RevokeReasons.SupersededByNewLogin;
            token.RevokedByIp = ipAddress;
        }

        // 2. Generate new cryptographic token
        var rawTokenBytes = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(rawTokenBytes);
        var rawToken = Convert.ToBase64String(rawTokenBytes);
        var tokenHash = HashToken(rawToken);

        var refreshToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = tokenHash,
            CreatedByIp = ipAddress,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(_jwtSettings.RefreshTokenExpirationDays)
        };

        _context.RefreshTokens.Add(refreshToken);
        await _context.SaveChangesAsync();

        return (refreshToken, rawToken);
    }

    public async Task<(bool Success, string? AccessToken, string? RawRefreshToken, string? ErrorReason)> RotateRefreshTokenAsync(string rawRefreshToken, string ipAddress)
    {
        var tokenHash = HashToken(rawRefreshToken);

        // Ignore query filters to check even if user was soft deleted or token is revoked
        var token = await _context.RefreshTokens
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.TokenHash == tokenHash);

        if (token == null)
        {
            return (false, null, null, "InvalidToken");
        }

        // Lightweight Replay Detection:
        if (token.RevokedAtUtc.HasValue)
        {
            if (token.RevokedReason == RevokeReasons.Rotated)
            {
                // Suspicious replay! Revoke the user's current active session immediately
                var activeTokens = await _context.RefreshTokens
                    .IgnoreQueryFilters()
                    .Where(r => r.UserId == token.UserId && r.RevokedAtUtc == null)
                    .ToListAsync();

                foreach (var active in activeTokens)
                {
                    active.RevokedAtUtc = DateTime.UtcNow;
                    active.RevokedReason = RevokeReasons.SuspiciousReplay;
                    active.RevokedByIp = ipAddress;
                }

                await _context.SaveChangesAsync();
                return (false, null, null, "SuspiciousReplayDetected");
            }

            return (false, null, null, "TokenRevoked");
        }

        if (token.IsExpired)
        {
            return (false, null, null, "TokenExpired");
        }

        // Fetch User and active roles/permissions
        var user = await _context.Users
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
                    .ThenInclude(r => r.RolePermissions)
                        .ThenInclude(rp => rp.Permission)
            .FirstOrDefaultAsync(u => u.Id == token.UserId);

        if (user == null || !user.IsActive || user.SoftDeletedAtUtc != null || user.IsLockedOut)
        {
            token.RevokedAtUtc = DateTime.UtcNow;
            token.RevokedReason = RevokeReasons.AccountDeactivated;
            token.RevokedByIp = ipAddress;
            await _context.SaveChangesAsync();
            return (false, null, null, "UserInactiveOrLocked");
        }

        // 1. Mark current token as Rotated
        token.RevokedAtUtc = DateTime.UtcNow;
        token.RevokedReason = RevokeReasons.Rotated;
        token.RevokedByIp = ipAddress;
        await _context.SaveChangesAsync();

        // 2. Issue new active token
        var (newRefreshToken, newRawRefreshToken) = await IssueRefreshTokenAsync(user.Id, ipAddress);

        // 3. Extract effective roles and permissions
        var roles = user.UserRoles.Select(ur => ur.Role.Name).Distinct().ToList();
        var permissions = user.UserRoles
            .SelectMany(ur => ur.Role.RolePermissions)
            .Select(rp => rp.Permission.Code)
            .Distinct()
            .ToList();

        // 4. Generate new access token (must_change_password reflects live DB state!)
        var newAccessToken = CreateAccessToken(user, roles, permissions);

        return (true, newAccessToken, newRawRefreshToken, null);
    }

    public async Task<bool> RevokeActiveSessionAsync(Guid userId, string reason, string? ipAddress = null)
    {
        var activeTokens = await _context.RefreshTokens
            .Where(r => r.UserId == userId && r.RevokedAtUtc == null)
            .ToListAsync();

        if (activeTokens.Count == 0)
        {
            return false;
        }

        foreach (var token in activeTokens)
        {
            token.RevokedAtUtc = DateTime.UtcNow;
            token.RevokedReason = reason;
            token.RevokedByIp = ipAddress ?? string.Empty;
        }

        await _context.SaveChangesAsync();
        return true;
    }
}
