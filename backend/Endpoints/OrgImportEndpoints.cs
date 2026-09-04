using System.Security.Claims;
using MEval.Api.DTOs;
using MEval.Api.Security;
using MEval.Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace MEval.Api.Endpoints;

public static class OrgImportEndpoints
{
    public static IEndpointRouteBuilder MapOrgImportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/org/imports")
            .WithTags("Organization Import")
            .RequireAuthorization()
            .AddEndpointFilter<FirstLoginGatewayFilter>();

        // 1. Template Download
        group.MapGet("/template", (IOrgImportService importService) =>
        {
            var bytes = importService.GenerateTemplate();
            return Results.File(
                bytes,
                contentType: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileDownloadName: "org_hierarchy_import_template.xlsx");
        })
        .RequirePermission("org.import")
        .WithName("GetOrgImportTemplate")
        .WithSummary("Download the 5-sheet Excel template for organization hierarchy and employee synchronization");

        // 2. Dry Run Validation
        group.MapPost("/dry-run", async (
            IFormFile file,
            IOrgImportService importService,
            HttpContext httpContext) =>
        {
            if (file == null || file.Length == 0)
            {
                return Results.BadRequest(new { error = "InvalidFile", message = "An Excel (.xlsx) file is required." });
            }

            var callerId = GetUserId(httpContext.User);
            if (callerId == null) return Results.Unauthorized();

            using var stream = file.OpenReadStream();
            var (success, result, error, statusCode) = await importService.DryRunAsync(
                stream,
                file.FileName,
                file.Length,
                callerId.Value);

            if (!success)
            {
                if (statusCode == 409)
                {
                    return Results.Conflict(new { error = "Conflict", message = error });
                }

                return Results.BadRequest(new { error = "BadRequest", message = error });
            }

            return Results.Ok(result);
        })
        .DisableAntiforgery()
        .RequirePermission("org.import")
        .WithName("DryRunOrgImport")
        .WithSummary("Validate workbook integrity, cycle detection, and constraints without committing database changes");

        // 3. Synchronous File Execution
        group.MapPost("/execute", async (
            IFormFile file,
            IOrgImportService importService,
            HttpContext httpContext) =>
        {
            if (file == null || file.Length == 0)
            {
                return Results.BadRequest(new { error = "InvalidFile", message = "An Excel (.xlsx) file is required." });
            }

            var callerId = GetUserId(httpContext.User);
            if (callerId == null) return Results.Unauthorized();
            var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";

            using var stream = file.OpenReadStream();
            var (success, response, error, statusCode) = await importService.ExecuteAsync(
                stream,
                file.FileName,
                file.Length,
                callerId.Value,
                ip);

            if (!success)
            {
                if (statusCode == 409)
                {
                    return Results.Conflict(new { error = "Conflict", message = error });
                }

                if (statusCode == 400)
                {
                    return Results.BadRequest(new { error = "ValidationFailed", message = error, response });
                }

                return Results.Problem(statusCode: statusCode, detail: error);
            }

            return Results.Ok(response);
        })
        .DisableAntiforgery()
        .RequirePermission("org.import")
        .WithName("ExecuteOrgImport")
        .WithSummary("Synchronously execute organization and employee synchronization with the uploaded workbook");

        return app;
    }

    private static int? GetUserId(ClaimsPrincipal user)
    {
        var idClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                      ?? user.FindFirst("sub")?.Value;

        return int.TryParse(idClaim, out var id) ? id : null;
    }
}
