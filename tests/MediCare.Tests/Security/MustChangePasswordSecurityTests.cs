using FluentAssertions;
using MediCare.Data.Entities;
using MediCare.Web.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Moq;
using System.Security.Claims;
using Xunit;

namespace MediCare.Tests.Security;

public class MustChangePasswordSecurityTests
{
    [Fact]
    public async Task Middleware_WhenUserHasMustChangePasswordClaim_RedirectsToChangePassword()
    {
        // Arrange
        var context = new DefaultHttpContext();
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "user-123"),
            new Claim(ClaimTypes.Role, "Pharmacist"),
            new Claim("MustChangePassword", "true")
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        context.User = new ClaimsPrincipal(identity);
        context.Request.Path = "/Prescriptions/Verify";

        var nextCalled = false;
        RequestDelegate next = (ctx) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var middleware = new MustChangePasswordMiddleware(next);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status302Found);
        context.Response.Headers.Location.ToString().Should().Be("/Account/ChangePassword");
    }

    [Fact]
    public async Task Middleware_WhenUserDoesNotHaveClaim_AllowsRequestThrough()
    {
        // Arrange
        var context = new DefaultHttpContext();
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "user-123"),
            new Claim(ClaimTypes.Role, "Pharmacist")
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        context.User = new ClaimsPrincipal(identity);
        context.Request.Path = "/Prescriptions/Verify";

        var nextCalled = false;
        RequestDelegate next = (ctx) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var middleware = new MustChangePasswordMiddleware(next);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Theory]
    [InlineData("/Account/ChangePassword")]
    [InlineData("/account/changepassword")]
    [InlineData("/Account/Logout")]
    [InlineData("/css/site.css")]
    [InlineData("/lib/bootstrap/bootstrap.min.css")]
    [InlineData("/js/site.js")]
    public async Task Middleware_WhenOnAllowedPath_AllowsRequestThroughEvenWithClaim(string path)
    {
        // Arrange
        var context = new DefaultHttpContext();
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "user-123"),
            new Claim("MustChangePassword", "true")
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        context.User = new ClaimsPrincipal(identity);
        context.Request.Path = path;

        var nextCalled = false;
        RequestDelegate next = (ctx) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var middleware = new MustChangePasswordMiddleware(next);

        // Act
        await middleware.InvokeAsync(context);

        // Assert: Must not cause infinite redirect loop
        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Middleware_WhenUnauthenticated_AllowsRequestThrough()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity()); // Unauthenticated
        context.Request.Path = "/Account/Login";

        var nextCalled = false;
        RequestDelegate next = (ctx) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var middleware = new MustChangePasswordMiddleware(next);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Middleware_WhenUserHasClaimAndRequestsApi_Returns403ForbiddenWithJson()
    {
        // Arrange
        var context = new DefaultHttpContext();
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "user-123"),
            new Claim(ClaimTypes.Role, "Pharmacist"),
            new Claim("MustChangePassword", "true")
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        context.User = new ClaimsPrincipal(identity);
        context.Request.Path = "/api/appointments/check-conflict";

        var nextCalled = false;
        RequestDelegate next = (ctx) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var middleware = new MustChangePasswordMiddleware(next);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        context.Response.ContentType.Should().Contain("application/json");
    }

    [Fact]
    public async Task Middleware_WhenUserHasClaimAndSendsJsonAcceptHeader_Returns403Forbidden()
    {
        // Arrange
        var context = new DefaultHttpContext();
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "user-123"),
            new Claim(ClaimTypes.Role, "Pharmacist"),
            new Claim("MustChangePassword", "true")
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        context.User = new ClaimsPrincipal(identity);
        context.Request.Path = "/Prescriptions/Dispense";
        context.Request.Headers.Accept = "application/json";

        var nextCalled = false;
        RequestDelegate next = (ctx) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var middleware = new MustChangePasswordMiddleware(next);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        context.Response.ContentType.Should().Contain("application/json");
    }
}
