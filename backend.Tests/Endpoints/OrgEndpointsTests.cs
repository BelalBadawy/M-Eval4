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

public class OrgEndpointsTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public OrgEndpointsTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync(string email, string password, string ip)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", ip);

        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password));
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var loginBody = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginBody!.AccessToken);
        return client;
    }

    [Fact]
    public async Task GetOrgStructure_WithoutPermission_ShouldReturn403Forbidden()
    {
        var normalClient = await CreateAuthenticatedClientAsync(_factory.NormalUserEmail, _factory.TestUserPassword, "10.0.1.50");

        var response = await normalClient.GetAsync("/api/v1/org/structure");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetOrgStructure_WithPermission_ShouldReturnActiveStructureTree()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            if (!db.Companies.Any(c => c.CompanyId == 900))
            {
                var company = new Company { CompanyId = 900, Name = "OrgTest Company", IsActive = true };
                var dept = new Department { DepartmentId = 910, CompanyId = 900, Name = "Engineering Dept", IsActive = true };
                var sec = new Section { SectionId = 920, DepartmentId = 910, Name = "DevOps Section", IsActive = true };
                var pos = new Position { PositionId = 930, Name = "Lead Architect", NLevel = 2, IsActive = true };

                db.Companies.Add(company);
                db.Departments.Add(dept);
                db.Sections.Add(sec);
                db.Positions.Add(pos);
                await db.SaveChangesAsync();
            }
        }

        var adminClient = await CreateAuthenticatedClientAsync(_factory.AdminUserEmail, _factory.TestUserPassword, "10.0.1.51");

        var response = await adminClient.GetAsync("/api/v1/org/structure");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var tree = await response.Content.ReadFromJsonAsync<OrgStructureTreeDto>();
        tree.Should().NotBeNull();
        tree!.Companies.Should().Contain(c => c.CompanyId == 900 && c.Name == "OrgTest Company");

        var comp = tree.Companies.First(c => c.CompanyId == 900);
        comp.Departments.Should().Contain(d => d.DepartmentId == 910 && d.Name == "Engineering Dept");
        comp.Departments.First(d => d.DepartmentId == 910).Sections.Should().Contain(s => s.SectionId == 920 && s.Name == "DevOps Section");
    }

    [Fact]
    public async Task GetCompaniesAndPositions_ShouldReturnLists()
    {
        var adminClient = await CreateAuthenticatedClientAsync(_factory.AdminUserEmail, _factory.TestUserPassword, "10.0.1.52");

        var compResponse = await adminClient.GetAsync("/api/v1/org/companies");
        compResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var posResponse = await adminClient.GetAsync("/api/v1/org/positions");
        posResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
