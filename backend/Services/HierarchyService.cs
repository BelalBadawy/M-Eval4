using MEval.Api.Data;
using MEval.Api.DTOs;
using MEval.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace MEval.Api.Services;

public class HierarchyService : IHierarchyService
{
    private readonly AppDbContext _context;
    private const int MaxTraversalDepth = 100;

    public HierarchyService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<(bool HasCycle, string? CyclePath)> DetectCycleAsync(
        int employeeId,
        int candidateManagerId,
        Dictionary<int, int?>? overlaidGraph = null)
    {
        if (employeeId == candidateManagerId)
        {
            return (true, $"{employeeId} -> {candidateManagerId}");
        }

        var visited = new HashSet<int> { employeeId };
        var path = new List<int> { employeeId, candidateManagerId };
        var current = candidateManagerId;
        int depth = 0;

        while (depth++ < MaxTraversalDepth)
        {
            if (current == employeeId)
            {
                return (true, string.Join(" -> ", path));
            }

            if (!visited.Add(current))
            {
                // Loop within the chain not involving employeeId
                return (true, string.Join(" -> ", path));
            }

            int? nextManagerId = null;

            if (overlaidGraph != null && overlaidGraph.TryGetValue(current, out var overlaidManager))
            {
                nextManagerId = overlaidManager;
            }
            else
            {
                nextManagerId = await _context.Employees
                    .Where(e => e.EmployeeId == current)
                    .Select(e => e.DirectManagerId)
                    .FirstOrDefaultAsync();
            }

            if (nextManagerId == null)
            {
                // Reached root without cycle
                break;
            }

            path.Add(nextManagerId.Value);
            current = nextManagerId.Value;
        }

        return (false, null);
    }

    public async Task<(bool IsValid, string? ErrorMessage)> ValidateManagerLinkAsync(
        int employeeId,
        int managerId,
        Dictionary<int, (int CompanyId, EmploymentStatus Status, bool IsActive)>? lookup = null)
    {
        if (employeeId == managerId)
        {
            return (false, "An employee cannot be their own direct manager.");
        }

        int empCompanyId;
        int mgrCompanyId;
        EmploymentStatus mgrStatus;
        bool mgrIsActive;

        if (lookup != null && lookup.TryGetValue(employeeId, out var empInfo) && lookup.TryGetValue(managerId, out var mgrInfo))
        {
            empCompanyId = empInfo.CompanyId;
            mgrCompanyId = mgrInfo.CompanyId;
            mgrStatus = mgrInfo.Status;
            mgrIsActive = mgrInfo.IsActive;
        }
        else
        {
            var emp = await _context.Employees
                .Where(e => e.EmployeeId == employeeId)
                .Select(e => new { e.CompanyId })
                .FirstOrDefaultAsync();

            var mgr = await _context.Employees
                .Where(e => e.EmployeeId == managerId)
                .Select(e => new { e.CompanyId, e.EmploymentStatus, e.IsActive })
                .FirstOrDefaultAsync();

            if (mgr == null)
            {
                return (false, $"Manager with ID {managerId} does not exist.");
            }

            if (emp == null)
            {
                return (false, $"Employee with ID {employeeId} does not exist.");
            }

            empCompanyId = emp.CompanyId;
            mgrCompanyId = mgr.CompanyId;
            mgrStatus = mgr.EmploymentStatus;
            mgrIsActive = mgr.IsActive;
        }

        if (mgrStatus != EmploymentStatus.Active || !mgrIsActive)
        {
            return (false, "Direct manager must be an active employee.");
        }

        if (empCompanyId != mgrCompanyId)
        {
            return (false, "Direct manager must belong to the same company as the employee.");
        }

        return (true, null);
    }

    public async Task<List<ManagerChainNodeDto>> GetManagerChainAsync(int employeeId)
    {
        var chain = new List<ManagerChainNodeDto>();
        var visited = new HashSet<int> { employeeId };

        var currentEmployee = await _context.Employees
            .Where(e => e.EmployeeId == employeeId)
            .Select(e => new { e.DirectManagerId })
            .FirstOrDefaultAsync();

        if (currentEmployee?.DirectManagerId == null)
        {
            return chain;
        }

        var currentManagerId = currentEmployee.DirectManagerId;
        int depth = 1;

        while (currentManagerId.HasValue && depth <= MaxTraversalDepth)
        {
            if (!visited.Add(currentManagerId.Value))
            {
                // Defensive cycle break
                break;
            }

            var manager = await _context.Employees
                .Where(e => e.EmployeeId == currentManagerId.Value)
                .Select(e => new
                {
                    e.EmployeeId,
                    e.FullName,
                    e.PositionId,
                    PositionName = e.Position.Name,
                    e.Position.NLevel,
                    e.DirectManagerId
                })
                .FirstOrDefaultAsync();

            if (manager == null)
            {
                break;
            }

            chain.Add(new ManagerChainNodeDto(
                manager.EmployeeId,
                manager.FullName,
                manager.PositionId,
                manager.PositionName,
                manager.NLevel,
                depth
            ));

            currentManagerId = manager.DirectManagerId;
            depth++;
        }

        return chain;
    }

