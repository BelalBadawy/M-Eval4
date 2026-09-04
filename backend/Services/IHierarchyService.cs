using MEval.Api.DTOs;
using MEval.Api.Models;

namespace MEval.Api.Services;

public interface IHierarchyService
{
    Task<(bool HasCycle, string? CyclePath)> DetectCycleAsync(
        int employeeId,
        int candidateManagerId,
        Dictionary<int, int?>? overlaidGraph = null);

    Task<(bool IsValid, string? ErrorMessage)> ValidateManagerLinkAsync(
        int employeeId,
        int managerId,
        Dictionary<int, (int CompanyId, EmploymentStatus Status, bool IsActive)>? lookup = null);

    Task<List<ManagerChainNodeDto>> GetManagerChainAsync(int employeeId);

    Task<List<DirectReportDto>> GetDirectReportsAsync(int managerId);

    Task<HierarchyAnomaliesDto> GetHierarchyAnomaliesAsync(int? companyId = null);

    Task<List<OrphanEmployeeDto>> GetOrphanEmployeesAsync(int? companyId = null);
}
