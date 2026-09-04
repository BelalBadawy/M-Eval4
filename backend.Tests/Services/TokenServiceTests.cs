using System.IdentityModel.Tokens.Jwt;
using FluentAssertions;
using MEval.Api.Configuration;
using MEval.Api.Data;
using MEval.Api.Models;
using MEval.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace MEval.Api.Tests.Services;

public class TokenServiceTests
{
    private (AppDbContext Context, TokenService Service, User TestUser) CreateTokenService()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var context = new AppDbContext(options);
        var jwtSettings = Options.Create(new JwtSettings
        {
            SecretKey = "TestSecretKeyThatIsAtLeast32BytesLongForSecurity!",
            Issuer = "MEval.Test",
            Audience = "MEval.TestClient",
            AccessTokenExpirationMinutes = 15,
            RefreshTokenExpirationDays = 7
        });

        var user = new User
        {
            Id = 1,
            FullName = "Test User",
            Email = "test@meval.local",
            PasswordHash = "hashedPassword",
            MustChangePassword = true,
            IsActive = true
        };

        var role = new Role { Id = 1, Name = "User", Level = 10 };
        var perm = new Permission { Id = 1, Code = "users.read", Module = "Users" };

        context.Users.Add(user);
        context.Roles.Add(role);
        context.Permissions.Add(perm);
        context.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
        context.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = perm.Id });
        context.SaveChanges();

        var service = new TokenService(context, jwtSettings);
        return (context, service, user);
    }

    [Fact]
    public void CreateAccessToken_ShouldIncludeExpectedClaims()
    {
        var (_, service, user) = CreateTokenService();
        var tokenString = service.CreateAccessToken(user, new[] { "User" }, new[] { "users.read" });

        var handler = new JwtSecurityTokenHandler();
        var token = handler.ReadJwtToken(tokenString);

        token.Claims.First(c => c.Type == JwtRegisteredClaimNames.Sub).Value.Should().Be(user.Id.ToString());
        token.Claims.First(c => c.Type == JwtRegisteredClaimNames.Email).Value.Should().Be(user.Email);
        token.Claims.First(c => c.Type == "must_change_password").Value.Should().Be("true");
        token.Claims.First(c => c.Type == "http://schemas.microsoft.com/ws/2008/06/identity/claims/role").Value.Should().Be("User");
        token.Claims.First(c => c.Type == "permission").Value.Should().Be("users.read");
    }

    [Fact]
    public async Task IssueRefreshToken_ShouldEnforceSingleActiveSession_OneDeviceWins()
    {
        var (context, service, user) = CreateTokenService();

        // Device A logs in
        var (tokenA, _) = await service.IssueRefreshTokenAsync(user.Id, "192.168.1.1");
        tokenA.RevokedAtUtc.Should().BeNull();

        // Device B logs in
        var (tokenB, _) = await service.IssueRefreshTokenAsync(user.Id, "192.168.1.2");

        // Verify Device A was superseded
        var refreshedA = await context.RefreshTokens.FindAsync(tokenA.Id);
        refreshedA.Should().NotBeNull();
        refreshedA!.RevokedAtUtc.Should().NotBeNull();
        refreshedA.RevokedReason.Should().Be(RevokeReasons.SupersededByNewLogin);

        // Verify Device B is the single active session
        var refreshedB = await context.RefreshTokens.FindAsync(tokenB.Id);
        refreshedB!.RevokedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task RotateRefreshToken_ShouldRotateAndPreserveMustChangePassword()
    {
        var (context, service, user) = CreateTokenService();

        var (initialToken, rawToken) = await service.IssueRefreshTokenAsync(user.Id, "127.0.0.1");

        // Rotate
        var (success, accessToken, newRawToken, error) = await service.RotateRefreshTokenAsync(rawToken, "127.0.0.1");

        success.Should().BeTrue();
        error.Should().BeNull();
        accessToken.Should().NotBeNull();
        newRawToken.Should().NotBeNull();

        // Old token should be marked Rotated
        var oldToken = await context.RefreshTokens.FindAsync(initialToken.Id);
        oldToken!.RevokedReason.Should().Be(RevokeReasons.Rotated);

        // Access token should still reflect must_change_password = true
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(accessToken);
        jwt.Claims.First(c => c.Type == "must_change_password").Value.Should().Be("true");
    }

    [Fact]
    public async Task RotateRefreshToken_ShouldDetectSuspiciousReplay_AndRevokeActiveSession()
    {
        var (context, service, user) = CreateTokenService();

        // 1. Initial login
        var (_, rawToken1) = await service.IssueRefreshTokenAsync(user.Id, "127.0.0.1");

        // 2. Normal rotation -> rawToken1 is now Rotated, rawToken2 is active
        var (success1, _, rawToken2, _) = await service.RotateRefreshTokenAsync(rawToken1, "127.0.0.1");
        success1.Should().BeTrue();

        // 3. Attacker replays rawToken1
        var (success2, _, _, error) = await service.RotateRefreshTokenAsync(rawToken1, "192.168.9.9");

        success2.Should().BeFalse();
        error.Should().Be("SuspiciousReplayDetected");

        // 4. Verify that rawToken2 (the active session) was immediately revoked with SuspiciousReplay
        var rawToken2Hash = service.HashToken(rawToken2!);
        var token2InDb = await context.RefreshTokens.FirstAsync(r => r.TokenHash == rawToken2Hash);
        token2InDb.RevokedAtUtc.Should().NotBeNull();
        token2InDb.RevokedReason.Should().Be(RevokeReasons.SuspiciousReplay);
    }
}
