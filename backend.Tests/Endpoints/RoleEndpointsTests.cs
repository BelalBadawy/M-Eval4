using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using MEval.Api.Data;
using MEval.Api.DTOs;
using MEval.Api.Models;
using MEval.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MEval.Api.Tests.Endpoints;

public class RoleEndpointsTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public RoleEndpointsTests(CustomWebApplicationFactory factory)
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
    public async Task GetRoles_WithoutPermission_ShouldReturn403Forbidden()
    {
        // Normal user does not have 'roles.manage' permission -> 403
        var client = await CreateAuthenticatedClientAsync(_factory.NormalUserEmail, _factory.TestUserPassword, "10.0.9.1");
        var response = await client.GetAsync("/api/v1/roles");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetRoles_WithRolesManagePermission_ShouldReturn200OkWithRoles()
    {
        // Admin user has 'roles.manage' permission -> 200 OK
        var client = await CreateAuthenticatedClientAsync(_factory.AdminUserEmail, _factory.TestUserPassword, "10.0.9.2");
        var response = await client.GetAsync("/api/v1/roles");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var roles = await response.Content.ReadFromJsonAsync<List<RoleDto>>();
        roles.Should().NotBeNull();
        roles!.Should().Contain(r => r.Name == "Admin");
        roles.Should().Contain(r => r.Name == "User");
    }

    [Fact]
    public async Task CreateRole_WhenLevelIsLessThanCallerLevel_ShouldReturn201Created()
    {
        // Admin (Level 50) creates role with Level 20 (< 50)
        var client = await CreateAuthenticatedClientAsync(_factory.AdminUserEmail, _factory.TestUserPassword, "10.0.9.3");
        var request = new CreateRoleRequest("Department Manager", "Department Level Manager", 20, new List<string> { "users.read" });

        var response = await client.PostAsJsonAsync("/api/v1/roles", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<RoleDto>();
        created.Should().NotBeNull();
        created!.Name.Should().Be("Department Manager");
        created.Level.Should().Be(20);
        created.Permissions.Should().Contain("users.read");
    }

    [Fact]
    public async Task CreateRole_WhenLevelIsEqualOrGreaterThanCallerLevel_ShouldReturn403HierarchyViolation()
    {
        // Admin (Level 50) attempts to create role with Level 50 (>= 50)
        var client = await CreateAuthenticatedClientAsync(_factory.AdminUserEmail, _factory.TestUserPassword, "10.0.9.4");
        var request = new CreateRoleRequest("Illegal Admin Clone", "Should fail", 50, null);

        var response = await client.PostAsJsonAsync("/api/v1/roles", request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("HierarchyViolation");
    }

    [Fact]
    public async Task AssignRole_WhenTargetRoleLevelIsEqualOrGreaterThanCallerLevel_ShouldReturn403HierarchyViolation()
    {
        // Admin (Level 50) tries to assign Admin role (Level 50) to another user
        var client = await CreateAuthenticatedClientAsync(_factory.AdminUserEmail, _factory.TestUserPassword, "10.0.9.5");

        var response = await client.PostAsync($"/api/v1/users/{_factory.NormalUserId}/roles/{_factory.AdminRoleId}", null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("HierarchyViolation");
    }

    [Fact]
    public async Task BulkAssignRole_ValidatesHierarchy_AndAssignsToEligibleUsers()
    {
        // Super Admin (Level 100) creates custom role (Level 35)
        var superClient = await CreateAuthenticatedClientAsync(_factory.SuperAdminUserEmail, _factory.TestUserPassword, "10.0.9.6");
        var roleName = "Auditor Assistant";
        var createRes = await superClient.PostAsJsonAsync("/api/v1/roles", new CreateRoleRequest(roleName, "Desc", 35, null));
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var role = await createRes.Content.ReadFromJsonAsync<RoleDto>();

        // Admin (Level 50) assigns role (Level 35 < 50) to TargetUser1 and TargetUser2 in bulk
        var adminClient = await CreateAuthenticatedClientAsync(_factory.AdminUserEmail, _factory.TestUserPassword, "10.0.9.7");
        var bulkRequest = new BulkAssignRoleRequest(role!.Id, new List<Guid> { _factory.TargetUser1Id, _factory.TargetUser2Id });

        var bulkResponse = await adminClient.PostAsJsonAsync("/api/v1/users/bulk-assign-role", bulkRequest);

        bulkResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await bulkResponse.Content.ReadFromJsonAsync<BulkAssignRoleResponse>();
        result.Should().NotBeNull();
        result!.SucceededCount.Should().Be(2);

        // Verify users actually have the role in DB
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user1HasRole = await db.UserRoles.AnyAsync(ur => ur.UserId == _factory.TargetUser1Id && ur.RoleId == role.Id);
        var user2HasRole = await db.UserRoles.AnyAsync(ur => ur.UserId == _factory.TargetUser2Id && ur.RoleId == role.Id);

        user1HasRole.Should().BeTrue();
        user2HasRole.Should().BeTrue();
    }
}
