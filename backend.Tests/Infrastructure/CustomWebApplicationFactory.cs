using MEval.Api.Data;
using MEval.Api.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MEval.Api.Tests.Infrastructure;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = Guid.NewGuid().ToString();

    public int TestUserId { get; } = 1;
    public string TestUserEmail { get; } = "john.doe@meval.local";
    public string TestUserPassword { get; } = "Mina@123";

    public int GatewayUserId { get; } = 2;
    public string GatewayUserEmail { get; } = "gateway.user@meval.local";

    public int LockoutUserId { get; } = 3;
    public string LockoutUserEmail { get; } = "lockout.user@meval.local";

    public int ChangePassReuseUserId { get; } = 4;
    public string ChangePassReuseUserEmail { get; } = "changepass.reuse@meval.local";

    public int ChangePassSuccessUserId { get; } = 5;
    public string ChangePassSuccessUserEmail { get; } = "changepass.success@meval.local";

    public int RateLimitUserId { get; } = 6;
    public string RateLimitUserEmail { get; } = "ratelimit.user@meval.local";

    public int ResetUserId { get; } = 7;
    public string ResetUserEmail { get; } = "reset.user@meval.local";

    public int ForceResetUserId { get; } = 8;
    public string ForceResetUserEmail { get; } = "forcereset.user@meval.local";

    public int AdminUserId { get; } = 9;
    public string AdminUserEmail { get; } = "admin.user@meval.local";

    public int SuperAdminUserId { get; } = 10;
    public string SuperAdminUserEmail { get; } = "superadmin.user@meval.local";

    public int NormalUserId { get; } = 11;
    public string NormalUserEmail { get; } = "normal.user@meval.local";

    public int TargetUser1Id { get; } = 12;
    public int TargetUser2Id { get; } = 13;

    public int SuperAdminRoleId { get; } = 1;
    public int AdminRoleId { get; } = 2;
    public int UserRoleId { get; } = 3;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            services.AddDbContext<AppDbContext>(options =>
            {
                options.UseInMemoryDatabase(_databaseName);
            });

            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureCreated();
            SeedTestData(db);
        });
    }

    private void SeedTestData(AppDbContext db)
    {
        var superAdminRole = new Role { Id = SuperAdminRoleId, Name = "Super Admin", Level = 100, IsSystemProtected = true };
        var adminRole = new Role { Id = AdminRoleId, Name = "Admin", Level = 50, IsSystemProtected = true };
        var userRole = new Role { Id = UserRoleId, Name = "User", Level = 10, IsSystemProtected = true };

        var permRead = new Permission { Id = 1, Code = "users.read", Module = "Users" };
        var permCreate = new Permission { Id = 2, Code = "users.create", Module = "Users" };
        var permUpdate = new Permission { Id = 3, Code = "users.update", Module = "Users" };
        var permDeactivate = new Permission { Id = 4, Code = "users.deactivate", Module = "Users" };
        var permDelete = new Permission { Id = 5, Code = "users.delete", Module = "Users" };
        var permUnlock = new Permission { Id = 6, Code = "users.unlock", Module = "Users" };
        var permForceLogout = new Permission { Id = 7, Code = "users.force-logout", Module = "Users" };
        var permReset = new Permission { Id = 8, Code = "users.reset-password", Module = "Users" };
        var permRolesManage = new Permission { Id = 9, Code = "roles.manage", Module = "Roles" };
        var permRolesAssign = new Permission { Id = 10, Code = "roles.assign", Module = "Roles" };
        var permAudit = new Permission { Id = 11, Code = "audit.read", Module = "Audit" };
        var permImport = new Permission { Id = 12, Code = "users.import", Module = "Users" };
        var permOrgRead = new Permission { Id = 13, Code = "org.read", Module = "Organization" };
        var permOrgImport = new Permission { Id = 14, Code = "org.import", Module = "Organization" };
        var permManageElig = new Permission { Id = 15, Code = "employees.manage-eligibility", Module = "Employees" };
        var permLinkUser = new Permission { Id = 16, Code = "employees.link-user", Module = "Employees" };

        db.Roles.AddRange(superAdminRole, adminRole, userRole);
        db.Permissions.AddRange(permRead, permCreate, permUpdate, permDeactivate, permDelete, permUnlock, permForceLogout, permReset, permRolesManage, permRolesAssign, permAudit, permImport, permOrgRead, permOrgImport, permManageElig, permLinkUser);

        db.RolePermissions.Add(new RolePermission { RoleId = userRole.Id, PermissionId = permRead.Id });

        db.RolePermissions.Add(new RolePermission { RoleId = adminRole.Id, PermissionId = permRead.Id });
        db.RolePermissions.Add(new RolePermission { RoleId = adminRole.Id, PermissionId = permCreate.Id });
        db.RolePermissions.Add(new RolePermission { RoleId = adminRole.Id, PermissionId = permUpdate.Id });
        db.RolePermissions.Add(new RolePermission { RoleId = adminRole.Id, PermissionId = permDeactivate.Id });
        db.RolePermissions.Add(new RolePermission { RoleId = adminRole.Id, PermissionId = permDelete.Id });
        db.RolePermissions.Add(new RolePermission { RoleId = adminRole.Id, PermissionId = permUnlock.Id });
        db.RolePermissions.Add(new RolePermission { RoleId = adminRole.Id, PermissionId = permForceLogout.Id });
        db.RolePermissions.Add(new RolePermission { RoleId = adminRole.Id, PermissionId = permReset.Id });
        db.RolePermissions.Add(new RolePermission { RoleId = adminRole.Id, PermissionId = permRolesManage.Id });
        db.RolePermissions.Add(new RolePermission { RoleId = adminRole.Id, PermissionId = permRolesAssign.Id });
        db.RolePermissions.Add(new RolePermission { RoleId = adminRole.Id, PermissionId = permAudit.Id });
        db.RolePermissions.Add(new RolePermission { RoleId = adminRole.Id, PermissionId = permImport.Id });
        db.RolePermissions.Add(new RolePermission { RoleId = adminRole.Id, PermissionId = permOrgRead.Id });
        db.RolePermissions.Add(new RolePermission { RoleId = adminRole.Id, PermissionId = permOrgImport.Id });
        db.RolePermissions.Add(new RolePermission { RoleId = adminRole.Id, PermissionId = permManageElig.Id });
        db.RolePermissions.Add(new RolePermission { RoleId = adminRole.Id, PermissionId = permLinkUser.Id });

        // Super Admin gets all permissions
        foreach (var perm in new[] { permRead, permCreate, permUpdate, permDeactivate, permDelete, permUnlock, permForceLogout, permReset, permRolesManage, permRolesAssign, permAudit, permImport, permOrgRead, permOrgImport, permManageElig, permLinkUser })
        {
            db.RolePermissions.Add(new RolePermission { RoleId = superAdminRole.Id, PermissionId = perm.Id });
        }

        var defaultHash = BCrypt.Net.BCrypt.HashPassword(TestUserPassword, 11);

        var users = new[]
        {
            new User
            {
                Id = TestUserId,
                FullName = "John Doe",
                Email = TestUserEmail,
                PasswordHash = defaultHash,
                MustChangePassword = true,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow
            },
            new User
            {
                Id = GatewayUserId,
                FullName = "Gateway User",
                Email = GatewayUserEmail,
                PasswordHash = defaultHash,
                MustChangePassword = true,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow
            },
            new User
            {
                Id = LockoutUserId,
                FullName = "Lockout User",
                Email = LockoutUserEmail,
                PasswordHash = defaultHash,
                MustChangePassword = true,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow
            },
            new User
            {
                Id = ChangePassReuseUserId,
                FullName = "Change Pass Reuse User",
                Email = ChangePassReuseUserEmail,
                PasswordHash = defaultHash,
                MustChangePassword = true,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow
            },
            new User
            {
                Id = ChangePassSuccessUserId,
                FullName = "Change Pass Success User",
                Email = ChangePassSuccessUserEmail,
                PasswordHash = defaultHash,
                MustChangePassword = true,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow
            },
            new User
            {
                Id = RateLimitUserId,
                FullName = "Rate Limit User",
                Email = RateLimitUserEmail,
                PasswordHash = defaultHash,
                MustChangePassword = true,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow
            },
            new User
            {
                Id = ResetUserId,
                FullName = "Reset User",
                Email = ResetUserEmail,
                PasswordHash = defaultHash,
                MustChangePassword = false,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow
            },
            new User
            {
                Id = ForceResetUserId,
                FullName = "Force Reset User",
                Email = ForceResetUserEmail,
                PasswordHash = defaultHash,
                MustChangePassword = false,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow
            },
            new User
            {
                Id = NormalUserId,
                FullName = "Normal User",
                Email = NormalUserEmail,
                PasswordHash = defaultHash,
                MustChangePassword = false,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow
            },
            new User
            {
                Id = TargetUser1Id,
                FullName = "Target User 1",
                Email = "target1@meval.local",
                PasswordHash = defaultHash,
                MustChangePassword = false,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow
            },
            new User
            {
                Id = TargetUser2Id,
                FullName = "Target User 2",
                Email = "target2@meval.local",
                PasswordHash = defaultHash,
                MustChangePassword = false,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow
            }
        };

        db.Users.AddRange(users);

        foreach (var u in users)
        {
            db.UserRoles.Add(new UserRole { UserId = u.Id, RoleId = userRole.Id, AssignedAtUtc = DateTime.UtcNow });
        }

        // Admin User (MustChangePassword = false, Admin role)
        var adminUser = new User
        {
            Id = AdminUserId,
            FullName = "Admin User",
            Email = AdminUserEmail,
            PasswordHash = defaultHash,
            MustChangePassword = false,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.Users.Add(adminUser);
        db.UserRoles.Add(new UserRole { UserId = adminUser.Id, RoleId = adminRole.Id, AssignedAtUtc = DateTime.UtcNow });

        // Super Admin User (MustChangePassword = false, Super Admin role)
        var superAdminUser = new User
        {
            Id = SuperAdminUserId,
            FullName = "Super Admin User",
            Email = SuperAdminUserEmail,
            PasswordHash = defaultHash,
            MustChangePassword = false,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.Users.Add(superAdminUser);
        db.UserRoles.Add(new UserRole { UserId = superAdminUser.Id, RoleId = superAdminRole.Id, AssignedAtUtc = DateTime.UtcNow });

        db.SaveChanges();
    }
}
