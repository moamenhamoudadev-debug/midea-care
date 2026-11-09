using Microsoft.AspNetCore.Http;

namespace MediCare.Web.Infrastructure;

/// <summary>
/// Intercepts requests for authenticated users possessing the 'MustChangePassword' claim.
/// Forces redirection to /Account/ChangePassword until the user successfully updates their password.
/// </summary>
public class MustChangePasswordMiddleware
{
    private readonly RequestDelegate _next;

    public MustChangePasswordMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var hasMustChangePasswordClaim = context.User.HasClaim(c =>
                c.Type == "MustChangePassword" &&
                string.Equals(c.Value, "true", StringComparison.OrdinalIgnoreCase));

            if (hasMustChangePasswordClaim)
            {
                var path = context.Request.Path.Value ?? string.Empty;

                // Allow password change endpoint, logout endpoint, and static assets
                var isAllowedEndpoint =
                    path.StartsWith("/Account/ChangePassword", StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith("/Account/Logout", StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith("/css", StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith("/js", StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith("/lib", StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith("/images", StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith("/favicon", StringComparison.OrdinalIgnoreCase);

                if (!isAllowedEndpoint)
                {
                    var isApiOrJson =
                        path.StartsWith("/api", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(context.Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase) ||
                        context.Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase);

                    if (isApiOrJson)
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsJsonAsync(new
                        {
                            error = "Password change required.",
                            redirectUrl = "/Account/ChangePassword"
                        });
                        return;
                    }

                    context.Response.Redirect("/Account/ChangePassword");
                    return;
                }
            }
        }

        await _next(context);
    }
}
