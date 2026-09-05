using FluentAssertions;
using MEval.Api.Data;
using MEval.Api.Models;
using MEval.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MEval.Api.Tests.Services;

public class HierarchyServiceTests
{
    private (AppDbContext Context, HierarchyService Service) CreateService()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var context = new AppDbContext(options);
        var service = new HierarchyService(context);
        return (context, service);
    }

    [Fact]
    public async Task DetectCycle_WhenSelfLoop_ShouldDetectCycle()
    {
        var (_, service) = CreateService();

        var (hasCycle, path) = await service.DetectCycleAsync(1, 1);

        hasCycle.Should().BeTrue();
        path.Should().Contain("1 -> 1");
    }

    [Fact]
    public async Task DetectCycle_WhenIndirectCycleInDatabase_ShouldDetectCycle()
    {
        var (context, service) = CreateService();

        // Seed: 2 reports to 1, 3 reports to 2
        var company = new Company { CompanyId = 1, Name = "Corp" };
        var pos = new Position { PositionId = 1, Name = "Role", NLevel = 2 };
        context.Companies.Add(company);
        context.Positions.Add(pos);

        var emp1 = new Employee { EmployeeId = 1, EmployeeNumber = "E1", FullName = "One", CompanyId = 1, PositionId = 1, DirectManagerId = null, HireDate = new DateOnly(2020, 1, 1) };
        var emp2 = new Employee { EmployeeId = 2, EmployeeNumber = "E2", FullName = "Two", CompanyId = 1, PositionId = 1, DirectManagerId = 1, HireDate = new DateOnly(2020, 1, 1) };
        var emp3 = new Employee { EmployeeId = 3, EmployeeNumber = "E3", FullName = "Three", CompanyId = 1, PositionId = 1, DirectManagerId = 2, HireDate = new DateOnly(2020, 1, 1) };

        context.Employees.AddRange(emp1, emp2, emp3);
        await context.SaveChangesAsync();

        // Now attempt to make emp1 report to emp3!
        var (hasCycle, path) = await service.DetectCycleAsync(1, 3);

        hasCycle.Should().BeTrue();
        path.Should().NotBeNull();
    }

    [Fact]
    public async Task DetectCycle_WhenCyclePassesThroughOverlaidGraph_ShouldDetectCycle()
    {
        var (context, service) = CreateService();

        // DB: 2 reports to 3
        var company = new Company { CompanyId = 1, Name = "Corp" };
        var pos = new Position { PositionId = 1, Name = "Role", NLevel = 2 };
        context.Companies.Add(company);
        context.Positions.Add(pos);

        var emp2 = new Employee { EmployeeId = 2, EmployeeNumber = "E2", FullName = "Two", CompanyId = 1, PositionId = 1, DirectManagerId = 3, HireDate = new DateOnly(2020, 1, 1) };
        var emp3 = new Employee { EmployeeId = 3, EmployeeNumber = "E3", FullName = "Three", CompanyId = 1, PositionId = 1, DirectManagerId = null, HireDate = new DateOnly(2020, 1, 1) };
        context.Employees.AddRange(emp2, emp3);
        await context.SaveChangesAsync();

        // Overlaid graph has: 3 reports to 1 (in file)
        var overlaid = new Dictionary<int, int?>
        {
            [3] = 1
        };

        // If 1 reports to 2: 1 -> 2 -> 3 -> 1 (Cycle!)
        var (hasCycle, _) = await service.DetectCycleAsync(1, 2, overlaid);

        hasCycle.Should().BeTrue();
    }

    [Fact]
    public async Task GetManagerChain_ShouldTraverseToRoot_AndReturnCorrectDepthAndNLevel()
    {
        var (context, service) = CreateService();

        var company = new Company { CompanyId = 1, Name = "Corp" };
        var posCeo = new Position { PositionId = 10, Name = "CEO", NLevel = 1 };
        var posVp = new Position { PositionId = 20, Name = "VP", NLevel = 2 };
        var posMgr = new Position { PositionId = 30, Name = "Manager", NLevel = 3 };
        var posDev = new Position { PositionId = 40, Name = "Dev", NLevel = 4 };

        context.Companies.Add(company);
        context.Positions.AddRange(posCeo, posVp, posMgr, posDev);

        var ceo = new Employee { EmployeeId = 1, EmployeeNumber = "E1", FullName = "CEO", CompanyId = 1, PositionId = 10, DirectManagerId = null, HireDate = new DateOnly(2020, 1, 1) };
        var vp = new Employee { EmployeeId = 2, EmployeeNumber = "E2", FullName = "VP", CompanyId = 1, PositionId = 20, DirectManagerId = 1, HireDate = new DateOnly(2020, 1, 1) };
        var mgr = new Employee { EmployeeId = 3, EmployeeNumber = "E3", FullName = "Manager", CompanyId = 1, PositionId = 30, DirectManagerId = 2, HireDate = new DateOnly(2020, 1, 1) };
        var dev = new Employee { EmployeeId = 4, EmployeeNumber = "E4", FullName = "Developer", CompanyId = 1, PositionId = 40, DirectManagerId = 3, HireDate = new DateOnly(2020, 1, 1) };

        context.Employees.AddRange(ceo, vp, mgr, dev);
        await context.SaveChangesAsync();

        var chain = await service.GetManagerChainAsync(dev.EmployeeId);

        chain.Should().HaveCount(3);
        chain[0].EmployeeId.Should().Be(mgr.EmployeeId);
        chain[0].PositionName.Should().Be("Manager");
        chain[0].NLevel.Should().Be(3);
        chain[0].Depth.Should().Be(1);

        chain[1].EmployeeId.Should().Be(vp.EmployeeId);
        chain[1].PositionName.Should().Be("VP");
        chain[1].NLevel.Should().Be(2);
        chain[1].Depth.Should().Be(2);

        chain[2].EmployeeId.Should().Be(ceo.EmployeeId);
        chain[2].PositionName.Should().Be("CEO");
        chain[2].NLevel.Should().Be(1);
        chain[2].Depth.Should().Be(3);
    }

    [Fact]
    public async Task ValidateManagerLink_ShouldRejectCrossCompanyAndInactiveManagers()
    {
        var (context, service) = CreateService();

        var comp1 = new Company { CompanyId = 1, Name = "Comp 1" };
        var comp2 = new Company { CompanyId = 2, Name = "Comp 2" };
        var pos = new Position { PositionId = 1, Name = "Role", NLevel = 2 };
        context.Companies.AddRange(comp1, comp2);
        context.Positions.Add(pos);

        var emp = new Employee { EmployeeId = 1, EmployeeNumber = "E1", FullName = "Emp", CompanyId = 1, PositionId = 1, HireDate = new DateOnly(2020, 1, 1) };
        var crossMgr = new Employee { EmployeeId = 2, EmployeeNumber = "E2", FullName = "Cross Mgr", CompanyId = 2, PositionId = 1, HireDate = new DateOnly(2020, 1, 1) };
        var inactiveMgr = new Employee { EmployeeId = 3, EmployeeNumber = "E3", FullName = "Inactive Mgr", CompanyId = 1, PositionId = 1, IsActive = false, HireDate = new DateOnly(2020, 1, 1) };

        context.Employees.AddRange(emp, crossMgr, inactiveMgr);
        await context.SaveChangesAsync();

        // 1. Cross company
        var (crossValid, crossErr) = await service.ValidateManagerLinkAsync(emp.EmployeeId, crossMgr.EmployeeId);
        crossValid.Should().BeFalse();
        crossErr.Should().Contain("same company");

        // 2. Inactive
        var (inactValid, inactErr) = await service.ValidateManagerLinkAsync(emp.EmployeeId, inactiveMgr.EmployeeId);
        inactValid.Should().BeFalse();
        inactErr.Should().Contain("active");
    }

    [Fact]
    public async Task HierarchyAnomalies_ShouldDetectOrphans_RootWithManager_AndStatusMismatches()
    {
        var (context, service) = CreateService();

        var company = new Company { CompanyId = 1, Name = "Corp" };
        var posCeo = new Position { PositionId = 1, Name = "CEO", NLevel = 1 };
        var posStaff = new Position { PositionId = 2, Name = "Staff", NLevel = 3 };

        context.Companies.Add(company);
        context.Positions.AddRange(posCeo, posStaff);

        // 1. Normal Root: NLevel = 1, DirectManagerId = null -> NOT AN ORPHAN
        var ceo = new Employee { EmployeeId = 1, EmployeeNumber = "E1", FullName = "Legit CEO", CompanyId = 1, PositionId = 1, DirectManagerId = null, HireDate = new DateOnly(2020, 1, 1) };

        // 2. Orphan: NLevel = 3, DirectManagerId = null -> ORPHAN!
        var orphan = new Employee { EmployeeId = 2, EmployeeNumber = "E2", FullName = "Orphan Worker", CompanyId = 1, PositionId = 2, DirectManagerId = null, HireDate = new DateOnly(2020, 1, 1) };

        // 3. Root With Manager Anomaly: NLevel = 1, DirectManagerId = 1 -> ROOT WITH MANAGER!
        var rogueCeo = new Employee { EmployeeId = 3, EmployeeNumber = "E3", FullName = "CEO With Boss", CompanyId = 1, PositionId = 1, DirectManagerId = 1, HireDate = new DateOnly(2020, 1, 1) };

        // 4. Inactive manager mismatch: reports to resigned manager
        var resignedMgr = new Employee { EmployeeId = 4, EmployeeNumber = "E4", FullName = "Resigned Mgr", CompanyId = 1, PositionId = 2, DirectManagerId = 1, EmploymentStatus = EmploymentStatus.Resigned, ResignationDate = new DateOnly(2024, 1, 1), HireDate = new DateOnly(2020, 1, 1) };
        var subordinate = new Employee { EmployeeId = 5, EmployeeNumber = "E5", FullName = "Subordinate", CompanyId = 1, PositionId = 2, DirectManagerId = 4, HireDate = new DateOnly(2020, 1, 1) };

        context.Employees.AddRange(ceo, orphan, rogueCeo, resignedMgr, subordinate);
        await context.SaveChangesAsync();

        var anomalies = await service.GetHierarchyAnomaliesAsync(company.CompanyId);

        // Verify Orphans
        anomalies.Orphans.Should().ContainSingle(o => o.EmployeeId == orphan.EmployeeId);
        anomalies.Orphans.Any(o => o.EmployeeId == ceo.EmployeeId).Should().BeFalse();

        // Verify Root with Manager Anomaly
        anomalies.RootWithManager.Should().ContainSingle(r => r.EmployeeId == rogueCeo.EmployeeId && r.DirectManagerId == ceo.EmployeeId);

        // Verify Mismatches (subordinate reporting to resigned manager)
        anomalies.Mismatches.Should().Contain(m => m.EmployeeId == subordinate.EmployeeId && m.DirectManagerId == resignedMgr.EmployeeId);
    }
}
