using FluentAssertions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace MediCare.Tests.Security;

public class SecurityHeadersTests
{
    [Fact]
    public async Task SecurityHeadersMiddleware_SetsExpectedDefensiveHeaders()
    {
        // Arrange
        var context = new DefaultHttpContext();
        RequestDelegate next = (ctx) =>
        {
            ctx.Response.StatusCode = 200;
            return Task.CompletedTask;
        };

        // Act: Run the security headers middleware delegate
        await MediCare.Web.Infrastructure.SecurityHeadersMiddleware.InvokeAsync(context, next);

        // Assert
        context.Response.Headers.Should().ContainKey("X-Content-Type-Options");
        context.Response.Headers["X-Content-Type-Options"].ToString().Should().Be("nosniff");

        context.Response.Headers.Should().ContainKey("X-Frame-Options");
        context.Response.Headers["X-Frame-Options"].ToString().Should().Be("SAMEORIGIN");

        context.Response.Headers.Should().ContainKey("Referrer-Policy");
        context.Response.Headers["Referrer-Policy"].ToString().Should().Be("strict-origin-when-cross-origin");

        context.Response.Headers.Should().ContainKey("Permissions-Policy");
        context.Response.Headers["Permissions-Policy"].ToString().Should().Contain("camera=");
        context.Response.Headers["Permissions-Policy"].ToString().Should().Contain("microphone=");

        context.Response.Headers.Should().ContainKey("Content-Security-Policy-Report-Only");
        var csp = context.Response.Headers["Content-Security-Policy-Report-Only"].ToString();
        csp.Should().Contain("default-src 'self'");
        csp.Should().NotContain("https://cdn.jsdelivr.net", "scripts and styles are now self-hosted under wwwroot/lib");
        csp.Should().NotContain("https://unpkg.com", "Leaflet is now self-hosted under wwwroot/lib");
        csp.Should().NotContain("https://fonts.googleapis.com", "fonts are now self-hosted under wwwroot/lib/fonts");
        csp.Should().NotContain("https://fonts.gstatic.com", "fonts are now self-hosted under wwwroot/lib/fonts");
    }

    [Fact]
    public void Views_VendorScriptsAreSelfHosted_NoThirdPartyScriptCdns()
    {
        // Arrange
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "MediCare.sln")))
        {
            dir = dir.Parent;
        }

        var viewsDir = Path.Combine(dir!.FullName, "src", "MediCare.Web", "Views");
        var viewFiles = Directory.GetFiles(viewsDir, "*.cshtml", SearchOption.AllDirectories);

        // Act & Assert
        foreach (var file in viewFiles)
        {
            var text = File.ReadAllText(file);
            text.Should().NotContain("https://cdn.jsdelivr.net", $"File {Path.GetFileName(file)} must use self-hosted assets instead of jsdelivr CDN");
            text.Should().NotContain("https://unpkg.com", $"File {Path.GetFileName(file)} must use self-hosted assets instead of unpkg CDN");
            text.Should().NotContain("https://cdnjs.cloudflare.com", $"File {Path.GetFileName(file)} must use self-hosted assets instead of cdnjs CDN");
            text.Should().NotContain("https://fonts.googleapis.com", $"File {Path.GetFileName(file)} must use self-hosted fonts");
            text.Should().NotContain("https://fonts.gstatic.com", $"File {Path.GetFileName(file)} must use self-hosted fonts");
        }
    }
}
