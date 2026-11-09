using System.Net;
using System.Text;
using FluentAssertions;
using MediCare.Services.Common;
using MediCare.Services.Implementations;
using Microsoft.Extensions.Options;
using Xunit;

namespace MediCare.Tests.Services;

public class OpenAiResponsesChatbotClientTests
{
    [Fact]
    public async Task GetGuidanceAsync_ParsesStructuredResponse_AndDoesNotStoreIt()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;
        var handler = new StubHttpMessageHandler(async (request, token) =>
        {
            capturedRequest = request;
            capturedBody = await request.Content!.ReadAsStringAsync(token);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {"output":[{"type":"message","content":[{"type":"output_text","text":"{\"reply_text\":\"A Dermatology specialist may be suitable. This is not a diagnosis.\",\"recommended_specialization\":\"Dermatology\"}"}]}]}
                    """, Encoding.UTF8, "application/json")
            };
        });
        using var httpClient = new HttpClient(handler);
        var client = new OpenAiResponsesChatbotClient(httpClient,
            Options.Create(new OpenAiChatbotSettings { ApiKey = "test-key", Model = "test-model" }));

        var result = await client.GetGuidanceAsync("fictional skin rash", "en");

        result.Should().NotBeNull();
        result!.RecommendedSpecialization.Should().Be("Dermatology");
        result.ReplyText.Should().Contain("not a diagnosis");
        capturedRequest!.RequestUri!.AbsoluteUri.Should().Be("https://api.openai.com/v1/responses");
        capturedRequest.Headers.Authorization!.Parameter.Should().Be("test-key");
        capturedBody.Should().Contain("\"store\":false");
        capturedBody.Should().Contain("\"strict\":true");
    }

    [Fact]
    public async Task GetGuidanceAsync_WhenProviderReturnsError_ReturnsNullForLocalFallback()
    {
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.TooManyRequests)));
        using var httpClient = new HttpClient(handler);
        var client = new OpenAiResponsesChatbotClient(httpClient,
            Options.Create(new OpenAiChatbotSettings { ApiKey = "test-key" }));

        var result = await client.GetGuidanceAsync("fictional symptom", "en");

        result.Should().BeNull();
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }
}
