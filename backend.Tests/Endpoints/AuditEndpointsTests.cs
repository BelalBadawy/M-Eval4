using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using MEval.Api.Data;
using MEval.Api.DTOs;
using MEval.Api.Models;
using MEval.Api.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MEval.Api.Tests.Endpoints;

public class AuditEndpointsTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public AuditEndpointsTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClientWithIp(string ip)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", ip);
        return client;
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync(string email, string password, string ip)
    {
        var client = CreateClientWithIp(ip);
        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password));
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var loginBody = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginBody!.AccessToken);
        return client;
    }

    [Fact]
    public async Task GetAuditLogs_WithoutPermission_ShouldReturn403Forbidden()
    {
        // Normal user does not have 'audit.read' permission -> 403
        var client = await CreateAuthenticatedClientAsync(_factory.NormalUserEmail, _factory.TestUserPassword, "10.0.14.1");
        var response = await client.GetAsync("/api/v1/audit/logs");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetAuditLogs_WithAuditReadPermission_ShouldReturn200OkWithPagedResult()
    {
        // Admin user has 'audit.read' permission -> 200 OK
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.AuditLogs.Add(new AuditLog
            {
                Action = "AuditEndpointTest.Action",
                EntityType = "TestEntity",
                EntityId = "Entity-123",
                Details = "{\"test\":true}",
                IpAddress = "10.0.14.2",
                ActorUserId = _factory.AdminUserId
            });
            await db.SaveChangesAsync();
        }

        var client = await CreateAuthenticatedClientAsync(_factory.AdminUserEmail, _factory.TestUserPassword, "10.0.14.2");
        var response = await client.GetAsync("/api/v1/audit/logs?action=AuditEndpointTest.Action");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<PaginatedListDto<AuditLogDto>>();
        result.Should().NotBeNull();
        result!.Items.Should().ContainSingle(l => l.Action == "AuditEndpointTest.Action" && l.EntityId == "Entity-123");
        result.TotalCount.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task GetAuditLogs_WithActorFilter_ShouldFilterCorrectly()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.AuditLogs.Add(new AuditLog
            {
                Action = "ActorFilterTest.Action",
                EntityType = "User",
                EntityId = _factory.AdminUserId.ToString(),
                Details = "{\"filtered\":true}",
                IpAddress = "10.0.14.3",
                ActorUserId = _factory.AdminUserId
            });
            await db.SaveChangesAsync();
        }

        var client = await CreateAuthenticatedClientAsync(_factory.AdminUserEmail, _factory.TestUserPassword, "10.0.14.3");
        var response = await client.GetAsync($"/api/v1/audit/logs?actorUserId={_factory.AdminUserId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<PaginatedListDto<AuditLogDto>>();
        result.Should().NotBeNull();
        result!.Items.Should().NotBeEmpty();
        result.Items.Should().OnlyContain(l => l.ActorUserId == _factory.AdminUserId);
    }
}
