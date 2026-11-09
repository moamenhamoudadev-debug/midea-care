using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using MediCare.Tests.Integration;
using Xunit;

namespace MediCare.Tests.Security;

[Trait("Category", "Security")]
public class PromoRateLimitSecurityTests : IClassFixture<MediCareWebApplicationFactory>
{
    private readonly MediCareWebApplicationFactory _factory;

    public PromoRateLimitSecurityTests(MediCareWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ValidatePromo_EleventhRequestInWindow_ReceivesHttp429TooManyRequests()
    {
        // Arrange: Create authenticated patient client with unique user ID
        var testUserId = "rate_limit_patient_" + Guid.NewGuid().ToString("N");
        var client = _factory.CreateAuthenticatedClient(testUserId, "Patient");

        var requestPayload = new
        {
            Code = "WELCOME10",
            Amount = 300m
        };

        // Act & Assert: First 10 requests within the window must be permitted by the rate limiter
        for (int i = 1; i <= 10; i++)
        {
            var response = await client.PostAsJsonAsync("/api/payment/validate-promo", requestPayload);
            response.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests,
                $"Request {i} of 10 should be within the permit limit and not rejected with 429");
        }

        // The 11th request in the 1-minute window must be rejected with 429 Too Many Requests
        var eleventhResponse = await client.PostAsJsonAsync("/api/payment/validate-promo", requestPayload);
        eleventhResponse.StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
            "The 11th validation request within the fixed window must be rejected by PromoRateLimitPolicy with HTTP 429");
    }

    [Fact]
    public async Task ValidatePromo_WhenNonPatientRole_ReturnsHttp403Forbidden()
    {
        // Arrange: Create authenticated doctor client
        var doctorClient = _factory.CreateAuthenticatedClient("doc_attacker_" + Guid.NewGuid().ToString("N"), "Doctor");

        var requestPayload = new
        {
            Code = "WELCOME10",
            Amount = 300m
        };

        // Act
        var response = await doctorClient.PostAsJsonAsync("/api/payment/validate-promo", requestPayload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "Non-patients (Doctors, Pharmacists, Admins) must receive 403 Forbidden on /api/payment/validate-promo");
    }

    [Fact]
    public async Task BookAppointmentGet_WhenNonPatientRole_ReturnsHttp403Forbidden()
    {
        // Arrange: Create authenticated doctor client
        var doctorClient = _factory.CreateAuthenticatedClient("doc_attacker_" + Guid.NewGuid().ToString("N"), "Doctor");

        // Act
        var response = await doctorClient.GetAsync("/Appointments/Book/1");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "Non-patients must receive 403 Forbidden when trying to access GET /Appointments/Book/{doctorId}");
    }
}
