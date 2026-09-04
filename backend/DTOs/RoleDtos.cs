namespace MEval.Api.DTOs;

public record RoleDto(
    int Id,
    string Name,
    string? Description,
    int Level,
    bool IsSystemProtected,
    List<string> Permissions
);

public record CreateRoleRequest(
    string Name,
    string? Description,
    int Level,
    List<string>? Permissions
);

public record UpdateRoleRequest(
    string? Description,
    int? Level
);

public record UpdateRolePermissionsRequest(
    List<string> Permissions
);

public record BulkAssignRoleRequest(
    int RoleId,
    List<int> UserIds
);

public record BulkAssignRoleResponse(
    int SucceededCount,
    int SkippedCount,
    List<string> Messages
);
