using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MediCare.Services.Common;
using MediCare.Services.Contracts;
using Microsoft.Extensions.Options;

namespace MediCare.Services.Implementations;

public sealed class OpenAiResponsesChatbotClient : IOpenAiChatbotClient
{
    private const string Endpoint = "https://api.openai.com/v1/responses";
    private readonly HttpClient _httpClient;
    private readonly OpenAiChatbotSettings _settings;

    public OpenAiResponsesChatbotClient(HttpClient httpClient, IOptions<OpenAiChatbotSettings> settings)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
    }

    public async Task<OpenAiChatbotResult?> GetGuidanceAsync(string message, string culture, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiKey)) return null;

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.ApiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(new
        {
            model = _settings.Model,
            store = false,
            instructions = "You are the MediCare educational clinic guide. This is not a clinical service. You may only suggest one broad medical specialty for a symptom description and give general appointment navigation information. Never diagnose, assess severity, rule out an emergency, prescribe or recommend medicine, tests, or treatment, and never request identity or payment details. If symptoms sound urgent, advise immediate local emergency care without trying to evaluate them. Do not invent clinic, doctor, pricing, or availability facts. If asked outside this scope, say a clinician or the relevant website page can help. Reply in Egyptian Arabic when the user's message is Arabic; otherwise use concise English. Be calm and brief. Specialty must be one of Cardiology, Dermatology, Pediatrics, Orthopedics, General Internal Medicine, or null. The reply must state that the suggestion is not a diagnosis.",
            input = message,
            text = new
            {
                format = new
                {
                    type = "json_schema",
                    name = "clinic_guide_response",
                    strict = true,
                    schema = new
                    {
                        type = "object",
                        properties = new
                        {
                            reply_text = new { type = "string" },
                            recommended_specialization = new { type = new[] { "string", "null" }, @enum = new string?[] { "Cardiology", "Dermatology", "Pediatrics", "Orthopedics", "General Internal Medicine", null } }
                        },
                        required = new[] { "reply_text", "recommended_specialization" },
                        additionalProperties = false
                    }
                }
            }
        }), Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode) return null;

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array) return null;

        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) continue;
            foreach (var part in content.EnumerateArray())
            {
                if (!part.TryGetProperty("type", out var type) || type.GetString() != "output_text" ||
                    !part.TryGetProperty("text", out var text)) continue;

                using var structured = JsonDocument.Parse(text.GetString() ?? "{}");
                var root = structured.RootElement;
                var reply = root.TryGetProperty("reply_text", out var replyElement) ? replyElement.GetString() : null;
                var specialty = root.TryGetProperty("recommended_specialization", out var specialtyElement) &&
                                specialtyElement.ValueKind == JsonValueKind.String
                    ? specialtyElement.GetString()
                    : null;
                return string.IsNullOrWhiteSpace(reply) ? null : new OpenAiChatbotResult(reply, specialty);
            }
        }

        return null;
    }
}
