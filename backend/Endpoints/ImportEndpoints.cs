using System.Security.Claims;
using MEval.Api.DTOs;
using MEval.Api.Models;
using MEval.Api.Security;
using MEval.Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace MEval.Api.Endpoints;

public static class ImportEndpoints
{
    public static IEndpointRouteBuilder MapImportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/imports")
            .WithTags("Imports")
            .RequireAuthorization()
            .AddEndpointFilter<FirstLoginGatewayFilter>();

        group.MapGet("/template", (IExcelImportService importService) =>
        {
            var bytes = importService.GenerateTemplate();
            return Results.File(
                bytes,
                contentType: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileDownloadName: "user_import_template.xlsx");
        })
        .RequirePermission("users.import")
        .WithName("GetImportTemplate")
        .WithSummary("Download the Excel user import template");

        group.MapPost("/dry-run", async (
            IFormFile file,
            [FromForm] DuplicateStrategy? duplicateStrategy,
            [FromForm] CommitPolicy? commitPolicy,
            IExcelImportService importService,
            HttpContext httpContext) =>
        {
            if (file == null || file.Length == 0)
            {
                return Results.BadRequest(new { error = "InvalidFile", message = "An Excel (.xlsx) file is required." });
            }

            var callerId = GetUserId(httpContext.User);
            if (callerId == null) return Results.Unauthorized();

            using var stream = file.OpenReadStream();
            var strategy = duplicateStrategy ?? DuplicateStrategy.Skip;
            var policy = commitPolicy ?? CommitPolicy.PartialValidOnly;

            var (success, result, error) = await importService.DryRunImportAsync(
                stream,
                file.FileName,
                file.Length,
                strategy,
                policy,
                callerId.Value);

            if (!success)
            {
                return Results.BadRequest(new { error = error ?? "DryRunFailed", message = error });
            }

            return Results.Ok(result);
        })
        .RequirePermission("users.import")
        .DisableAntiforgery()
        .WithName("DryRunImport")
        .WithSummary("Validate and dry-run user import file, staging rows in database");

        group.MapPost("/{id:guid}/execute", async (
            Guid id,
            IExcelImportService importService,
            HttpContext httpContext) =>
        {
            var callerId = GetUserId(httpContext.User);
            if (callerId == null) return Results.Unauthorized();

            var (success, response, error) = await importService.ExecuteImportAsync(id, callerId.Value);
            if (!success)
            {
                if (error?.Contains("ConcurrentImportInProgress") == true)
                {
                    return Results.Conflict(new { error = "ConcurrentImportInProgress", message = error });
                }

                if (error == "BatchNotFound")
                {
                    return Results.NotFound(new { error = "BatchNotFound", message = "Import batch not found." });
                }

                return Results.BadRequest(new { error = error ?? "ExecutionFailed", message = error });
            }

            return Results.Ok(response);
        })
        .RequirePermission("users.import")
        .WithName("ExecuteImport")
        .WithSummary("Execute validated import batch with live DB re-validation");

        group.MapPost("/{id:guid}/cancel", async (
            Guid id,
            IExcelImportService importService,
            HttpContext httpContext) =>
        {
            var callerId = GetUserId(httpContext.User);
            if (callerId == null) return Results.Unauthorized();

            var (success, error) = await importService.CancelImportAsync(id, callerId.Value);
            if (!success)
            {
                return error == "BatchNotFound"
                    ? Results.NotFound(new { error = "BatchNotFound", message = "Import batch not found." })
                    : Results.BadRequest(new { error = error ?? "CancelFailed", message = error });
            }

            return Results.Ok(new { message = "Import batch cancelled successfully." });
        })
        .RequirePermission("users.import")
        .WithName("CancelImport")
        .WithSummary("Cancel pending or validated import batch");

        group.MapPost("/{id:guid}/rollback", async (
            Guid id,
            IExcelImportService importService,
            HttpContext httpContext) =>
        {
            var callerId = GetUserId(httpContext.User);
            if (callerId == null) return Results.Unauthorized();

            var (success, error) = await importService.RollbackImportAsync(id, callerId.Value);
            if (!success)
            {
                return error == "BatchNotFound"
                    ? Results.NotFound(new { error = "BatchNotFound", message = "Import batch not found." })
                    : Results.BadRequest(new { error = error ?? "RollbackFailed", message = error });
            }

            return Results.Ok(new { message = "Import batch rolled back successfully." });
        })
        .RequirePermission("users.import")
        .WithName("RollbackImport")
        .WithSummary("Rollback created users from an import batch");

        group.MapGet("/history", async (IExcelImportService importService) =>
        {
            var history = await importService.GetImportHistoryAsync();
            return Results.Ok(history);
        })
        .RequirePermission("users.import")
        .WithName("GetImportHistory")
        .WithSummary("Get user import history");

        group.MapGet("/{id:guid}", async (Guid id, IExcelImportService importService) =>
        {
            var batch = await importService.GetImportByIdAsync(id);
            return batch == null
                ? Results.NotFound(new { error = "BatchNotFound", message = "Import batch not found." })
                : Results.Ok(batch);
        })
        .RequirePermission("users.import")
        .WithName("GetImportById")
        .WithSummary("Get import batch details by ID");

        group.MapGet("/{id:guid}/errors.xlsx", async (
            Guid id,
            IExcelImportService importService) =>
        {
            var fileBytes = await importService.GenerateErrorReportAsync(id);
            if (fileBytes == null)
            {
                return Results.NotFound(new { error = "NotFound", message = "No errors found for this import batch." });
            }

            return Results.File(
                fileBytes,
                contentType: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileDownloadName: $"import_errors_{id}.xlsx");
        })
        .RequirePermission("users.import")
        .WithName("GetImportErrors")
        .WithSummary("Download error report spreadsheet for import batch");

        return app;
    }

    private static Guid? GetUserId(ClaimsPrincipal user)
    {
        var idClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                      ?? user.FindFirst("sub")?.Value;

        return Guid.TryParse(idClaim, out var guid) ? guid : null;
    }
}
