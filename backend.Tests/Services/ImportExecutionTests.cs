using ClosedXML.Excel;
using FluentAssertions;
using MEval.Api.Configuration;
using MEval.Api.Data;
using MEval.Api.Models;
using MEval.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace MEval.Api.Tests.Services;

public class ImportExecutionTests
{
    private (AppDbContext Context, ExcelImportService Service, Guid CallerId) CreateService()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var context = new AppDbContext(options);
        var securitySettings = Options.Create(new SecuritySettings
        {
            DefaultUserPassword = "Mina@123",
            MaxImportFileSizeMb = 5,
            MaxImportRows = 5000
        });
        var jwtSettings = Options.Create(new JwtSettings
        {
            SecretKey = "TestSecretKeyThatIsAtLeast32BytesLongForSecurity!"
        });

        var tokenService = new TokenService(context, jwtSettings);
        var passwordPolicyService = new PasswordPolicyService(securitySettings);
        var emailService = new EmailService(Microsoft.Extensions.Logging.Abstractions.NullLogger<EmailService>.Instance);

        var callerUser = new User
        {
            Id = Guid.NewGuid(),
            FullName = "Admin Caller",
            Email = "admincaller@meval.local",
            PasswordHash = "hash",
            IsActive = true
        };
        context.Users.Add(callerUser);
        context.SaveChanges();

        var service = new ExcelImportService(context, tokenService, passwordPolicyService, emailService, securitySettings);
        return (context, service, callerUser.Id);
    }

    [Fact]
    public async Task ExecuteImport_ShouldRecheckLiveDbDuplicates_AndPurgeStagedRows()
    {
        var (context, service, callerId) = CreateService();

        // Seed default role
        var defaultRole = new Role { Id = Guid.NewGuid(), Name = "User", Level = 10 };
        context.Roles.Add(defaultRole);
        await context.SaveChangesAsync();

        // 1. Prepare excel with 2 candidate users
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Users");
        ws.Cell(1, 1).Value = "FullName";
        ws.Cell(1, 2).Value = "Email";
        ws.Cell(2, 1).Value = "Candidate One";
        ws.Cell(2, 2).Value = "candidate1@meval.local";
        ws.Cell(3, 1).Value = "Candidate Two";
        ws.Cell(3, 2).Value = "candidate2@meval.local";

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;

        // 2. Perform dry-run -> both candidate1 and candidate2 are staged as Valid
        var (dryRunSuccess, dryRunResult, _) = await service.DryRunImportAsync(
            ms, "test.xlsx", ms.Length, DuplicateStrategy.Skip, CommitPolicy.PartialValidOnly, callerId);

        dryRunSuccess.Should().BeTrue();
        dryRunResult!.ValidRows.Should().Be(2);

        // 3. Simulate another admin creating "candidate1@meval.local" directly in the DB between dry-run and execute!
        context.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            FullName = "Candidate One Interleaved",
            Email = "candidate1@meval.local",
            PasswordHash = "hash",
            IsActive = true
        });
        await context.SaveChangesAsync();

        // 4. Execute the import batch
        var (execSuccess, execResponse, error) = await service.ExecuteImportAsync(dryRunResult.BatchId, callerId);

        execSuccess.Should().BeTrue();
        execResponse.Should().NotBeNull();
        execResponse!.CreatedCount.Should().Be(1); // Only candidate2 created!
        execResponse.SkippedCount.Should().Be(1); // candidate1 was skipped per DuplicateStrategy.Skip

        // 5. Verify staged rows table was purged post-execution
        var remainingStagedRows = await context.ImportBatchRows
            .Where(r => r.BatchId == dryRunResult.BatchId)
            .ToListAsync();
        remainingStagedRows.Should().BeEmpty();

        // 6. Verify candidate2 exists with User role and MustChangePassword = true
        var createdUser = await context.Users
            .Include(u => u.UserRoles)
            .FirstOrDefaultAsync(u => u.Email == "candidate2@meval.local");

        createdUser.Should().NotBeNull();
        createdUser!.MustChangePassword.Should().BeTrue();
        createdUser.UserRoles.Should().NotBeEmpty();
    }

    [Fact]
    public async Task CancelImport_ShouldSetStatusCancelled_AndPurgeStagedRows()
    {
        var (context, service, callerId) = CreateService();

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Users");
        ws.Cell(1, 1).Value = "FullName";
        ws.Cell(1, 2).Value = "Email";
        ws.Cell(2, 1).Value = "To Cancel";
        ws.Cell(2, 2).Value = "cancel@meval.local";

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;

        var (_, dryRunResult, _) = await service.DryRunImportAsync(
            ms, "cancel.xlsx", ms.Length, DuplicateStrategy.Skip, CommitPolicy.PartialValidOnly, callerId);

        var (cancelSuccess, error) = await service.CancelImportAsync(dryRunResult!.BatchId, callerId);
        cancelSuccess.Should().BeTrue();

        var batch = await context.ImportBatches.FindAsync(dryRunResult.BatchId);
        batch!.Status.Should().Be(ImportStatus.Cancelled);
        batch.CancelledAtUtc.Should().NotBeNull();

        // Staged rows purged
        var remainingStaged = await context.ImportBatchRows.Where(r => r.BatchId == dryRunResult.BatchId).ToListAsync();
        remainingStaged.Should().BeEmpty();
    }

    [Fact]
    public async Task RollbackImport_ShouldDeactivateCreatedUsers_AndPreserveExistingUsers()
    {
        var (context, service, callerId) = CreateService();

        var defaultRole = new Role { Id = Guid.NewGuid(), Name = "User", Level = 10 };
        context.Roles.Add(defaultRole);

        // Pre-existing user in DB
        var preExistingUser = new User
        {
            Id = Guid.NewGuid(),
            FullName = "Pre Existing User",
            Email = "preexisting@meval.local",
            PasswordHash = "hash",
            IsActive = true
        };
        context.Users.Add(preExistingUser);
        await context.SaveChangesAsync();

        // Import file containing 1 new user and 1 duplicate with Update strategy
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Users");
        ws.Cell(1, 1).Value = "FullName";
        ws.Cell(1, 2).Value = "Email";
        ws.Cell(2, 1).Value = "Newly Created";
        ws.Cell(2, 2).Value = "newlycreated@meval.local";
        ws.Cell(3, 1).Value = "Pre Existing Updated";
        ws.Cell(3, 2).Value = "preexisting@meval.local";

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;

        var (_, dryRunResult, _) = await service.DryRunImportAsync(
            ms, "rollback.xlsx", ms.Length, DuplicateStrategy.Update, CommitPolicy.PartialValidOnly, callerId);

        var (execSuccess, _, _) = await service.ExecuteImportAsync(dryRunResult!.BatchId, callerId);
        execSuccess.Should().BeTrue();

        // Now rollback the batch
        var (rollbackSuccess, error) = await service.RollbackImportAsync(dryRunResult.BatchId, callerId);
        rollbackSuccess.Should().BeTrue();

        var batch = await context.ImportBatches.FindAsync(dryRunResult.BatchId);
        batch!.Status.Should().Be(ImportStatus.RolledBack);

        // Verify the newly created user was rolled back and deactivated
        var newUser = await context.Users.FirstAsync(u => u.Email == "newlycreated@meval.local");
        newUser.IsRolledBack.Should().BeTrue();
        newUser.IsActive.Should().BeFalse();

        // Verify the pre-existing user remains active!
        var existing = await context.Users.FirstAsync(u => u.Email == "preexisting@meval.local");
        existing.IsRolledBack.Should().BeFalse();
        existing.IsActive.Should().BeTrue();
    }
}
