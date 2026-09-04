namespace MEval.Api.DTOs;

public record OrgImportRowErrorDto(
    string SheetName,
    int RowNumber,
    string Identifier,
    string FieldName,
    string Reason,
    string? RawValue = null
);

public record OrgImportSummaryDto(
    int CompaniesCount,
    int DepartmentsCount,
    int SectionsCount,
    int PositionsCount,
    int EmployeesTotal,
    int EmployeesCreated,
    int EmployeesUpdated,
    int EmployeesSkipped,
    int EmployeesFailed,
    int OffboardedCascadeCount,
    int AnomaliesFlagged,
    List<OrgImportRowErrorDto> Errors
);

public record OrgImportDryRunResultDto(
    bool IsValid,
    int TotalRows,
    int ErrorCount,
    OrgImportSummaryDto Summary
);

public record OrgImportExecuteResponse(
    bool Success,
    string Message,
    OrgImportSummaryDto Summary
);
