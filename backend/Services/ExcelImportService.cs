using System.Text.RegularExpressions;
using ClosedXML.Excel;
using MEval.Api.Configuration;
using MEval.Api.Data;
using MEval.Api.DTOs;
using MEval.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MEval.Api.Services;

public interface IExcelImportService
{
    byte[] GenerateTemplate();
    Task<(bool Success, ImportDryRunResultDto? Result, string? ErrorReason)> DryRunImportAsync(
        Stream fileStream,
        string fileName,
        long fileSize,
        DuplicateStrategy strategy,
        CommitPolicy commitPolicy,
        Guid callerUserId);
    Task<byte[]?> GenerateErrorReportAsync(Guid batchId);
    Task<(bool Success, ImportExecuteResponse? Response, string? ErrorReason)> ExecuteImportAsync(Guid batchId, Guid callerUserId);
    Task<(bool Success, string? ErrorReason)> CancelImportAsync(Guid batchId, Guid callerUserId);
    Task<(bool Success, string? ErrorReason)> RollbackImportAsync(Guid batchId, Guid callerUserId);
    Task<List<ImportHistoryDto>> GetImportHistoryAsync();
    Task<ImportHistoryDto?> GetImportByIdAsync(Guid id);
}

public class ExcelImportService : IExcelImportService
{
    private readonly AppDbContext _context;
    private readonly ITokenService _tokenService;
    private readonly IPasswordPolicyService _passwordPolicyService;
    private readonly IEmailService _emailService;
    private readonly SecuritySettings _securitySettings;

