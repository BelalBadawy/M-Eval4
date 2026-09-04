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

public class EmployeeEndpointsTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public EmployeeEndpointsTests(CustomWebApplicationFactory factory)
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

    private async Task SeedTestDataAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (!db.Companies.Any(c => c.CompanyId == 700))
        {
            var company = new Company { CompanyId = 700, Name = "EmpTest Company", IsActive = true };
            var dept = new Department { DepartmentId = 710, CompanyId = 700, Name = "EmpTest Dept", IsActive = true };
            var pos1 = new Position { PositionId = 750, Name = "Director", NLevel = 1, IsActive = true };
            var pos2 = new Position { PositionId = 751, Name = "Manager", NLevel = 2, IsActive = true };

            db.Companies.Add(company);
            db.Departments.Add(dept);
            db.Positions.AddRange(pos1, pos2);

            // Director (NLevel 1, no manager)
            var empRoot = new Employee
            {
                EmployeeId = 7001,
                EmployeeNumber = "EMP-7001",
                FullName = "Director Employee",
                CompanyId = 700,
                DepartmentId = 710,
                PositionId = 750,
                DirectManagerId = null,
                EmploymentStatus = EmploymentStatus.Active,
                HireDate = new DateOnly(2020, 1, 1),
                IsEvaluationEligible = true,
                IsActive = true
            };

            // Manager reporting to Director
            var empSub = new Employee
            {
                EmployeeId = 7002,
                EmployeeNumber = "EMP-7002",
                FullName = "Subordinate Employee",
                CompanyId = 700,
                DepartmentId = 710,
                PositionId = 751,
                DirectManagerId = 7001,
                EmploymentStatus = EmploymentStatus.Active,
                HireDate = new DateOnly(2021, 1, 1),
                IsEvaluationEligible = true,
                IsActive = true
            };

            db.Employees.AddRange(empRoot, empSub);
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task EmployeeEndpoints_WithoutPermissions_ShouldReturn403Forbidden()
    {
        await SeedTestDataAsync();
        var normalClient = await CreateAuthenticatedClientAsync(_factory.NormalUserEmail, _factory.TestUserPassword, "10.0.2.1");

        // 1. Search employees deny
        var getResponse = await normalClient.GetAsync("/api/v1/employees");
        getResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 2. Eligibility deny
        var putResponse = await normalClient.PutAsJsonAsync("/api/v1/employees/7001/eligibility", new UpdateEligibilityRequest(false));
        putResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 3. Link user deny
        var linkResponse = await normalClient.PostAsJsonAsync("/api/v1/employees/7001/link-user", new LinkUserAccountRequest(_factory.TargetUser1Id));
        linkResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SearchEmployees_WithFilters_ShouldReturnResults()
    {
        await SeedTestDataAsync();
        var adminClient = await CreateAuthenticatedClientAsync(_factory.AdminUserEmail, _factory.TestUserPassword, "10.0.2.2");

        var response = await adminClient.GetAsync("/api/v1/employees?search=Director");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var paged = await response.Content.ReadFromJsonAsync<PaginatedListDto<EmployeeSummaryDto>>();
        paged.Should().NotBeNull();
        paged!.Items.Should().Contain(e => e.EmployeeId == 7001);
    }

    [Fact]
    public async Task GetManagerChainAndDirectReports_ShouldReturnHierarchyData()
    {
        await SeedTestDataAsync();
        var adminClient = await CreateAuthenticatedClientAsync(_factory.AdminUserEmail, _factory.TestUserPassword, "10.0.2.3");

        // Manager chain of 7002 -> should reach 7001
        var chainResponse = await adminClient.GetAsync("/api/v1/employees/7002/manager-chain");
        chainResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var chain = await chainResponse.Content.ReadFromJsonAsync<List<ManagerChainNodeDto>>();
        chain.Should().NotBeNull();
        chain!.Should().ContainSingle(c => c.EmployeeId == 7001 && c.Depth == 1);

        // Direct reports of 7001 -> should contain 7002
        var reportsResponse = await adminClient.GetAsync("/api/v1/employees/7001/direct-reports");
        reportsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var reports = await reportsResponse.Content.ReadFromJsonAsync<List<DirectReportDto>>();
        reports.Should().NotBeNull();
        reports!.Should().Contain(r => r.EmployeeId == 7002);
    }

    [Fact]
    public async Task UpdateEligibility_ShouldToggleFlag()
    {
        await SeedTestDataAsync();
        var adminClient = await CreateAuthenticatedClientAsync(_factory.AdminUserEmail, _factory.TestUserPassword, "10.0.2.4");

        var putResponse = await adminClient.PutAsJsonAsync(
            "/api/v1/employees/7001/eligibility",
            new UpdateEligibilityRequest(false, "Performance probation"));
        putResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var getResponse = await adminClient.GetAsync("/api/v1/employees/7001");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var emp = await getResponse.Content.ReadFromJsonAsync<EmployeeDetailDto>();
        emp!.IsEvaluationEligible.Should().BeFalse();
    }

    [Fact]
    public async Task LinkUser_AndPreventDoubleLinkWith409Conflict()
    {
        await SeedTestDataAsync();
        var adminClient = await CreateAuthenticatedClientAsync(_factory.AdminUserEmail, _factory.TestUserPassword, "10.0.2.5");

        // 1. Link TargetUser1 to Employee 7001
        var linkResponse = await adminClient.PostAsJsonAsync(
            "/api/v1/employees/7001/link-user",
            new LinkUserAccountRequest(_factory.TargetUser1Id));
        linkResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. Attempt to link TargetUser1 to Employee 7002 -> 409 Conflict
        var doubleLinkResponse = await adminClient.PostAsJsonAsync(
            "/api/v1/employees/7002/link-user",
            new LinkUserAccountRequest(_factory.TargetUser1Id));
        doubleLinkResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // 3. Unlink TargetUser1 from Employee 7001
        var unlinkResponse = await adminClient.PostAsJsonAsync(
            "/api/v1/employees/7001/unlink-user",
            new { });
        unlinkResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. Now linking TargetUser1 to Employee 7002 succeeds
        var newLinkResponse = await adminClient.PostAsJsonAsync(
            "/api/v1/employees/7002/link-user",
            new LinkUserAccountRequest(_factory.TargetUser1Id));
        newLinkResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
