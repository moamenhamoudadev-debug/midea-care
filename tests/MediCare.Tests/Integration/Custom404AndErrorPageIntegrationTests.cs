using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Xunit;

namespace MediCare.Tests.Integration;

public class Custom404AndErrorPageIntegrationTests : IClassFixture<MediCareWebApplicationFactory>
{
    private readonly MediCareWebApplicationFactory _factory;

    public Custom404AndErrorPageIntegrationTests(MediCareWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Get_UnknownBrowserRoute_ReturnsHttp404WithCustomErrorPage()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/blah");

        // Assert: Must return HTTP 404 status code (not 200 or 302)
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("404 - الصفحة غير موجودة");
        content.Should().Contain("العودة للصفحة الرئيسية");
    }

    [Fact]
    public async Task Get_UnknownApiRoute_ReturnsHttp404WithoutHtmlErrorPage()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/nonexistent-clinical-endpoint");

        // Assert: Must preserve standard API 404 behavior and never return HTML error page
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().NotContain("<!DOCTYPE html>");
        content.Should().NotContain("404 - الصفحة غير موجودة");
    }

    [Fact]
    public async Task Get_UnknownRouteWithJsonAcceptHeader_ReturnsHttp404WithoutHtmlErrorPage()
    {
        // Arrange
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/unknown-path");
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        // Act
        var response = await client.SendAsync(request);

        // Assert: API/JSON clients must not receive an HTML response
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().NotContain("<!DOCTYPE html>");
        content.Should().NotContain("404 - الصفحة غير موجودة");
    }

    [Fact]
    public async Task Get_HomeErrorDirectly_ReturnsOkWithoutRedirectLoop()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/Home/Error");

        // Assert: Direct error page request succeeds with HTTP 200 and no infinite loop
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("عذراً، حدث خطأ غير متوقع");
    }
}
