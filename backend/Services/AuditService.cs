using System.Text.Json;
using MEval.Api.Data;
using MEval.Api.DTOs;
using MEval.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace MEval.Api.Services;

public interface IAuditService
{
    Task LogAsync(Guid? actorUserId, string action, string entityType, string? entityId, object? details, string? ipAddress);
    Task<PaginatedListDto<AuditLogDto>> GetAuditLogsAsync(AuditLogFilterParams filters);
}

public class AuditService : IAuditService
{
    private readonly AppDbContext _context;

    public AuditService(AppDbContext context)
    {
        _context = context;
    }

    public async Task LogAsync(Guid? actorUserId, string action, string entityType, string? entityId, object? details, string? ipAddress)
    {
        var detailsJson = details != null ? JsonSerializer.Serialize(details) : string.Empty;

        var auditLog = new AuditLog
        {
            Id = Guid.NewGuid(),
            ActorUserId = actorUserId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId ?? string.Empty,
            Details = detailsJson,
            IpAddress = ipAddress ?? string.Empty,
            TimestampUtc = DateTime.UtcNow
        };

        _context.AuditLogs.Add(auditLog);
        await _context.SaveChangesAsync();
    }

    public async Task<PaginatedListDto<AuditLogDto>> GetAuditLogsAsync(AuditLogFilterParams filters)
    {
        var query = _context.AuditLogs
            .AsNoTracking()
            .Include(a => a.ActorUser)
            .AsQueryable();

        if (filters.ActorUserId.HasValue)
        {
            query = query.Where(a => a.ActorUserId == filters.ActorUserId.Value);
        }

        if (!string.IsNullOrWhiteSpace(filters.Action))
        {
            query = query.Where(a => a.Action == filters.Action);
        }

        if (!string.IsNullOrWhiteSpace(filters.EntityType))
        {
            query = query.Where(a => a.EntityType == filters.EntityType);
        }

        if (!string.IsNullOrWhiteSpace(filters.EntityId))
        {
            query = query.Where(a => a.EntityId == filters.EntityId);
        }

        if (filters.FromUtc.HasValue)
        {
            query = query.Where(a => a.TimestampUtc >= filters.FromUtc.Value);
        }

        if (filters.ToUtc.HasValue)
        {
            query = query.Where(a => a.TimestampUtc <= filters.ToUtc.Value);
        }

        var totalCount = await query.CountAsync();
        var pageSize = Math.Clamp(filters.PageSize, 1, 100);
        var pageIndex = Math.Max(1, filters.PageIndex);
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        var items = await query
            .OrderByDescending(a => a.TimestampUtc)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new AuditLogDto(
                a.Id,
                a.ActorUserId,
                a.ActorUser != null ? a.ActorUser.FullName : null,
                a.Action,
                a.EntityType,
                a.EntityId,
                a.Details,
                a.IpAddress,
                a.TimestampUtc
            ))
            .ToListAsync();

        return new PaginatedListDto<AuditLogDto>(items, totalCount, pageIndex, pageSize, totalPages);
    }
}
