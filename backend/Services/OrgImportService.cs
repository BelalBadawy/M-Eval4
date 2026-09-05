using System.Globalization;
using ClosedXML.Excel;
using MEval.Api.Data;
using MEval.Api.DTOs;
using MEval.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace MEval.Api.Services;

public class OrgImportService : IOrgImportService
{
    private readonly AppDbContext _context;
    private readonly IHierarchyService _hierarchyService;
    private readonly ITokenService _tokenService;
    private readonly IAuditService _auditService;

    // Single active import lock across dry-run and execute
    private static readonly SemaphoreSlim _importLock = new(1, 1);

    private const long MaxFileSizeBytes = 5 * 1024 * 1024; // 5 MB
    private const int MaxTotalRows = 5000;

    public OrgImportService(
        AppDbContext context,
        IHierarchyService hierarchyService,
        ITokenService tokenService,
        IAuditService auditService)
    {
        _context = context;
        _hierarchyService = hierarchyService;
        _tokenService = tokenService;
        _auditService = auditService;
    }

    public byte[] GenerateTemplate()
    {
        using var workbook = new XLWorkbook();

        // 1. Companies
        var wsCompanies = workbook.Worksheets.Add("Companies");
        wsCompanies.Cell(1, 1).Value = "CompanyId";
        wsCompanies.Cell(1, 2).Value = "Name";
        wsCompanies.Cell(2, 1).Value = 1;
        wsCompanies.Cell(2, 2).Value = "Acme Global Operations";

        // 2. Departments
        var wsDepts = workbook.Worksheets.Add("Departments");
        wsDepts.Cell(1, 1).Value = "DepartmentId";
        wsDepts.Cell(1, 2).Value = "CompanyId";
        wsDepts.Cell(1, 3).Value = "Name";
        wsDepts.Cell(2, 1).Value = 10;
        wsDepts.Cell(2, 2).Value = 1;
        wsDepts.Cell(2, 3).Value = "Information Technology";

        // 3. Sections
        var wsSections = workbook.Worksheets.Add("Sections");
        wsSections.Cell(1, 1).Value = "SectionId";
        wsSections.Cell(1, 2).Value = "DepartmentId";
        wsSections.Cell(1, 3).Value = "Name";
        wsSections.Cell(2, 1).Value = 100;
        wsSections.Cell(2, 2).Value = 10;
        wsSections.Cell(2, 3).Value = "Software Architecture";

        // 4. Positions
        var wsPositions = workbook.Worksheets.Add("Positions");
        wsPositions.Cell(1, 1).Value = "PositionId";
        wsPositions.Cell(1, 2).Value = "Name";
        wsPositions.Cell(1, 3).Value = "NLevel";
        wsPositions.Cell(2, 1).Value = 500;
        wsPositions.Cell(2, 2).Value = "Principal Architect";
        wsPositions.Cell(2, 3).Value = 2;

        // 5. Employees
        var wsEmployees = workbook.Worksheets.Add("Employees");
        string[] empHeaders =
        {
            "EmployeeId", "EmployeeNumber", "FullName", "Email",
            "CompanyId", "CompanyName", "DepartmentId", "DepartmentName",
            "SectionId", "SectionName", "PositionId", "PositionName", "NLevel",
            "ManagerEmployeeId", "EmploymentStatus", "HireDate", "ResignationDate",
            "IsEvaluationEligible"
        };
        for (int i = 0; i < empHeaders.Length; i++)
        {
            wsEmployees.Cell(1, i + 1).Value = empHeaders[i];
        }

        wsEmployees.Cell(2, 1).Value = 1001;
        wsEmployees.Cell(2, 2).Value = "EMP-1001";
        wsEmployees.Cell(2, 3).Value = "Alice Smith";
        wsEmployees.Cell(2, 4).Value = "alice.smith@meval.local";
        wsEmployees.Cell(2, 5).Value = 1;
        wsEmployees.Cell(2, 6).Value = "Acme Global Operations";
        wsEmployees.Cell(2, 7).Value = 10;
        wsEmployees.Cell(2, 8).Value = "Information Technology";
        wsEmployees.Cell(2, 9).Value = 100;
        wsEmployees.Cell(2, 10).Value = "Software Architecture";
        wsEmployees.Cell(2, 11).Value = 500;
        wsEmployees.Cell(2, 12).Value = "Principal Architect";
        wsEmployees.Cell(2, 13).Value = 2;
        wsEmployees.Cell(2, 14).Value = ""; // ManagerEmployeeId
        wsEmployees.Cell(2, 15).Value = 1; // Active
        wsEmployees.Cell(2, 16).Value = "2023-01-15";
        wsEmployees.Cell(2, 17).Value = "";
        wsEmployees.Cell(2, 18).Value = 1; // Eligible

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }

