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

    public Guid TestUserId { get; } = Guid.NewGuid();
    public string TestUserEmail { get; } = "john.doe@meval.local";
    public string TestUserPassword { get; } = "Mina@123";

    public Guid GatewayUserId { get; } = Guid.NewGuid();
    public string GatewayUserEmail { get; } = "gateway.user@meval.local";

    public Guid LockoutUserId { get; } = Guid.NewGuid();
    public string LockoutUserEmail { get; } = "lockout.user@meval.local";

    public Guid ChangePassReuseUserId { get; } = Guid.NewGuid();
    public string ChangePassReuseUserEmail { get; } = "changepass.reuse@meval.local";

    public Guid ChangePassSuccessUserId { get; } = Guid.NewGuid();
    public string ChangePassSuccessUserEmail { get; } = "changepass.success@meval.local";

    public Guid RateLimitUserId { get; } = Guid.NewGuid();
    public string RateLimitUserEmail { get; } = "ratelimit.user@meval.local";

    public Guid ResetUserId { get; } = Guid.NewGuid();
    public string ResetUserEmail { get; } = "reset.user@meval.local";

    public Guid ForceResetUserId { get; } = Guid.NewGuid();
    public string ForceResetUserEmail { get; } = "forcereset.user@meval.local";

    public Guid AdminUserId { get; } = Guid.NewGuid();
    public string AdminUserEmail { get; } = "admin.user@meval.local";

    public Guid SuperAdminUserId { get; } = Guid.NewGuid();
    public string SuperAdminUserEmail { get; } = "superadmin.user@meval.local";

    public Guid NormalUserId { get; } = Guid.NewGuid();
    public string NormalUserEmail { get; } = "normal.user@meval.local";

    public Guid TargetUser1Id { get; } = Guid.NewGuid();
    public Guid TargetUser2Id { get; } = Guid.NewGuid();

    public Guid AdminRoleId { get; } = Guid.NewGuid();
    public Guid UserRoleId { get; } = Guid.NewGuid();
    public Guid SuperAdminRoleId { get; } = Guid.NewGuid();

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

        var permRead = new Permission { Id = Guid.NewGuid(), Code = "users.read", Module = "Users" };
        var permCreate = new Permission { Id = Guid.NewGuid(), Code = "users.create", Module = "Users" };
        var permUpdate = new Permission { Id = Guid.NewGuid(), Code = "users.update", Module = "Users" };
        var permDeactivate = new Permission { Id = Guid.NewGuid(), Code = "users.deactivate", Module = "Users" };
        var permDelete = new Permission { Id = Guid.NewGuid(), Code = "users.delete", Module = "Users" };
        var permUnlock = new Permission { Id = Guid.NewGuid(), Code = "users.unlock", Module = "Users" };
        var permForceLogout = new Permission { Id = Guid.NewGuid(), Code = "users.force-logout", Module = "Users" };
        var permReset = new Permission { Id = Guid.NewGuid(), Code = "users.reset-password", Module = "Users" };
        var permRolesManage = new Permission { Id = Guid.NewGuid(), Code = "roles.manage", Module = "Roles" };
        var permRolesAssign = new Permission { Id = Guid.NewGuid(), Code = "roles.assign", Module = "Roles" };
        var permAudit = new Permission { Id = Guid.NewGuid(), Code = "audit.read", Module = "Audit" };
        var permImport = new Permission { Id = Guid.NewGuid(), Code = "users.import", Module = "Users" };

        db.Roles.AddRange(superAdminRole, adminRole, userRole);
        db.Permissions.AddRange(permRead, permCreate, permUpdate, permDeactivate, permDelete, permUnlock, permForceLogout, permReset, permRolesManage, permRolesAssign, permAudit, permImport);

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
