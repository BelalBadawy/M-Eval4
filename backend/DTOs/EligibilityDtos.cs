namespace MEval.Api.DTOs;

public record CompanyEligibilitySummaryDto(
    int CompanyId,
    string CompanyName,
    int TotalEmployees,
    int EligibleCount,
    int ExcludedCount
);

public record EligibilitySummaryDto(
    int TotalEmployees,
    int EligibleCount,
    int ExcludedCount,
    List<CompanyEligibilitySummaryDto> CompanyBreakdown
);

public record EligibilityEmployeeDto(
    int EmployeeId,
    string EmployeeNumber,
    string FullName,
    string? Email,
    int CompanyId,
    string CompanyName,
    int? DepartmentId,
    string? DepartmentName,
    int PositionId,
    string PositionName,
    int NLevel,
    bool IsEvaluationEligible,
    string Status
);

public record ExcludedEmployeeDto(
    int EmployeeId,
    string EmployeeNumber,
    string FullName,
    string? Email,
    int CompanyId,
    string CompanyName,
    int? DepartmentId,
    string? DepartmentName,
    int PositionId,
    string PositionName,
    int NLevel,
    string Reason
);

public record BulkFlagUpdateRequest(
    List<int> EmployeeIds,
    bool IsEvaluationEligible,
    string Reason
);

public record BulkFlagUpdateResponse(
    int UpdatedCount,
    bool IsEvaluationEligible,
    string Message
);