    public async Task<List<DirectReportDto>> GetDirectReportsAsync(int managerId)
    {
        return await _context.Employees
            .Where(e => e.DirectManagerId == managerId && e.IsActive && e.EmploymentStatus == EmploymentStatus.Active)
            .OrderBy(e => e.FullName)
            .Select(e => new DirectReportDto(
                e.EmployeeId,
                e.EmployeeNumber,
                e.FullName,
                e.PositionId,
                e.Position.Name,
                e.Position.NLevel,
                e.IsEvaluationEligible,
                e.EmploymentStatus
            ))
            .ToListAsync();
    }

    public async Task<HierarchyAnomaliesDto> GetHierarchyAnomaliesAsync(int? companyId = null)
    {
        var baseQuery = _context.Employees
            .Include(e => e.Position)
            .Include(e => e.Company)
            .Include(e => e.DirectManager)
                .ThenInclude(m => m!.Company)
            .Where(e => e.IsActive && e.EmploymentStatus == EmploymentStatus.Active);

        if (companyId.HasValue)
        {
            baseQuery = baseQuery.Where(e => e.CompanyId == companyId.Value);
        }

        var employees = await baseQuery.ToListAsync();

        var orphans = new List<OrphanEmployeeDto>();
        var rootWithManager = new List<RootWithManagerAnomalyDto>();
        var mismatches = new List<ManagerAnomalyDto>();

        foreach (var emp in employees)
        {
            // 1. Orphan: DirectManagerId == null && NLevel > 1
            if (emp.DirectManagerId == null && emp.Position.NLevel > 1)
            {
                orphans.Add(new OrphanEmployeeDto(
                    emp.EmployeeId,
                    emp.EmployeeNumber,
                    emp.FullName,
                    emp.CompanyId,
                    emp.Company.Name,
                    emp.PositionId,
                    emp.Position.Name,
                    emp.Position.NLevel
                ));
            }

            // 2. Root with Manager: DirectManagerId != null && NLevel == 1
            if (emp.DirectManagerId != null && emp.Position.NLevel == 1)
            {
                rootWithManager.Add(new RootWithManagerAnomalyDto(
                    emp.EmployeeId,
                    emp.EmployeeNumber,
                    emp.FullName,
                    emp.DirectManagerId.Value,
                    emp.DirectManager?.FullName ?? "Unknown",
                    emp.PositionId,
                    emp.Position.Name,
                    emp.Position.NLevel
                ));
            }

            // 3. Manager Mismatches (inactive, resigned, or cross-company)
            if (emp.DirectManagerId != null)
            {
                var mgr = emp.DirectManager;
                if (mgr == null)
                {
                    mismatches.Add(new ManagerAnomalyDto(
                        emp.EmployeeId,
                        emp.EmployeeNumber,
                        emp.FullName,
                        emp.DirectManagerId.Value,
                        "Unknown",
                        "Direct manager does not exist in database"
                    ));
                }
                else if (mgr.EmploymentStatus != EmploymentStatus.Active || !mgr.IsActive)
                {
                    mismatches.Add(new ManagerAnomalyDto(
                        emp.EmployeeId,
                        emp.EmployeeNumber,
                        emp.FullName,
                        mgr.EmployeeId,
                        mgr.FullName,
                        $"Direct manager is {mgr.EmploymentStatus} (IsActive: {mgr.IsActive})"
                    ));
                }
                else if (mgr.CompanyId != emp.CompanyId)
                {
                    mismatches.Add(new ManagerAnomalyDto(
                        emp.EmployeeId,
                        emp.EmployeeNumber,
                        emp.FullName,
                        mgr.EmployeeId,
                        mgr.FullName,
                        $"Direct manager belongs to Company {mgr.CompanyId} while employee is in Company {emp.CompanyId}"
                    ));
                }
            }
        }

        return new HierarchyAnomaliesDto(orphans, rootWithManager, mismatches);
    }

    public async Task<List<OrphanEmployeeDto>> GetOrphanEmployeesAsync(int? companyId = null)
    {
        var anomalies = await GetHierarchyAnomaliesAsync(companyId);
        return anomalies.Orphans;
    }
}
