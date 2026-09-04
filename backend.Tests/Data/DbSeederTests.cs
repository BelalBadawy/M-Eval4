using FluentAssertions;
using MEval.Api.Configuration;
using MEval.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace MEval.Api.Tests.Data;

public class DbSeederTests
{
    private (AppDbContext Context, DbSeeder Seeder) CreateSeeder()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var context = new AppDbContext(options);
        var securitySettings = Options.Create(new SecuritySettings
        {
            DefaultUserPassword = "Mina@123",
            BcryptWorkFactor = 4 // fast for tests
        });

        var seeder = new DbSeeder(context, securitySettings);
        return (context, seeder);
    }

    [Fact]
    public async Task SeedAsync_ShouldSeedRolesPermissionsAndInitialAdmin()
    {
        var (context, seeder) = CreateSeeder();

        await seeder.SeedAsync();

        // Verify Roles
        var roles = await context.Roles.ToListAsync();
        roles.Should().HaveCount(4);
        roles.Should().Contain(r => r.Name == "Super Admin" && r.Level == 100 && r.IsSystemProtected);
        roles.Should().Contain(r => r.Name == "Admin" && r.Level == 50 && r.IsSystemProtected);
        roles.Should().Contain(r => r.Name == "Auditor" && r.Level == 30 && r.IsSystemProtected);
        roles.Should().Contain(r => r.Name == "User" && r.Level == 10 && r.IsSystemProtected);

        // Verify Permissions
        var permissions = await context.Permissions.ToListAsync();
        permissions.Should().HaveCount(12);
        permissions.Should().Contain(p => p.Code == "users.create");
        permissions.Should().Contain(p => p.Code == "users.import");
        permissions.Should().Contain(p => p.Code == "roles.manage");
        permissions.Should().Contain(p => p.Code == "audit.read");

        // Verify Super Admin has all permissions
        var superAdmin = roles.First(r => r.Name == "Super Admin");
        var superAdminPermissions = await context.RolePermissions
            .Where(rp => rp.RoleId == superAdmin.Id)
            .ToListAsync();
        superAdminPermissions.Should().HaveCount(12);

        // Verify Initial Admin User
        var adminUser = await context.Users.Include(u => u.UserRoles).FirstOrDefaultAsync(u => u.Email == "admin@meval.local");
        adminUser.Should().NotBeNull();
        adminUser!.MustChangePassword.Should().BeTrue();
        adminUser.IsActive.Should().BeTrue();
        adminUser.UserRoles.Should().ContainSingle(ur => ur.RoleId == superAdmin.Id);

        // Verify password hash matches Mina@123
        BCrypt.Net.BCrypt.Verify("Mina@123", adminUser.PasswordHash).Should().BeTrue();
    }

    [Fact]
    public async Task SeedAsync_ShouldBeIdempotent()
    {
        var (context, seeder) = CreateSeeder();

        // Run twice
        await seeder.SeedAsync();
        await seeder.SeedAsync();

        var roles = await context.Roles.ToListAsync();
        roles.Should().HaveCount(4);

        var permissions = await context.Permissions.ToListAsync();
        permissions.Should().HaveCount(12);

        var adminCount = await context.Users.CountAsync(u => u.Email == "admin@meval.local");
        adminCount.Should().Be(1);
    }
}
