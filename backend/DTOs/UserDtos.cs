using MEval.Api.Models;

namespace MEval.Api.DTOs;

public record CreateUserRequest(
    string FullName,
    string Email,
    string? PhoneNumber
);

public record UpdateUserRequest(
    string FullName,
    string? PhoneNumber
);

public record UpdateProfileMeRequest(
    string? PhoneNumber
);

public record UserDetailDto(
    int Id,
    string FullName,
    string Email,
    string? PhoneNumber,
    bool IsActive,
    bool MustChangePassword,
    bool IsLockedOut,
    DateTime? LockoutEndUtc,
    UserSource Source,
    int? ImportBatchId,
    DateTime CreatedAtUtc,
    DateTime? PasswordChangedAtUtc,
    List<string> Roles,
    List<string> Permissions
);

public record UserFilterParams(
    string? Search,
    string? Role,
    string? Status, // "active", "inactive", "locked"
    UserSource? Source,
    int? BatchId,
    bool? StillOnDefaultPassword,
    int PageIndex = 1,
    int PageSize = 20
);

public record PaginatedListDto<T>(
    List<T> Items,
    int TotalCount,
    int PageIndex,
    int PageSize,
    int TotalPages
);
