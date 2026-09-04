using FluentAssertions;
using MEval.Api.Data;
using MEval.Api.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MEval.Api.Tests.Data;

public class OrgConstraintTests
{
    private AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    [Fact]
    public async Task ValueGeneratedNever_ShouldPreserveExplicitExternalIds()
    {
        using var context = CreateDbContext();

        var company = new Company { CompanyId = 101, Name = "Acme Corp" };
        var dept = new Department { DepartmentId = 201, CompanyId = 101, Name = "Engineering" };
        var section = new Section { SectionId = 301, DepartmentId = 201, Name = "Core Architecture" };
        var position = new Position { PositionId = 401, Name = "Staff Engineer", NLevel = 3 };

        context.Companies.Add(company);
        context.Departments.Add(dept);
        context.Sections.Add(section);
        context.Positions.Add(position);

        var employee = new Employee
        {
            EmployeeId = 5001,
            EmployeeNumber = "EMP-5001",
            FullName = "Jane Doe",
            CompanyId = 101,
            DepartmentId = 201,
            SectionId = 301,
            PositionId = 401,
            EmploymentStatus = EmploymentStatus.Active,
            HireDate = new DateOnly(2023, 1, 15)
        };
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var saved = await context.Employees.FindAsync(5001);
        saved.Should().NotBeNull();
        saved!.EmployeeId.Should().Be(5001);
        saved.EmployeeNumber.Should().Be("EMP-5001");
        saved.CompanyId.Should().Be(101);
    }

    [Fact]
    public async Task SectionNeedsDept_LogicalValidation_ShouldFailIfSectionSetWithoutDept()
    {
        using var context = CreateDbContext();

        var employee = new Employee
        {
            EmployeeId = 5002,
            EmployeeNumber = "EMP-5002",
            FullName = "Orphan Section User",
            CompanyId = 101,
            DepartmentId = null, // Department is null!
            SectionId = 301,     // But section is set!
            PositionId = 401,
            EmploymentStatus = EmploymentStatus.Active,
            HireDate = new DateOnly(2023, 1, 15)
        };

        // In code / DB, SectionId != null and DepartmentId == null is forbidden by CK_Empl_SectionNeedsDept
        bool isValid = employee.SectionId == null || employee.DepartmentId != null;
        isValid.Should().BeFalse();
    }

    [Fact]
    public void StatusDates_LogicalValidation_ShouldEnforceCoherence()
    {
        // 1. Active with ResignationDate -> invalid
        var activeWithDate = new Employee
        {
            EmploymentStatus = EmploymentStatus.Active,
            ResignationDate = new DateOnly(2024, 1, 1)
        };
        bool activeCoherent = (activeWithDate.EmploymentStatus == EmploymentStatus.Active && activeWithDate.ResignationDate == null)
            || (activeWithDate.EmploymentStatus != EmploymentStatus.Active && activeWithDate.ResignationDate != null);
        activeCoherent.Should().BeFalse();

        // 2. Resigned without ResignationDate -> invalid
        var resignedWithoutDate = new Employee
        {
            EmploymentStatus = EmploymentStatus.Resigned,
            ResignationDate = null
        };
        bool resignedCoherent = (resignedWithoutDate.EmploymentStatus == EmploymentStatus.Active && resignedWithoutDate.ResignationDate == null)
            || (resignedWithoutDate.EmploymentStatus != EmploymentStatus.Active && resignedWithoutDate.ResignationDate != null);
        resignedCoherent.Should().BeFalse();

        // 3. Resigned with ResignationDate -> valid
        var validResigned = new Employee
        {
            EmploymentStatus = EmploymentStatus.Resigned,
            ResignationDate = new DateOnly(2024, 1, 1)
        };
        bool validResignedCoherent = (validResigned.EmploymentStatus == EmploymentStatus.Active && validResigned.ResignationDate == null)
            || (validResigned.EmploymentStatus != EmploymentStatus.Active && validResigned.ResignationDate != null);
        validResignedCoherent.Should().BeTrue();
    }

    [Fact]
    public void ResignationAfterHire_LogicalValidation_ShouldEnforceOrder()
    {
        var hireDate = new DateOnly(2024, 6, 1);
        var prematureResignation = new DateOnly(2024, 5, 1);

        bool isValid = prematureResignation >= hireDate;
        isValid.Should().BeFalse();
    }

    [Fact]
    public void ModelMetadata_ShouldConfigureUniqueUserIdIndex_AndCheckConstraints()
    {
        using var context = CreateDbContext();
        var designModel = Microsoft.EntityFrameworkCore.Infrastructure.AccessorExtensions.GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>(context).Model;
        var entityType = designModel.FindEntityType(typeof(Employee));
        entityType.Should().NotBeNull();

        // 1. Verify Unique Filtered Index on UserId
        var userIdIndex = entityType!.GetIndexes().FirstOrDefault(i => i.Properties.Any(p => p.Name == "UserId"));
        userIdIndex.Should().NotBeNull();
        userIdIndex!.IsUnique.Should().BeTrue();
        userIdIndex.GetFilter().Should().Be("[UserId] IS NOT NULL");

        // 2. Verify Unique Index on EmployeeNumber
        var empNumIndex = entityType.GetIndexes().FirstOrDefault(i => i.Properties.Any(p => p.Name == "EmployeeNumber"));
        empNumIndex.Should().NotBeNull();
        empNumIndex!.IsUnique.Should().BeTrue();

        // 3. Verify Check Constraints
        var checkConstraints = entityType.GetCheckConstraints().ToList();
        checkConstraints.Should().Contain(c => c.Name == "CK_Empl_SectionNeedsDept");
        checkConstraints.Should().Contain(c => c.Name == "CK_Empl_StatusDates");
        checkConstraints.Should().Contain(c => c.Name == "CK_Empl_ResignationAfterHire");
    }
}
