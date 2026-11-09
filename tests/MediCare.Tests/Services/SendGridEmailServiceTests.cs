using System.Net;
using System.Text.Json;
using FluentAssertions;
using MediCare.Services.Common;
using MediCare.Services.Contracts;
using MediCare.Services.Implementations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Xunit;

namespace MediCare.Tests.Services;

public class SendGridEmailServiceTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SendEmailAsync_WhenRecipientIsEmpty_ReturnsFalse(string? emptyEmail)
    {
        // Arrange
        var options = Options.Create(new SendGridSettings { Enabled = true, ApiKey = "SG.test-key" });
        var service = new SendGridEmailService(options, NullLogger<SendGridEmailService>.Instance);

        // Act
        var result = await service.SendEmailAsync(emptyEmail!, "Test Subject", "<p>Hello</p>");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task SendEmailAsync_WhenDisabled_SimulatesAndReturnsTrue()
    {
        // Arrange
        var options = Options.Create(new SendGridSettings
        {
            Enabled = false,
            ApiKey = "SG.any-key",
            SenderEmail = "clinic@medicare.com",
            SenderName = "MediCare"
        });
        var service = new SendGridEmailService(options, NullLogger<SendGridEmailService>.Instance);

        // Act
        var result = await service.SendEmailAsync("patient@example.com", "Appointment Confirmed", "<p>Confirmed</p>");

        // Assert
        result.Should().BeTrue("Disabled mode must simulate successfully without making network calls");
    }

    [Fact]
    public async Task SendEmailAsync_WhenMissingApiKey_SimulatesAndReturnsTrue()
    {
        // Arrange
        var options = Options.Create(new SendGridSettings
        {
            Enabled = true,
            ApiKey = "", // Empty API key
            SenderEmail = "clinic@medicare.com",
            SenderName = "MediCare"
        });
        var service = new SendGridEmailService(options, NullLogger<SendGridEmailService>.Instance);

        // Act
        var result = await service.SendEmailAsync("patient@example.com", "Appointment Confirmed", "<p>Confirmed</p>");

        // Assert
        result.Should().BeTrue("Missing API key must gracefully fall back to simulation mode");
    }

    [Fact]
    public async Task SendEmailAsync_WhenConfiguredWithApiKey_SendsAuthorizedV3Request_ReturnsTrue()
    {
        // Arrange
        var mockHttpHandler = new Mock<HttpMessageHandler>();
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;

        mockHttpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>(async (req, _) =>
            {
                capturedRequest = req;
                if (req.Content != null)
                {
                    capturedBody = await req.Content.ReadAsStringAsync();
                }
                return new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.Accepted // SendGrid returns 202 Accepted
                };
            });

        using var httpClient = new HttpClient(mockHttpHandler.Object);
        var options = Options.Create(new SendGridSettings
        {
            Enabled = true,
            ApiKey = "SG.VALID_PRODUCTION_KEY_XYZ_123",
            SenderEmail = "appointments@medicare-clinic.com",
            SenderName = "MediCare Official"
        });

        var service = new SendGridEmailService(options, NullLogger<SendGridEmailService>.Instance, httpClient);

        // Act
        var result = await service.SendEmailAsync("patient@domain.com", "Your Prescription is Ready", "<b>Prescription #100</b>");

        // Assert
        result.Should().BeTrue();
        capturedRequest.Should().NotBeNull();
        capturedRequest!.RequestUri.Should().Be(new Uri("https://api.sendgrid.com/v3/mail/send"));
        capturedRequest.Headers.Authorization.Should().NotBeNull();
        capturedRequest.Headers.Authorization!.Scheme.Should().Be("Bearer");
        capturedRequest.Headers.Authorization.Parameter.Should().Be("SG.VALID_PRODUCTION_KEY_XYZ_123");

        capturedBody.Should().NotBeNull();
        using var jsonDoc = JsonDocument.Parse(capturedBody!);
        var root = jsonDoc.RootElement;
        root.GetProperty("subject").GetString().Should().Be("Your Prescription is Ready");
        root.GetProperty("from").GetProperty("email").GetString().Should().Be("appointments@medicare-clinic.com");
        root.GetProperty("from").GetProperty("name").GetString().Should().Be("MediCare Official");
    }

    [Fact]
    public async Task SendEmailAsync_WhenSendGridReturnsErrorStatusCode_LogsAndReturnsFalse()
    {
        // Arrange
        var mockHttpHandler = new Mock<HttpMessageHandler>();
        mockHttpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.Unauthorized,
                Content = new StringContent("{\"errors\":[{\"message\":\"The provided authorization grant is invalid\"}]}")
            });

        using var httpClient = new HttpClient(mockHttpHandler.Object);
        var options = Options.Create(new SendGridSettings
        {
            Enabled = true,
            ApiKey = "SG.EXPIRED_KEY",
            SenderEmail = "appointments@medicare.com"
        });

        var service = new SendGridEmailService(options, NullLogger<SendGridEmailService>.Instance, httpClient);

        // Act
        var result = await service.SendEmailAsync("patient@domain.com", "Test", "<p>Body</p>");

        // Assert
        result.Should().BeFalse("Unauthorized API key must be reported as false delivery failure");
    }

    [Fact]
    public async Task DynamicResolution_WhenSendGridEnabled_ResolvesSendGridEmailService()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<SendGridSettings>(opt =>
        {
            opt.Enabled = true;
            opt.ApiKey = "SG.real-api-key";
        });
        services.Configure<SmtpSettings>(_ => { });

        MediCare.Services.Extensions.ServiceCollectionExtensions.AddApplicationServices(services);
        using var provider = services.BuildServiceProvider();

        // Act
        var emailService = provider.GetRequiredService<IEmailService>();

        // Assert
        emailService.Should().BeOfType<SendGridEmailService>("When SendGrid is enabled with an API key, SendGridEmailService must be resolved");
    }

    [Fact]
    public async Task DynamicResolution_WhenSendGridDisabled_ResolvesMailKitEmailService()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<SendGridSettings>(opt =>
        {
            opt.Enabled = false;
            opt.ApiKey = "";
        });
        services.Configure<SmtpSettings>(_ => { });

        MediCare.Services.Extensions.ServiceCollectionExtensions.AddApplicationServices(services);
        using var provider = services.BuildServiceProvider();

        // Act
        var emailService = provider.GetRequiredService<IEmailService>();

        // Assert
        emailService.Should().BeOfType<MailKitEmailService>("When SendGrid is disabled, MailKitEmailService must be resolved as standard fallback");
    }
}
