using FluentAssertions;
using MediCare.Web.Infrastructure;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace MediCare.Tests.Security;

public class CookiePolicySecurityTests
{
    [Fact]
    public void ConfigureSecurityCookies_InDevelopment_SetsSameAsRequestForLocalHttp()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthentication();

        // Act
        services.ConfigureSecurityCookies(isDevelopment: true);
        var provider = services.BuildServiceProvider();

        var authOptions = provider.GetRequiredService<IOptionsSnapshot<CookieAuthenticationOptions>>()
            .Get(Microsoft.AspNetCore.Identity.IdentityConstants.ApplicationScheme);
        var antiforgeryOptions = provider.GetRequiredService<IOptions<AntiforgeryOptions>>().Value;

        // Assert
        authOptions.Cookie.SecurePolicy.Should().Be(CookieSecurePolicy.SameAsRequest,
            "development must allow local HTTP testing without browsers dropping the auth cookie");
        antiforgeryOptions.Cookie.SecurePolicy.Should().Be(CookieSecurePolicy.SameAsRequest,
            "development must allow local HTTP testing for antiforgery verification");
    }

    [Fact]
    public void ConfigureSecurityCookies_InProduction_EnforcesSecurePolicyAlways()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthentication();

        // Act
        services.ConfigureSecurityCookies(isDevelopment: false);
        var provider = services.BuildServiceProvider();

        var authOptions = provider.GetRequiredService<IOptionsSnapshot<CookieAuthenticationOptions>>()
            .Get(Microsoft.AspNetCore.Identity.IdentityConstants.ApplicationScheme);
        var antiforgeryOptions = provider.GetRequiredService<IOptions<AntiforgeryOptions>>().Value;

        // Assert
        authOptions.Cookie.SecurePolicy.Should().Be(CookieSecurePolicy.Always,
            "production must strictly require HTTPS for all authentication cookies");
        antiforgeryOptions.Cookie.SecurePolicy.Should().Be(CookieSecurePolicy.Always,
            "production must strictly require HTTPS for antiforgery cookies");
        authOptions.Cookie.HttpOnly.Should().BeTrue();
        antiforgeryOptions.Cookie.HttpOnly.Should().BeTrue();
    }
}
