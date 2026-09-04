using System.Security.Claims;
using MEval.Api.DTOs;
using MEval.Api.Security;
using MEval.Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace MEval.Api.Endpoints;

public static class RoleEndpoints
{
    public static IEndpointRouteBuilder MapRoleEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/roles")
            .WithTags("Roles")
            .RequireAuthorization()
            .AddEndpointFilter<FirstLoginGatewayFilter>();

        group.MapGet("/", async (IRoleService roleService) =>
        {
            var roles = await roleService.GetRolesAsync();
            return Results.Ok(roles);
        })
        .RequirePermission("roles.manage")
        .WithName("GetRoles")
        .WithSummary("List all roles with their assigned permissions");

        group.MapGet("/{id:int}", async (int id, IRoleService roleService) =>
        {
            var role = await roleService.GetRoleByIdAsync(id);
            return role == null
                ? Results.NotFound(new { error = "RoleNotFound", message = "Role not found." })
                : Results.Ok(role);
        })
        .RequirePermission("roles.manage")
        .WithName("GetRoleById")
        .WithSummary("Get role details by ID");

        group.MapPost("/", async (
            CreateRoleRequest request,
            IRoleService roleService,
            HttpContext httpContext) =>
        {
            var callerId = GetUserId(httpContext.User);
            if (callerId == null) return Results.Unauthorized();

            var (success, role, error) = await roleService.CreateRoleAsync(request, callerId.Value);
            if (!success)
            {
                return error?.Contains("HierarchyViolation") == true
                    ? Results.Json(new { error = "HierarchyViolation", message = error }, statusCode: StatusCodes.Status403Forbidden)
                    : Results.BadRequest(new { error = error ?? "Failed", message = "Could not create role." });
            }

            return Results.Created($"/api/v1/roles/{role!.Id}", role);
        })
        .RequirePermission("roles.manage")
        .WithName("CreateRole")
        .WithSummary("Create a new custom role");

        group.MapPut("/{id:int}", async (
            int id,
            UpdateRoleRequest request,
            IRoleService roleService,
            HttpContext httpContext) =>
        {
            var callerId = GetUserId(httpContext.User);
            if (callerId == null) return Results.Unauthorized();

            var (success, error) = await roleService.UpdateRoleAsync(id, request, callerId.Value);
            if (!success)
            {
                return error switch
                {
                    "RoleNotFound" => Results.NotFound(new { error = "RoleNotFound", message = "Role not found." }),
                    _ when error?.Contains("HierarchyViolation") == true => Results.Json(new { error = "HierarchyViolation", message = error }, statusCode: StatusCodes.Status403Forbidden),
                    _ => Results.BadRequest(new { error = error ?? "Failed", message = "Could not update role." })
                };
            }

            return Results.Ok(new { message = "Role updated successfully." });
        })
        .RequirePermission("roles.manage")
        .WithName("UpdateRole")
        .WithSummary("Update custom role details");

        group.MapDelete("/{id:int}", async (
            int id,
            IRoleService roleService,
            HttpContext httpContext) =>
        {
            var callerId = GetUserId(httpContext.User);
            if (callerId == null) return Results.Unauthorized();

            var (success, error) = await roleService.DeleteRoleAsync(id, callerId.Value);
            if (!success)
            {
                return error switch
                {
                    "RoleNotFound" => Results.NotFound(new { error = "RoleNotFound", message = "Role not found." }),
                    "CannotDeleteSystemProtectedRole" => Results.BadRequest(new { error = "CannotDeleteSystemProtectedRole", message = "System protected roles cannot be deleted." }),
                    _ when error?.Contains("HierarchyViolation") == true => Results.Json(new { error = "HierarchyViolation", message = error }, statusCode: StatusCodes.Status403Forbidden),
                    _ => Results.BadRequest(new { error = error ?? "Failed", message = error })
                };
            }

            return Results.Ok(new { message = "Role deleted successfully." });
        })
        .RequirePermission("roles.manage")
        .WithName("DeleteRole")
        .WithSummary("Delete a custom role");

