using MEval.Api.Data;
using MEval.Api.DTOs;
using MEval.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace MEval.Api.Services;

public interface IRoleService
{
    Task<List<RoleDto>> GetRolesAsync();
    Task<RoleDto?> GetRoleByIdAsync(Guid id);
    Task<(bool Success, RoleDto? Role, string? ErrorReason)> CreateRoleAsync(CreateRoleRequest request, Guid callerUserId);
    Task<(bool Success, string? ErrorReason)> UpdateRoleAsync(Guid id, UpdateRoleRequest request, Guid callerUserId);
    Task<(bool Success, string? ErrorReason)> DeleteRoleAsync(Guid id, Guid callerUserId);
    Task<(bool Success, string? ErrorReason)> UpdateRolePermissionsAsync(Guid id, List<string> permissionCodes, Guid callerUserId);
    Task<(bool Success, string? ErrorReason)> AssignRoleAsync(Guid userId, Guid roleId, Guid callerUserId);
    Task<(bool Success, string? ErrorReason)> RemoveRoleAsync(Guid userId, Guid roleId, Guid callerUserId);
    Task<(bool Success, BulkAssignRoleResponse? Response, string? ErrorReason)> BulkAssignRoleAsync(BulkAssignRoleRequest request, Guid callerUserId);
    Task<int> GetCallerMaxLevelAsync(Guid callerUserId);
}

public class RoleService : IRoleService
{
    private readonly AppDbContext _context;
    private readonly ITokenService _tokenService;

    public RoleService(AppDbContext context, ITokenService tokenService)
    {
        _context = context;
        _tokenService = tokenService;
    }

    public async Task<int> GetCallerMaxLevelAsync(Guid callerUserId)
    {
        var levels = await _context.UserRoles
            .Where(ur => ur.UserId == callerUserId)
            .Select(ur => ur.Role.Level)
            .ToListAsync();

        return levels.Count > 0 ? levels.Max() : 0;
    }

    public async Task<List<RoleDto>> GetRolesAsync()
    {
        return await _context.Roles
            .AsNoTracking()
            .Include(r => r.RolePermissions)
                .ThenInclude(rp => rp.Permission)
            .Select(r => new RoleDto(
                r.Id,
                r.Name,
                r.Description,
                r.Level,
                r.IsSystemProtected,
                r.RolePermissions.Select(rp => rp.Permission.Code).ToList()
            ))
            .ToListAsync();
    }

    public async Task<RoleDto?> GetRoleByIdAsync(Guid id)
    {
        var role = await _context.Roles
            .AsNoTracking()
            .Include(r => r.RolePermissions)
                .ThenInclude(rp => rp.Permission)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (role == null)
        {
            return null;
        }

        return new RoleDto(
            role.Id,
            role.Name,
            role.Description,
            role.Level,
            role.IsSystemProtected,
            role.RolePermissions.Select(rp => rp.Permission.Code).ToList()
        );
    }

    public async Task<(bool Success, RoleDto? Role, string? ErrorReason)> CreateRoleAsync(CreateRoleRequest request, Guid callerUserId)
    {
        var callerLevel = await GetCallerMaxLevelAsync(callerUserId);
        if (request.Level >= callerLevel)
        {
            return (false, null, "HierarchyViolation: You cannot create a role with level greater than or equal to your own.");
        }

        if (await _context.Roles.AnyAsync(r => r.Name == request.Name.Trim()))
        {
            return (false, null, "RoleNameAlreadyExists");
        }

        var role = new Role
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Description = request.Description ?? string.Empty,
            Level = request.Level,
            IsSystemProtected = false
        };

        _context.Roles.Add(role);

        if (request.Permissions != null && request.Permissions.Count > 0)
        {
            var permissions = await _context.Permissions
                .Where(p => request.Permissions.Contains(p.Code))
                .ToListAsync();

            foreach (var perm in permissions)
            {
                _context.RolePermissions.Add(new RolePermission
                {
                    RoleId = role.Id,
                    PermissionId = perm.Id
                });
            }
        }

        await _context.SaveChangesAsync();

