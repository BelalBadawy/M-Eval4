using MEval.Api.DTOs;

namespace MEval.Api.Services;

public interface IEligibilityService
{
    Task<EligibilitySummaryDto> GetSummaryAsync(int? companyId = null);

    Task<PaginatedListDto<EligibilityEmployeeDto>> SearchEmployeesAsync(
        bool? isEligible = null,
        int? companyId = null,
        int? departmentId = null,
        string? search = null,
        int pageIndex = 1,
        int pageSize = 50
    );

    Task<PaginatedListDto<ExcludedEmployeeDto>> GetExcludedEmployeesAsync(
        int? companyId = null,
        int? departmentId = null,
        string? search = null,
        int pageIndex = 1,
        int pageSize = 50
    );

    Task<(bool Success, BulkFlagUpdateResponse? Response, string? Error, List<int>? MissingEmployeeIds, int StatusCode)> BulkFlagUpdateAsync(
        BulkFlagUpdateRequest request,
        int actorUserId,
        string actorIpAddress
    );
}