        group.MapPut("/{id:int}/permissions", async (
            int id,
            UpdateRolePermissionsRequest request,
            IRoleService roleService,
            HttpContext httpContext) =>
        {
            var callerId = GetUserId(httpContext.User);
            if (callerId == null) return Results.Unauthorized();

            var (success, error) = await roleService.UpdateRolePermissionsAsync(id, request.Permissions, callerId.Value);
            if (!success)
            {
                return error switch
                {
                    "RoleNotFound" => Results.NotFound(new { error = "RoleNotFound", message = "Role not found." }),
                    _ when error?.Contains("HierarchyViolation") == true => Results.Json(new { error = "HierarchyViolation", message = error }, statusCode: StatusCodes.Status403Forbidden),
                    _ => Results.BadRequest(new { error = error ?? "Failed", message = error })
                };
            }

            return Results.Ok(new { message = "Role permissions updated successfully." });
        })
        .RequirePermission("roles.manage")
        .WithName("UpdateRolePermissions")
        .WithSummary("Atomic replacement of role permissions");

        // Assignment routes under /api/v1/users
        var userGroup = app.MapGroup("/api/v1/users")
            .WithTags("Users")
            .RequireAuthorization()
            .AddEndpointFilter<FirstLoginGatewayFilter>();

        userGroup.MapPost("/{id:int}/roles/{roleId:int}", async (
            int id,
            int roleId,
            IRoleService roleService,
            HttpContext httpContext) =>
        {
            var callerId = GetUserId(httpContext.User);
            if (callerId == null) return Results.Unauthorized();

            var (success, error) = await roleService.AssignRoleAsync(id, roleId, callerId.Value);
            if (!success)
            {
                return error switch
                {
                    "RoleNotFound" => Results.NotFound(new { error = "RoleNotFound", message = "Role not found." }),
                    "UserNotFoundOrInactive" => Results.NotFound(new { error = "UserNotFoundOrInactive", message = "User not found or inactive." }),
                    _ when error?.Contains("HierarchyViolation") == true => Results.Json(new { error = "HierarchyViolation", message = error }, statusCode: StatusCodes.Status403Forbidden),
                    _ => Results.BadRequest(new { error = error ?? "Failed", message = error })
                };
            }

            return Results.Ok(new { message = "Role assigned successfully." });
        })
        .RequirePermission("roles.assign")
        .WithName("AssignRole")
        .WithSummary("Assign role to user");

        userGroup.MapDelete("/{id:int}/roles/{roleId:int}", async (
            int id,
            int roleId,
            IRoleService roleService,
            HttpContext httpContext) =>
        {
            var callerId = GetUserId(httpContext.User);
            if (callerId == null) return Results.Unauthorized();

            var (success, error) = await roleService.RemoveRoleAsync(id, roleId, callerId.Value);
            if (!success)
            {
                return error switch
                {
                    "RoleNotFound" => Results.NotFound(new { error = "RoleNotFound", message = "Role not found." }),
                    _ when error?.Contains("HierarchyViolation") == true => Results.Json(new { error = "HierarchyViolation", message = error }, statusCode: StatusCodes.Status403Forbidden),
                    _ => Results.BadRequest(new { error = error ?? "Failed", message = error })
                };
            }

            return Results.Ok(new { message = "Role removed successfully." });
        })
        .RequirePermission("roles.assign")
        .WithName("RemoveRole")
        .WithSummary("Remove role from user");

        userGroup.MapPost("/bulk-assign-role", async (
            BulkAssignRoleRequest request,
            IRoleService roleService,
            HttpContext httpContext) =>
        {
            var callerId = GetUserId(httpContext.User);
            if (callerId == null) return Results.Unauthorized();

            var (success, response, error) = await roleService.BulkAssignRoleAsync(request, callerId.Value);
            if (!success)
            {
                return error switch
                {
                    "RoleNotFound" => Results.NotFound(new { error = "RoleNotFound", message = "Role not found." }),
                    _ when error?.Contains("HierarchyViolation") == true => Results.Json(new { error = "HierarchyViolation", message = error }, statusCode: StatusCodes.Status403Forbidden),
                    _ => Results.BadRequest(new { error = error ?? "Failed", message = error })
                };
            }

            return Results.Ok(response);
        })
        .RequirePermission("roles.assign")
        .WithName("BulkAssignRole")
        .WithSummary("Bulk assign role to multiple users");

        return app;
    }

    private static int? GetUserId(ClaimsPrincipal user)
    {
        var idClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                      ?? user.FindFirst("sub")?.Value;

        return int.TryParse(idClaim, out var id) ? id : null;
    }
}
