using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using MEval.Api.DTOs;
using MEval.Api.Tests.Infrastructure;
using Xunit;

namespace MEval.Api.Tests.Endpoints;

public class AuthEndpointsTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public AuthEndpointsTests(CustomWebApplicationFactory factory)
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
    public async Task Login_WithValidCredentials_ShouldReturnTokensAndMustChangePasswordFlag()
    {
        var client = CreateClientWithIp("10.0.1.1");
        var request = new LoginRequest(_factory.TestUserEmail, _factory.TestUserPassword);
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        body.Should().NotBeNull();
        body!.AccessToken.Should().NotBeNullOrWhiteSpace();
        body.RefreshToken.Should().NotBeNullOrWhiteSpace();
        body.User.Email.Should().Be(_factory.TestUserEmail);
        body.User.MustChangePassword.Should().BeTrue();
    }

    [Fact]
    public async Task FirstLoginGateway_WhenMustChangePasswordIsTrue_ShouldBlockProtectedEndpointsWith403()
    {
        var client = CreateClientWithIp("10.0.2.1");

        // 1. Login with GatewayUser
        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(_factory.GatewayUserEmail, _factory.TestUserPassword));
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginBody!.AccessToken);

        // 2. Try accessing protected endpoint /api/v1/auth/session
        var sessionResponse = await client.GetAsync("/api/v1/auth/session");

        sessionResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var errorContent = await sessionResponse.Content.ReadAsStringAsync();
        errorContent.Should().Contain("PasswordChangeRequired");
    }

    [Fact]
    public async Task FirstLoginGateway_WhenMustChangePasswordIsTrue_ShouldAllowRefreshAndLogout()
    {
        var client = CreateClientWithIp("10.0.3.1");

        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(_factory.GatewayUserEmail, _factory.TestUserPassword));
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginBody!.AccessToken);

        // 1. Refresh endpoint is allowed
        var refreshResponse = await client.PostAsJsonAsync("/api/v1/auth/refresh",
            new RefreshTokenRequest(loginBody.RefreshToken));
        refreshResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var refreshBody = await refreshResponse.Content.ReadFromJsonAsync<RefreshTokenResponse>();
        refreshBody.Should().NotBeNull();

        // 2. Logout endpoint is allowed
        var logoutResponse = await client.PostAsJsonAsync("/api/v1/auth/logout", new { });
        logoutResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ChangePassword_WithDefaultPasswordReuse_ShouldReturnBadRequest()
    {
        var client = CreateClientWithIp("10.0.4.1");

        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(_factory.ChangePassReuseUserEmail, _factory.TestUserPassword));
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginBody!.AccessToken);

        // Attempt to set new password back to default password "Mina@123"
        var changeRequest = new ChangePasswordRequest(_factory.TestUserPassword, _factory.TestUserPassword);
        var changeResponse = await client.PostAsJsonAsync("/api/v1/auth/change-password", changeRequest);

        changeResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var errorContent = await changeResponse.Content.ReadAsStringAsync();
        errorContent.Should().Contain("PasswordPolicyViolation");
    }

    [Fact]
    public async Task ChangePassword_WithValidNewPassword_ShouldClearFlagAndUnblockAccount()
    {
        var client = CreateClientWithIp("10.0.5.1");

        // 1. Login with temporary password
        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(_factory.ChangePassSuccessUserEmail, _factory.TestUserPassword));
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginBody!.AccessToken);

        // 2. Change password to compliant new password
        var newPassword = "BrandNewSecret#2026";
        var changeRequest = new ChangePasswordRequest(_factory.TestUserPassword, newPassword);
        var changeResponse = await client.PostAsJsonAsync("/api/v1/auth/change-password", changeRequest);

        changeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var changeBody = await changeResponse.Content.ReadFromJsonAsync<LoginResponse>();
        changeBody!.User.MustChangePassword.Should().BeFalse();

        // 3. New token can now access previously blocked protected routes
        var authenticatedClient = CreateClientWithIp("10.0.5.2");
        authenticatedClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", changeBody.AccessToken);

        var sessionResponse = await authenticatedClient.GetAsync("/api/v1/auth/session");
        sessionResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var sessionBody = await sessionResponse.Content.ReadFromJsonAsync<SessionResponse>();
        sessionBody!.MustChangePassword.Should().BeFalse();
    }

    [Fact]
    public async Task Login_ShouldLockout_After5ConsecutiveFailedAttempts()
    {
        var client = CreateClientWithIp("10.0.6.1");
        var email = _factory.LockoutUserEmail;

        // Perform 5 failed attempts
        for (int i = 0; i < 5; i++)
        {
            var res = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, "WrongPassword123!"));
            if (i < 4)
            {
                res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            }
            else
            {
                // 5th failed attempt locks out the account
                res.StatusCode.Should().Be((HttpStatusCode)423);
                var content = await res.Content.ReadAsStringAsync();
                content.Should().Contain("AccountLocked");
            }
        }

        // Even with the correct password, access is rejected while locked
        var lockedResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, _factory.TestUserPassword));
        lockedResponse.StatusCode.Should().Be((HttpStatusCode)423);
    }

    [Fact]
    public async Task Login_WhenExceedingRateLimit_ShouldReturn429TooManyRequests()
    {
        // IP 10.0.7.1 sends 10 requests allowed, then the 11th must return 429
        var client = CreateClientWithIp("10.0.7.1");
        var email = _factory.RateLimitUserEmail;

        HttpResponseMessage? lastResponse = null;
        for (int i = 0; i < 11; i++)
        {
            lastResponse = await client.PostAsJsonAsync("/api/v1/auth/login",
                new LoginRequest(email, "SomePassword123!"));
        }

        lastResponse.Should().NotBeNull();
        lastResponse!.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }
}