    public async Task<(bool Success, OrgImportDryRunResultDto? Result, string? ErrorReason, int StatusCode)> DryRunAsync(
        Stream fileStream,
        string fileName,
        long fileSize,
        int actorUserId)
    {
        if (fileSize > MaxFileSizeBytes)
        {
            return (false, null, $"File exceeds maximum size of 5 MB ({fileSize} bytes).", 400);
        }

        if (!await _importLock.WaitAsync(0))
        {
            return (false, null, "An organization import or dry-run is currently in progress. Please retry shortly.", 409);
        }

        try
        {
            var parsed = await ParseAndValidateAsync(fileStream);
            if (!parsed.Success)
            {
                return (false, null, parsed.ErrorReason, parsed.StatusCode);
            }

            var summary = parsed.Summary!;
            var isValid = summary.Errors.Count == 0;
            var totalRows = summary.CompaniesCount + summary.DepartmentsCount + summary.SectionsCount + summary.PositionsCount + summary.EmployeesTotal;

            var dryRunResult = new OrgImportDryRunResultDto(
                isValid,
                totalRows,
                summary.Errors.Count,
                summary
            );

            return (true, dryRunResult, null, 200);
        }
        finally
        {
            _importLock.Release();
        }
    }

    public async Task<(bool Success, OrgImportExecuteResponse? Response, string? ErrorReason, int StatusCode)> ExecuteAsync(
        Stream fileStream,
        string fileName,
        long fileSize,
        int actorUserId,
        string actorIpAddress)
    {
        if (fileSize > MaxFileSizeBytes)
        {
            return (false, null, $"File exceeds maximum size of 5 MB ({fileSize} bytes).", 400);
        }

        if (!await _importLock.WaitAsync(0))
        {
            return (false, null, "An organization import or dry-run is currently in progress. Please retry shortly.", 409);
        }

        try
        {
            var parsed = await ParseAndValidateAsync(fileStream);
            if (!parsed.Success)
            {
                await _auditService.LogAsync(
                    actorUserId,
                    "OrgImportFailed",
                    "Import",
                    fileName,
                    new { error = parsed.ErrorReason, fileName },
                    actorIpAddress
                );
                return (false, null, parsed.ErrorReason, parsed.StatusCode);
            }

            var parsedData = parsed.Data!;
            var summary = parsed.Summary!;

            if (summary.Errors.Count > 0)
            {
                await _auditService.LogAsync(
                    actorUserId,
                    "OrgImportFailed",
                    "Import",
                    fileName,
                    new { error = "ValidationErrorsInFile", errorCount = summary.Errors.Count, fileName },
                    actorIpAddress
                );

                var errorMsg = $"Import rejected with {summary.Errors.Count} error(s). First error: [{summary.Errors[0].SheetName} Row {summary.Errors[0].RowNumber}] {summary.Errors[0].Reason}";
                return (false, new OrgImportExecuteResponse(false, errorMsg, summary), errorMsg, 400);
            }

            // Atomic Single Transaction (AllOrNothing)
            using var tx = _context.Database.IsRelational() ? await _context.Database.BeginTransactionAsync() : null;
            try
            {
                // 1. Upsert Companies
                foreach (var c in parsedData.Companies.Values)
                {
                    var existing = await _context.Companies.FindAsync(c.CompanyId);
                    if (existing == null)
                    {
                        _context.Companies.Add(new Company
                        {
                            CompanyId = c.CompanyId,
                            Name = c.Name,
                            IsActive = true,
                            CreatedAtUtc = DateTime.UtcNow,
                            UpdatedAtUtc = DateTime.UtcNow
                        });
                    }
                    else
                    {
                        existing.Name = c.Name;
                        existing.IsActive = true;
                        existing.UpdatedAtUtc = DateTime.UtcNow;
                    }
                }
                await _context.SaveChangesAsync();

                // 2. Upsert Departments
                foreach (var d in parsedData.Departments.Values)
                {
                    var existing = await _context.Departments.FindAsync(d.DepartmentId);
                    if (existing == null)
                    {
                        _context.Departments.Add(new Department
                        {
                            DepartmentId = d.DepartmentId,
                            CompanyId = d.CompanyId,
                            Name = d.Name,
                            IsActive = true,
                            CreatedAtUtc = DateTime.UtcNow,
                            UpdatedAtUtc = DateTime.UtcNow
                        });
                    }
                    else
                    {
                        existing.Name = d.Name;
                        existing.IsActive = true;
                        existing.UpdatedAtUtc = DateTime.UtcNow;
                    }
                }
                await _context.SaveChangesAsync();

                // 3. Upsert Sections
                foreach (var s in parsedData.Sections.Values)
                {
                    var existing = await _context.Sections.FindAsync(s.SectionId);
                    if (existing == null)
                    {
                        _context.Sections.Add(new Section
                        {
                            SectionId = s.SectionId,
                            DepartmentId = s.DepartmentId,
                            Name = s.Name,
                            IsActive = true,
                            CreatedAtUtc = DateTime.UtcNow,
                            UpdatedAtUtc = DateTime.UtcNow
                        });
                    }
                    else
                    {
                        existing.Name = s.Name;
                        existing.IsActive = true;
                        existing.UpdatedAtUtc = DateTime.UtcNow;
                    }
                }
                await _context.SaveChangesAsync();

                // 4. Upsert Positions
                foreach (var p in parsedData.Positions.Values)
                {
                    var existing = await _context.Positions.FindAsync(p.PositionId);
                    if (existing == null)
                    {
                        _context.Positions.Add(new Position
                        {
                            PositionId = p.PositionId,
                            Name = p.Name,
                            NLevel = p.NLevel,
                            IsActive = true,
                            CreatedAtUtc = DateTime.UtcNow,
                            UpdatedAtUtc = DateTime.UtcNow
                        });
                    }
                    else
                    {
                        existing.Name = p.Name;
                        existing.NLevel = p.NLevel;
                        existing.IsActive = true;
                        existing.UpdatedAtUtc = DateTime.UtcNow;
                    }
                }
                await _context.SaveChangesAsync();

                // 5. Reset absent employees' eligibility (ORG-013 / ORG-022 / ORG-028)
                var fileEmpIds = parsedData.Employees.Select(e => e.EmployeeId).ToHashSet();
                var absentEligible = await _context.Employees
                    .Where(e => !fileEmpIds.Contains(e.EmployeeId) && e.IsEvaluationEligible)
                    .ToListAsync();

                foreach (var absent in absentEligible)
                {
                    absent.IsEvaluationEligible = false;
                    absent.UpdatedAtUtc = DateTime.UtcNow;
                }
                await _context.SaveChangesAsync();

                // 6. Upsert Employees (with in-memory manager resolution, file flag, & offboarding cascade)
                int createdCount = 0;
                int updatedCount = 0;
                int offboardedCount = 0;

                foreach (var empRow in parsedData.Employees)
                {
                    var existing = await _context.Employees.FindAsync(empRow.EmployeeId);
                    if (existing == null)
                    {
                        // Created
                        var newEmp = new Employee
                        {
                            EmployeeId = empRow.EmployeeId,
                            EmployeeNumber = empRow.EmployeeNumber,
                            FullName = empRow.FullName,
                            Email = empRow.Email,
                            CompanyId = empRow.CompanyId,
                            DepartmentId = empRow.DepartmentId,
                            SectionId = empRow.SectionId,
                            PositionId = empRow.PositionId,
                            DirectManagerId = empRow.ManagerEmployeeId,
                            EmploymentStatus = empRow.EmploymentStatus,
                            HireDate = empRow.HireDate,
                            ResignationDate = empRow.ResignationDate,
                            IsEvaluationEligible = empRow.IsEvaluationEligible, // File value
                            IsActive = true,                                     // Default local
                            CreatedAtUtc = DateTime.UtcNow,
                            UpdatedAtUtc = DateTime.UtcNow
                        };
                        _context.Employees.Add(newEmp);
                        createdCount++;
                    }
                    else
                    {
                        // Updated — preserve local columns: UserId, IsActive; IsEvaluationEligible is owned by file (P1)
                        var previousStatus = existing.EmploymentStatus;
                        var newStatus = empRow.EmploymentStatus;

                        existing.FullName = empRow.FullName;
                        existing.Email = empRow.Email;
                        existing.CompanyId = empRow.CompanyId;
                        existing.DepartmentId = empRow.DepartmentId;
                        existing.SectionId = empRow.SectionId;
                        existing.PositionId = empRow.PositionId;
                        existing.DirectManagerId = empRow.ManagerEmployeeId;
                        existing.EmploymentStatus = newStatus;
                        existing.HireDate = empRow.HireDate;
                        existing.ResignationDate = empRow.ResignationDate;
                        existing.IsEvaluationEligible = empRow.IsEvaluationEligible; // Overwritten from file!
                        existing.UpdatedAtUtc = DateTime.UtcNow;

                        // Offboarding cascade (ORG-026)
                        if (previousStatus == EmploymentStatus.Active &&
                            (newStatus == EmploymentStatus.Resigned || newStatus == EmploymentStatus.Terminated) &&
                            existing.UserId.HasValue)
                        {
                            var linkedUser = await _context.Users.FindAsync(existing.UserId.Value);
                            if (linkedUser != null)
                            {
                                linkedUser.IsActive = false;
                                linkedUser.UpdatedAtUtc = DateTime.UtcNow;

                                await _tokenService.RevokeActiveSessionAsync(
                                    linkedUser.Id,
                                    RevokeReasons.EmployeeOffboarded,
                                    actorIpAddress
                                );

                                await _auditService.LogAsync(
                                    actorUserId,
                                    "EmployeeOffboarded",
                                    "Employee",
                                    existing.EmployeeId.ToString(),
                                    new
                                    {
                                        employeeId = existing.EmployeeId,
                                        userId = linkedUser.Id,
                                        userEmail = linkedUser.Email,
                                        newStatus = newStatus.ToString()
                                    },
                                    actorIpAddress
                                );

                                offboardedCount++;
                            }
                        }

                        updatedCount++;
                    }
                }

                await _context.SaveChangesAsync();
                if (tx != null)
                {
                    await tx.CommitAsync();
                }

                summary = summary with
                {
                    EmployeesCreated = createdCount,
                    EmployeesUpdated = updatedCount,
                    OffboardedCascadeCount = offboardedCount
                };

                await _auditService.LogAsync(
                    actorUserId,
                    "OrgImportExecuted",
                    "Import",
                    fileName,
                    new
                    {
                        fileName,
                        createdCount,
                        updatedCount,
                        offboardedCount,
                        anomalies = summary.AnomaliesFlagged
                    },
                    actorIpAddress
                );

                return (true, new OrgImportExecuteResponse(true, "Organization structure and employees imported successfully.", summary), null, 200);
            }
            catch (Exception ex)
            {
                if (tx != null)
                {
                    await tx.RollbackAsync();
                }

                await _auditService.LogAsync(
                    actorUserId,
                    "OrgImportFailed",
                    "Import",
                    fileName,
                    new { error = ex.Message, fileName },
                    actorIpAddress
                );

                return (false, null, $"Database transaction failed: {ex.Message}", 500);
            }
        }
        finally
        {
            _importLock.Release();
        }
    }

