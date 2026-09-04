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

public class ExcelImportServiceTests
{
    private (AppDbContext Context, ExcelImportService Service) CreateService(int maxRows = 5000)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var context = new AppDbContext(options);
        var settings = Options.Create(new SecuritySettings
        {
            DefaultUserPassword = "Mina@123",
            MaxImportFileSizeMb = 5,
            MaxImportRows = maxRows
        });
        var jwtSettings = Options.Create(new JwtSettings
        {
            SecretKey = "TestSecretKeyThatIsAtLeast32BytesLongForSecurity!"
        });

        var tokenService = new TokenService(context, jwtSettings);
        var passwordPolicyService = new PasswordPolicyService(settings);
        var emailService = new EmailService(Microsoft.Extensions.Logging.Abstractions.NullLogger<EmailService>.Instance);

        var service = new ExcelImportService(context, tokenService, passwordPolicyService, emailService, settings);
        return (context, service);
    }

    [Fact]
    public void GenerateTemplate_ShouldProduceValidWorkbookWithExpectedColumns()
    {
        var (_, service) = CreateService();
        var bytes = service.GenerateTemplate();

        bytes.Should().NotBeNullOrEmpty();

        using var ms = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(ms);
        var sheet = workbook.Worksheet(1);
        sheet.Name.Should().Be("Users");

        sheet.Cell(1, 1).GetString().Should().Be("FullName");
        sheet.Cell(1, 2).GetString().Should().Be("Email");
    }

    [Theory]
    [InlineData("Anne-Marie", "Anne-Marie")]
    [InlineData("=cmd|' /C calc'!A0", "cmd|' /C calc'!A0")]
    [InlineData("+12345", "12345")]
    [InlineData("-test-string-here", "test-string-here")]
    [InlineData("@SUM(A1:A5)", "SUM(A1:A5)")]
    public void SanitizeLeadingFormula_ShouldNeutralizeLeadingSymbols_AndPreserveInternalHyphens(string input, string expected)
    {
        var result = ExcelImportService.SanitizeLeadingFormula(input);
        result.Should().Be(expected);
    }

    [Fact]
    public async Task DryRunImport_WhenForbiddenColumnsExist_ShouldRejectImmediately()
    {
        var (context, service) = CreateService();

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Users");
        sheet.Cell(1, 1).Value = "FullName";
        sheet.Cell(1, 2).Value = "Email";
        sheet.Cell(1, 3).Value = "Password"; // Forbidden column!
        sheet.Cell(2, 1).Value = "Hacker User";
        sheet.Cell(2, 2).Value = "hacker@meval.local";
        sheet.Cell(2, 3).Value = "SecretPassword123";

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        ms.Position = 0;

        var callerId = 99;
        var (success, result, error) = await service.DryRunImportAsync(
            ms, "forbidden.xlsx", ms.Length, DuplicateStrategy.Skip, CommitPolicy.PartialValidOnly, callerId);

        success.Should().BeFalse();
        error.Should().Contain("ForbiddenColumn");
    }

    [Fact]
    public async Task DryRunImport_WhenRowsExceedLimit_ShouldReject()
    {
        var (context, service) = CreateService(5);

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Users");
        sheet.Cell(1, 1).Value = "FullName";
        sheet.Cell(1, 2).Value = "Email";

        for (int i = 1; i <= 6; i++)
        {
            sheet.Cell(i + 1, 1).Value = $"User {i}";
            sheet.Cell(i + 1, 2).Value = $"user{i}@meval.local";
        }

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        ms.Position = 0;

        var (success, _, error) = await service.DryRunImportAsync(
            ms, "toolarge.xlsx", ms.Length, DuplicateStrategy.Skip, CommitPolicy.PartialValidOnly, 99);

        success.Should().BeFalse();
        error.Should().Contain("RowLimitExceeded");
    }

    [Fact]
    public async Task DryRunImport_ShouldAccuratelyDetectDuplicates_AndStageValidRows()
    {
        var (context, service) = CreateService();

        // Seed existing DB user
        var existingUser = new User
        {
            FullName = "Existing User",
            Email = "existing@meval.local",
            PasswordHash = "hash",
            IsActive = true
        };
        context.Users.Add(existingUser);
        await context.SaveChangesAsync();

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Users");
        sheet.Cell(1, 1).Value = "FullName";
        sheet.Cell(1, 2).Value = "Email";

        // Row 2: Valid
        sheet.Cell(2, 1).Value = "New User 1";
        sheet.Cell(2, 2).Value = "new1@meval.local";

        // Row 3: DB Duplicate
        sheet.Cell(3, 1).Value = "Duplicate in DB";
        sheet.Cell(3, 2).Value = "existing@meval.local";

        // Row 4: In-File Duplicate (duplicate of Row 2)
        sheet.Cell(4, 1).Value = "New User 1 Duplicate";
        sheet.Cell(4, 2).Value = "new1@meval.local";

        // Row 5: Invalid email
        sheet.Cell(5, 1).Value = "Invalid Email User";
        sheet.Cell(5, 2).Value = "not-an-email";

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        ms.Position = 0;

        var callerId = 99;
        var (success, result, error) = await service.DryRunImportAsync(
            ms, "batch.xlsx", ms.Length, DuplicateStrategy.Skip, CommitPolicy.PartialValidOnly, callerId);

        success.Should().BeTrue();
        result.Should().NotBeNull();
        result!.TotalRows.Should().Be(4);
        result.ValidRows.Should().Be(1);
        result.DbDuplicates.Should().Be(1);
        result.InFileDuplicates.Should().Be(1);
        result.InvalidRows.Should().Be(1);

        // Verify rows staged in database table
        var staged = await context.ImportBatchRows.Where(r => r.BatchId == result.BatchId).ToListAsync();
        staged.Should().HaveCount(4);
        staged.First(r => r.Email == "new1@meval.local" && r.RowNumber == 2).Status.Should().Be(RowStatus.Valid);
        staged.First(r => r.Email == "existing@meval.local").Status.Should().Be(RowStatus.DuplicateInDb);
        staged.First(r => r.RowNumber == 4).Status.Should().Be(RowStatus.DuplicateInFile);
        staged.First(r => r.RowNumber == 5).Status.Should().Be(RowStatus.Invalid);
    }
}
