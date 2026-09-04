using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace MEval.Api.Security;

public class FirstLoginGatewayFilter : IEndpointFilter
{
    private static readonly HashSet<(string Path, string Method)> AllowedRoutes = new()
    {
        ("/api/v1/auth/change-password", "POST"),
        ("/api/v1/auth/refresh", "POST"),
        ("/api/v1/auth/logout", "POST")
    };

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var user = httpContext.User;

        if (user.Identity?.IsAuthenticated == true)
        {
            var mustChangePasswordClaim = user.FindFirst("must_change_password")?.Value;
            if (string.Equals(mustChangePasswordClaim, "true", StringComparison.OrdinalIgnoreCase))
            {
                var requestPath = httpContext.Request.Path.Value?.TrimEnd('/') ?? string.Empty;
                var requestMethod = httpContext.Request.Method;

                var isAllowed = AllowedRoutes.Any(r => 
                    string.Equals(requestPath, r.Path, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(requestMethod, r.Method, StringComparison.OrdinalIgnoreCase));

                if (!isAllowed)
                {
                    return Results.Json(new
                    {
                        error = "PasswordChangeRequired",
                        message = "You must change your default password before accessing this resource."
                    }, statusCode: StatusCodes.Status403Forbidden);
                }
            }
        }

        return await next(context);
    }
}
