using System.Security.Claims;
using MEval.Api.DTOs;
using MEval.Api.Models;
using MEval.Api.Security;
using MEval.Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace MEval.Api.Endpoints;

public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/users")
            .WithTags("Users")
            .RequireAuthorization()
            .AddEndpointFilter<FirstLoginGatewayFilter>();

        // 1. Self-Service Profile Routes (/api/v1/users/me)
        // Disambiguated by route constraint from {id:guid}
        group.MapGet("/me", async (
            IUserLifecycleService userService,
            HttpContext httpContext) =>
        {
            var userId = GetUserId(httpContext.User);
            if (userId == null) return Results.Unauthorized();

            var user = await userService.GetUserByIdAsync(userId.Value);
            return user == null ? Results.NotFound() : Results.Ok(user);
        })
        .WithName("GetUserProfileMe")
        .WithSummary("Get current user's profile details");

        group.MapPut("/me", async (
            UpdateProfileMeRequest request,
            IUserLifecycleService userService,
            HttpContext httpContext) =>
        {
            var userId = GetUserId(httpContext.User);
            if (userId == null) return Results.Unauthorized();

            var (success, error) = await userService.UpdateProfileMeAsync(userId.Value, request);
            return success ? Results.Ok(new { message = "Profile updated successfully." }) : Results.BadRequest(new { error });
        })
        .WithName("UpdateUserProfileMe")
        .WithSummary("Update current user's non-sensitive profile info");

        // 2. Admin User Lifecycle Routes
        group.MapPost("/", async (
            CreateUserRequest request,
            IUserLifecycleService userService,
            HttpContext httpContext) =>
        {
            var callerId = GetUserId(httpContext.User);
            if (callerId == null) return Results.Unauthorized();

            var (success, user, error) = await userService.CreateUserAsync(request, callerId.Value);
            if (!success)
            {
                if (error == "EmailAlreadyExists")
                {
                    return Results.Conflict(new { error = "EmailAlreadyExists", message = "A user with this email address already exists." });
                }

                if (error?.StartsWith("UserSoftDeleted") == true)
                {
                    return Results.BadRequest(new { error = "UserSoftDeleted", message = error });
                }

                return Results.BadRequest(new { error = error ?? "Failed", message = "Could not create user." });
            }

            return Results.Created($"/api/v1/users/{user!.Id}", user);
        })
        .RequirePermission("users.create")
        .WithName("CreateUser")
        .WithSummary("Create a new user account with default credentials");

        group.MapGet("/", async (
            string? search,
            string? role,
            string? status,
            UserSource? source,
            Guid? batchId,
            bool? stillOnDefaultPassword,
            int pageIndex,
            int pageSize,
            IUserLifecycleService userService) =>
        {
            var filters = new UserFilterParams(
                search,
                role,
                status,
                source,
                batchId,
                stillOnDefaultPassword,
                pageIndex <= 0 ? 1 : pageIndex,
                pageSize <= 0 ? 20 : pageSize
            );

            var result = await userService.SearchUsersAsync(filters);
            return Results.Ok(result);
        })
        .RequirePermission("users.read")
        .WithName("SearchUsers")
        .WithSummary("Search and filter users with pagination");

        group.MapGet("/{id:guid}", async (Guid id, IUserLifecycleService userService) =>
        {
            var user = await userService.GetUserByIdAsync(id);
            return user == null
                ? Results.NotFound(new { error = "UserNotFound", message = "User not found." })
                : Results.Ok(user);
        })
        .RequirePermission("users.read")
        .WithName("GetUserById")
        .WithSummary("Get user details by ID");

        group.MapPut("/{id:guid}", async (
            Guid id,
            UpdateUserRequest request,
            IUserLifecycleService userService,
            HttpContext httpContext) =>
        {
            var callerId = GetUserId(httpContext.User);
            if (callerId == null) return Results.Unauthorized();

            var (success, error) = await userService.UpdateUserAsync(id, request, callerId.Value);
            if (!success)
            {
                return Results.NotFound(new { error = "UserNotFound", message = "User not found." });
            }

            return Results.Ok(new { message = "User updated successfully." });
        })
        .RequirePermission("users.update")
        .WithName("UpdateUser")
        .WithSummary("Update user profile info");

        group.MapPost("/{id:guid}/deactivate", async (
            Guid id,
            IUserLifecycleService userService,
            HttpContext httpContext) =>
        {
            var callerId = GetUserId(httpContext.User);
            if (callerId == null) return Results.Unauthorized();

            var (success, error) = await userService.DeactivateUserAsync(id, callerId.Value);
            if (!success)
            {
                return error?.Contains("CannotDeactivate") == true
                    ? Results.Json(new { error = "GuardViolation", message = error }, statusCode: StatusCodes.Status403Forbidden)
                    : Results.BadRequest(new { error = error ?? "Failed", message = "Could not deactivate user." });
            }

            return Results.Ok(new { message = "User deactivated successfully." });
        })
        .RequirePermission("users.deactivate")
        .WithName("DeactivateUser")
        .WithSummary("Deactivate user account and revoke active session");

        group.MapPost("/{id:guid}/reactivate", async (
            Guid id,
            IUserLifecycleService userService,
            HttpContext httpContext) =>
        {
            var callerId = GetUserId(httpContext.User);
            if (callerId == null) return Results.Unauthorized();

            var (success, error) = await userService.ReactivateUserAsync(id, callerId.Value);
            if (!success)
            {
                return Results.NotFound(new { error = "UserNotFound", message = "User not found." });
            }

            return Results.Ok(new { message = "User reactivated successfully." });
        })
        .RequirePermission("users.deactivate")
        .WithName("ReactivateUser")
        .WithSummary("Reactivate user account");

        group.MapPost("/{id:guid}/unlock", async (
            Guid id,
            IUserLifecycleService userService,
            HttpContext httpContext) =>
        {
            var callerId = GetUserId(httpContext.User);
            if (callerId == null) return Results.Unauthorized();

            var (success, error) = await userService.UnlockUserAsync(id, callerId.Value);
            if (!success)
            {
                return Results.NotFound(new { error = "UserNotFound", message = "User not found." });
            }

            return Results.Ok(new { message = "User unlocked successfully." });
        })
        .RequirePermission("users.unlock")
        .WithName("UnlockUser")
        .WithSummary("Reset failed attempts and unlock account");

        group.MapPost("/{id:guid}/force-logout", async (
            Guid id,
            IUserLifecycleService userService,
            HttpContext httpContext) =>
        {
            var callerId = GetUserId(httpContext.User);
            if (callerId == null) return Results.Unauthorized();

            await userService.ForceLogoutUserAsync(id, callerId.Value);
            return Results.Ok(new { message = "User session has been revoked." });
        })
        .RequirePermission("users.force-logout")
        .WithName("ForceLogoutUser")
        .WithSummary("Revoke active session for target user");

        group.MapDelete("/{id:guid}", async (
            Guid id,
            IUserLifecycleService userService,
            HttpContext httpContext) =>
        {
            var callerId = GetUserId(httpContext.User);
            if (callerId == null) return Results.Unauthorized();

            var (success, error) = await userService.SoftDeleteUserAsync(id, callerId.Value);
            if (!success)
            {
                return error?.Contains("CannotDeactivate") == true
                    ? Results.Json(new { error = "GuardViolation", message = error }, statusCode: StatusCodes.Status403Forbidden)
                    : Results.BadRequest(new { error = error ?? "Failed", message = "Could not delete user." });
            }

            return Results.Ok(new { message = "User deleted successfully." });
        })
        .RequirePermission("users.delete")
        .WithName("DeleteUser")
        .WithSummary("Soft-delete user account and revoke session");

        // Admin force-reset-password
        group.MapPost("/{id:guid}/force-reset-password", async (
            Guid id,
            IPasswordResetService passwordResetService,
            HttpContext httpContext) =>
        {
            var ip = httpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault()
                     ?? httpContext.Connection.RemoteIpAddress?.ToString()
                     ?? "127.0.0.1";

            var (success, error) = await passwordResetService.ForceResetPasswordAsync(id, ip);

            if (!success)
            {
                return error switch
                {
                    "UserNotFound" => Results.NotFound(new { error = "UserNotFound", message = "User not found." }),
                    _ => Results.BadRequest(new { error = error ?? "Failed", message = "Failed to force reset password." })
                };
            }

            return Results.Ok(new
            {
                message = "User password has been reset to the default temporary password and must be changed upon next login."
            });
        })
        .RequirePermission("users.reset-password")
        .WithName("ForceResetPassword")
        .WithSummary("Administrator force-reset user password to default temporary password");

        return app;
    }

    private static Guid? GetUserId(ClaimsPrincipal user)
    {
        var idClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                      ?? user.FindFirst("sub")?.Value;

        return Guid.TryParse(idClaim, out var guid) ? guid : null;
    }
}
