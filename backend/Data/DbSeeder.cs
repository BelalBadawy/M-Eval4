using MEval.Api.Configuration;
using MEval.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MEval.Api.Data;

public class DbSeeder
{
    private readonly AppDbContext _context;
    private readonly SecuritySettings _securitySettings;

    public DbSeeder(AppDbContext context, IOptions<SecuritySettings> securitySettings)
    {
        _context = context;
        _securitySettings = securitySettings.Value;
    }

    public async Task SeedAsync()
    {
        // 1. Seed Permissions
        var permissionCodes = new[]
        {
            ("users.create", "Create new users", "Users"),
            ("users.read", "Read user profiles and lists", "Users"),
            ("users.update", "Update user profile information", "Users"),
            ("users.deactivate", "Deactivate or reactivate user accounts", "Users"),
            ("users.delete", "Soft delete user accounts", "Users"),
            ("users.unlock", "Unlock locked out user accounts", "Users"),
            ("users.reset-password", "Force reset user passwords to default", "Users"),
            ("users.force-logout", "Force logout active sessions for a user", "Users"),
            ("users.import", "Upload and execute bulk user Excel imports", "Users"),
            ("roles.manage", "Create, edit, and delete custom roles", "Roles"),
            ("roles.assign", "Assign or remove roles for users", "Roles"),
            ("audit.read", "Query and view security and audit logs", "Audit"),
            ("org.read", "Read organizational structure and employee hierarchy", "Organization"),
            ("org.import", "Upload and execute HR organizational master data imports", "Organization"),
            ("employees.manage-eligibility", "Override evaluation eligibility for employees", "Employees"),
            ("employees.link-user", "Bind or unbind employee records to application user accounts", "Employees")
        };

        var existingPermissions = await _context.Permissions.ToDictionaryAsync(p => p.Code);
        var permissionsToInsert = new List<Permission>();

        foreach (var (code, desc, module) in permissionCodes)
        {
            if (!existingPermissions.TryGetValue(code, out var existing))
            {
                var newPerm = new Permission
                {
                    Code = code,
                    Description = desc,
                    Module = module
                };
                permissionsToInsert.Add(newPerm);
                existingPermissions[code] = newPerm;
            }
        }

        if (permissionsToInsert.Count > 0)
        {
            _context.Permissions.AddRange(permissionsToInsert);
            await _context.SaveChangesAsync();
        }

        // 2. Seed System Roles
        var systemRoles = new[]
        {
            ("Super Admin", "Full system administrative access", 100),
            ("Admin", "User management and provisioning", 50),
            ("Auditor", "Read-only access to audit trails and security logs", 30),
            ("User", "Standard authenticated internal user", 10)
        };

        var existingRoles = await _context.Roles.ToDictionaryAsync(r => r.Name);
        var rolesToInsert = new List<Role>();

        foreach (var (name, desc, level) in systemRoles)
        {
            if (!existingRoles.TryGetValue(name, out var existing))
            {
                var newRole = new Role
                {
                    Name = name,
                    Description = desc,
                    Level = level,
                    IsSystemProtected = true
                };
                rolesToInsert.Add(newRole);
                existingRoles[name] = newRole;
            }
        }

        if (rolesToInsert.Count > 0)
        {
            _context.Roles.AddRange(rolesToInsert);
            await _context.SaveChangesAsync();
        }

        // 3. Seed Role-Permissions
        var superAdminRole = existingRoles["Super Admin"];
        var adminRole = existingRoles["Admin"];
        var auditorRole = existingRoles["Auditor"];

        var existingRolePermissions = await _context.RolePermissions
            .Select(rp => $"{rp.RoleId}_{rp.PermissionId}")
            .ToHashSetAsync();

        var rolePermissionsToAdd = new List<RolePermission>();

        // Super Admin gets all permissions
        foreach (var perm in existingPermissions.Values)
        {
            var key = $"{superAdminRole.Id}_{perm.Id}";
            if (!existingRolePermissions.Contains(key))
            {
                rolePermissionsToAdd.Add(new RolePermission { RoleId = superAdminRole.Id, PermissionId = perm.Id });
                existingRolePermissions.Add(key);
            }
        }

        // Admin gets user management, role assignment, audit read, and org management
        var adminPermCodes = new[]
        {
            "users.create", "users.read", "users.update", "users.deactivate",
            "users.delete", "users.unlock", "users.reset-password", "users.force-logout",
            "users.import", "roles.assign", "audit.read",
            "org.read", "org.import", "employees.manage-eligibility", "employees.link-user"
        };
        foreach (var code in adminPermCodes)
        {
            if (existingPermissions.TryGetValue(code, out var perm))
            {
                var key = $"{adminRole.Id}_{perm.Id}";
                if (!existingRolePermissions.Contains(key))
                {
                    rolePermissionsToAdd.Add(new RolePermission { RoleId = adminRole.Id, PermissionId = perm.Id });
                    existingRolePermissions.Add(key);
                }
            }
        }

        // Auditor gets audit.read, users.read, and org.read
        var auditorPermCodes = new[] { "audit.read", "users.read", "org.read" };
        foreach (var code in auditorPermCodes)
        {
            if (existingPermissions.TryGetValue(code, out var perm))
            {
                var key = $"{auditorRole.Id}_{perm.Id}";
                if (!existingRolePermissions.Contains(key))
                {
                    rolePermissionsToAdd.Add(new RolePermission { RoleId = auditorRole.Id, PermissionId = perm.Id });
                    existingRolePermissions.Add(key);
                }
            }
        }

        if (rolePermissionsToAdd.Count > 0)
        {
            _context.RolePermissions.AddRange(rolePermissionsToAdd);
            await _context.SaveChangesAsync();
        }

        // 4. Seed Initial Super Admin Account
        var rootAdminEmail = "admin@meval.local";
        var rootAdmin = await _context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == rootAdminEmail);

        if (rootAdmin == null)
        {
            var workFactor = _securitySettings.BcryptWorkFactor > 0 ? _securitySettings.BcryptWorkFactor : 11;
            var defaultPassword = !string.IsNullOrEmpty(_securitySettings.DefaultUserPassword)
                ? _securitySettings.DefaultUserPassword
                : "Mina@123";

            var newAdmin = new User
            {
                FullName = "System Administrator",
                Email = rootAdminEmail,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(defaultPassword, workFactor),
                MustChangePassword = true,
                IsActive = true,
                Source = UserSource.Manual
            };

            _context.Users.Add(newAdmin);
            await _context.SaveChangesAsync();

            _context.UserRoles.Add(new UserRole
            {
                UserId = newAdmin.Id,
                RoleId = superAdminRole.Id
            });
            await _context.SaveChangesAsync();
        }
    }
}