    private class ParsedData
    {
        public Dictionary<int, CompanyRow> Companies { get; } = new();
        public Dictionary<int, DepartmentRow> Departments { get; } = new();
        public Dictionary<int, SectionRow> Sections { get; } = new();
        public Dictionary<int, PositionRow> Positions { get; } = new();
        public List<EmployeeRow> Employees { get; } = new();
    }

    private record CompanyRow(int CompanyId, string Name);
    private record DepartmentRow(int DepartmentId, int CompanyId, string Name);
    private record SectionRow(int SectionId, int DepartmentId, string Name);
    private record PositionRow(int PositionId, string Name, int NLevel);
    private record EmployeeRow(
        int EmployeeId,
        string EmployeeNumber,
        string FullName,
        string? Email,
        int CompanyId,
        string? CompanyName,
        int? DepartmentId,
        string? DepartmentName,
        int? SectionId,
        string? SectionName,
        int PositionId,
        string? PositionName,
        int? NLevel,
        int? ManagerEmployeeId,
        EmploymentStatus EmploymentStatus,
        DateOnly HireDate,
        DateOnly? ResignationDate,
        bool IsEvaluationEligible
    );

    private async Task<(bool Success, ParsedData? Data, OrgImportSummaryDto? Summary, string? ErrorReason, int StatusCode)> ParseAndValidateAsync(Stream fileStream)
    {
        using var memoryStream = new MemoryStream();
        await fileStream.CopyToAsync(memoryStream);
        memoryStream.Position = 0;

        IXLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(memoryStream);
        }
        catch (Exception ex)
        {
            return (false, null, null, $"Invalid Excel file: {ex.Message}", 400);
        }

