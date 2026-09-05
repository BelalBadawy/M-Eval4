using FluentAssertions;
using MEval.Api.Data;
using MEval.Api.DTOs;
using MEval.Api.Models;
using MEval.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MEval.Api.Tests.Services;

public class EligibilityServiceTests
{
    private (AppDbContext Context, EligibilityService Service) CreateService()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var context = new AppDbContext(options);
        var auditService = new AuditService(context);
        var service = new EligibilityService(context, auditService);

        return (context, service);
    }

    private void SeedBaseOrg(AppDbContext context)
    {
        context.Companies.Add(new Company { CompanyId = 1, Name = "Acme Corp" });
        context.Companies.Add(new Company { CompanyId = 2, Name = "Globex Inc" });

        context.Departments.Add(new Department { DepartmentId = 10, CompanyId = 1, Name = "Engineering" });
        context.Departments.Add(new Department { DepartmentId = 20, CompanyId = 2, Name = "Sales" });

        context.Positions.Add(new Position { PositionId = 500, Name = "VP", NLevel = 1 });
        context.Positions.Add(new Position { PositionId = 501, Name = "Lead", NLevel = 2 });
    }

    [Fact]
    public async Task ELIG_1_PredicateIsOneCondition_TrueIsEligible_FalseIsExcluded()
    {
        var (context, service) = CreateService();
        SeedBaseOrg(context);

        context.Employees.Add(new Employee
        {
            EmployeeId = 100,
            EmployeeNumber = "E-100",
            FullName = "Eligible Person",
            CompanyId = 1,
            DepartmentId = 10,
            PositionId = 500,
            IsEvaluationEligible = true,
            HireDate = new DateOnly(2020, 1, 1)
        });

        context.Employees.Add(new Employee
        {
            EmployeeId = 200,
            EmployeeNumber = "E-200",
            FullName = "Excluded Person",
            CompanyId = 1,
            DepartmentId = 10,
            PositionId = 501,
            IsEvaluationEligible = false,
            HireDate = new DateOnly(2020, 1, 1)
        });

        await context.SaveChangesAsync();

        // 1. Search default (eligible only)
        var eligibleResult = await service.SearchEmployeesAsync();
        eligibleResult.TotalCount.Should().Be(1);
        eligibleResult.Items.Should().ContainSingle(e => e.EmployeeId == 100);

        // 2. Search excluded
        var excludedResult = await service.GetExcludedEmployeesAsync();
        excludedResult.TotalCount.Should().Be(1);
        excludedResult.Items.Should().ContainSingle(e => e.EmployeeId == 200 && e.Reason == "hr-flag-false");
    }

    [Fact]
    public async Task ELIG_13_ResignedWithFlag1_IsEligible()
    {
        var (context, service) = CreateService();
        SeedBaseOrg(context);

        // Resigned employee included in HR file with flag 1 (e.g. for year-end evaluation)
        context.Employees.Add(new Employee
        {
            EmployeeId = 300,
            EmployeeNumber = "E-300",
            FullName = "Resigned Evaluated Employee",
            CompanyId = 1,
            DepartmentId = 10,
            PositionId = 501,
            EmploymentStatus = EmploymentStatus.Resigned,
            ResignationDate = new DateOnly(2026, 8, 1),
            IsEvaluationEligible = true, // Flag is 1!
            HireDate = new DateOnly(2020, 1, 1)
        });

        await context.SaveChangesAsync();

        var result = await service.SearchEmployeesAsync(isEligible: true);
        result.TotalCount.Should().Be(1);
        result.Items.Should().ContainSingle(e => e.EmployeeId == 300 && e.Status == "Resigned");
    }

    [Fact]
    public async Task ELIG_14_IsActiveFalseWithFlag1_IsEligible()
    {
        var (context, service) = CreateService();
        SeedBaseOrg(context);

        // Employee with IsActive = false (data correction) but flag = 1
        context.Employees.Add(new Employee
        {
            EmployeeId = 400,
            EmployeeNumber = "E-400",
            FullName = "Data Correction Employee",
            CompanyId = 1,
            DepartmentId = 10,
            PositionId = 501,
            IsActive = false,
            IsEvaluationEligible = true, // Flag is 1!
            HireDate = new DateOnly(2020, 1, 1)
        });

        await context.SaveChangesAsync();

        var result = await service.SearchEmployeesAsync(isEligible: true);
        result.TotalCount.Should().Be(1);
        result.Items.Should().ContainSingle(e => e.EmployeeId == 400);
    }

    [Fact]
    public async Task Summary_ReturnsAccurateHeadcount_AndPerCompanyBreakdown()
    {
        var (context, service) = CreateService();
        SeedBaseOrg(context);

        // Company 1: 2 eligible, 1 excluded
        context.Employees.Add(new Employee { EmployeeId = 1, EmployeeNumber = "C1-1", FullName = "A", CompanyId = 1, PositionId = 500, IsEvaluationEligible = true, HireDate = new DateOnly(2020, 1, 1) });
        context.Employees.Add(new Employee { EmployeeId = 2, EmployeeNumber = "C1-2", FullName = "B", CompanyId = 1, PositionId = 501, IsEvaluationEligible = true, HireDate = new DateOnly(2020, 1, 1) });
        context.Employees.Add(new Employee { EmployeeId = 3, EmployeeNumber = "C1-3", FullName = "C", CompanyId = 1, PositionId = 501, IsEvaluationEligible = false, HireDate = new DateOnly(2020, 1, 1) });

        // Company 2: 1 eligible, 2 excluded
        context.Employees.Add(new Employee { EmployeeId = 4, EmployeeNumber = "C2-1", FullName = "D", CompanyId = 2, PositionId = 500, IsEvaluationEligible = true, HireDate = new DateOnly(2020, 1, 1) });
        context.Employees.Add(new Employee { EmployeeId = 5, EmployeeNumber = "C2-2", FullName = "E", CompanyId = 2, PositionId = 501, IsEvaluationEligible = false, HireDate = new DateOnly(2020, 1, 1) });
        context.Employees.Add(new Employee { EmployeeId = 6, EmployeeNumber = "C2-3", FullName = "F", CompanyId = 2, PositionId = 501, IsEvaluationEligible = false, HireDate = new DateOnly(2020, 1, 1) });

        await context.SaveChangesAsync();

        var summary = await service.GetSummaryAsync();

        summary.TotalEmployees.Should().Be(6);
        summary.EligibleCount.Should().Be(3);
        summary.ExcludedCount.Should().Be(3);
        summary.CompanyBreakdown.Should().HaveCount(2);

        var c1 = summary.CompanyBreakdown.First(c => c.CompanyId == 1);
        c1.TotalEmployees.Should().Be(3);
        c1.EligibleCount.Should().Be(2);
        c1.ExcludedCount.Should().Be(1);

        var c2 = summary.CompanyBreakdown.First(c => c.CompanyId == 2);
        c2.TotalEmployees.Should().Be(3);
        c2.EligibleCount.Should().Be(1);
        c2.ExcludedCount.Should().Be(2);
    }

    [Fact]
    public async Task BulkFlagUpdate_AllOrNothing_WhenMissingId_RejectsWith400AndMissingIds()
    {
        var (context, service) = CreateService();
        SeedBaseOrg(context);

        context.Employees.Add(new Employee { EmployeeId = 1, EmployeeNumber = "E1", FullName = "A", CompanyId = 1, PositionId = 500, IsEvaluationEligible = false, HireDate = new DateOnly(2020, 1, 1) });
        await context.SaveChangesAsync();

        // Submit IDs: 1 (exists), 999 (missing)
        var request = new BulkFlagUpdateRequest(
            new List<int> { 1, 999 },
            true,
            "Promotion batch override"
        );

        var (success, response, error, missingIds, statusCode) = await service.BulkFlagUpdateAsync(request, 1, "127.0.0.1");

        statusCode.Should().Be(400);
        success.Should().BeFalse();
        missingIds.Should().ContainSingle(id => id == 999);
        error.Should().Contain("1 employee ID(s) do not exist");

        // Verify existing employee 1 was NOT updated (AllOrNothing)
        var emp1 = await context.Employees.FindAsync(1);
        emp1!.IsEvaluationEligible.Should().BeFalse();
    }

    [Fact]
    public async Task BulkFlagUpdate_Exceeds500Limit_RejectsWith400()
    {
        var (_, service) = CreateService();

        var request = new BulkFlagUpdateRequest(
            Enumerable.Range(1, 501).ToList(),
            true,
            "Oversized batch"
        );

        var (success, _, error, _, statusCode) = await service.BulkFlagUpdateAsync(request, 1, "127.0.0.1");

        statusCode.Should().Be(400);
        success.Should().BeFalse();
        error.Should().Contain("exceeds maximum limit of 500");
    }

    [Fact]
    public async Task BulkFlagUpdate_SuccessfulBatch_UpdatesFlagsAndEmitsAuditWithIp()
    {
        var (context, service) = CreateService();
        SeedBaseOrg(context);

        context.Employees.Add(new Employee { EmployeeId = 10, EmployeeNumber = "E10", FullName = "A", CompanyId = 1, PositionId = 500, IsEvaluationEligible = false, HireDate = new DateOnly(2020, 1, 1) });
        context.Employees.Add(new Employee { EmployeeId = 11, EmployeeNumber = "E11", FullName = "B", CompanyId = 1, PositionId = 501, IsEvaluationEligible = false, HireDate = new DateOnly(2020, 1, 1) });
        await context.SaveChangesAsync();

        var request = new BulkFlagUpdateRequest(
            new List<int> { 10, 11 },
            true,
            "Special inclusion batch"
        );

        var (success, response, error, _, statusCode) = await service.BulkFlagUpdateAsync(request, 42, "192.168.1.50");

        statusCode.Should().Be(200);
        success.Should().BeTrue();
        response!.UpdatedCount.Should().Be(2);
        response.IsEvaluationEligible.Should().BeTrue();

        var emp10 = await context.Employees.FindAsync(10);
        var emp11 = await context.Employees.FindAsync(11);
        emp10!.IsEvaluationEligible.Should().BeTrue();
        emp11!.IsEvaluationEligible.Should().BeTrue();

        // Audit log verified
        var audit = await context.AuditLogs.FirstOrDefaultAsync(a => a.Action == "EligibilityBulkChanged");
        audit.Should().NotBeNull();
        audit!.ActorUserId.Should().Be(42);
        audit.IpAddress.Should().Be("192.168.1.50");
        audit.Details.Should().Contain("Special inclusion batch");
    }
}
