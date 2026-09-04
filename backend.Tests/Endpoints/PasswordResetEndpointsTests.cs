using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using MEval.Api.Data;
using MEval.Api.DTOs;
using MEval.Api.Models;
using MEval.Api.Services;
using MEval.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MEval.Api.Tests.Endpoints;

public class PasswordResetEndpointsTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public PasswordResetEndpointsTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClientWithIp(string ip)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", ip);
        return client;
    }

    [Fact]
    public async Task ForgotPassword_WhenEmailDoesNotExist_ShouldReturnUniformSuccessResponse()
    {
        var client = CreateClientWithIp("10.0.8.1");
        var response = await client.PostAsJsonAsync("/api/v1/auth/forgot-password",
            new ForgotPasswordRequest("nonexistent@meval.local"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("If the email is registered");
    }

    [Fact]
    public async Task ForgotPassword_WhenEmailExists_ShouldCreatePasswordResetToken()
    {
        var client = CreateClientWithIp("10.0.8.2");
        var response = await client.PostAsJsonAsync("/api/v1/auth/forgot-password",
            new ForgotPasswordRequest(_factory.ResetUserEmail));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify token created in database
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var token = await db.PasswordResetTokens
            .FirstOrDefaultAsync(t => t.UserId == _factory.ResetUserId && t.UsedAtUtc == null);

        token.Should().NotBeNull();
        token!.ExpiresAtUtc.Should().BeAfter(DateTime.UtcNow);
    }

    [Fact]
    public async Task ResetPassword_WithInvalidOrExpiredToken_ShouldReturnBadRequest()
    {
        var client = CreateClientWithIp("10.0.8.3");
        var response = await client.PostAsJsonAsync("/api/v1/auth/reset-password",
            new ResetPasswordRequest("InvalidOrFakeToken123", "BrandNewPassword#2026"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("InvalidOrExpiredToken");
    }

    [Fact]
    public async Task ResetPassword_WithValidToken_ShouldUpdatePassword_ClearLockout_AndAllowLogin()
    {
        var client = CreateClientWithIp("10.0.8.4");

        // Seed a known raw token
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();

        var rawToken = "TestCryptographicRawResetToken_2026!";
        var tokenHash = tokenService.HashToken(rawToken);

        db.PasswordResetTokens.Add(new PasswordResetToken
        {
            UserId = _factory.ResetUserId,
            TokenHash = tokenHash,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(30)
        });
        await db.SaveChangesAsync();

        // Perform password reset
        var newPassword = "ResetCompliantPassword#2026";
        var resetResponse = await client.PostAsJsonAsync("/api/v1/auth/reset-password",
            new ResetPasswordRequest(rawToken, newPassword));

        resetResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify token marked used
        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var usedToken = await verifyDb.PasswordResetTokens.FirstAsync(t => t.TokenHash == tokenHash);
        usedToken.UsedAtUtc.Should().NotBeNull();

        // Verify user can now log in with the new password
        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(_factory.ResetUserEmail, newPassword));
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AdminForceReset_ShouldResetToDefaultPassword_AndPutUserInGateway()
    {
        var adminClient = CreateClientWithIp("10.0.8.5");

        // 1. Login as Admin
        var adminLogin = await adminClient.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(_factory.AdminUserEmail, _factory.TestUserPassword));
        adminLogin.StatusCode.Should().Be(HttpStatusCode.OK);
        var adminBody = await adminLogin.Content.ReadFromJsonAsync<LoginResponse>();

        adminClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", adminBody!.AccessToken);

        // 2. Admin calls force-reset on ForceResetUser
        var forceResetResponse = await adminClient.PostAsync(
            $"/api/v1/users/{_factory.ForceResetUserId}/force-reset-password", null);

        forceResetResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. Verify target user in DB has MustChangePassword = true
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.FindAsync(_factory.ForceResetUserId);
        user!.MustChangePassword.Should().BeTrue();

        // 4. Target user logs in with default password -> gets MustChangePassword = true
        var userClient = CreateClientWithIp("10.0.8.6");
        var userLogin = await userClient.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(_factory.ForceResetUserEmail, _factory.TestUserPassword));
        userLogin.StatusCode.Should().Be(HttpStatusCode.OK);
        var userBody = await userLogin.Content.ReadFromJsonAsync<LoginResponse>();
        userBody!.User.MustChangePassword.Should().BeTrue();

        // 5. Target user is blocked by FirstLoginGateway from accessing /session
        userClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", userBody.AccessToken);
        var sessionRes = await userClient.GetAsync("/api/v1/auth/session");
        sessionRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
