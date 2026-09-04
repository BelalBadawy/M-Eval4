using MEval.Api.DTOs;

namespace MEval.Api.Services;

public interface IEmployeeService
{
    Task<PaginatedListDto<EmployeeSummaryDto>> SearchEmployeesAsync(EmployeeFilterParams filters);

    Task<EmployeeDetailDto?> GetEmployeeByIdAsync(int employeeId);

    Task<(bool Success, string? ErrorReason)> SetEvaluationEligibilityAsync(
        int employeeId,
        bool isEligible,
        string? reason,
        int actorUserId,
        string actorIpAddress);

    Task<(bool Success, string? ErrorReason, int StatusCode)> LinkUserAccountAsync(
        int employeeId,
        int userId,
        int actorUserId,
        string actorIpAddress);

    Task<(bool Success, string? ErrorReason)> UnlinkUserAccountAsync(
        int employeeId,
        int actorUserId,
        string actorIpAddress);
}