        using (workbook)
        {
            var errors = new List<OrgImportRowErrorDto>();
            var data = new ParsedData();

            int totalRowCount = 0;

            // 1. Companies Sheet
            if (!workbook.TryGetWorksheet("Companies", out var wsCompanies))
            {
                return (false, null, null, "Missing required worksheet 'Companies'.", 400);
            }
            var compRows = wsCompanies.RangeUsed()?.RowsUsed().Skip(1) ?? Enumerable.Empty<IXLRangeRow>();
            foreach (var row in compRows)
            {
                totalRowCount++;
                int rNum = row.RowNumber();
                var idStr = row.Cell(1).GetString().Trim();
                var name = ExcelImportService.SanitizeLeadingFormula(row.Cell(2).GetString().Trim());

                if (!int.TryParse(idStr, out var compId) || compId <= 0)
                {
                    errors.Add(new OrgImportRowErrorDto("Companies", rNum, idStr, "CompanyId", "Invalid or missing integer CompanyId.", idStr));
                    continue;
                }
                if (string.IsNullOrWhiteSpace(name))
                {
                    errors.Add(new OrgImportRowErrorDto("Companies", rNum, idStr, "Name", "Company Name is required."));
                    continue;
                }

                if (data.Companies.TryGetValue(compId, out var existingC))
                {
                    if (existingC.Name != name)
                    {
                        errors.Add(new OrgImportRowErrorDto("Companies", rNum, compId.ToString(), "Name", $"In-file duplicate CompanyId {compId} with differing name '{name}' (expected '{existingC.Name}')."));
                    }
                }
                else
                {
                    data.Companies[compId] = new CompanyRow(compId, name);
                }
            }

            // 2. Departments Sheet
            if (!workbook.TryGetWorksheet("Departments", out var wsDepts))
            {
                return (false, null, null, "Missing required worksheet 'Departments'.", 400);
            }
            var deptRows = wsDepts.RangeUsed()?.RowsUsed().Skip(1) ?? Enumerable.Empty<IXLRangeRow>();
            foreach (var row in deptRows)
            {
                totalRowCount++;
                int rNum = row.RowNumber();
                var deptIdStr = row.Cell(1).GetString().Trim();
                var compIdStr = row.Cell(2).GetString().Trim();
                var name = ExcelImportService.SanitizeLeadingFormula(row.Cell(3).GetString().Trim());

                if (!int.TryParse(deptIdStr, out var deptId) || deptId <= 0)
                {
                    errors.Add(new OrgImportRowErrorDto("Departments", rNum, deptIdStr, "DepartmentId", "Invalid integer DepartmentId.", deptIdStr));
                    continue;
                }
                if (!int.TryParse(compIdStr, out var compId) || compId <= 0)
                {
                    errors.Add(new OrgImportRowErrorDto("Departments", rNum, deptIdStr, "CompanyId", "Invalid integer CompanyId.", compIdStr));
                    continue;
                }
                if (string.IsNullOrWhiteSpace(name))
                {
                    errors.Add(new OrgImportRowErrorDto("Departments", rNum, deptIdStr, "Name", "Department Name is required."));
                    continue;
                }

                // Verify company exists
                if (!data.Companies.ContainsKey(compId))
                {
                    // Check if exists in DB
                    var inDb = await _context.Companies.AnyAsync(c => c.CompanyId == compId);
                    if (!inDb)
                    {
                        errors.Add(new OrgImportRowErrorDto("Departments", rNum, deptIdStr, "CompanyId", $"Referenced CompanyId {compId} does not exist in file or database.", compIdStr));
                        continue;
                    }
                }

                // Check in-file duplicate / re-parenting
                if (data.Departments.TryGetValue(deptId, out var existingD))
                {
                    if (existingD.CompanyId != compId)
                    {
                        errors.Add(new OrgImportRowErrorDto("Departments", rNum, deptIdStr, "CompanyId", $"In-file re-parenting of DepartmentId {deptId} to CompanyId {compId} is forbidden."));
                    }
                }
                else
                {
                    // Check DB re-parenting
                    var dbDept = await _context.Departments.FindAsync(deptId);
                    if (dbDept != null && dbDept.CompanyId != compId)
                    {
                        errors.Add(new OrgImportRowErrorDto("Departments", rNum, deptIdStr, "CompanyId", $"Re-parenting DepartmentId {deptId} (belongs to Company {dbDept.CompanyId}) to Company {compId} is forbidden."));
                    }
                    data.Departments[deptId] = new DepartmentRow(deptId, compId, name);
                }
            }

            // 3. Sections Sheet
            if (!workbook.TryGetWorksheet("Sections", out var wsSections))
            {
                return (false, null, null, "Missing required worksheet 'Sections'.", 400);
            }
            var sectionRows = wsSections.RangeUsed()?.RowsUsed().Skip(1) ?? Enumerable.Empty<IXLRangeRow>();
            foreach (var row in sectionRows)
            {
                totalRowCount++;
                int rNum = row.RowNumber();
                var secIdStr = row.Cell(1).GetString().Trim();
                var deptIdStr = row.Cell(2).GetString().Trim();
                var name = ExcelImportService.SanitizeLeadingFormula(row.Cell(3).GetString().Trim());

                if (!int.TryParse(secIdStr, out var secId) || secId <= 0)
                {
                    errors.Add(new OrgImportRowErrorDto("Sections", rNum, secIdStr, "SectionId", "Invalid integer SectionId.", secIdStr));
                    continue;
                }
                if (!int.TryParse(deptIdStr, out var deptId) || deptId <= 0)
                {
                    errors.Add(new OrgImportRowErrorDto("Sections", rNum, secIdStr, "DepartmentId", "Invalid integer DepartmentId.", deptIdStr));
                    continue;
                }
                if (string.IsNullOrWhiteSpace(name))
                {
                    errors.Add(new OrgImportRowErrorDto("Sections", rNum, secIdStr, "Name", "Section Name is required."));
                    continue;
                }

                // Verify department exists
                if (!data.Departments.ContainsKey(deptId))
                {
                    var inDb = await _context.Departments.AnyAsync(d => d.DepartmentId == deptId);
                    if (!inDb)
                    {
                        errors.Add(new OrgImportRowErrorDto("Sections", rNum, secIdStr, "DepartmentId", $"Referenced DepartmentId {deptId} does not exist in file or database.", deptIdStr));
                        continue;
                    }
                }

                // Check DB re-parenting
                var dbSec = await _context.Sections.FindAsync(secId);
                if (dbSec != null && dbSec.DepartmentId != deptId)
                {
                    errors.Add(new OrgImportRowErrorDto("Sections", rNum, secIdStr, "DepartmentId", $"Re-parenting SectionId {secId} (belongs to Department {dbSec.DepartmentId}) to Department {deptId} is forbidden."));
                }
                data.Sections[secId] = new SectionRow(secId, deptId, name);
            }

            // 4. Positions Sheet
            if (!workbook.TryGetWorksheet("Positions", out var wsPositions))
            {
                return (false, null, null, "Missing required worksheet 'Positions'.", 400);
            }
            var posRows = wsPositions.RangeUsed()?.RowsUsed().Skip(1) ?? Enumerable.Empty<IXLRangeRow>();
            foreach (var row in posRows)
            {
                totalRowCount++;
                int rNum = row.RowNumber();
                var posIdStr = row.Cell(1).GetString().Trim();
                var name = ExcelImportService.SanitizeLeadingFormula(row.Cell(2).GetString().Trim());
                var nLevelStr = row.Cell(3).GetString().Trim();

                if (!int.TryParse(posIdStr, out var posId) || posId <= 0)
                {
                    errors.Add(new OrgImportRowErrorDto("Positions", rNum, posIdStr, "PositionId", "Invalid integer PositionId.", posIdStr));
                    continue;
                }
                if (string.IsNullOrWhiteSpace(name))
                {
                    errors.Add(new OrgImportRowErrorDto("Positions", rNum, posIdStr, "Name", "Position Name is required."));
                    continue;
                }
                if (!int.TryParse(nLevelStr, out var nLevel) || nLevel < 1)
                {
                    errors.Add(new OrgImportRowErrorDto("Positions", rNum, posIdStr, "NLevel", "NLevel must be an integer >= 1.", nLevelStr));
                    continue;
                }

                data.Positions[posId] = new PositionRow(posId, name, nLevel);
            }

            // 5. Employees Sheet
            if (!workbook.TryGetWorksheet("Employees", out var wsEmployees))
            {
                return (false, null, null, "Missing required worksheet 'Employees'.", 400);
            }
            var empRows = wsEmployees.RangeUsed()?.RowsUsed().Skip(1) ?? Enumerable.Empty<IXLRangeRow>();

            var seenEmployeeNumbers = new Dictionary<string, int>(); // number -> employeeId
            var seenEmails = new HashSet<string>();
            int anomaliesCount = 0;
            int flagSetEligible = 0;
            int flagSetIneligible = 0;

            foreach (var row in empRows)
            {
                totalRowCount++;
                int rNum = row.RowNumber();

                var empIdStr = row.Cell(1).GetString().Trim();
                var empNum = row.Cell(2).GetString().Trim();
                var fullName = ExcelImportService.SanitizeLeadingFormula(row.Cell(3).GetString().Trim());
                var emailRaw = row.Cell(4).GetString().Trim();
                var email = string.IsNullOrWhiteSpace(emailRaw) ? null : emailRaw.ToLowerInvariant();

                var compIdStr = row.Cell(5).GetString().Trim();
                var compNameInfo = row.Cell(6).GetString().Trim();
                var deptIdStr = row.Cell(7).GetString().Trim();
                var deptNameInfo = row.Cell(8).GetString().Trim();
                var secIdStr = row.Cell(9).GetString().Trim();
                var secNameInfo = row.Cell(10).GetString().Trim();
                var posIdStr = row.Cell(11).GetString().Trim();
                var posNameInfo = row.Cell(12).GetString().Trim();
                var nLevelStr = row.Cell(13).GetString().Trim();

                var mgrIdStr = row.Cell(14).GetString().Trim();
                var statusStr = row.Cell(15).GetString().Trim();
                var hireDateStr = row.Cell(16).GetString().Trim();
                var resDateStr = row.Cell(17).GetString().Trim();
                var eligStr = row.Cell(18).GetString().Trim();

                bool isEvaluationEligible;
                if (eligStr == "1" || string.Equals(eligStr, "true", StringComparison.OrdinalIgnoreCase))
                {
                    isEvaluationEligible = true;
                    flagSetEligible++;
                }
                else if (eligStr == "0" || string.Equals(eligStr, "false", StringComparison.OrdinalIgnoreCase))
                {
                    isEvaluationEligible = false;
                    flagSetIneligible++;
                }
                else
                {
                    errors.Add(new OrgImportRowErrorDto("Employees", rNum, empIdStr, "IsEvaluationEligible", "Value must be 1 (Eligible) or 0 (Ineligible).", eligStr));
                    continue;
                }

                // Validation: Required IDs
                if (!int.TryParse(empIdStr, out var empId) || empId <= 0)
                {
                    errors.Add(new OrgImportRowErrorDto("Employees", rNum, empIdStr, "EmployeeId", "Invalid integer EmployeeId.", empIdStr));
                    continue;
                }
                if (string.IsNullOrWhiteSpace(empNum))
                {
                    errors.Add(new OrgImportRowErrorDto("Employees", rNum, empIdStr, "EmployeeNumber", "EmployeeNumber is required."));
                    continue;
                }
                if (string.IsNullOrWhiteSpace(fullName))
                {
                    errors.Add(new OrgImportRowErrorDto("Employees", rNum, empIdStr, "FullName", "FullName is required."));
                    continue;
                }
                if (!int.TryParse(compIdStr, out var compId) || compId <= 0)
                {
                    errors.Add(new OrgImportRowErrorDto("Employees", rNum, empIdStr, "CompanyId", "Invalid integer CompanyId.", compIdStr));
                    continue;
                }
                if (!int.TryParse(posIdStr, out var posId) || posId <= 0)
                {
                    errors.Add(new OrgImportRowErrorDto("Employees", rNum, empIdStr, "PositionId", "Invalid integer PositionId.", posIdStr));
                    continue;
                }

                int? deptId = int.TryParse(deptIdStr, out var dId) && dId > 0 ? dId : null;
                int? secId = int.TryParse(secIdStr, out var sId) && sId > 0 ? sId : null;
                int? mgrId = int.TryParse(mgrIdStr, out var mId) && mId > 0 ? mId : null;

                // Status
                if (!int.TryParse(statusStr, out var stVal) || !Enum.IsDefined(typeof(EmploymentStatus), stVal))
                {
                    errors.Add(new OrgImportRowErrorDto("Employees", rNum, empIdStr, "EmploymentStatus", "EmploymentStatus must be 1 (Active), 2 (Resigned), or 3 (Terminated).", statusStr));
                    continue;
                }
                var status = (EmploymentStatus)stVal;

                // Dates
                if (!DateOnly.TryParseExact(hireDateStr, new[] { "yyyy-MM-dd", "M/d/yyyy", "MM/dd/yyyy" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var hireDate))
                {
                    if (row.Cell(16).TryGetValue<DateTime>(out var dtHire))
                    {
                        hireDate = DateOnly.FromDateTime(dtHire);
                    }
                    else
                    {
                        errors.Add(new OrgImportRowErrorDto("Employees", rNum, empIdStr, "HireDate", "Valid HireDate (YYYY-MM-DD) is required.", hireDateStr));
                        continue;
                    }
                }

                DateOnly? resDate = null;
                if (!string.IsNullOrWhiteSpace(resDateStr))
                {
                    if (DateOnly.TryParseExact(resDateStr, new[] { "yyyy-MM-dd", "M/d/yyyy", "MM/dd/yyyy" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var rd))
                    {
                        resDate = rd;
                    }
                    else if (row.Cell(17).TryGetValue<DateTime>(out var dtRes))
                    {
                        resDate = DateOnly.FromDateTime(dtRes);
                    }
                    else
                    {
                        errors.Add(new OrgImportRowErrorDto("Employees", rNum, empIdStr, "ResignationDate", "Invalid ResignationDate format (YYYY-MM-DD expected).", resDateStr));
                        continue;
                    }
                }

                // Check Constraint CK_Empl_StatusDates
                if (status == EmploymentStatus.Active && resDate.HasValue)
                {
                    errors.Add(new OrgImportRowErrorDto("Employees", rNum, empIdStr, "ResignationDate", "Active employee must have NULL ResignationDate."));
                    continue;
                }
                if (status != EmploymentStatus.Active && !resDate.HasValue)
                {
                    errors.Add(new OrgImportRowErrorDto("Employees", rNum, empIdStr, "ResignationDate", "Resigned/Terminated employee must have a non-null ResignationDate."));
                    continue;
                }

                // Check Constraint CK_Empl_ResignationAfterHire
                if (resDate.HasValue && resDate.Value < hireDate)
                {
                    errors.Add(new OrgImportRowErrorDto("Employees", rNum, empIdStr, "ResignationDate", $"ResignationDate ({resDate.Value}) cannot be earlier than HireDate ({hireDate})."));
                    continue;
                }

                // Check Constraint CK_Empl_SectionNeedsDept
                if (secId.HasValue && !deptId.HasValue)
                {
                    errors.Add(new OrgImportRowErrorDto("Employees", rNum, empIdStr, "SectionId", "SectionId cannot be specified when DepartmentId is NULL."));
                    continue;
                }

                // Verify placement: Department in Company
                if (deptId.HasValue)
                {
                    if (data.Departments.TryGetValue(deptId.Value, out var dRow))
                    {
                        if (dRow.CompanyId != compId)
                        {
                            errors.Add(new OrgImportRowErrorDto("Employees", rNum, empIdStr, "DepartmentId", $"Department {deptId.Value} belongs to Company {dRow.CompanyId}, not Company {compId}."));
                            continue;
                        }
                    }
                    else
                    {
                        var dbDept = await _context.Departments.FindAsync(deptId.Value);
                        if (dbDept == null || dbDept.CompanyId != compId)
                        {
                            errors.Add(new OrgImportRowErrorDto("Employees", rNum, empIdStr, "DepartmentId", $"Department {deptId.Value} does not belong to Company {compId}."));
                            continue;
                        }
                    }
                }

                // Verify placement: Section in Department
                if (secId.HasValue && deptId.HasValue)
                {
                    if (data.Sections.TryGetValue(secId.Value, out var sRow))
                    {
                        if (sRow.DepartmentId != deptId.Value)
                        {
                            errors.Add(new OrgImportRowErrorDto("Employees", rNum, empIdStr, "SectionId", $"Section {secId.Value} belongs to Department {sRow.DepartmentId}, not Department {deptId.Value}."));
                            continue;
                        }
                    }
                    else
                    {
                        var dbSec = await _context.Sections.FindAsync(secId.Value);
                        if (dbSec == null || dbSec.DepartmentId != deptId.Value)
                        {
                            errors.Add(new OrgImportRowErrorDto("Employees", rNum, empIdStr, "SectionId", $"Section {secId.Value} does not belong to Department {deptId.Value}."));
                            continue;
                        }
                    }
                }

                // Informational columns validation (must match resolved lookup)
                if (!string.IsNullOrWhiteSpace(compNameInfo))
                {
                    var resolvedCompName = data.Companies.TryGetValue(compId, out var cVal) ? cVal.Name : (await _context.Companies.FindAsync(compId))?.Name;
                    if (resolvedCompName != null && !resolvedCompName.Equals(compNameInfo, StringComparison.OrdinalIgnoreCase))
                    {
                        errors.Add(new OrgImportRowErrorDto("Employees", rNum, empIdStr, "CompanyName", $"Informational CompanyName '{compNameInfo}' does not match resolved Company '{resolvedCompName}'."));
                        continue;
                    }
                }

                // Position & NLevel check
                int resolvedNLevel = 0;
                string? resolvedPosName = null;
                if (data.Positions.TryGetValue(posId, out var pRow))
                {
                    resolvedNLevel = pRow.NLevel;
                    resolvedPosName = pRow.Name;
                }
                else
                {
                    var dbPos = await _context.Positions.FindAsync(posId);
                    if (dbPos != null)
                    {
                        resolvedNLevel = dbPos.NLevel;
                        resolvedPosName = dbPos.Name;
                    }
                    else
                    {
                        errors.Add(new OrgImportRowErrorDto("Employees", rNum, empIdStr, "PositionId", $"Position {posId} does not exist in file or database."));
                        continue;
                    }
                }

                if (!string.IsNullOrWhiteSpace(posNameInfo) && !resolvedPosName.Equals(posNameInfo, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add(new OrgImportRowErrorDto("Employees", rNum, empIdStr, "PositionName", $"Informational PositionName '{posNameInfo}' does not match Position {posId} ('{resolvedPosName}')."));
                    continue;
                }

                if (int.TryParse(nLevelStr, out var nlInfo) && nlInfo != resolvedNLevel)
                {
                    errors.Add(new OrgImportRowErrorDto("Employees", rNum, empIdStr, "NLevel", $"Informational NLevel {nlInfo} does not match Position {posId} (NLevel {resolvedNLevel})."));
                    continue;
                }

                // Check NLevel = 1 with Manager anomaly (Accepted and flagged)
                if (resolvedNLevel == 1 && mgrId.HasValue)
                {
                    anomaliesCount++;
                }

                // Immutable business key check (ORG-005)
                var existingEmp = await _context.Employees.FindAsync(empId);
                if (existingEmp != null && existingEmp.EmployeeNumber != empNum)
                {
                    errors.Add(new OrgImportRowErrorDto("Employees", rNum, empIdStr, "EmployeeNumber", $"Cannot change immutable EmployeeNumber for existing EmployeeId {empId} from '{existingEmp.EmployeeNumber}' to '{empNum}'."));
                    continue;
                }

                // Duplicate EmployeeNumber check
                if (seenEmployeeNumbers.TryGetValue(empNum, out var prevEmpId) && prevEmpId != empId)
                {
                    errors.Add(new OrgImportRowErrorDto("Employees", rNum, empIdStr, "EmployeeNumber", $"Duplicate EmployeeNumber '{empNum}' used by EmployeeId {prevEmpId} and {empId}."));
                    continue;
                }
                seenEmployeeNumbers[empNum] = empId;

                // Active email uniqueness check
                if (!string.IsNullOrWhiteSpace(email))
                {
                    if (seenEmails.Contains(email))
                    {
                        errors.Add(new OrgImportRowErrorDto("Employees", rNum, empIdStr, "Email", $"Duplicate active email '{email}' in import file."));
                        continue;
                    }
                    seenEmails.Add(email);

                    // Check DB email collision
                    var dbEmailInUse = await _context.Employees
                        .AnyAsync(e => e.Email == email && e.IsActive && e.EmployeeId != empId);
                    if (dbEmailInUse)
                    {
                        errors.Add(new OrgImportRowErrorDto("Employees", rNum, empIdStr, "Email", $"Email '{email}' is already in use by another active employee."));
                        continue;
                    }
                }

                data.Employees.Add(new EmployeeRow(
                    empId,
                    empNum,
                    fullName,
                    email,
                    compId,
                    compNameInfo,
                    deptId,
                    deptNameInfo,
                    secId,
                    secNameInfo,
                    posId,
                    posNameInfo,
                    resolvedNLevel,
                    mgrId,
                    status,
                    hireDate,
                    resDate,
                    isEvaluationEligible
                ));
            }

            if (totalRowCount > MaxTotalRows)
            {
                return (false, null, null, $"Total rows across all sheets ({totalRowCount}) exceeds the maximum limit of {MaxTotalRows}.", 400);
            }

            // 6. Overlaid Graph Cycle & Manager Integrity Check
            var overlaidGraph = new Dictionary<int, int?>();
            var empInfoLookup = new Dictionary<int, (int CompanyId, EmploymentStatus Status, bool IsActive)>();

            foreach (var e in data.Employees)
            {
                overlaidGraph[e.EmployeeId] = e.ManagerEmployeeId;
                empInfoLookup[e.EmployeeId] = (e.CompanyId, e.EmploymentStatus, true);
            }

            foreach (var e in data.Employees)
            {
                if (e.ManagerEmployeeId.HasValue)
                {
                    int mgrId = e.ManagerEmployeeId.Value;

                    // Direct self loop
                    if (e.EmployeeId == mgrId)
                    {
                        errors.Add(new OrgImportRowErrorDto("Employees", 0, e.EmployeeId.ToString(), "ManagerEmployeeId", "An employee cannot be their own direct manager."));
                        continue;
                    }

                    // Cycle detection on overlaid graph
                    var (hasCycle, cyclePath) = await _hierarchyService.DetectCycleAsync(e.EmployeeId, mgrId, overlaidGraph);
                    if (hasCycle)
                    {
                        errors.Add(new OrgImportRowErrorDto("Employees", 0, e.EmployeeId.ToString(), "ManagerEmployeeId", $"Cyclical reporting relationship detected: {cyclePath}"));
                        continue;
                    }

                    // Manager link validation
                    var (isValid, errMsg) = await _hierarchyService.ValidateManagerLinkAsync(e.EmployeeId, mgrId, empInfoLookup);
                    if (!isValid)
                    {
                        errors.Add(new OrgImportRowErrorDto("Employees", 0, e.EmployeeId.ToString(), "ManagerEmployeeId", errMsg ?? "Invalid manager link."));
                    }
                }
            }

            var fileEmpIds = data.Employees.Select(e => e.EmployeeId).ToHashSet();
            int absentResetCount = await _context.Employees
                .CountAsync(e => !fileEmpIds.Contains(e.EmployeeId) && e.IsEvaluationEligible);

            var summary = new OrgImportSummaryDto(
                data.Companies.Count,
                data.Departments.Count,
                data.Sections.Count,
                data.Positions.Count,
                data.Employees.Count,
                0,
                0,
                0,
                errors.Count,
                0,
                anomaliesCount,
                absentResetCount,
                flagSetEligible,
                flagSetIneligible,
                errors
            );

            return (true, data, summary, null, 200);
        }
    }
}
