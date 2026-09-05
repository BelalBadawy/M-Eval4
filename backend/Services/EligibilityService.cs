using MEval.Api.Data;
using MEval.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace MEval.Api.Services;

public class EligibilityService : IEligibilityService
{
    private readonly AppDbContext _context;
    private readonly IAuditService _auditService;

    public EligibilityService(AppDbContext context, IAuditService auditService)
    {
        _context = context;
        _auditService = auditService;
    }

    public async Task<EligibilitySummaryDto> GetSummaryAsync(int? companyId = null)
    {
        var query = _context.Employees
            .Include(e => e.Company)
            .AsNoTracking();

        if (companyId.HasValue)
        {
            query = query.Where(e => e.CompanyId == companyId.Value);
        }

        var employees = await query.ToListAsync();

        int totalEmployees = employees.Count;
        int eligibleCount = employees.Count(e => e.IsEvaluationEligible);
        int excludedCount = totalEmployees - eligibleCount;

        var companyGroups = employees
            .GroupBy(e => new { e.CompanyId, e.Company.Name })
            .OrderBy(g => g.Key.Name)
            .Select(g =>
            {
                int compTotal = g.Count();
                int compEligible = g.Count(e => e.IsEvaluationEligible);
                return new CompanyEligibilitySummaryDto(
                    g.Key.CompanyId,
                    g.Key.Name,
                    compTotal,
                    compEligible,
                    compTotal - compEligible
                );
            })
            .ToList();

        return new EligibilitySummaryDto(
            totalEmployees,
            eligibleCount,
            excludedCount,
            companyGroups
        );
    }

    public async Task<PaginatedListDto<EligibilityEmployeeDto>> SearchEmployeesAsync(
        bool? isEligible = null,
        int? companyId = null,
        int? departmentId = null,
        string? search = null,
        int pageIndex = 1,
        int pageSize = 50)
    {
        if (pageIndex < 1) pageIndex = 1;
        if (pageSize < 1) pageSize = 50;
        if (pageSize > 200) pageSize = 200;

        // Default: null -> true (eligible population per ELIG-004)
        bool effectiveIsEligible = isEligible ?? true;

        var query = _context.Employees
            .Include(e => e.Company)
            .Include(e => e.Department)
            .Include(e => e.Position)
            .AsNoTracking()
            .Where(e => e.IsEvaluationEligible == effectiveIsEligible);

        if (companyId.HasValue)
        {
            query = query.Where(e => e.CompanyId == companyId.Value);
        }

        if (departmentId.HasValue)
        {
            query = query.Where(e => e.DepartmentId == departmentId.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(e =>
                e.FullName.ToLower().Contains(term) ||
                e.EmployeeNumber.ToLower().Contains(term) ||
                (e.Email != null && e.Email.ToLower().Contains(term)));
        }

        int totalCount = await query.CountAsync();
        int totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        var items = await query
            .OrderBy(e => e.EmployeeNumber)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new EligibilityEmployeeDto(
                e.EmployeeId,
                e.EmployeeNumber,
                e.FullName,
                e.Email,
                e.CompanyId,
                e.Company.Name,
                e.DepartmentId,
                e.Department != null ? e.Department.Name : null,
                e.PositionId,
                e.Position.Name,
                e.Position.NLevel,
                e.IsEvaluationEligible,
                e.EmploymentStatus.ToString()
            ))
            .ToListAsync();

        return new PaginatedListDto<EligibilityEmployeeDto>(
            items,
            totalCount,
            pageIndex,
            pageSize,
            totalPages
        );
    }

    public async Task<PaginatedListDto<ExcludedEmployeeDto>> GetExcludedEmployeesAsync(
        int? companyId = null,
        int? departmentId = null,
        string? search = null,
        int pageIndex = 1,
        int pageSize = 50)
    {
        if (pageIndex < 1) pageIndex = 1;
        if (pageSize < 1) pageSize = 50;
        if (pageSize > 200) pageSize = 200;

        var query = _context.Employees
            .Include(e => e.Company)
            .Include(e => e.Department)
            .Include(e => e.Position)
            .AsNoTracking()
            .Where(e => !e.IsEvaluationEligible);

        if (companyId.HasValue)
        {
            query = query.Where(e => e.CompanyId == companyId.Value);
        }

        if (departmentId.HasValue)
        {
            query = query.Where(e => e.DepartmentId == departmentId.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(e =>
                e.FullName.ToLower().Contains(term) ||
                e.EmployeeNumber.ToLower().Contains(term) ||
                (e.Email != null && e.Email.ToLower().Contains(term)));
        }

        int totalCount = await query.CountAsync();
        int totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        var items = await query
            .OrderBy(e => e.EmployeeNumber)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new ExcludedEmployeeDto(
                e.EmployeeId,
                e.EmployeeNumber,
                e.FullName,
                e.Email,
                e.CompanyId,
                e.Company.Name,
                e.DepartmentId,
                e.Department != null ? e.Department.Name : null,
                e.PositionId,
                e.Position.Name,
                e.Position.NLevel,
                "hr-flag-false"
            ))
            .ToListAsync();

        return new PaginatedListDto<ExcludedEmployeeDto>(
            items,
            totalCount,
            pageIndex,
            pageSize,
            totalPages
        );
    }

    public async Task<(bool Success, BulkFlagUpdateResponse? Response, string? Error, List<int>? MissingEmployeeIds, int StatusCode)> BulkFlagUpdateAsync(
        BulkFlagUpdateRequest request,
        int actorUserId,
        string actorIpAddress)
    {
        if (request.EmployeeIds == null || request.EmployeeIds.Count == 0)
        {
            return (false, null, "At least one employee ID is required.", null, 400);
        }

        if (request.EmployeeIds.Count > 500)
        {
            return (false, null, "Batch size exceeds maximum limit of 500 employees.", null, 400);
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return (false, null, "Reason is required for bulk eligibility override.", null, 400);
        }

        var distinctIds = request.EmployeeIds.Distinct().ToList();

        // AllOrNothing validation: all requested IDs must exist in the database
        var existingEmployees = await _context.Employees
            .Where(e => distinctIds.Contains(e.EmployeeId))
            .ToListAsync();

        var foundIds = existingEmployees.Select(e => e.EmployeeId).ToHashSet();
        var missingIds = distinctIds.Where(id => !foundIds.Contains(id)).ToList();

        if (missingIds.Count > 0)
        {
            return (false, null, $"Bulk update failed: {missingIds.Count} employee ID(s) do not exist in the database.", missingIds, 400);
        }

        // Apply updates
        foreach (var emp in existingEmployees)
        {
            emp.IsEvaluationEligible = request.IsEvaluationEligible;
            emp.UpdatedAtUtc = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();

        // Write batch audit log with actor IP address
        await _auditService.LogAsync(
            actorUserId,
            "EligibilityBulkChanged",
            "Employee",
            $"{distinctIds.Count} employees",
            new
            {
                count = distinctIds.Count,
                isEvaluationEligible = request.IsEvaluationEligible,
                reason = request.Reason,
                employeeIds = distinctIds
            },
            actorIpAddress
        );

        var response = new BulkFlagUpdateResponse(
            distinctIds.Count,
            request.IsEvaluationEligible,
            $"Successfully updated evaluation eligibility for {distinctIds.Count} employees."
        );

        return (true, response, null, null, 200);
    }
}
