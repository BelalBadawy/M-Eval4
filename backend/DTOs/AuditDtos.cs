namespace MEval.Api.DTOs;

public record AuditLogDto(
    Guid Id,
    Guid? ActorUserId,
    string? ActorUserName,
    string Action,
    string EntityType,
    string EntityId,
    string Details,
    string IpAddress,
    DateTime TimestampUtc
);

public record AuditLogFilterParams(
    Guid? ActorUserId,
    string? Action,
    string? EntityType,
    string? EntityId,
    DateTime? FromUtc,
    DateTime? ToUtc,
    int PageIndex = 1,
    int PageSize = 20
);
