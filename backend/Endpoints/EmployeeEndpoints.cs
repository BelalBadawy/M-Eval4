using System.Security.Claims;
using MEval.Api.DTOs;
using MEval.Api.Models;
using MEval.Api.Security;
using MEval.Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace MEval.Api.Endpoints;

public static class EmployeeEndpoints
{
    public static IEndpointRouteBuilder MapEmployeeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/employees")
            .WithTags("Employees")
            .RequireAuthorization()
            .AddEndpointFilter<FirstLoginGatewayFilter>();

        // 1. Search & Filter Employees with Pagination
        group.MapGet("/", async (
            string? search,
            int? companyId,
            int? departmentId,
            int? sectionId,
            int? positionId,
            int? managerId,
            int? nLevel,
            EmploymentStatus? status,
            bool? isEvaluationEligible,
            bool? hasLinkedAccount,
            int? pageIndex,
            int? pageSize,
            IEmployeeService employeeService) =>
        {
            var filters = new EmployeeFilterParams(
                Search: search,
                CompanyId: companyId,
                DepartmentId: departmentId,
                SectionId: sectionId,
                PositionId: positionId,
                ManagerId: managerId,
                NLevel: nLevel,
                Status: status,
                IsEvaluationEligible: isEvaluationEligible,
                HasLinkedAccount: hasLinkedAccount,
                PageIndex: pageIndex.GetValueOrDefault(1) <= 0 ? 1 : pageIndex.GetValueOrDefault(1),
                PageSize: pageSize.GetValueOrDefault(20) <= 0 ? 20 : pageSize.GetValueOrDefault(20)
            );

            var result = await employeeService.SearchEmployeesAsync(filters);
            return Results.Ok(result);
        })
        .RequirePermission("org.read")
        .WithName("SearchEmployees")
        .WithSummary("Search and filter employees with multifaceted criteria and pagination");

        // 2. Orphans query (/api/v1/employees/orphans)
        group.MapGet("/orphans", async (IHierarchyService hierarchyService) =>
        {
            var orphans = await hierarchyService.GetOrphanEmployeesAsync();
            return Results.Ok(orphans);
        })
        .RequirePermission("org.read")
        .WithName("GetEmployeeOrphans")
        .WithSummary("Retrieve active employees missing a direct manager who are not top-tier executives (NLevel > 1)");

        // 3. Hierarchy Anomalies Audit (/api/v1/employees/anomalies)
        group.MapGet("/anomalies", async (IHierarchyService hierarchyService) =>
        {
            var anomalies = await hierarchyService.GetHierarchyAnomaliesAsync();
            return Results.Ok(anomalies);
        })
        .RequirePermission("org.read")
        .WithName("GetHierarchyAnomalies")
        .WithSummary("Retrieve comprehensive hierarchy anomalies (orphans, NLevel 1 with manager, cross-company/inactive managers)");

        // 4. Get Employee by ID
        group.MapGet("/{id:int}", async (int id, IEmployeeService employeeService) =>
        {
            var employee = await employeeService.GetEmployeeByIdAsync(id);
            return employee == null
                ? Results.NotFound(new { error = "NotFound", message = $"Employee with ID {id} not found." })
                : Results.Ok(employee);
        })
        .RequirePermission("org.read")
        .WithName("GetEmployeeById")
        .WithSummary("Get full employee details by employee ID");

        // 5. Manager Chain (Upward traversal to root, max 100 hops)
        group.MapGet("/{id:int}/manager-chain", async (int id, IHierarchyService hierarchyService) =>
        {
            var chain = await hierarchyService.GetManagerChainAsync(id);
            return Results.Ok(chain);
        })
        .RequirePermission("org.read")
        .WithName("GetManagerChain")
        .WithSummary("Traverse upward reporting line from employee to root executive (capped at 100 hops)");

        // 6. Direct Reports
        group.MapGet("/{id:int}/direct-reports", async (int id, IHierarchyService hierarchyService) =>
        {
            var reports = await hierarchyService.GetDirectReportsAsync(id);
            return Results.Ok(reports);
        })
        .RequirePermission("org.read")
        .WithName("GetDirectReports")
        .WithSummary("Retrieve all direct subordinate employees for a given manager");

        // 7. Update Evaluation Eligibility
        group.MapPut("/{id:int}/eligibility", async (
            int id,
            UpdateEligibilityRequest request,
            IEmployeeService employeeService,
            HttpContext httpContext) =>
        {
            var callerId = GetUserId(httpContext.User);
            if (callerId == null) return Results.Unauthorized();
            var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";

            var (success, error) = await employeeService.SetEvaluationEligibilityAsync(id, request.IsEvaluationEligible, request.Reason, callerId.Value, ip);
            if (!success)
            {
                return error != null && error.Contains("not found", StringComparison.OrdinalIgnoreCase)
                    ? Results.NotFound(new { error = "NotFound", message = error })
                    : Results.BadRequest(new { error = "BadRequest", message = error });
            }

            return Results.Ok(new { message = $"Employee evaluation eligibility updated to {request.IsEvaluationEligible}." });
        })
        .RequirePermission("employees.manage-eligibility")
        .WithName("UpdateEvaluationEligibility")
        .WithSummary("Toggle an employee's evaluation eligibility status");

        // 8. Link User Account
        group.MapPost("/{id:int}/link-user", async (
            int id,
            LinkUserAccountRequest request,
            IEmployeeService employeeService,
            HttpContext httpContext) =>
        {
            var callerId = GetUserId(httpContext.User);
            if (callerId == null) return Results.Unauthorized();
            var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";

            var (success, error, statusCode) = await employeeService.LinkUserAccountAsync(id, request.UserId, callerId.Value, ip);
            if (!success)
            {
                if (statusCode == 409)
                {
                    return Results.Conflict(new { error = "Conflict", message = error });
                }

                if (statusCode == 404)
                {
                    return Results.NotFound(new { error = "NotFound", message = error });
                }

                return Results.BadRequest(new { error = "BadRequest", message = error });
            }

            return Results.Ok(new { message = $"User account {request.UserId} linked to employee {id} successfully." });
        })
        .RequirePermission("employees.link-user")
        .WithName("LinkUserAccount")
        .WithSummary("Link an employee to an existing active application user account");

        // 9. Unlink User Account
        group.MapPost("/{id:int}/unlink-user", async (
            int id,
            IEmployeeService employeeService,
            HttpContext httpContext) =>
        {
            var callerId = GetUserId(httpContext.User);
            if (callerId == null) return Results.Unauthorized();
            var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";

            var (success, error) = await employeeService.UnlinkUserAccountAsync(id, callerId.Value, ip);
            if (!success)
            {
                return error != null && error.Contains("not found", StringComparison.OrdinalIgnoreCase)
                    ? Results.NotFound(new { error = "NotFound", message = error })
                    : Results.BadRequest(new { error = "BadRequest", message = error });
            }

            return Results.Ok(new { message = $"User account unlinked from employee {id} successfully." });
        })
        .RequirePermission("employees.link-user")
        .WithName("UnlinkUserAccount")
        .WithSummary("Unlink an application user account from an employee");

        return app;
    }

    private static int? GetUserId(ClaimsPrincipal user)
    {
        var idClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                      ?? user.FindFirst("sub")?.Value;

        return int.TryParse(idClaim, out var id) ? id : null;
    }
}
