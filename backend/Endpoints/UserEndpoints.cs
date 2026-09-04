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
        // Disambiguated by route constraint from {id:int}
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

                if (error != null && error.StartsWith("UserSoftDeleted"))
                {
                    return Results.Conflict(new { error = "UserSoftDeleted", message = "A soft-deleted user with this email exists. Please reactivate or restore." });
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
            int? batchId,
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

        group.MapGet("/{id:int}", async (int id, IUserLifecycleService userService) =>
        {
            var user = await userService.GetUserByIdAsync(id);
            return user == null
                ? Results.NotFound(new { error = "UserNotFound", message = "User not found." })
                : Results.Ok(user);
        })
        .RequirePermission("users.read")
        .WithName("GetUserById")
        .WithSummary("Get user details by ID");

        group.MapPut("/{id:int}", async (
            int id,
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

        group.MapPost("/{id:int}/deactivate", async (
            int id,
            IUserLifecycleService userService,
            HttpContext httpContext) =>
        {
            var callerId = GetUserId(httpContext.User);
            if (callerId == null) return Results.Unauthorized();

            var (success, error) = await userService.DeactivateUserAsync(id, callerId.Value);
            if (!success)
            {
                if (error != null && error.StartsWith("CannotDeactivateSelf"))
                {
                    return Results.Json(new { error = "CannotDeactivateSelf", message = "You cannot deactivate your own account." }, statusCode: StatusCodes.Status403Forbidden);
                }

                if (error != null && error.StartsWith("CannotDeactivateLastAdmin"))
                {
                    return Results.Json(new { error = "CannotDeactivateLastAdmin", message = "You cannot deactivate the last active administrator." }, statusCode: StatusCodes.Status403Forbidden);
                }

                return Results.NotFound(new { error = "UserNotFound", message = "User not found." });
            }

            return Results.Ok(new { message = "User account deactivated successfully." });
        })
        .RequirePermission("users.deactivate")
        .WithName("DeactivateUser")
        .WithSummary("Deactivate a user account and revoke sessions");

        group.MapPost("/{id:int}/reactivate", async (
            int id,
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

            return Results.Ok(new { message = "User account reactivated successfully." });
        })
        .RequirePermission("users.deactivate")
        .WithName("ReactivateUser")
        .WithSummary("Reactivate a user account");

        group.MapPost("/{id:int}/unlock", async (
            int id,
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

            return Results.Ok(new { message = "User account unlocked successfully." });
        })
        .RequirePermission("users.unlock")
        .WithName("UnlockUser")
        .WithSummary("Unlock locked-out user account");

        group.MapPost("/{id:int}/force-logout", async (
            int id,
            IUserLifecycleService userService,
            HttpContext httpContext) =>
        {
            var callerId = GetUserId(httpContext.User);
            if (callerId == null) return Results.Unauthorized();

            var (success, error) = await userService.ForceLogoutUserAsync(id, callerId.Value);
            if (!success)
            {
                return Results.NotFound(new { error = "UserNotFound", message = "User not found." });
            }

            return Results.Ok(new { message = "User active sessions have been revoked." });
        })
        .RequirePermission("users.force-logout")
        .WithName("ForceLogoutUser")
        .WithSummary("Admin force-logout all active sessions for a user");

        group.MapDelete("/{id:int}", async (
            int id,
            IUserLifecycleService userService,
            HttpContext httpContext) =>
        {
            var callerId = GetUserId(httpContext.User);
            if (callerId == null) return Results.Unauthorized();

            var (success, error) = await userService.SoftDeleteUserAsync(id, callerId.Value);
            if (!success)
            {
                if (error != null && error.StartsWith("CannotDeactivateSelf"))
                {
                    return Results.Json(new { error = "CannotDeleteSelf", message = "You cannot delete your own account." }, statusCode: StatusCodes.Status403Forbidden);
                }

                if (error != null && error.StartsWith("CannotDeactivateLastAdmin"))
                {
                    return Results.Json(new { error = "CannotDeleteLastAdmin", message = "You cannot delete the last active administrator." }, statusCode: StatusCodes.Status403Forbidden);
                }

                return Results.NotFound(new { error = "UserNotFound", message = "User not found." });
            }

            return Results.Ok(new { message = "User soft-deleted successfully." });
        })
        .RequirePermission("users.delete")
        .WithName("DeleteUser")
        .WithSummary("Soft-delete a user account");

        // Admin force-reset-password
        group.MapPost("/{id:int}/force-reset-password", async (
            int id,
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

    private static int? GetUserId(ClaimsPrincipal user)
    {
        var idClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                      ?? user.FindFirst("sub")?.Value;

        return int.TryParse(idClaim, out var id) ? id : null;
    }
}
