using MEval.Api.DTOs;
using MEval.Api.Security;
using MEval.Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace MEval.Api.Endpoints;

public static class AuditEndpoints
{
    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/audit")
            .WithTags("Audit")
            .RequireAuthorization()
            .AddEndpointFilter<FirstLoginGatewayFilter>();

        group.MapGet("/logs", async (
            IAuditService auditService,
            Guid? actorUserId = null,
            string? action = null,
            string? entityType = null,
            string? entityId = null,
            DateTime? fromUtc = null,
            DateTime? toUtc = null,
            int pageIndex = 1,
            int pageSize = 20) =>
        {
            var filters = new AuditLogFilterParams(
                actorUserId,
                action,
                entityType,
                entityId,
                fromUtc,
                toUtc,
                pageIndex <= 0 ? 1 : pageIndex,
                pageSize <= 0 ? 20 : pageSize
            );

            var logs = await auditService.GetAuditLogsAsync(filters);
            return Results.Ok(logs);
        })
        .RequirePermission("audit.read")
        .WithName("GetAuditLogs")
        .WithSummary("Query immutable audit logs with pagination and filtering");

        return app;
    }
}
