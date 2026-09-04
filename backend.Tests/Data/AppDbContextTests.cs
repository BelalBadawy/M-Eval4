using FluentAssertions;
using MEval.Api.Data;
using MEval.Api.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MEval.Api.Tests.Data;

public class AppDbContextTests
{
    private AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    [Fact]
    public async Task AuditLog_ShouldThrowException_WhenUpdateIsAttempted()
    {
        using var context = CreateDbContext();
        var audit = new AuditLog
        {
            Action = "Test.Action",
            EntityType = "User",
            EntityId = "123",
            Details = "Initial Details",
            IpAddress = "127.0.0.1"
        };

        context.AuditLogs.Add(audit);
        await context.SaveChangesAsync();

        // Attempt update
        audit.Details = "Modified Details";

        var act = async () => await context.SaveChangesAsync();
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*immutable*");
    }

    [Fact]
    public async Task AuditLog_ShouldThrowException_WhenDeleteIsAttempted()
    {
        using var context = CreateDbContext();
        var audit = new AuditLog
        {
            Action = "Test.Action",
            EntityType = "User",
            EntityId = "123",
            Details = "Details",
            IpAddress = "127.0.0.1"
        };

        context.AuditLogs.Add(audit);
        await context.SaveChangesAsync();

        // Attempt delete
        context.AuditLogs.Remove(audit);

        var act = async () => await context.SaveChangesAsync();
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*immutable*");
    }

    [Fact]
    public async Task SoftDeletedUser_ShouldBeFilteredOutByDefault()
    {
        using var context = CreateDbContext();
        var activeUser = new User
        {
            FullName = "Active User",
            Email = "active@test.com",
            PasswordHash = "hash1"
        };

        var deletedUser = new User
        {
            FullName = "Deleted User",
            Email = "deleted@test.com",
            PasswordHash = "hash2",
            SoftDeletedAtUtc = DateTime.UtcNow
        };

        context.Users.AddRange(activeUser, deletedUser);
        await context.SaveChangesAsync();

        var users = await context.Users.ToListAsync();
        users.Should().ContainSingle(u => u.Email == "active@test.com");
        users.Should().NotContain(u => u.Email == "deleted@test.com");

        // With IgnoreQueryFilters, deleted user is retrievable
        var allUsers = await context.Users.IgnoreQueryFilters().ToListAsync();
        allUsers.Should().HaveCount(2);
    }
}
