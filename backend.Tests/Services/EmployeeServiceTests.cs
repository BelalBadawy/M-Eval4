using FluentAssertions;
using MEval.Api.Data;
using MEval.Api.DTOs;
using MEval.Api.Models;
using MEval.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MEval.Api.Tests.Services;

public class EmployeeServiceTests
{
    private (AppDbContext Context, EmployeeService Service) CreateService()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var context = new AppDbContext(options);
        var auditService = new AuditService(context);
        var service = new EmployeeService(context, auditService);
        return (context, service);
    }

    [Fact]
    public async Task SearchEmployees_WithFilters_ShouldReturnFilteredPagedResults()
    {
        var (context, service) = CreateService();

        var company = new Company { CompanyId = 1, Name = "Tech Corp" };
        var dept = new Department { DepartmentId = 1, CompanyId = 1, Name = "Engineering" };
        var pos1 = new Position { PositionId = 1, Name = "Lead", NLevel = 2 };
        var pos2 = new Position { PositionId = 2, Name = "Junior", NLevel = 4 };

        context.Companies.Add(company);
        context.Departments.Add(dept);
        context.Positions.AddRange(pos1, pos2);

        var emp1 = new Employee { EmployeeId = 1, EmployeeNumber = "EMP-01", FullName = "Lead Developer", CompanyId = 1, DepartmentId = 1, PositionId = 1, IsEvaluationEligible = true, UserId = 10, HireDate = new DateOnly(2020, 1, 1) };
        var emp2 = new Employee { EmployeeId = 2, EmployeeNumber = "EMP-02", FullName = "Junior Developer", CompanyId = 1, DepartmentId = 1, PositionId = 2, IsEvaluationEligible = false, UserId = null, HireDate = new DateOnly(2022, 1, 1) };

        context.Employees.AddRange(emp1, emp2);
        await context.SaveChangesAsync();

        // 1. Filter by hasLinkedAccount = true
        var linkedResult = await service.SearchEmployeesAsync(new EmployeeFilterParams(HasLinkedAccount: true));
        linkedResult.TotalCount.Should().Be(1);
        linkedResult.Items.Should().ContainSingle(e => e.EmployeeId == 1);

        // 2. Filter by isEvaluationEligible = false
        var ineligibleResult = await service.SearchEmployeesAsync(new EmployeeFilterParams(IsEvaluationEligible: false));
        ineligibleResult.TotalCount.Should().Be(1);
        ineligibleResult.Items.Should().ContainSingle(e => e.EmployeeId == 2);

        // 3. Search by text "Junior"
        var textResult = await service.SearchEmployeesAsync(new EmployeeFilterParams(Search: "junior"));
        textResult.TotalCount.Should().Be(1);
        textResult.Items.Should().ContainSingle(e => e.EmployeeId == 2);
    }

    [Fact]
    public async Task LinkUserAccount_ValidationScenarios()
    {
        var (context, service) = CreateService();

        var company = new Company { CompanyId = 1, Name = "Corp" };
        var pos = new Position { PositionId = 1, Name = "Dev", NLevel = 3 };
        context.Companies.Add(company);
        context.Positions.Add(pos);

        var emp1 = new Employee { EmployeeId = 1, EmployeeNumber = "E1", FullName = "Emp 1", CompanyId = 1, PositionId = 1, HireDate = new DateOnly(2020, 1, 1) };
        var emp2 = new Employee { EmployeeId = 2, EmployeeNumber = "E2", FullName = "Emp 2", CompanyId = 1, PositionId = 1, HireDate = new DateOnly(2020, 1, 1) };

        var activeUser = new User { Id = 101, FullName = "Active User", Email = "active@meval.local", PasswordHash = "hash", IsActive = true };
        var inactiveUser = new User { Id = 102, FullName = "Inactive User", Email = "inactive@meval.local", PasswordHash = "hash", IsActive = false };

        context.Employees.AddRange(emp1, emp2);
        context.Users.AddRange(activeUser, inactiveUser);
        await context.SaveChangesAsync();

        // Scenario 1: Nonexistent user -> 404
        var (s1, err1, code1) = await service.LinkUserAccountAsync(1, 999, 1, "127.0.0.1");
        s1.Should().BeFalse();
        code1.Should().Be(404);

        // Scenario 2: Deactivated user -> 400
        var (s2, err2, code2) = await service.LinkUserAccountAsync(1, inactiveUser.Id, 1, "127.0.0.1");
        s2.Should().BeFalse();
        code2.Should().Be(400);

        // Scenario 3: Valid link -> 200
        var (s3, err3, code3) = await service.LinkUserAccountAsync(1, activeUser.Id, 1, "127.0.0.1");
        s3.Should().BeTrue();
        code3.Should().Be(200);

        // Scenario 4: Double link (linking activeUser to emp2) -> 409 Conflict
        var (s4, err4, code4) = await service.LinkUserAccountAsync(2, activeUser.Id, 1, "127.0.0.1");
        s4.Should().BeFalse();
        code4.Should().Be(409);
        err4.Should().Be("UserAlreadyLinked");

        // Verify audit log recorded
        var audit = await context.AuditLogs.FirstOrDefaultAsync(a => a.Action == "UserLinked" && a.EntityId == "1");
        audit.Should().NotBeNull();
    }

    [Fact]
    public async Task SetEvaluationEligibility_ShouldUpdateAndRecordAudit()
    {
        var (context, service) = CreateService();

        var company = new Company { CompanyId = 1, Name = "Corp" };
        var pos = new Position { PositionId = 1, Name = "Dev", NLevel = 3 };
        context.Companies.Add(company);
        context.Positions.Add(pos);

        var emp = new Employee { EmployeeId = 1, EmployeeNumber = "E1", FullName = "Emp 1", CompanyId = 1, PositionId = 1, IsEvaluationEligible = true, HireDate = new DateOnly(2020, 1, 1) };
        context.Employees.Add(emp);
        await context.SaveChangesAsync();

        var (success, error) = await service.SetEvaluationEligibilityAsync(1, false, "Probationary period", 99, "127.0.0.1");

        success.Should().BeTrue();
        var updated = await context.Employees.FindAsync(1);
        updated!.IsEvaluationEligible.Should().BeFalse();

        var audit = await context.AuditLogs.FirstOrDefaultAsync(a => a.Action == "EligibilityChanged" && a.EntityId == "1");
        audit.Should().NotBeNull();
        audit!.Details.Should().Contain("Probationary period");
    }
}
