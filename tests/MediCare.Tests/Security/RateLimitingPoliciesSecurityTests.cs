using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using MediCare.Tests.Integration;
using Xunit;

namespace MediCare.Tests.Security;

[Trait("Category", "Security")]
public class RateLimitingPoliciesSecurityTests : IClassFixture<MediCareWebApplicationFactory>
{
    private readonly MediCareWebApplicationFactory _factory;

    public RateLimitingPoliciesSecurityTests(MediCareWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ChatbotRateLimitPolicy_EleventhRequestInWindow_ReceivesHttp429TooManyRequests()
    {
        var client = _factory.CreateAnonymousClient();
        var payload = new { message = "Hello assistant", culture = "en", consentGiven = false };

        for (int i = 1; i <= 10; i++)
        {
            var response = await client.PostAsJsonAsync("/api/chatbot/message", payload);
            response.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests,
                $"Chatbot request {i} of 10 must be permitted");
        }

        var eleventhResponse = await client.PostAsJsonAsync("/api/chatbot/message", payload);
        eleventhResponse.StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
            "The 11th request to Chatbot API must be rejected with 429 Too Many Requests");
    }

    [Fact]
    public async Task PrescriptionVerificationPolicy_SixteenthRequestInWindow_ReceivesHttp429TooManyRequests()
    {
        var client = _factory.CreateAnonymousClient();

        for (int i = 1; i <= 15; i++)
        {
            var response = await client.GetAsync("/Prescriptions/Verify?token=invalid_dummy_token_for_rate_limit_test");
            response.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests,
                $"Prescription verification request {i} of 15 must be permitted");
        }

        var sixteenthResponse = await client.GetAsync("/Prescriptions/Verify?token=invalid_dummy_token_for_rate_limit_test");
        sixteenthResponse.StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
            "The 16th request to public prescription token verification must be rejected with 429 Too Many Requests");
    }

    [Fact]
    public async Task LoginRateLimitPolicy_SixthRequestInWindow_ReceivesHttp429TooManyRequests()
    {
        var client = _factory.CreateAnonymousClient();
        var form = new Dictionary<string, string>
        {
            ["Email"] = "nonexistent.user@clinic.test",
            ["Password"] = "WrongPassword123!"
        };

        for (int i = 1; i <= 5; i++)
        {
            var response = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(form));
            response.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests,
                $"Login request {i} of 5 must be permitted");
        }

        var sixthResponse = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(form));
        sixthResponse.StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
            "The 6th login attempt from the same client within 1 minute must be rejected with 429 Too Many Requests");
    }

    [Fact]
    public async Task RegistrationRateLimitPolicy_SixthRequestInWindow_ReceivesHttp429TooManyRequests()
    {
        var client = _factory.CreateAnonymousClient();
        var form = new Dictionary<string, string>
        {
            ["FullName"] = "Spam User",
            ["Email"] = "spam@clinic.test",
            ["Password"] = "Short1!",
            ["ConfirmPassword"] = "Short1!"
        };

        for (int i = 1; i <= 5; i++)
        {
            var response = await client.PostAsync("/Account/RegisterPatient", new FormUrlEncodedContent(form));
            response.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests,
                $"Registration request {i} of 5 must be permitted");
        }

        var sixthResponse = await client.PostAsync("/Account/RegisterPatient", new FormUrlEncodedContent(form));
        sixthResponse.StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
            "The 6th registration attempt from the same client within 1 minute must be rejected with 429 Too Many Requests");
    }
}
