using MEval.Api.Models;

namespace MEval.Api.DTOs;

public record EmployeeDetailDto(
    int EmployeeId,
    string EmployeeNumber,
    string FullName,
    string? Email,
    int CompanyId,
    string CompanyName,
    int? DepartmentId,
    string? DepartmentName,
    int? SectionId,
    string? SectionName,
    int PositionId,
    string PositionName,
    int NLevel,
    int? DirectManagerId,
    string? DirectManagerName,
    EmploymentStatus EmploymentStatus,
    int? UserId,
    string? LinkedUserEmail,
    bool IsEvaluationEligible,
    DateOnly HireDate,
    DateOnly? ResignationDate,
    bool IsActive,
    DateTime CreatedAtUtc
);

public record EmployeeSummaryDto(
    int EmployeeId,
    string EmployeeNumber,
    string FullName,
    string? Email,
    int CompanyId,
    string CompanyName,
    string? DepartmentName,
    string PositionName,
    int NLevel,
    string? DirectManagerName,
    EmploymentStatus EmploymentStatus,
    bool IsEvaluationEligible,
    bool HasLinkedAccount
);

public record EmployeeFilterParams(
    string? Search = null,
    int? CompanyId = null,
    int? DepartmentId = null,
    int? SectionId = null,
    int? PositionId = null,
    int? ManagerId = null,
    int? NLevel = null,
    EmploymentStatus? Status = null,
    bool? IsEvaluationEligible = null,
    bool? HasLinkedAccount = null,
    int PageIndex = 1,
    int PageSize = 20
);

public record ManagerChainNodeDto(
    int EmployeeId,
    string FullName,
    int PositionId,
    string PositionName,
    int NLevel,
    int Depth
);

public record DirectReportDto(
    int EmployeeId,
    string EmployeeNumber,
    string FullName,
    int PositionId,
    string PositionName,
    int NLevel,
    bool IsEvaluationEligible,
    EmploymentStatus EmploymentStatus
);

public record OrphanEmployeeDto(
    int EmployeeId,
    string EmployeeNumber,
    string FullName,
    int CompanyId,
    string CompanyName,
    int PositionId,
    string PositionName,
    int NLevel
);

public record RootWithManagerAnomalyDto(
    int EmployeeId,
    string EmployeeNumber,
    string FullName,
    int DirectManagerId,
    string DirectManagerName,
    int PositionId,
    string PositionName,
    int NLevel
);

public record ManagerAnomalyDto(
    int EmployeeId,
    string EmployeeNumber,
    string FullName,
    int DirectManagerId,
    string DirectManagerName,
    string Reason
);

public record HierarchyAnomaliesDto(
    List<OrphanEmployeeDto> Orphans,
    List<RootWithManagerAnomalyDto> RootWithManager,
    List<ManagerAnomalyDto> Mismatches
);

public record UpdateEligibilityRequest(
    bool IsEvaluationEligible,
    string? Reason = null
);

public record LinkUserAccountRequest(
    int UserId
);
