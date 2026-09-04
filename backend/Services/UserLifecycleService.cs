using MEval.Api.Configuration;
using MEval.Api.Data;
using MEval.Api.DTOs;
using MEval.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MEval.Api.Services;

public interface IUserLifecycleService
{
    Task<(bool Success, UserDetailDto? User, string? ErrorReason)> CreateUserAsync(CreateUserRequest request, int callerUserId);
    Task<PaginatedListDto<UserDetailDto>> SearchUsersAsync(UserFilterParams filters);
    Task<UserDetailDto?> GetUserByIdAsync(int id);
    Task<(bool Success, string? ErrorReason)> UpdateUserAsync(int id, UpdateUserRequest request, int callerUserId);
    Task<(bool Success, string? ErrorReason)> UpdateProfileMeAsync(int currentUserId, UpdateProfileMeRequest request);
    Task<(bool Success, string? ErrorReason)> DeactivateUserAsync(int id, int callerUserId);
    Task<(bool Success, string? ErrorReason)> ReactivateUserAsync(int id, int callerUserId);
    Task<(bool Success, string? ErrorReason)> UnlockUserAsync(int id, int callerUserId);
    Task<(bool Success, string? ErrorReason)> ForceLogoutUserAsync(int id, int callerUserId);
    Task<(bool Success, string? ErrorReason)> SoftDeleteUserAsync(int id, int callerUserId);
}

public class UserLifecycleService : IUserLifecycleService
{
    private readonly AppDbContext _context;
    private readonly ITokenService _tokenService;
    private readonly IPasswordPolicyService _passwordPolicyService;
    private readonly SecuritySettings _securitySettings;

    public UserLifecycleService(
        AppDbContext context,
        ITokenService tokenService,
        IPasswordPolicyService passwordPolicyService,
        IOptions<SecuritySettings> securitySettings)
    {
        _context = context;
        _tokenService = tokenService;
        _passwordPolicyService = passwordPolicyService;
        _securitySettings = securitySettings.Value;
    }

    public async Task<(bool Success, UserDetailDto? User, string? ErrorReason)> CreateUserAsync(CreateUserRequest request, int callerUserId)
    {
        var email = request.Email?.Trim().ToLowerInvariant() ?? string.Empty;

        // Check if email already exists (including soft-deleted)
        var existing = await _context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == email);

        if (existing != null)
        {
            if (existing.SoftDeletedAtUtc != null)
            {
                return (false, null, "UserSoftDeleted: User with email is soft-deleted; requires manual administrator restore");
            }

            return (false, null, "EmailAlreadyExists");
        }

        var defaultPassword = _securitySettings.DefaultUserPassword ?? "Mina@123";
        var passwordHash = _passwordPolicyService.HashPassword(defaultPassword);

        var user = new User
        {
            FullName = request.FullName.Trim(),
            Email = email,
            PhoneNumber = request.PhoneNumber,
            PasswordHash = passwordHash,
            MustChangePassword = true,
            IsActive = true,
            Source = UserSource.Manual,
            CreatedAtUtc = DateTime.UtcNow
        };

        _context.Users.Add(user);

        // Assign default User role
        var defaultRole = await _context.Roles.FirstOrDefaultAsync(r => r.Name == "User");
        if (defaultRole != null)
        {
            user.UserRoles.Add(new UserRole
            {
                User = user,
                Role = defaultRole,
                AssignedAtUtc = DateTime.UtcNow,
                AssignedByUserId = callerUserId
            });
        }

        await _context.SaveChangesAsync();

