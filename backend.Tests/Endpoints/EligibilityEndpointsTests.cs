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

public class EligibilityEndpointsTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public EligibilityEndpointsTests(CustomWebApplicationFactory factory)
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

        if (!db.Companies.Any(c => c.CompanyId == 900))
        {
            var company = new Company { CompanyId = 900, Name = "EligTest Company", IsActive = true };
            var dept = new Department { DepartmentId = 910, CompanyId = 900, Name = "EligTest Dept", IsActive = true };
            var pos1 = new Position { PositionId = 950, Name = "VP", NLevel = 1, IsActive = true };
            var pos2 = new Position { PositionId = 951, Name = "Engineer", NLevel = 2, IsActive = true };

            db.Companies.Add(company);
            db.Departments.Add(dept);
            db.Positions.AddRange(pos1, pos2);

            // 9001: Eligible
            db.Employees.Add(new Employee
            {
                EmployeeId = 9001,
                EmployeeNumber = "EMP-9001",
                FullName = "Eligible John",
                Email = "eligible.john@meval.local",
                CompanyId = 900,
                DepartmentId = 910,
                PositionId = 950,
                EmploymentStatus = EmploymentStatus.Active,
                HireDate = new DateOnly(2020, 1, 1),
                IsEvaluationEligible = true,
                IsActive = true
            });

            // 9002: Ineligible (Excluded)
            db.Employees.Add(new Employee
            {
                EmployeeId = 9002,
                EmployeeNumber = "EMP-9002",
                FullName = "Ineligible Jane",
                Email = "ineligible.jane@meval.local",
                CompanyId = 900,
                DepartmentId = 910,
                PositionId = 951,
                EmploymentStatus = EmploymentStatus.Active,
                HireDate = new DateOnly(2021, 1, 1),
                IsEvaluationEligible = false,
                IsActive = true
            });

            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task EligibilityEndpoints_Unauthenticated_ShouldReturn401Unauthorized()
    {
        var client = _factory.CreateClient();

        var summary = await client.GetAsync("/api/v1/eligibility/summary");
        summary.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var employees = await client.GetAsync("/api/v1/eligibility/employees");
        employees.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var excluded = await client.GetAsync("/api/v1/eligibility/excluded");
        excluded.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var bulk = await client.PostAsJsonAsync("/api/v1/eligibility/bulk-flag-update", new BulkFlagUpdateRequest(new List<int> { 9001 }, false, "test"));
        bulk.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task EligibilityEndpoints_WithoutPermission_ShouldReturn403Forbidden()
    {
        var normalClient = await CreateAuthenticatedClientAsync(_factory.NormalUserEmail, _factory.TestUserPassword, "10.0.5.1");

        // Normal user does not have org.read
        var summary = await normalClient.GetAsync("/api/v1/eligibility/summary");
        summary.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Normal user does not have employees.manage-eligibility
        var bulk = await normalClient.PostAsJsonAsync("/api/v1/eligibility/bulk-flag-update", new BulkFlagUpdateRequest(new List<int> { 9001 }, false, "test"));
        bulk.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetSummary_WithAdmin_ShouldReturn200OkWithBreakdown()
    {
        await SeedTestDataAsync();
        var adminClient = await CreateAuthenticatedClientAsync(_factory.AdminUserEmail, _factory.TestUserPassword, "10.0.5.2");

        var response = await adminClient.GetAsync("/api/v1/eligibility/summary?companyId=900");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var summary = await response.Content.ReadFromJsonAsync<EligibilitySummaryDto>();
        summary.Should().NotBeNull();
        summary!.TotalEmployees.Should().Be(2);
        summary.EligibleCount.Should().Be(1);
        summary.ExcludedCount.Should().Be(1);
        summary.CompanyBreakdown.Should().ContainSingle(c => c.CompanyId == 900);
    }

    [Fact]
    public async Task SearchEmployees_Default_ShouldReturnEligibleOnly()
    {
        await SeedTestDataAsync();
        var adminClient = await CreateAuthenticatedClientAsync(_factory.AdminUserEmail, _factory.TestUserPassword, "10.0.5.3");

        // Default: no isEligible query param -> returns eligible only
        var response = await adminClient.GetAsync("/api/v1/eligibility/employees?companyId=900");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<PaginatedListDto<EligibilityEmployeeDto>>();
        result.Should().NotBeNull();
        result!.Items.Should().ContainSingle(e => e.EmployeeId == 9001);
        result.Items.Should().NotContain(e => e.EmployeeId == 9002);
    }

    [Fact]
    public async Task GetExcluded_ShouldReturnIneligibleWithReasonHrFlagFalse()
    {
        await SeedTestDataAsync();
        var adminClient = await CreateAuthenticatedClientAsync(_factory.AdminUserEmail, _factory.TestUserPassword, "10.0.5.4");

        var response = await adminClient.GetAsync("/api/v1/eligibility/excluded?companyId=900");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<PaginatedListDto<ExcludedEmployeeDto>>();
        result.Should().NotBeNull();
        result!.Items.Should().ContainSingle(e => e.EmployeeId == 9002 && e.Reason == "hr-flag-false");
    }

    [Fact]
    public async Task BulkFlagUpdate_WithValidAdmin_ShouldSucceedAndToggleFlag()
    {
        await SeedTestDataAsync();
        var adminClient = await CreateAuthenticatedClientAsync(_factory.AdminUserEmail, _factory.TestUserPassword, "10.0.5.5");

        // Toggle 9002 to true
        var request = new BulkFlagUpdateRequest(
            new List<int> { 9002 },
            true,
            "Re-inclusion batch"
        );

        var response = await adminClient.PostAsJsonAsync("/api/v1/eligibility/bulk-flag-update", request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<BulkFlagUpdateResponse>();
        body.Should().NotBeNull();
        body!.UpdatedCount.Should().Be(1);
        body.IsEvaluationEligible.Should().BeTrue();

        // Restore back to false
        await adminClient.PostAsJsonAsync("/api/v1/eligibility/bulk-flag-update", new BulkFlagUpdateRequest(new List<int> { 9002 }, false, "restore"));
    }
}