    private static readonly Regex EmailRegex = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly HashSet<string> ForbiddenColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "Role", "Roles", "Department", "Password"
    };

    public ExcelImportService(
        AppDbContext context,
        ITokenService tokenService,
        IPasswordPolicyService passwordPolicyService,
        IEmailService emailService,
        IOptions<SecuritySettings> securitySettings)
    {
        _context = context;
        _tokenService = tokenService;
        _passwordPolicyService = passwordPolicyService;
        _emailService = emailService;
        _securitySettings = securitySettings.Value;
    }

    public byte[] GenerateTemplate()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Users");

        // Headers
        worksheet.Cell(1, 1).Value = "FullName";
        worksheet.Cell(1, 2).Value = "Email";

        var headerRange = worksheet.Range(1, 1, 1, 2);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.FromTheme(XLThemeColor.Accent1, 0.8);

        // Sample Row
        worksheet.Cell(2, 1).Value = "John Doe";
        worksheet.Cell(2, 2).Value = "john.doe@example.com";

        worksheet.Columns().AdjustToContents();

        using var memoryStream = new MemoryStream();
        workbook.SaveAs(memoryStream);
        return memoryStream.ToArray();
    }

    public async Task<(bool Success, ImportDryRunResultDto? Result, string? ErrorReason)> DryRunImportAsync(
        Stream fileStream,
        string fileName,
        long fileSize,
        DuplicateStrategy strategy,
        CommitPolicy commitPolicy,
        Guid callerUserId)
    {
        var maxBytes = (_securitySettings.MaxImportFileSizeMb > 0 ? _securitySettings.MaxImportFileSizeMb : 5) * 1024 * 1024;
        if (fileSize > maxBytes)
        {
            return (false, null, $"FileTooLarge: Maximum allowed file size is {_securitySettings.MaxImportFileSizeMb} MB.");
        }

        using var workbook = new XLWorkbook(fileStream);
        var worksheet = workbook.Worksheets.FirstOrDefault();
        if (worksheet == null)
        {
            return (false, null, "EmptyFile: No worksheets found in the Excel workbook.");
        }

        // 1. Inspect Headers (Row 1)
        var firstRow = worksheet.Row(1);
        int fullNameCol = -1;
        int emailCol = -1;

        foreach (var cell in firstRow.CellsUsed())
        {
            var header = cell.GetString().Trim();
            if (ForbiddenColumns.Contains(header))
            {
                return (false, null, $"ForbiddenColumn: Column '{header}' is forbidden. Imports only accept FullName and Email.");
            }

            if (string.Equals(header, "FullName", StringComparison.OrdinalIgnoreCase))
            {
                fullNameCol = cell.Address.ColumnNumber;
            }
            else if (string.Equals(header, "Email", StringComparison.OrdinalIgnoreCase))
            {
                emailCol = cell.Address.ColumnNumber;
            }
        }

        if (fullNameCol == -1 || emailCol == -1)
        {
            return (false, null, "MissingHeaders: Worksheet must contain 'FullName' and 'Email' columns.");
        }

        // 2. Count data rows
        var lastRowUsed = worksheet.LastRowUsed()?.RowNumber() ?? 1;
        var totalRows = lastRowUsed - 1;

        if (totalRows <= 0)
        {
            return (false, null, "EmptyFile: File contains no data rows.");
        }

        var maxRows = _securitySettings.MaxImportRows > 0 ? _securitySettings.MaxImportRows : 5000;
        if (totalRows > maxRows)
        {
            return (false, null, $"RowLimitExceeded: File contains {totalRows} rows, exceeding the limit of {maxRows}.");
        }

        // 3. Extract and parse data rows
        var stagedRows = new List<ImportBatchRow>();
        var errors = new List<ImportRowError>();

        var rawRowsData = new List<(int RowNum, string RawName, string RawEmail)>();
        for (int r = 2; r <= lastRowUsed; r++)
        {
            var row = worksheet.Row(r);
            var rawName = row.Cell(fullNameCol).GetString();
            var rawEmail = row.Cell(emailCol).GetString();
            rawRowsData.Add((r, rawName, rawEmail));
        }

        var candidateEmails = rawRowsData
            .Select(x => SanitizeLeadingFormula(x.RawEmail).Trim().ToLowerInvariant())
            .Where(e => !string.IsNullOrEmpty(e))
            .Distinct()
            .ToList();

        var dbExistingUsers = await _context.Users
            .IgnoreQueryFilters()
            .Where(u => candidateEmails.Contains(u.Email))
            .ToListAsync();

        var dbUserMap = dbExistingUsers.ToDictionary(u => u.Email, StringComparer.OrdinalIgnoreCase);
        var seenInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        int inFileDupCount = 0;
        int dbDupCount = 0;
        int validCount = 0;
        int invalidCount = 0;

        var batchId = Guid.NewGuid();

        foreach (var item in rawRowsData)
        {
            var rowNum = item.RowNum;
            var fullName = SanitizeLeadingFormula(item.RawName);
            var email = SanitizeLeadingFormula(item.RawEmail).Trim().ToLowerInvariant();

            bool rowHasError = false;

            if (string.IsNullOrWhiteSpace(fullName))
            {
                errors.Add(new ImportRowError
                {
                    Id = Guid.NewGuid(),
                    BatchId = batchId,
                    RowNumber = rowNum,
                    ColumnName = "FullName",
                    Reason = "FullName is required.",
                    RawValue = TruncateRaw(item.RawName)
                });
                rowHasError = true;
            }
            else if (fullName.Length > 150)
            {
                errors.Add(new ImportRowError
                {
                    Id = Guid.NewGuid(),
                    BatchId = batchId,
                    RowNumber = rowNum,
                    ColumnName = "FullName",
                    Reason = "FullName cannot exceed 150 characters.",
                    RawValue = TruncateRaw(item.RawName)
                });
                rowHasError = true;
            }

            if (string.IsNullOrWhiteSpace(email))
            {
                errors.Add(new ImportRowError
                {
                    Id = Guid.NewGuid(),
                    BatchId = batchId,
                    RowNumber = rowNum,
                    ColumnName = "Email",
                    Reason = "Email is required.",
                    RawValue = TruncateRaw(item.RawEmail)
                });
                rowHasError = true;
            }
            else if (!EmailRegex.IsMatch(email) || email.Length > 255)
            {
                errors.Add(new ImportRowError
                {
                    Id = Guid.NewGuid(),
                    BatchId = batchId,
                    RowNumber = rowNum,
                    ColumnName = "Email",
                    Reason = "Invalid email address format.",
                    RawValue = TruncateRaw(item.RawEmail)
                });
                rowHasError = true;
            }

            if (rowHasError)
            {
                invalidCount++;
                stagedRows.Add(new ImportBatchRow
                {
                    Id = Guid.NewGuid(),
                    BatchId = batchId,
                    RowNumber = rowNum,
                    FullName = fullName,
                    Email = email,
                    Status = RowStatus.Invalid
                });
                continue;
            }

            if (seenInFile.Contains(email))
            {
                inFileDupCount++;
                errors.Add(new ImportRowError
                {
                    Id = Guid.NewGuid(),
                    BatchId = batchId,
                    RowNumber = rowNum,
                    ColumnName = "Email",
                    Reason = "Duplicate email address found in the import file.",
                    RawValue = TruncateRaw(item.RawEmail)
                });

                stagedRows.Add(new ImportBatchRow
                {
                    Id = Guid.NewGuid(),
                    BatchId = batchId,
                    RowNumber = rowNum,
                    FullName = fullName,
                    Email = email,
                    Status = RowStatus.DuplicateInFile
                });
                continue;
            }

            seenInFile.Add(email);

            if (dbUserMap.TryGetValue(email, out var dbUser))
            {
                if (dbUser.SoftDeletedAtUtc != null)
                {
                    invalidCount++;
                    errors.Add(new ImportRowError
                    {
                        Id = Guid.NewGuid(),
                        BatchId = batchId,
                        RowNumber = rowNum,
                        ColumnName = "Email",
                        Reason = "User with email is soft-deleted; requires manual administrator restore",
                        RawValue = TruncateRaw(item.RawEmail)
                    });

                    stagedRows.Add(new ImportBatchRow
                    {
                        Id = Guid.NewGuid(),
                        BatchId = batchId,
                        RowNumber = rowNum,
                        FullName = fullName,
                        Email = email,
                        Status = RowStatus.Invalid
                    });
                }
                else
                {
                    dbDupCount++;
                    stagedRows.Add(new ImportBatchRow
                    {
                        Id = Guid.NewGuid(),
                        BatchId = batchId,
                        RowNumber = rowNum,
                        FullName = fullName,
                        Email = email,
                        Status = RowStatus.DuplicateInDb
                    });
                }
                continue;
            }

            validCount++;
            stagedRows.Add(new ImportBatchRow
            {
                Id = Guid.NewGuid(),
                BatchId = batchId,
                RowNumber = rowNum,
                FullName = fullName,
                Email = email,
                Status = RowStatus.Valid
            });
        }

        var batch = new ImportBatch
        {
            Id = batchId,
            FileName = fileName,
            FileSize = fileSize,
            TotalRows = totalRows,
            ValidRows = validCount,
            InvalidRows = invalidCount,
            Status = ImportStatus.Validated,
            DuplicateStrategy = strategy,
            CommitPolicy = commitPolicy,
            CreatedByUserId = callerUserId,
            CreatedAtUtc = DateTime.UtcNow
        };

        _context.ImportBatches.Add(batch);
        _context.ImportBatchRows.AddRange(stagedRows);
        _context.ImportRowErrors.AddRange(errors);
        await _context.SaveChangesAsync();

        var errorDtos = errors.Select(e => new ImportRowErrorDto(
            e.RowNumber,
            e.ColumnName,
            e.Reason,
            e.RawValue
        )).ToList();

        var resultDto = new ImportDryRunResultDto(
            batch.Id,
            batch.FileName,
            batch.TotalRows,
            validCount,
            invalidCount,
            inFileDupCount,
            dbDupCount,
            strategy,
            commitPolicy,
            errorDtos
        );

        return (true, resultDto, null);
    }

    public async Task<byte[]?> GenerateErrorReportAsync(Guid batchId)
    {
        var errors = await _context.ImportRowErrors
            .AsNoTracking()
            .Where(e => e.BatchId == batchId)
            .OrderBy(e => e.RowNumber)
            .ToListAsync();

        if (errors.Count == 0)
        {
            return null;
        }

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("ImportErrors");

        ws.Cell(1, 1).Value = "Row Number";
        ws.Cell(1, 2).Value = "Column Name";
        ws.Cell(1, 3).Value = "Reason";
        ws.Cell(1, 4).Value = "Submitted Value";

        var header = ws.Range(1, 1, 1, 4);
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.LightCoral;

        for (int i = 0; i < errors.Count; i++)
        {
            var err = errors[i];
            ws.Cell(i + 2, 1).Value = err.RowNumber;
            ws.Cell(i + 2, 2).Value = err.ColumnName;
            ws.Cell(i + 2, 3).Value = err.Reason;
            ws.Cell(i + 2, 4).Value = err.RawValue ?? string.Empty;
        }

        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }

    public async Task<(bool Success, ImportExecuteResponse? Response, string? ErrorReason)> ExecuteImportAsync(Guid batchId, Guid callerUserId)
    {
        // 1. Concurrency check: Single active import lock
        var isProcessing = await _context.ImportBatches.AnyAsync(b => b.Status == ImportStatus.Processing);
        if (isProcessing)
        {
            return (false, null, "ConcurrentImportInProgress: Another import batch is currently being processed.");
        }

        var batch = await _context.ImportBatches
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(b => b.Id == batchId);
        if (batch == null)
        {
            return (false, null, "BatchNotFound");
        }

        if (batch.Status != ImportStatus.Validated)
        {
            return (false, null, $"InvalidStatus: Batch status is {batch.Status}. Only Validated batches can be executed.");
        }

        batch.Status = ImportStatus.Processing;
        await _context.SaveChangesAsync();

        var stagedRows = await _context.ImportBatchRows
            .Where(r => r.BatchId == batchId)
            .OrderBy(r => r.RowNumber)
            .ToListAsync();

        var defaultRole = await _context.Roles.FirstOrDefaultAsync(r => r.Name == "User");
        var defaultPassword = _securitySettings.DefaultUserPassword ?? "Mina@123";
        var defaultPasswordHash = _passwordPolicyService.HashPassword(defaultPassword);

        // 2. Re-validate against LIVE database state
        var candidateEmails = stagedRows.Select(r => r.Email).Distinct().ToList();
        var liveUsers = await _context.Users
            .IgnoreQueryFilters()
            .Where(u => candidateEmails.Contains(u.Email))
            .ToListAsync();

        var liveUserMap = liveUsers.ToDictionary(u => u.Email, StringComparer.OrdinalIgnoreCase);

        int createdCount = 0;
        int updatedCount = 0;
        int skippedCount = 0;
        int failedCount = 0;

        var newUsers = new List<User>();
        var newUserRoles = new List<UserRole>();

        foreach (var row in stagedRows)
        {
            if (row.Status == RowStatus.Invalid || row.Status == RowStatus.DuplicateInFile)
            {
                failedCount++;
                continue;
            }

            if (liveUserMap.TryGetValue(row.Email, out var existingUser))
            {
                if (existingUser.SoftDeletedAtUtc != null)
                {
                    failedCount++;
                    continue;
                }

                // Apply duplicate strategy against current DB state
                if (batch.DuplicateStrategy == DuplicateStrategy.Skip)
                {
                    skippedCount++;
                }
                else if (batch.DuplicateStrategy == DuplicateStrategy.Update)
                {
                    existingUser.FullName = row.FullName;
                    existingUser.UpdatedAtUtc = DateTime.UtcNow;
                    updatedCount++;
                }
                else // FailRow
                {
                    failedCount++;
                }
            }
            else
            {
                var newUser = new User
                {
                    Id = Guid.NewGuid(),
                    FullName = row.FullName,
                    Email = row.Email,
                    PasswordHash = defaultPasswordHash,
                    MustChangePassword = true,
                    IsActive = true,
                    Source = UserSource.Imported,
                    ImportBatchId = batch.Id,
                    CreatedAtUtc = DateTime.UtcNow
                };

                newUsers.Add(newUser);
                liveUserMap[newUser.Email] = newUser;

                if (defaultRole != null)
                {
                    newUserRoles.Add(new UserRole
                    {
                        UserId = newUser.Id,
                        RoleId = defaultRole.Id,
                        AssignedAtUtc = DateTime.UtcNow,
                        AssignedByUserId = callerUserId
                    });
                }

                createdCount++;
            }
        }

        // Commit policy enforcement
        if (batch.CommitPolicy == CommitPolicy.AllOrNothing && failedCount > 0)
        {
            batch.Status = ImportStatus.Failed;
            batch.FailedRows = failedCount;
            _context.ImportBatchRows.RemoveRange(stagedRows);
            await _context.SaveChangesAsync();
            return (false, null, $"CommitPolicyViolation: Batch failed under AllOrNothing policy with {failedCount} failed rows.");
        }

        _context.Users.AddRange(newUsers);
        _context.UserRoles.AddRange(newUserRoles);

        batch.CreatedRows = createdCount;
        batch.UpdatedRows = updatedCount;
        batch.FailedRows = failedCount;
        batch.Status = ImportStatus.Completed;
        batch.CompletedAtUtc = DateTime.UtcNow;

        // Purge staged rows post-completion
        _context.ImportBatchRows.RemoveRange(stagedRows);

        await _context.SaveChangesAsync();

        var caller = await _context.Users.FindAsync(callerUserId);
        if (caller != null)
        {
            await _emailService.SendImportCompletedNotificationAsync(caller.Email, batch.Id, batch.TotalRows, createdCount);
        }

        return (true, new ImportExecuteResponse(batch.Id, batch.Status, createdCount, updatedCount, skippedCount, failedCount), null);
    }

    public async Task<(bool Success, string? ErrorReason)> CancelImportAsync(Guid batchId, Guid callerUserId)
    {
        var batch = await _context.ImportBatches
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(b => b.Id == batchId);
        if (batch == null)
        {
            return (false, "BatchNotFound");
        }

        if (batch.Status == ImportStatus.Completed || batch.Status == ImportStatus.RolledBack)
        {
            return (false, "CannotCancelCompletedOrRolledBackBatch");
        }

        batch.Status = ImportStatus.Cancelled;
        batch.CancelledAtUtc = DateTime.UtcNow;

        var stagedRows = await _context.ImportBatchRows.Where(r => r.BatchId == batchId).ToListAsync();
        _context.ImportBatchRows.RemoveRange(stagedRows);

        await _context.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? ErrorReason)> RollbackImportAsync(Guid batchId, Guid callerUserId)
    {
        var batch = await _context.ImportBatches
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(b => b.Id == batchId);
        if (batch == null)
        {
            return (false, "BatchNotFound");
        }

        if (batch.Status != ImportStatus.Completed)
        {
            return (false, "OnlyCompletedBatchesCanBeRolledBack");
        }

        var createdUsers = await _context.Users
            .Where(u => u.ImportBatchId == batchId)
            .ToListAsync();

        foreach (var user in createdUsers)
        {
            user.IsRolledBack = true;
            user.IsActive = false;
            user.UpdatedAtUtc = DateTime.UtcNow;
            await _tokenService.RevokeActiveSessionAsync(user.Id, RevokeReasons.BatchRolledBack);
        }

        batch.Status = ImportStatus.RolledBack;
        await _context.SaveChangesAsync();

        return (true, null);
    }

    public async Task<List<ImportHistoryDto>> GetImportHistoryAsync()
    {
        return await _context.ImportBatches
            .AsNoTracking()
            .Include(b => b.CreatedByUser)
            .OrderByDescending(b => b.CreatedAtUtc)
            .Select(b => new ImportHistoryDto(
                b.Id,
                b.FileName,
                b.FileSize,
                b.TotalRows,
                b.CreatedRows,
                b.UpdatedRows,
                b.FailedRows,
                b.Status,
                b.CreatedAtUtc,
                b.CompletedAtUtc,
                b.CreatedByUser.FullName
            ))
            .ToListAsync();
    }

    public async Task<ImportHistoryDto?> GetImportByIdAsync(Guid id)
    {
        var b = await _context.ImportBatches
            .AsNoTracking()
            .Include(b => b.CreatedByUser)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (b == null) return null;

        return new ImportHistoryDto(
            b.Id,
            b.FileName,
            b.FileSize,
            b.TotalRows,
            b.CreatedRows,
            b.UpdatedRows,
            b.FailedRows,
            b.Status,
            b.CreatedAtUtc,
            b.CompletedAtUtc,
            b.CreatedByUser.FullName
        );
    }

    public static string SanitizeLeadingFormula(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        var trimmed = input.Trim();

        while (trimmed.Length > 0 && (trimmed[0] == '=' || trimmed[0] == '+' || trimmed[0] == '-' || trimmed[0] == '@'))
        {
            trimmed = trimmed[1..].TrimStart();
        }

        return trimmed;
    }

    private static string? TruncateRaw(string? val)
    {
        if (string.IsNullOrEmpty(val)) return null;
        return val.Length > 100 ? val[..97] + "..." : val;
    }
}