        var detail = await GetUserByIdAsync(user.Id);
        return (true, detail, null);
    }

    public async Task<PaginatedListDto<UserDetailDto>> SearchUsersAsync(UserFilterParams filters)
    {
        var query = _context.Users
            .AsNoTracking()
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
                    .ThenInclude(r => r.RolePermissions)
                        .ThenInclude(rp => rp.Permission)
            .AsQueryable();

        // Search text
        if (!string.IsNullOrWhiteSpace(filters.Search))
        {
            var search = filters.Search.Trim().ToLower();
            query = query.Where(u => u.FullName.ToLower().Contains(search) || u.Email.ToLower().Contains(search));
        }

        // Filter by role
        if (!string.IsNullOrWhiteSpace(filters.Role))
        {
            query = query.Where(u => u.UserRoles.Any(ur => ur.Role.Name == filters.Role));
        }

        // Filter by status
        if (!string.IsNullOrWhiteSpace(filters.Status))
        {
            var status = filters.Status.ToLower();
            if (status == "active")
            {
                query = query.Where(u => u.IsActive && (u.LockoutEndUtc == null || u.LockoutEndUtc <= DateTime.UtcNow));
            }
            else if (status == "inactive")
            {
                query = query.Where(u => !u.IsActive);
            }
            else if (status == "locked")
            {
                query = query.Where(u => u.LockoutEndUtc != null && u.LockoutEndUtc > DateTime.UtcNow);
            }
        }

        // Filter by source
        if (filters.Source.HasValue)
        {
            query = query.Where(u => u.Source == filters.Source.Value);
        }

        // Filter by batchId
        if (filters.BatchId.HasValue)
        {
            query = query.Where(u => u.ImportBatchId == filters.BatchId.Value);
        }

        // Filter by stillOnDefaultPassword
        if (filters.StillOnDefaultPassword.HasValue)
        {
            query = query.Where(u => u.MustChangePassword == filters.StillOnDefaultPassword.Value);
        }

        var totalCount = await query.CountAsync();
        var pageSize = Math.Clamp(filters.PageSize, 1, 100);
        var pageIndex = Math.Max(1, filters.PageIndex);
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        var items = await query
            .OrderByDescending(u => u.CreatedAtUtc)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new UserDetailDto(
                u.Id,
                u.FullName,
                u.Email,
                u.PhoneNumber,
                u.IsActive,
                u.MustChangePassword,
                u.LockoutEndUtc != null && u.LockoutEndUtc > DateTime.UtcNow,
                u.LockoutEndUtc,
                u.Source,
                u.ImportBatchId,
                u.CreatedAtUtc,
                u.PasswordChangedAtUtc,
                u.UserRoles.Select(ur => ur.Role.Name).ToList(),
                u.UserRoles.SelectMany(ur => ur.Role.RolePermissions).Select(rp => rp.Permission.Code).Distinct().ToList()
            ))
            .ToListAsync();

        return new PaginatedListDto<UserDetailDto>(items, totalCount, pageIndex, pageSize, totalPages);
    }

    public async Task<UserDetailDto?> GetUserByIdAsync(int id)
    {
        var user = await _context.Users
            .AsNoTracking()
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
                    .ThenInclude(r => r.RolePermissions)
                        .ThenInclude(rp => rp.Permission)
            .FirstOrDefaultAsync(u => u.Id == id);

        if (user == null)
        {
            return null;
        }

        return new UserDetailDto(
            user.Id,
            user.FullName,
            user.Email,
            user.PhoneNumber,
            user.IsActive,
            user.MustChangePassword,
            user.IsLockedOut,
            user.LockoutEndUtc,
            user.Source,
            user.ImportBatchId,
            user.CreatedAtUtc,
            user.PasswordChangedAtUtc,
            user.UserRoles.Select(ur => ur.Role.Name).ToList(),
            user.UserRoles.SelectMany(ur => ur.Role.RolePermissions).Select(rp => rp.Permission.Code).Distinct().ToList()
        );
    }

    public async Task<(bool Success, string? ErrorReason)> UpdateUserAsync(int id, UpdateUserRequest request, int callerUserId)
    {
        var user = await _context.Users.FindAsync(id);
        if (user == null)
        {
            return (false, "UserNotFound");
        }

        user.FullName = request.FullName.Trim();
        user.PhoneNumber = request.PhoneNumber;
        user.UpdatedAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? ErrorReason)> UpdateProfileMeAsync(int currentUserId, UpdateProfileMeRequest request)
    {
        var user = await _context.Users.FindAsync(currentUserId);
        if (user == null)
        {
            return (false, "UserNotFound");
        }

        user.PhoneNumber = request.PhoneNumber;
        user.UpdatedAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? ErrorReason)> DeactivateUserAsync(int id, int callerUserId)
    {
        if (id == callerUserId)
        {
            return (false, "CannotDeactivateSelf: You cannot deactivate your own account.");
        }

        var user = await _context.Users
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Id == id);

        if (user == null)
        {
            return (false, "UserNotFound");
        }

        // Check last active admin guard
        var isAdmin = user.UserRoles.Any(ur => ur.Role.Name == "Admin" || ur.Role.Name == "Super Admin");
        if (isAdmin)
        {
            var activeAdminCount = await _context.UserRoles
                .Where(ur => (ur.Role.Name == "Admin" || ur.Role.Name == "Super Admin") &&
                             ur.User.IsActive &&
                             ur.User.SoftDeletedAtUtc == null)
                .Select(ur => ur.UserId)
                .Distinct()
                .CountAsync();

            if (activeAdminCount <= 1)
            {
                return (false, "CannotDeactivateLastAdmin: You cannot deactivate the last active administrator account.");
            }
        }

        user.IsActive = false;
        await _tokenService.RevokeActiveSessionAsync(id, RevokeReasons.AccountDeactivated);
        await _context.SaveChangesAsync();

        return (true, null);
    }

    public async Task<(bool Success, string? ErrorReason)> ReactivateUserAsync(int id, int callerUserId)
    {
        var user = await _context.Users.FindAsync(id);
        if (user == null)
        {
            return (false, "UserNotFound");
        }

        user.IsActive = true;
        await _context.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? ErrorReason)> UnlockUserAsync(int id, int callerUserId)
    {
        var user = await _context.Users.FindAsync(id);
        if (user == null)
        {
            return (false, "UserNotFound");
        }

        user.FailedLoginAttempts = 0;
        user.LockoutEndUtc = null;
        await _context.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? ErrorReason)> ForceLogoutUserAsync(int id, int callerUserId)
    {
        var revoked = await _tokenService.RevokeActiveSessionAsync(id, RevokeReasons.AdminForceLogout);
        return (true, null);
    }

    public async Task<(bool Success, string? ErrorReason)> SoftDeleteUserAsync(int id, int callerUserId)
    {
        if (id == callerUserId)
        {
            return (false, "CannotDeactivateSelf: You cannot delete your own account.");
        }

        var user = await _context.Users
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Id == id);

        if (user == null)
        {
            return (false, "UserNotFound");
        }

        var isAdmin = user.UserRoles.Any(ur => ur.Role.Name == "Admin" || ur.Role.Name == "Super Admin");
        if (isAdmin)
        {
            var activeAdminCount = await _context.UserRoles
                .Where(ur => (ur.Role.Name == "Admin" || ur.Role.Name == "Super Admin") &&
                             ur.User.IsActive &&
                             ur.User.SoftDeletedAtUtc == null)
                .Select(ur => ur.UserId)
                .Distinct()
                .CountAsync();

            if (activeAdminCount <= 1)
            {
                return (false, "CannotDeactivateLastAdmin: You cannot delete the last active administrator account.");
            }
        }

        user.IsActive = false;
        user.SoftDeletedAtUtc = DateTime.UtcNow;
        await _tokenService.RevokeActiveSessionAsync(id, RevokeReasons.AccountDeactivated);
        await _context.SaveChangesAsync();

        return (true, null);
    }
}
