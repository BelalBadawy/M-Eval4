using System.Security.Claims;
using MEval.Api.DTOs;
using MEval.Api.Security;
using MEval.Api.Services;

namespace MEval.Api.Endpoints;

public static class EligibilityEndpoints
{
    public static void MapEligibilityEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/eligibility")
            .WithTags("Eligibility")
            .RequireAuthorization();

        // 1. Eligibility Summary
        group.MapGet("/summary", async (int? companyId, IEligibilityService eligibilityService) =>
        {
            var summary = await eligibilityService.GetSummaryAsync(companyId);
            return Results.Ok(summary);
        })
        .RequirePermission("org.read")
        .WithName("GetEligibilitySummary")
        .WithSummary("Retrieve overall headcount and company breakdown for evaluation eligibility");

        // 2. Search / Query Eligible Population (Defaults to isEligible = true per ELIG-004)
        group.MapGet("/employees", async (
            bool? isEligible,
            int? companyId,
            int? departmentId,
            string? search,
            int? page,
            int? pageSize,
            IEligibilityService eligibilityService) =>
        {
            var result = await eligibilityService.SearchEmployeesAsync(
                isEligible,
                companyId,
                departmentId,
                search,
                page ?? 1,
                pageSize ?? 50
            );
            return Results.Ok(result);
        })
        .RequirePermission("org.read")
        .WithName("SearchEligibleEmployees")
        .WithSummary("Query employees by evaluation eligibility (defaults to eligible population)");

        // 3. Excluded Population
        group.MapGet("/excluded", async (
            int? companyId,
            int? departmentId,
            string? search,
            int? page,
            int? pageSize,
            IEligibilityService eligibilityService) =>
        {
            var result = await eligibilityService.GetExcludedEmployeesAsync(
                companyId,
                departmentId,
                search,
                page ?? 1,
                pageSize ?? 50
            );
            return Results.Ok(result);
        })
        .RequirePermission("org.read")
        .WithName("GetExcludedEmployees")
        .WithSummary("Query excluded employees where IsEvaluationEligible is false (reason: hr-flag-false)");

        // 4. Bulk Flag Update
        group.MapPost("/bulk-flag-update", async (
            BulkFlagUpdateRequest request,
            IEligibilityService eligibilityService,
            HttpContext httpContext) =>
        {
            var callerId = GetUserId(httpContext.User);
            if (callerId == null) return Results.Unauthorized();
            var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";

            var (success, response, error, missingIds, statusCode) = await eligibilityService.BulkFlagUpdateAsync(request, callerId.Value, ip);
            if (!success)
            {
                return Results.Json(new
                {
                    error = "BadRequest",
                    message = error,
                    missingEmployeeIds = missingIds
                }, statusCode: statusCode);
            }

            return Results.Ok(response);
        })
        .RequirePermission("employees.manage-eligibility")
        .WithName("BulkFlagUpdate")
        .WithSummary("Update evaluation eligibility flag for up to 500 employees in an AllOrNothing transaction");
    }

    private static int? GetUserId(ClaimsPrincipal user)
    {
        var idClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(idClaim, out var id) ? id : null;
    }
}
