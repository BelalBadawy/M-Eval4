using ClosedXML.Excel;
using FluentAssertions;
using MEval.Api.Configuration;
using MEval.Api.Data;
using MEval.Api.Models;
using MEval.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace MEval.Api.Tests.Services;

public class OrgImportServiceTests
{
    private (AppDbContext Context, OrgImportService Service, ITokenService TokenService) CreateService()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var context = new AppDbContext(options);
        var jwtSettings = Options.Create(new JwtSettings
        {
            SecretKey = "TestSecretKeyThatIsAtLeast32BytesLongForSecurity!"
        });

        var tokenService = new TokenService(context, jwtSettings);
        var auditService = new AuditService(context);
        var hierarchyService = new HierarchyService(context);
        var service = new OrgImportService(context, hierarchyService, tokenService, auditService);

        return (context, service, tokenService);
    }

    private MemoryStream CreateValidWorkbookStream(Action<XLWorkbook>? customize = null)
    {
        var wb = new XLWorkbook();

        // 1. Companies
        var wsC = wb.Worksheets.Add("Companies");
        wsC.Cell(1, 1).Value = "CompanyId";
        wsC.Cell(1, 2).Value = "Name";
        wsC.Cell(2, 1).Value = 1;
        wsC.Cell(2, 2).Value = "Acme Global Operations";

        // 2. Departments
        var wsD = wb.Worksheets.Add("Departments");
        wsD.Cell(1, 1).Value = "DepartmentId";
        wsD.Cell(1, 2).Value = "CompanyId";
        wsD.Cell(1, 3).Value = "Name";
        wsD.Cell(2, 1).Value = 10;
        wsD.Cell(2, 2).Value = 1;
        wsD.Cell(2, 3).Value = "Information Technology";

        // 3. Sections
        var wsS = wb.Worksheets.Add("Sections");
        wsS.Cell(1, 1).Value = "SectionId";
        wsS.Cell(1, 2).Value = "DepartmentId";
        wsS.Cell(1, 3).Value = "Name";
        wsS.Cell(2, 1).Value = 100;
        wsS.Cell(2, 2).Value = 10;
        wsS.Cell(2, 3).Value = "Software Architecture";

        // 4. Positions
        var wsP = wb.Worksheets.Add("Positions");
        wsP.Cell(1, 1).Value = "PositionId";
        wsP.Cell(1, 2).Value = "Name";
        wsP.Cell(1, 3).Value = "NLevel";
        wsP.Cell(2, 1).Value = 500;
        wsP.Cell(2, 2).Value = "Chief Executive Officer";
        wsP.Cell(2, 3).Value = 1;
        wsP.Cell(3, 1).Value = 501;
        wsP.Cell(3, 2).Value = "Principal Architect";
        wsP.Cell(3, 3).Value = 2;

        // 5. Employees
        var wsE = wb.Worksheets.Add("Employees");
        string[] headers =
        {
            "EmployeeId", "EmployeeNumber", "FullName", "Email",
            "CompanyId", "CompanyName", "DepartmentId", "DepartmentName",
            "SectionId", "SectionName", "PositionId", "PositionName", "NLevel",
            "ManagerEmployeeId", "EmploymentStatus", "HireDate", "ResignationDate"
        };
        for (int i = 0; i < headers.Length; i++)
        {
            wsE.Cell(1, i + 1).Value = headers[i];
        }

        // Row 2: Top executive CEO (NLevel 1, no manager)
        wsE.Cell(2, 1).Value = 1000;
        wsE.Cell(2, 2).Value = "EMP-1000";
        wsE.Cell(2, 3).Value = "Chief Executive";
        wsE.Cell(2, 4).Value = "ceo@meval.local";
        wsE.Cell(2, 5).Value = 1;
        wsE.Cell(2, 6).Value = "Acme Global Operations";
        wsE.Cell(2, 7).Value = 10;
        wsE.Cell(2, 8).Value = "Information Technology";
        wsE.Cell(2, 9).Value = 100;
        wsE.Cell(2, 10).Value = "Software Architecture";
        wsE.Cell(2, 11).Value = 500;
        wsE.Cell(2, 12).Value = "Chief Executive Officer";
        wsE.Cell(2, 13).Value = 1;
        wsE.Cell(2, 14).Value = "";
        wsE.Cell(2, 15).Value = 1; // Active
        wsE.Cell(2, 16).Value = "2020-01-01";
        wsE.Cell(2, 17).Value = "";

        // Row 3: Staff Architect (NLevel 2, reporting to CEO)
        wsE.Cell(3, 1).Value = 1001;
        wsE.Cell(3, 2).Value = "EMP-1001";
        wsE.Cell(3, 3).Value = "Alice Smith";
        wsE.Cell(3, 4).Value = "alice.smith@meval.local";
        wsE.Cell(3, 5).Value = 1;
        wsE.Cell(3, 6).Value = "Acme Global Operations";
        wsE.Cell(3, 7).Value = 10;
        wsE.Cell(3, 8).Value = "Information Technology";
        wsE.Cell(3, 9).Value = 100;
        wsE.Cell(3, 10).Value = "Software Architecture";
        wsE.Cell(3, 11).Value = 501;
        wsE.Cell(3, 12).Value = "Principal Architect";
        wsE.Cell(3, 13).Value = 2;
        wsE.Cell(3, 14).Value = 1000;
        wsE.Cell(3, 15).Value = 1; // Active
        wsE.Cell(3, 16).Value = "2021-06-01";
        wsE.Cell(3, 17).Value = "";

        customize?.Invoke(wb);

        var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;
        return ms;
    }

    [Fact]
    public void GenerateTemplate_ShouldReturnValid5SheetWorkbook()
    {
        var (_, service, _) = CreateService();

        var bytes = service.GenerateTemplate();
        bytes.Should().NotBeEmpty();

        using var ms = new MemoryStream(bytes);
        using var wb = new XLWorkbook(ms);

        wb.Worksheets.Should().Contain(ws => ws.Name == "Companies");
        wb.Worksheets.Should().Contain(ws => ws.Name == "Departments");
        wb.Worksheets.Should().Contain(ws => ws.Name == "Sections");
        wb.Worksheets.Should().Contain(ws => ws.Name == "Positions");
        wb.Worksheets.Should().Contain(ws => ws.Name == "Employees");
    }

    [Fact]
    public async Task DryRun_WithValidWorkbook_ShouldSucceedWithZeroErrors()
    {
        var (_, service, _) = CreateService();
        using var stream = CreateValidWorkbookStream();

        var (success, result, error, status) = await service.DryRunAsync(stream, "valid.xlsx", stream.Length, 1);

        status.Should().Be(200);
        success.Should().BeTrue();
        result.Should().NotBeNull();
        result!.IsValid.Should().BeTrue();
        result.ErrorCount.Should().Be(0);
        result.Summary.CompaniesCount.Should().Be(1);
        result.Summary.DepartmentsCount.Should().Be(1);
        result.Summary.SectionsCount.Should().Be(1);
        result.Summary.PositionsCount.Should().Be(2);
        result.Summary.EmployeesTotal.Should().Be(2);
    }

    [Fact]
    public async Task DryRun_LookupReParenting_ShouldBeRejectedAsRowError()
    {
        var (context, service, _) = CreateService();

        // Seed DB with Department 10 belonging to Company 1
        context.Companies.Add(new Company { CompanyId = 1, Name = "Original Company" });
        context.Companies.Add(new Company { CompanyId = 2, Name = "Target Company" });
        context.Departments.Add(new Department { DepartmentId = 10, CompanyId = 1, Name = "IT Dept" });
        await context.SaveChangesAsync();

        // Import attempts to change Department 10's CompanyId to 2
        using var stream = CreateValidWorkbookStream(wb =>
        {
            var wsD = wb.Worksheet("Departments");
            wsD.Cell(2, 2).Value = 2; // Re-parenting to Company 2!
        });

        var (success, result, error, status) = await service.DryRunAsync(stream, "reparent.xlsx", stream.Length, 1);

        status.Should().Be(200);
        success.Should().BeTrue();
        result!.IsValid.Should().BeFalse();
        result.ErrorCount.Should().BeGreaterThan(0);
        result.Summary.Errors.Should().Contain(e =>
            e.SheetName == "Departments" &&
            e.Reason.Contains("Re-parenting") &&
            e.Reason.Contains("forbidden"));
    }

    [Fact]
    public async Task Execute_LookupRename_ShouldSucceedAndPropagateNewName()
    {
        var (context, service, _) = CreateService();

        // Seed DB with existing lookup names
        context.Companies.Add(new Company { CompanyId = 1, Name = "Old Company Name" });
        await context.SaveChangesAsync();

        using var stream = CreateValidWorkbookStream(wb =>
        {
            var wsC = wb.Worksheet("Companies");
            wsC.Cell(2, 2).Value = "Brand New Company Name";
            var wsE = wb.Worksheet("Employees");
            wsE.Cell(2, 6).Value = "Brand New Company Name";
            wsE.Cell(3, 6).Value = "Brand New Company Name";
        });

        var (success, response, error, status) = await service.ExecuteAsync(stream, "rename.xlsx", stream.Length, 1, "127.0.0.1");

        status.Should().Be(200);
        success.Should().BeTrue();
        response!.Success.Should().BeTrue();

        var updatedCompany = await context.Companies.FindAsync(1);
        updatedCompany!.Name.Should().Be("Brand New Company Name");
    }

    [Fact]
    public async Task DryRun_EmployeeIdWithDifferentNumber_ShouldBeRejected()
    {
        var (context, service, _) = CreateService();

        // Seed employee 1000 with EMP-1000
        context.Companies.Add(new Company { CompanyId = 1, Name = "Acme Global Operations" });
        context.Positions.Add(new Position { PositionId = 500, Name = "CEO", NLevel = 1 });
        context.Employees.Add(new Employee
        {
            EmployeeId = 1000,
            EmployeeNumber = "EMP-1000",
            FullName = "Chief Executive",
            CompanyId = 1,
            PositionId = 500,
            EmploymentStatus = EmploymentStatus.Active,
            HireDate = new DateOnly(2020, 1, 1)
        });
        await context.SaveChangesAsync();

        // Import attempts to update EmployeeId 1000 with a DIFFERENT EmployeeNumber "EMP-DIFFERENT"
        using var stream = CreateValidWorkbookStream(wb =>
        {
            var wsE = wb.Worksheet("Employees");
            wsE.Cell(2, 2).Value = "EMP-DIFFERENT";
        });

        var (success, result, _, status) = await service.DryRunAsync(stream, "diff-number.xlsx", stream.Length, 1);

        status.Should().Be(200);
        result!.IsValid.Should().BeFalse();
        result.Summary.Errors.Should().Contain(e =>
            e.SheetName == "Employees" &&
            e.Reason.Contains("immutable EmployeeNumber"));
    }

    [Fact]
    public async Task DryRun_InformationalColumnMismatch_ShouldBeRejected()
    {
        var (_, service, _) = CreateService();

        // Provide NLevel 99 in informational column while Position 500 has NLevel 1
        using var stream = CreateValidWorkbookStream(wb =>
        {
            var wsE = wb.Worksheet("Employees");
            wsE.Cell(2, 13).Value = 99; // Mismatch with Position 500 NLevel
        });

        var (success, result, _, status) = await service.DryRunAsync(stream, "info-mismatch.xlsx", stream.Length, 1);

        status.Should().Be(200);
        result!.IsValid.Should().BeFalse();
        result.Summary.Errors.Should().Contain(e =>
            e.SheetName == "Employees" &&
            e.Reason.Contains("Informational NLevel 99 does not match Position 500 (NLevel 1)"));
    }

    [Fact]
    public async Task DryRun_OverlaidCycleDetection_ShouldBeRejected()
    {
        var (context, service, _) = CreateService();

        // DB: Employee 200 reports to Employee 1000
        context.Companies.Add(new Company { CompanyId = 1, Name = "Acme" });
        context.Positions.Add(new Position { PositionId = 500, Name = "Lead", NLevel = 2 });
        context.Employees.Add(new Employee
        {
            EmployeeId = 200,
            EmployeeNumber = "EMP-200",
            FullName = "Middle Lead",
            CompanyId = 1,
            PositionId = 500,
            DirectManagerId = 1000, // 200 -> 1000
            EmploymentStatus = EmploymentStatus.Active,
            HireDate = new DateOnly(2020, 1, 1)
        });
        await context.SaveChangesAsync();

        // File: Employee 1000 reports to Employee 200 -> Cycle! (1000 -> 200 -> 1000)
        using var stream = CreateValidWorkbookStream(wb =>
        {
            var wsE = wb.Worksheet("Employees");
            wsE.Cell(2, 14).Value = 200; // 1000 reports to 200
        });

        var (success, result, _, status) = await service.DryRunAsync(stream, "cycle.xlsx", stream.Length, 1);

        status.Should().Be(200);
        result!.IsValid.Should().BeFalse();
        result.Summary.Errors.Should().Contain(e =>
            e.SheetName == "Employees" &&
            e.Reason.Contains("Cyclical reporting relationship detected"));
    }

    [Fact]
    public async Task Execute_ManagerDefinedAfterSubordinateInFile_ShouldSucceedInSinglePass()
    {
        var (context, service, _) = CreateService();

        // Subordinate on row 2, Manager on row 3
        using var stream = CreateValidWorkbookStream(wb =>
        {
            var wsE = wb.Worksheet("Employees");

            // Swap order of rows 2 and 3: put row 3 (subordinate 1001) first, and row 2 (manager 1000) second
            // Subordinate: 1001 reports to 1000
            wsE.Cell(2, 1).Value = 1001;
            wsE.Cell(2, 2).Value = "EMP-1001";
            wsE.Cell(2, 3).Value = "Alice Subordinate";
            wsE.Cell(2, 4).Value = "alice@meval.local";
            wsE.Cell(2, 5).Value = 1;
            wsE.Cell(2, 6).Value = "Acme Global Operations";
            wsE.Cell(2, 7).Value = 10;
            wsE.Cell(2, 8).Value = "Information Technology";
            wsE.Cell(2, 9).Value = 100;
            wsE.Cell(2, 10).Value = "Software Architecture";
            wsE.Cell(2, 11).Value = 501;
            wsE.Cell(2, 12).Value = "Principal Architect";
            wsE.Cell(2, 13).Value = 2;
            wsE.Cell(2, 14).Value = 1000; // Manager defined on NEXT row!
            wsE.Cell(2, 15).Value = 1;
            wsE.Cell(2, 16).Value = "2021-01-01";
            wsE.Cell(2, 17).Value = "";

            // Manager: 1000
            wsE.Cell(3, 1).Value = 1000;
            wsE.Cell(3, 2).Value = "EMP-1000";
            wsE.Cell(3, 3).Value = "Bob Manager";
            wsE.Cell(3, 4).Value = "bob@meval.local";
            wsE.Cell(3, 5).Value = 1;
            wsE.Cell(3, 6).Value = "Acme Global Operations";
            wsE.Cell(3, 7).Value = 10;
            wsE.Cell(3, 8).Value = "Information Technology";
            wsE.Cell(3, 9).Value = 100;
            wsE.Cell(3, 10).Value = "Software Architecture";
            wsE.Cell(3, 11).Value = 500;
            wsE.Cell(3, 12).Value = "Chief Executive Officer";
            wsE.Cell(3, 13).Value = 1;
            wsE.Cell(3, 14).Value = "";
            wsE.Cell(3, 15).Value = 1;
            wsE.Cell(3, 16).Value = "2020-01-01";
            wsE.Cell(3, 17).Value = "";
        });

        var (success, response, error, status) = await service.ExecuteAsync(stream, "order.xlsx", stream.Length, 1, "127.0.0.1");

        status.Should().Be(200);
        success.Should().BeTrue();

        var subordinate = await context.Employees.FindAsync(1001);
        subordinate.Should().NotBeNull();
        subordinate!.DirectManagerId.Should().Be(1000);
    }

    [Fact]
    public async Task Execute_OffboardingCascade_ShouldDeactivateLinkedUserAndRevokeSession()
    {
        var (context, service, tokenService) = CreateService();

        // Create linked active user with a refresh token
        var linkedUser = new User
        {
            Id = 55,
            FullName = "Alice Smith",
            Email = "alice.smith@meval.local",
            PasswordHash = "hash",
            IsActive = true
        };
        context.Users.Add(linkedUser);

        // Active refresh token
        var token = new RefreshToken
        {
            UserId = 55,
            TokenHash = "tokenhash123",
            ExpiresAtUtc = DateTime.UtcNow.AddDays(7),
            CreatedAtUtc = DateTime.UtcNow
        };
        context.RefreshTokens.Add(token);

        // Pre-existing active employee in DB linked to user 55
        context.Companies.Add(new Company { CompanyId = 1, Name = "Acme Global Operations" });
        context.Departments.Add(new Department { DepartmentId = 10, CompanyId = 1, Name = "Information Technology" });
        context.Sections.Add(new Section { SectionId = 100, DepartmentId = 10, Name = "Software Architecture" });
        context.Positions.Add(new Position { PositionId = 500, Name = "Chief Executive Officer", NLevel = 1 });
        context.Positions.Add(new Position { PositionId = 501, Name = "Principal Architect", NLevel = 2 });

        context.Employees.Add(new Employee
        {
            EmployeeId = 1001,
            EmployeeNumber = "EMP-1001",
            FullName = "Alice Smith",
            Email = "alice.smith@meval.local",
            CompanyId = 1,
            DepartmentId = 10,
            SectionId = 100,
            PositionId = 501,
            EmploymentStatus = EmploymentStatus.Active,
            HireDate = new DateOnly(2021, 6, 1),
            UserId = 55,
            IsEvaluationEligible = true,
            IsActive = true
        });
        await context.SaveChangesAsync();

        // Import file changes Alice's status to Resigned (2) with ResignationDate
        using var stream = CreateValidWorkbookStream(wb =>
        {
            var wsE = wb.Worksheet("Employees");
            wsE.Cell(3, 15).Value = 2; // Resigned
            wsE.Cell(3, 17).Value = "2026-09-01"; // ResignationDate
        });

        var (success, response, error, status) = await service.ExecuteAsync(stream, "offboard.xlsx", stream.Length, 1, "127.0.0.1");

        status.Should().Be(200);
        success.Should().BeTrue();
        response!.Summary.OffboardedCascadeCount.Should().Be(1);

        // Verify linked user is deactivated
        var refreshedUser = await context.Users.FindAsync(55);
        refreshedUser!.IsActive.Should().BeFalse();

        // Verify refresh token is revoked with RevokedReason.EmployeeOffboarded
        var refreshedToken = await context.RefreshTokens.FirstOrDefaultAsync(t => t.UserId == 55);
        refreshedToken!.RevokedAtUtc.Should().NotBeNull();
        refreshedToken.RevokedReason.Should().Be(RevokeReasons.EmployeeOffboarded);

        // Verify Audit log contains EmployeeOffboarded
        var audit = await context.AuditLogs.FirstOrDefaultAsync(a => a.Action == "EmployeeOffboarded" && a.EntityId == "1001");
        audit.Should().NotBeNull();
    }

    [Fact]
    public async Task Execute_ShouldPreserveLocalColumns_IsEvaluationEligible_UserId_IsActive()
    {
        var (context, service, _) = CreateService();

        // Pre-existing employee with custom local values
        context.Companies.Add(new Company { CompanyId = 1, Name = "Acme Global Operations" });
        context.Departments.Add(new Department { DepartmentId = 10, CompanyId = 1, Name = "Information Technology" });
        context.Sections.Add(new Section { SectionId = 100, DepartmentId = 10, Name = "Software Architecture" });
        context.Positions.Add(new Position { PositionId = 500, Name = "Chief Executive Officer", NLevel = 1 });
        context.Positions.Add(new Position { PositionId = 501, Name = "Principal Architect", NLevel = 2 });

        context.Employees.Add(new Employee
        {
            EmployeeId = 1001,
            EmployeeNumber = "EMP-1001",
            FullName = "Alice Original",
            Email = "alice.smith@meval.local",
            CompanyId = 1,
            DepartmentId = 10,
            SectionId = 100,
            PositionId = 501,
            EmploymentStatus = EmploymentStatus.Active,
            HireDate = new DateOnly(2021, 6, 1),
            UserId = 99,
            IsEvaluationEligible = false, // Local customization
            IsActive = false              // Local data-correction flag
        });
        await context.SaveChangesAsync();

        using var stream = CreateValidWorkbookStream(wb =>
        {
            var wsE = wb.Worksheet("Employees");
            wsE.Cell(3, 3).Value = "Alice Name Updated In HR";
        });

        var (success, response, error, status) = await service.ExecuteAsync(stream, "update.xlsx", stream.Length, 1, "127.0.0.1");

        status.Should().Be(200);
        success.Should().BeTrue();

        var emp = await context.Employees.FindAsync(1001);
        emp!.FullName.Should().Be("Alice Name Updated In HR");
        // Local columns must NOT be overwritten
        emp.UserId.Should().Be(99);
        emp.IsEvaluationEligible.Should().BeFalse();
        emp.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task ConcurrentImport_WhenLockHeld_ShouldReturn409Conflict()
    {
        var (_, service, _) = CreateService();

        // Acquire the static semaphore lock directly via reflection or task simulation
        var lockField = typeof(OrgImportService).GetField("_importLock", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var semaphore = (SemaphoreSlim)lockField!.GetValue(null)!;

        await semaphore.WaitAsync();
        try
        {
            using var stream = CreateValidWorkbookStream();
            var (success, result, error, status) = await service.DryRunAsync(stream, "test.xlsx", stream.Length, 1);

            status.Should().Be(409);
            success.Should().BeFalse();
            error.Should().Contain("currently in progress");
        }
        finally
        {
            semaphore.Release();
        }
    }
}
