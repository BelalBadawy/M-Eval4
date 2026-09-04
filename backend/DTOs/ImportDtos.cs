using MEval.Api.Models;

namespace MEval.Api.DTOs;

public record ImportDryRunResultDto(
    Guid BatchId,
    string FileName,
    int TotalRows,
    int ValidRows,
    int InvalidRows,
    int InFileDuplicates,
    int DbDuplicates,
    DuplicateStrategy DuplicateStrategy,
    CommitPolicy CommitPolicy,
    List<ImportRowErrorDto> Errors
);

public record ImportRowErrorDto(
    int RowNumber,
    string ColumnName,
    string Reason,
    string? RawValue
);

public record ImportExecuteResponse(
    Guid BatchId,
    ImportStatus Status,
    int CreatedCount,
    int UpdatedCount,
    int SkippedCount,
    int FailedCount
);

public record ImportHistoryDto(
    Guid Id,
    string FileName,
    long FileSize,
    int TotalRows,
    int CreatedRows,
    int UpdatedRows,
    int FailedRows,
    ImportStatus Status,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc,
    string CreatedByUserName
);
