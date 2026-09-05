using MEval.Api.Data;
using MEval.Api.DTOs;
using MEval.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace MEval.Api.Services;

public class EmployeeService : IEmployeeService
{
    private readonly AppDbContext _context;
    private readonly IAuditService _auditService;

    public EmployeeService(AppDbContext context, IAuditService auditService)
    {
        _context = context;
        _auditService = auditService;
    }

    public async Task<PaginatedListDto<EmployeeSummaryDto>> SearchEmployeesAsync(EmployeeFilterParams filters)
    {
        var query = _context.Employees
            .Include(e => e.Company)
            .Include(e => e.Department)
            .Include(e => e.Position)
            .Include(e => e.DirectManager)
            .AsQueryable();

        // 1. Text Search
        if (!string.IsNullOrWhiteSpace(filters.Search))
        {
            var term = filters.Search.Trim().ToLower();
            query = query.Where(e =>
                e.FullName.ToLower().Contains(term) ||
                e.EmployeeNumber.ToLower().Contains(term) ||
                (e.Email != null && e.Email.ToLower().Contains(term)));
        }

        // 2. Company Filter
        if (filters.CompanyId.HasValue)
        {
            query = query.Where(e => e.CompanyId == filters.CompanyId.Value);
        }

        // 3. Department Filter
        if (filters.DepartmentId.HasValue)
        {
            query = query.Where(e => e.DepartmentId == filters.DepartmentId.Value);
        }

        // 4. Section Filter
        if (filters.SectionId.HasValue)
        {
            query = query.Where(e => e.SectionId == filters.SectionId.Value);
        }

        // 5. Position Filter
        if (filters.PositionId.HasValue)
        {
            query = query.Where(e => e.PositionId == filters.PositionId.Value);
        }

        // 6. Manager Filter
        if (filters.ManagerId.HasValue)
        {
            query = query.Where(e => e.DirectManagerId == filters.ManagerId.Value);
        }

        // 7. NLevel Filter
        if (filters.NLevel.HasValue)
        {
            query = query.Where(e => e.Position.NLevel == filters.NLevel.Value);
        }

        // 8. Employment Status Filter
        if (filters.Status.HasValue)
        {
            query = query.Where(e => e.EmploymentStatus == filters.Status.Value);
        }

        // 9. Eligibility Filter
        if (filters.IsEvaluationEligible.HasValue)
        {
            query = query.Where(e => e.IsEvaluationEligible == filters.IsEvaluationEligible.Value);
        }

        // 10. Has Linked Account Filter
        if (filters.HasLinkedAccount.HasValue)
        {
            query = filters.HasLinkedAccount.Value
                ? query.Where(e => e.UserId != null)
                : query.Where(e => e.UserId == null);
        }

        var totalCount = await query.CountAsync();
        var pageIndex = Math.Max(1, filters.PageIndex);
        var pageSize = Math.Clamp(filters.PageSize, 1, 100);

        var items = await query
            .OrderBy(e => e.FullName)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new EmployeeSummaryDto(
                e.EmployeeId,
                e.EmployeeNumber,
                e.FullName,
                e.Email,
                e.CompanyId,
                e.Company.Name,
                e.Department != null ? e.Department.Name : null,
                e.Position.Name,
                e.Position.NLevel,
                e.DirectManager != null ? e.DirectManager.FullName : null,
                e.EmploymentStatus,
                e.IsEvaluationEligible,
                e.UserId != null
            ))
            .ToListAsync();

        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
        return new PaginatedListDto<EmployeeSummaryDto>(items, totalCount, pageIndex, pageSize, totalPages);
    }

    public async Task<EmployeeDetailDto?> GetEmployeeByIdAsync(int employeeId)
    {
        return await _context.Employees
            .Include(e => e.Company)
            .Include(e => e.Department)
            .Include(e => e.Section)
            .Include(e => e.Position)
            .Include(e => e.DirectManager)
            .Include(e => e.User)
            .Where(e => e.EmployeeId == employeeId)
            .Select(e => new EmployeeDetailDto(
                e.EmployeeId,
                e.EmployeeNumber,
                e.FullName,
                e.Email,
                e.CompanyId,
                e.Company.Name,
                e.DepartmentId,
                e.Department != null ? e.Department.Name : null,
                e.SectionId,
                e.Section != null ? e.Section.Name : null,
                e.PositionId,
                e.Position.Name,
                e.Position.NLevel,
                e.DirectManagerId,
                e.DirectManager != null ? e.DirectManager.FullName : null,
                e.EmploymentStatus,
                e.UserId,
                e.User != null ? e.User.Email : null,
                e.IsEvaluationEligible,
                e.HireDate,
                e.ResignationDate,
                e.IsActive,
                e.CreatedAtUtc
            ))
            .FirstOrDefaultAsync();
    }

    public async Task<(bool Success, string? ErrorReason)> SetEvaluationEligibilityAsync(
        int employeeId,
        bool isEligible,
        string? reason,
        int actorUserId,
        string actorIpAddress)
    {
        var employee = await _context.Employees.FindAsync(employeeId);
        if (employee == null)
        {
            return (false, "EmployeeNotFound");
        }

        var previousEligibility = employee.IsEvaluationEligible;
        employee.IsEvaluationEligible = isEligible;
        employee.UpdatedAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        await _auditService.LogAsync(
            actorUserId,
            "EligibilityChanged",
            "Employee",
            employeeId.ToString(),
            new { isEligible, previous = previousEligibility, reason },
            actorIpAddress
        );

        return (true, null);
    }

    public async Task<(bool Success, string? ErrorReason, int StatusCode)> LinkUserAccountAsync(
        int employeeId,
        int userId,
        int actorUserId,
        string actorIpAddress)
    {
        var employee = await _context.Employees.FindAsync(employeeId);
        if (employee == null)
        {
            return (false, "EmployeeNotFound", 404);
        }

        var user = await _context.Users.FindAsync(userId);
        if (user == null)
        {
            return (false, "UserNotFound", 404);
        }

        if (!user.IsActive)
        {
            return (false, "UserInactive", 400);
        }

        // Check if user is already linked to another employee
        var alreadyLinked = await _context.Employees
            .AnyAsync(e => e.UserId == userId && e.EmployeeId != employeeId);

        if (alreadyLinked)
        {
            return (false, "UserAlreadyLinked", 409);
        }

        // Check if employee already has a linked user
        if (employee.UserId.HasValue && employee.UserId.Value != userId)
        {
            return (false, "EmployeeAlreadyLinked", 409);
        }

        employee.UserId = userId;
        employee.UpdatedAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        await _auditService.LogAsync(
            actorUserId,
            "UserLinked",
            "Employee",
            employeeId.ToString(),
            new { userId, userEmail = user.Email },
            actorIpAddress
        );

        return (true, null, 200);
    }

    public async Task<(bool Success, string? ErrorReason)> UnlinkUserAccountAsync(
        int employeeId,
        int actorUserId,
        string actorIpAddress)
    {
        var employee = await _context.Employees.FindAsync(employeeId);
        if (employee == null)
        {
            return (false, "EmployeeNotFound");
        }

        var previousUserId = employee.UserId;
        employee.UserId = null;
        employee.UpdatedAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        await _auditService.LogAsync(
            actorUserId,
            "UserUnlinked",
            "Employee",
            employeeId.ToString(),
            new { unlinkedUserId = previousUserId },
            actorIpAddress
        );

        return (true, null);
    }
}
