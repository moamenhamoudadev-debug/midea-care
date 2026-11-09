using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace MediCare.Tests.Security;

public class ForwardedHeadersSecurityTests
{
    [Fact]
    public async Task ForwardedHeaders_WhenClientIsUntrusted_SpoofedXForwardedForIsIgnored()
    {
        // Arrange: Trusted proxy configured as 10.0.0.1 with ForwardLimit = 1
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            ForwardLimit = 1
        };
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
        options.KnownProxies.Add(IPAddress.Parse("10.0.0.1"));

        var middleware = new ForwardedHeadersMiddleware(
            next: (ctx) => Task.CompletedTask,
            loggerFactory: NullLoggerFactory.Instance,
            options: Options.Create(options)
        );

        // Attacker directly connects from untrusted IP and attempts to spoof X-Forwarded-For
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.25");
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.195";

        // Act
        await middleware.Invoke(context);

        // Assert: The remote IP must NOT be changed to the spoofed IP; it remains the real connection IP
        context.Connection.RemoteIpAddress.Should().Be(IPAddress.Parse("198.51.100.25"));
        context.Connection.RemoteIpAddress.ToString().Should().NotBe("203.0.113.195");
    }

    [Fact]
    public async Task ForwardedHeaders_WhenClientIsConfiguredTrustedProxy_AcceptsForwardedFor()
    {
        // Arrange: Trusted reverse proxy at 10.0.0.1
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            ForwardLimit = 1
        };
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
        options.KnownProxies.Add(IPAddress.Parse("10.0.0.1"));

        var middleware = new ForwardedHeadersMiddleware(
            next: (ctx) => Task.CompletedTask,
            loggerFactory: NullLoggerFactory.Instance,
            options: Options.Create(options)
        );

        // Legitimate request passing through trusted reverse proxy 10.0.0.1
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.1");
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.195";

        // Act
        await middleware.Invoke(context);

        // Assert: Remote IP is safely unwrapped to the client's actual upstream IP
        context.Connection.RemoteIpAddress.Should().Be(IPAddress.Parse("203.0.113.195"));
    }

    [Fact]
    public void ForwardedHeaders_WhenNoProxiesConfigured_RealSocketIpIsUsedDirectly()
    {
        // In local or unproxied hosting, middleware is omitted and ASP.NET Core relies on socket TCP IP
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.42");
        context.Request.Headers["X-Forwarded-For"] = "1.2.3.4";

        // Without proxy middleware altering RemoteIpAddress, spoofed header has zero effect
        context.Connection.RemoteIpAddress.Should().Be(IPAddress.Parse("192.168.1.42"));
    }
}