        var dto = await GetRoleByIdAsync(role.Id);
        return (true, dto, null);
    }

    public async Task<(bool Success, string? ErrorReason)> UpdateRoleAsync(Guid id, UpdateRoleRequest request, Guid callerUserId)
    {
        var role = await _context.Roles.FindAsync(id);
        if (role == null)
        {
            return (false, "RoleNotFound");
        }

        var callerLevel = await GetCallerMaxLevelAsync(callerUserId);
        if (role.Level >= callerLevel)
        {
            return (false, "HierarchyViolation: You cannot edit a role with level greater than or equal to your own.");
        }

        if (request.Level.HasValue)
        {
            if (request.Level.Value >= callerLevel)
            {
                return (false, "HierarchyViolation: You cannot elevate a role level to greater than or equal to your own.");
            }

            if (role.IsSystemProtected && request.Level.Value != role.Level)
            {
                return (false, "CannotModifySystemProtectedRoleLevel");
            }

            role.Level = request.Level.Value;
        }

        if (request.Description != null)
        {
            role.Description = request.Description;
        }

        await _context.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? ErrorReason)> DeleteRoleAsync(Guid id, Guid callerUserId)
    {
        var role = await _context.Roles.FindAsync(id);
        if (role == null)
        {
            return (false, "RoleNotFound");
        }

        if (role.IsSystemProtected)
        {
            return (false, "CannotDeleteSystemProtectedRole");
        }

        var callerLevel = await GetCallerMaxLevelAsync(callerUserId);
        if (role.Level >= callerLevel)
        {
            return (false, "HierarchyViolation: You cannot delete a role with level greater than or equal to your own.");
        }

        var inUse = await _context.UserRoles.AnyAsync(ur => ur.RoleId == id);
        if (inUse)
        {
            return (false, "RoleInUse: One or more users are currently assigned to this role.");
        }

        _context.Roles.Remove(role);
        await _context.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? ErrorReason)> UpdateRolePermissionsAsync(Guid id, List<string> permissionCodes, Guid callerUserId)
    {
        var role = await _context.Roles
            .Include(r => r.RolePermissions)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (role == null)
        {
            return (false, "RoleNotFound");
        }

        var callerLevel = await GetCallerMaxLevelAsync(callerUserId);
        if (role.Level >= callerLevel)
        {
            return (false, "HierarchyViolation: You cannot modify permissions of a role with level greater than or equal to your own.");
        }

        // 1. Clear existing permissions for this role
        _context.RolePermissions.RemoveRange(role.RolePermissions);

        // 2. Attach new permissions
        var validPermissions = await _context.Permissions
            .Where(p => permissionCodes.Contains(p.Code))
            .ToListAsync();

        foreach (var perm in validPermissions)
        {
            _context.RolePermissions.Add(new RolePermission
            {
                RoleId = role.Id,
                PermissionId = perm.Id
            });
        }

        await _context.SaveChangesAsync();

        // 3. Immediately revoke active sessions for all affected users
        var userIds = await _context.UserRoles
            .Where(ur => ur.RoleId == id)
            .Select(ur => ur.UserId)
            .Distinct()
            .ToListAsync();

        foreach (var userId in userIds)
        {
            await _tokenService.RevokeActiveSessionAsync(userId, RevokeReasons.RoleDowngraded);
        }

        return (true, null);
    }

    public async Task<(bool Success, string? ErrorReason)> AssignRoleAsync(Guid userId, Guid roleId, Guid callerUserId)
    {
        var role = await _context.Roles.FindAsync(roleId);
        if (role == null)
        {
            return (false, "RoleNotFound");
        }

        var callerLevel = await GetCallerMaxLevelAsync(callerUserId);
        if (role.Level >= callerLevel)
        {
            return (false, "HierarchyViolation: You cannot assign a role with level greater than or equal to your own.");
        }

        var user = await _context.Users.FindAsync(userId);
        if (user == null || !user.IsActive || user.SoftDeletedAtUtc != null)
        {
            return (false, "UserNotFoundOrInactive");
        }

        var existing = await _context.UserRoles
            .FirstOrDefaultAsync(ur => ur.UserId == userId && ur.RoleId == roleId);

        if (existing == null)
        {
            _context.UserRoles.Add(new UserRole
            {
                UserId = userId,
                RoleId = roleId,
                AssignedAtUtc = DateTime.UtcNow,
                AssignedByUserId = callerUserId
            });

            await _context.SaveChangesAsync();
            await _tokenService.RevokeActiveSessionAsync(userId, RevokeReasons.RoleDowngraded);
        }

        return (true, null);
    }

    public async Task<(bool Success, string? ErrorReason)> RemoveRoleAsync(Guid userId, Guid roleId, Guid callerUserId)
    {
        var role = await _context.Roles.FindAsync(roleId);
        if (role == null)
        {
            return (false, "RoleNotFound");
        }

        var callerLevel = await GetCallerMaxLevelAsync(callerUserId);
        if (role.Level >= callerLevel)
        {
            return (false, "HierarchyViolation: You cannot remove a role with level greater than or equal to your own.");
        }

        var existing = await _context.UserRoles
            .FirstOrDefaultAsync(ur => ur.UserId == userId && ur.RoleId == roleId);

        if (existing != null)
        {
            _context.UserRoles.Remove(existing);
            await _context.SaveChangesAsync();
            await _tokenService.RevokeActiveSessionAsync(userId, RevokeReasons.RoleDowngraded);
        }

        return (true, null);
    }

    public async Task<(bool Success, BulkAssignRoleResponse? Response, string? ErrorReason)> BulkAssignRoleAsync(BulkAssignRoleRequest request, Guid callerUserId)
    {
        var role = await _context.Roles.FindAsync(request.RoleId);
        if (role == null)
        {
            return (false, null, "RoleNotFound");
        }

        var callerLevel = await GetCallerMaxLevelAsync(callerUserId);
        if (role.Level >= callerLevel)
        {
            return (false, null, "HierarchyViolation: You cannot assign a role with level greater than or equal to your own.");
        }

        int succeeded = 0;
        int skipped = 0;
        var messages = new List<string>();

        foreach (var userId in request.UserIds.Distinct())
        {
            var user = await _context.Users.FindAsync(userId);
            if (user == null || !user.IsActive || user.SoftDeletedAtUtc != null)
            {
                skipped++;
                messages.Add($"User {userId} is not active or not found.");
                continue;
            }

            var alreadyAssigned = await _context.UserRoles
                .AnyAsync(ur => ur.UserId == userId && ur.RoleId == request.RoleId);

            if (alreadyAssigned)
            {
                skipped++;
                messages.Add($"User {userId} already has role {role.Name}.");
                continue;
            }

            _context.UserRoles.Add(new UserRole
            {
                UserId = userId,
                RoleId = request.RoleId,
                AssignedAtUtc = DateTime.UtcNow,
                AssignedByUserId = callerUserId
            });

            succeeded++;
            await _tokenService.RevokeActiveSessionAsync(userId, RevokeReasons.RoleDowngraded);
        }

        await _context.SaveChangesAsync();

        return (true, new BulkAssignRoleResponse(succeeded, skipped, messages), null);
    }
}
