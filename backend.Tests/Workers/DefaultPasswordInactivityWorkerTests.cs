using FluentAssertions;
using MEval.Api.Configuration;
using MEval.Api.Data;
using MEval.Api.Models;
using MEval.Api.Services;
using MEval.Api.Workers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace MEval.Api.Tests.Workers;

public class DefaultPasswordInactivityWorkerTests
{
    [Fact]
    public async Task ProcessInactivityScan_ShouldDeactivateAccountsOlderThanGracePeriod_WithMustChangePasswordTrue()
    {
        var services = new ServiceCollection();
        var dbName = Guid.NewGuid().ToString();

        services.AddDbContext<AppDbContext>(options =>
        {
            options.UseInMemoryDatabase(dbName);
        });

        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IEmailService, EmailService>();
        services.AddLogging();
        services.Configure<JwtSettings>(opt =>
        {
            opt.SecretKey = "TestSecretKeyThatIsAtLeast32BytesLongForSecurity!";
        });

        var securitySettings = Options.Create(new SecuritySettings
        {
            DefaultPasswordGracePeriodDays = 14
        });
        services.AddSingleton(securitySettings);

        var sp = services.BuildServiceProvider();

        // Seed Users
        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var adminRole = new Role { Id = 1, Name = "Admin", Level = 50 };
            var adminUser = new User
            {
                Id = 1,
                FullName = "Admin",
                Email = "admin@meval.local",
                PasswordHash = "hash",
                MustChangePassword = false,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow.AddDays(-30)
            };
            db.Roles.Add(adminRole);
            db.Users.Add(adminUser);
            db.UserRoles.Add(new UserRole { UserId = adminUser.Id, RoleId = adminRole.Id });

            // User 1: 15 days old, MustChangePassword = true -> SHOULD BE DEACTIVATED
            var expiredUser = new User
            {
                Id = 2,
                FullName = "Expired Inactive User",
                Email = "expired@meval.local",
                PasswordHash = "hash",
                MustChangePassword = true,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow.AddDays(-15)
            };

            // User 2: 5 days old, MustChangePassword = true -> SHOULD REMAIN ACTIVE
            var freshUser = new User
            {
                Id = 3,
                FullName = "Fresh User",
                Email = "fresh@meval.local",
                PasswordHash = "hash",
                MustChangePassword = true,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow.AddDays(-5)
            };

            // User 3: 20 days old, MustChangePassword = false -> SHOULD REMAIN ACTIVE
            var changedPasswordUser = new User
            {
                Id = 4,
                FullName = "Changed Password User",
                Email = "changed@meval.local",
                PasswordHash = "hash",
                MustChangePassword = false,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow.AddDays(-20)
            };

            db.Users.AddRange(expiredUser, freshUser, changedPasswordUser);
            await db.SaveChangesAsync();
        }

        // Run worker scan
        var worker = new DefaultPasswordInactivityWorker(sp, NullLogger<DefaultPasswordInactivityWorker>.Instance, securitySettings);
        var suspendedCount = await worker.ProcessInactivityScanAsync();

        suspendedCount.Should().Be(1);

        // Verify database state
        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var expired = await db.Users.FirstAsync(u => u.Email == "expired@meval.local");
            expired.IsActive.Should().BeFalse();

            var fresh = await db.Users.FirstAsync(u => u.Email == "fresh@meval.local");
            fresh.IsActive.Should().BeTrue();

            var changed = await db.Users.FirstAsync(u => u.Email == "changed@meval.local");
            changed.IsActive.Should().BeTrue();

            var audit = await db.AuditLogs.FirstOrDefaultAsync(a => a.Action == "User.AutoSuspended");
            audit.Should().NotBeNull();
            audit!.EntityId.Should().Be(expired.Id.ToString());
            audit.Details.Should().Contain("remained on temporary default password");
        }
    }
}
