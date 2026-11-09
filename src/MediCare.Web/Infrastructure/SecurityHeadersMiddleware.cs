using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace MediCare.Web.Infrastructure;

public static class SecurityHeadersMiddleware
{
    public static async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        // Standard OWASP defensive security headers
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
        context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        context.Response.Headers["X-XSS-Protection"] = "0";

        // Permissions-Policy: Restrict geolocation; allow camera and microphone for teleconsultations (self and Jitsi Meet)
        context.Response.Headers["Permissions-Policy"] = "camera=(self \"https://meet.jit.si\"), microphone=(self \"https://meet.jit.si\"), geolocation=()";

        // Content-Security-Policy-Report-Only: Safely audits script/style/font/image/connect violations without breaking existing views
        context.Response.Headers["Content-Security-Policy-Report-Only"] =
            "default-src 'self'; " +
            "script-src 'self' 'unsafe-inline' 'unsafe-eval'; " +
            "style-src 'self' 'unsafe-inline'; " +
            "font-src 'self' data:; " +
            "img-src 'self' data: https://*.tile.openstreetmap.org; " +
            "connect-src 'self' wss: ws: https://meet.jit.si; " +
            "frame-src 'self' https://meet.jit.si; " +
            "object-src 'none'; " +
            "base-uri 'self';";

        await next(context);
    }
}

public static class SecurityHeadersExtensions
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
    {
        return app.Use(SecurityHeadersMiddleware.InvokeAsync);
    }
}
