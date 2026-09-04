namespace MEval.Api.DTOs;

public record AuditLogDto(
    int Id,
    int? ActorUserId,
    string? ActorUserName,
    string Action,
    string EntityType,
    string EntityId,
    string Details,
    string IpAddress,
    DateTime TimestampUtc
);

public record AuditLogFilterParams(
    int? ActorUserId,
    string? Action,
    string? EntityType,
    string? EntityId,
    DateTime? FromUtc,
    DateTime? ToUtc,
    int PageIndex = 1,
    int PageSize = 20
);
