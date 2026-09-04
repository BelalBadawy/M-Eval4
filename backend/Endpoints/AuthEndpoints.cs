using System.Security.Claims;
using MEval.Api.DTOs;
using MEval.Api.Security;
using MEval.Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace MEval.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth")
            .WithTags("Authentication");

        // Anonymous endpoints
        group.MapPost("/login", async (
            LoginRequest request,
            IAuthService authService,
            HttpContext httpContext) =>
        {
            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            {
                return Results.BadRequest(new { error = "InvalidRequest", message = "Email and password are required." });
            }

            var ip = GetClientIp(httpContext);
            var (success, response, error, lockoutMinutes) = await authService.LoginAsync(request.Email, request.Password, ip);

            if (!success)
            {
                return error switch
                {
                    "AccountLocked" => Results.Json(new
                    {
                        error = "AccountLocked",
                        message = $"Account is temporarily locked. Please try again in {lockoutMinutes} minutes."
                    }, statusCode: StatusCodes.Status423Locked),

                    "AccountDeactivated" => Results.Json(new
                    {
                        error = "AccountDeactivated",
                        message = "Account is inactive or disabled. Contact your administrator."
                    }, statusCode: StatusCodes.Status403Forbidden),

                    _ => Results.Json(new
                    {
                        error = "InvalidCredentials",
                        message = "Invalid email or password."
                    }, statusCode: StatusCodes.Status401Unauthorized)
                };
            }

            return Results.Ok(response);
        })
        .AllowAnonymous()
        .RequireRateLimiting("auth-limit")
        .WithName("Login")
        .WithSummary("Authenticate with email and password");

        group.MapPost("/forgot-password", async (
            ForgotPasswordRequest request,
            IPasswordResetService passwordResetService,
            HttpContext httpContext) =>
        {
            if (string.IsNullOrWhiteSpace(request.Email))
            {
                return Results.BadRequest(new { error = "InvalidRequest", message = "Email is required." });
            }

            var ip = GetClientIp(httpContext);
            await passwordResetService.RequestPasswordResetAsync(request.Email, ip);

            // Uniform response against user enumeration
            return Results.Ok(new { message = "If the email is registered, password reset instructions have been sent." });
        })
        .AllowAnonymous()
        .RequireRateLimiting("auth-limit")
        .WithName("ForgotPassword")
        .WithSummary("Request a password reset link/token");

        group.MapPost("/reset-password", async (
            ResetPasswordRequest request,
            IPasswordResetService passwordResetService,
            HttpContext httpContext) =>
        {
            if (string.IsNullOrWhiteSpace(request.Token) || string.IsNullOrWhiteSpace(request.NewPassword))
            {
                return Results.BadRequest(new { error = "InvalidRequest", message = "Reset token and new password are required." });
            }

            var ip = GetClientIp(httpContext);
            var (success, error, validationErrors) = await passwordResetService.ResetPasswordAsync(request.Token, request.NewPassword, ip);

            if (!success)
            {
                return error switch
                {
                    "InvalidOrExpiredToken" => Results.BadRequest(new
                    {
                        error = "InvalidOrExpiredToken",
                        message = "The password reset token is invalid or has expired."
                    }),

                    "PasswordPolicyViolation" => Results.BadRequest(new
                    {
                        error = "PasswordPolicyViolation",
                        message = "New password does not meet security requirements.",
                        errors = validationErrors
                    }),

                    _ => Results.BadRequest(new
                    {
                        error = error ?? "Failed",
                        message = "Failed to reset password."
                    })
                };
            }

            return Results.Ok(new { message = "Password has been successfully reset. Please log in with your new password." });
        })
        .AllowAnonymous()
        .WithName("ResetPassword")
        .WithSummary("Reset password using reset token");

        group.MapPost("/refresh", async (
            RefreshTokenRequest request,
            ITokenService tokenService,
            HttpContext httpContext) =>
        {
            if (string.IsNullOrWhiteSpace(request.RefreshToken))
            {
                return Results.BadRequest(new { error = "InvalidRequest", message = "Refresh token is required." });
            }

            var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var (success, accessToken, newRawRefreshToken, error) = await tokenService.RotateRefreshTokenAsync(request.RefreshToken, ip);

            if (!success)
            {
                return error switch
                {
                    "SuspiciousReplayDetected" => Results.Json(new
                    {
                        error = "SuspiciousReplayDetected",
                        message = "Suspicious token activity detected. All active sessions have been terminated."
                    }, statusCode: StatusCodes.Status401Unauthorized),

                    "TokenExpired" => Results.Json(new
                    {
                        error = "TokenExpired",
                        message = "Refresh token has expired."
                    }, statusCode: StatusCodes.Status401Unauthorized),

                    "TokenRevoked" => Results.Json(new
                    {
                        error = "TokenRevoked",
                        message = "Refresh token has been revoked."
                    }, statusCode: StatusCodes.Status401Unauthorized),

                    _ => Results.Json(new
                    {
                        error = "InvalidToken",
                        message = "Invalid refresh token."
                    }, statusCode: StatusCodes.Status401Unauthorized)
                };
            }

            return Results.Ok(new RefreshTokenResponse(accessToken!, newRawRefreshToken!));
        })
        .AllowAnonymous()
        .WithName("RefreshToken")
        .WithSummary("Rotate refresh token and get a fresh access token");

        // Authenticated endpoints
        var protectedGroup = group.MapGroup("")
            .RequireAuthorization()
            .AddEndpointFilter<FirstLoginGatewayFilter>();

        protectedGroup.MapPost("/change-password", async (
            ChangePasswordRequest request,
            IAuthService authService,
            HttpContext httpContext) =>
        {
            var userId = GetUserId(httpContext.User);
            if (userId == null)
            {
                return Results.Unauthorized();
            }

            var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var (success, response, error, validationErrors) = await authService.ChangePasswordAsync(
                userId.Value, request.CurrentPassword, request.NewPassword, ip);

            if (!success)
            {
                return error switch
                {
                    "InvalidCurrentPassword" => Results.BadRequest(new
                    {
                        error = "InvalidCurrentPassword",
                        message = "The current password provided is incorrect."
                    }),

                    "PasswordPolicyViolation" => Results.BadRequest(new
                    {
                        error = "PasswordPolicyViolation",
                        message = "Password does not meet complexity requirements.",
                        errors = validationErrors
                    }),

                    _ => Results.BadRequest(new
                    {
                        error = error ?? "Failed",
                        message = "Failed to change password."
                    })
                };
            }

            return Results.Ok(response);
        })
        .WithName("ChangePassword")
        .WithSummary("Change user password and clear mandatory change flag");

        protectedGroup.MapPost("/logout", async (
            IAuthService authService,
            HttpContext httpContext) =>
        {
            var userId = GetUserId(httpContext.User);
            if (userId == null)
            {
                return Results.Unauthorized();
            }

            var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            await authService.LogoutAsync(userId.Value, ip);

            return Results.Ok(new { message = "Logged out successfully." });
        })
        .WithName("Logout")
        .WithSummary("Revoke active session and logout");

        protectedGroup.MapGet("/session", async (
            IAuthService authService,
            HttpContext httpContext) =>
        {
            var userId = GetUserId(httpContext.User);
            if (userId == null)
            {
                return Results.Unauthorized();
            }

            var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var session = await authService.GetSessionAsync(userId.Value, ip);
            if (session == null)
            {
                return Results.Unauthorized();
            }

            return Results.Ok(session);
        })
        .WithName("GetSession")
        .WithSummary("Get current active session details");

        protectedGroup.MapGet("/me", async (
            IAuthService authService,
            HttpContext httpContext) =>
        {
            var userId = GetUserId(httpContext.User);
            if (userId == null)
            {
                return Results.Unauthorized();
            }

            var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var session = await authService.GetSessionAsync(userId.Value, ip);
            if (session == null)
            {
                return Results.Unauthorized();
            }

            return Results.Ok(session);
        })
        .WithName("GetCurrentUser")
        .WithSummary("Get current authenticated user profile");

        return app;
    }

    private static string GetClientIp(HttpContext httpContext)
    {
        return httpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault()
               ?? httpContext.Connection.RemoteIpAddress?.ToString()
               ?? "127.0.0.1";
    }

    private static int? GetUserId(ClaimsPrincipal user)
    {
        var idClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                      ?? user.FindFirst("sub")?.Value;

        return int.TryParse(idClaim, out var id) ? id : null;
    }
}
