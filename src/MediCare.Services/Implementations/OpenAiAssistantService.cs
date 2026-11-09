using System;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MediCare.Services.Common;
using MediCare.Services.Contracts;
using MediCare.Services.DTOs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MediCare.Services.Implementations;

/// <summary>
/// Configurable AI Assistant service integrating with OpenAI Responses API
/// and Azure OpenAI with strict privacy, consent, and medical safety guardrails.
/// </summary>
public sealed class OpenAiAssistantService : IAiAssistantService
{
    private const string DefaultOpenAiEndpoint = "https://api.openai.com/v1/responses";
    private readonly HttpClient _httpClient;
    private readonly AiAssistantSettings _settings;
    private readonly ILogger<OpenAiAssistantService> _logger;

    public OpenAiAssistantService(
        HttpClient httpClient,
        IOptions<AiAssistantSettings> settings,
        ILogger<OpenAiAssistantService> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public bool IsConfigured => _settings.IsConfigured;
    public string ProviderName => _settings.Provider;

    public async Task<AiAssistantResponseDto> GenerateGuidanceAsync(
        AiAssistantRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var culture = request.Culture ?? "en";
        bool isArabic = culture.StartsWith("ar", StringComparison.OrdinalIgnoreCase);

        // 1. Mandatory Explicit Consent Enforcement
        if (!request.ConsentGiven)
        {
            return new AiAssistantResponseDto
            {
                ReplyText = isArabic
                    ? "يلزم تأكيد الموافقة الصريحة قبل إرسال نص الأعراض إلى مزود الذكاء الاصطناعي. يرجى تفعيل مربع الموافقة، أو المتابعة باستخدام الدليل المحلي."
                    : "Explicit consent is required before transmitting symptom text to an external AI provider. Please check the consent box, or continue using the local guide.",
                IsConsentRequiredNotice = true,
                ProviderUsed = "local",
                Disclaimer = MedicalSafetyGuard.GetStandardDisclaimer(culture)
            };
        }

        // 2. Deterministic Server-Side Medical Emergency Check
        if (MedicalSafetyGuard.IsEmergencySymptom(request.Message))
        {
            return new AiAssistantResponseDto
            {
                ReplyText = MedicalSafetyGuard.GetEmergencyGuidance(culture),
                IsEmergency = true,
                ProviderUsed = "local_safety_guard",
                Disclaimer = MedicalSafetyGuard.GetStandardDisclaimer(culture)
            };
        }

        // 3. Deterministic Server-Side Medication & Prescription Check
        if (MedicalSafetyGuard.IsMedicationOrPrescriptionRequest(request.Message))
        {
            return new AiAssistantResponseDto
            {
                ReplyText = MedicalSafetyGuard.GetMedicationSafetyNotice(culture),
                IsEmergency = false,
                ProviderUsed = "local_safety_guard",
                Disclaimer = MedicalSafetyGuard.GetStandardDisclaimer(culture)
            };
        }

        // 4. Privacy & Personal Identifier (PII) Redaction Guard
        var privacyResult = PatientDataPrivacyGuard.SanitizeInput(request.Message);
        string sanitizedPrompt = privacyResult.SanitizedMessage;

        // 5. Provider Configuration Check
        if (!IsConfigured)
        {
            return new AiAssistantResponseDto
            {
                ReplyText = isArabic
                    ? "خدمة الذكاء الاصطناعي الخارجي غير مهيأة حالياً. يعمل المساعد في وضع الدليل الإرشادي المحلي."
                    : "The external AI assistant is currently unconfigured. Operating in local guide mode.",
                ProviderUsed = "unconfigured_local",
                PiiRedacted = privacyResult.ContainsPii,
                Disclaimer = MedicalSafetyGuard.GetStandardDisclaimer(culture)
            };
        }

        // 6. External Provider Invocation with Safe Error Handling
        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(5, _settings.TimeoutSeconds)));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            bool isAzure = string.Equals(_settings.Provider, "AzureOpenAI", StringComparison.OrdinalIgnoreCase);
            var endpoint = GetEndpoint(isAzure);

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
            ConfigureAuthentication(httpRequest, isAzure);
            httpRequest.Content = CreatePayload(sanitizedPrompt, isAzure);

            using var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, linkedCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("AI provider responded with non-success status code {StatusCode}. Reverting to local guidance.", response.StatusCode);
                return CreateFallbackResponse(culture, privacyResult.ContainsPii);
            }

            var guidance = isAzure
                ? await ParseAzureChatCompletionAsync(response, linkedCts.Token)
                : await ParseOpenAiResponsesAsync(response, linkedCts.Token);

            if (guidance == null)
            {
                return CreateFallbackResponse(culture, privacyResult.ContainsPii);
            }

            return new AiAssistantResponseDto
            {
                ReplyText = guidance.ReplyText,
                RecommendedSpecialization = IsAllowedSpecialty(guidance.RecommendedSpecialization) ? guidance.RecommendedSpecialization : null,
                ProviderUsed = isAzure ? "azure_openai" : "openai",
                PiiRedacted = privacyResult.ContainsPii,
                Disclaimer = MedicalSafetyGuard.GetStandardDisclaimer(culture)
            };
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("AI provider request timed out after {Timeout} seconds.", _settings.TimeoutSeconds);
            return CreateFallbackResponse(culture, privacyResult.ContainsPii);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error occurred during AI provider communication. Safe local fallback engaged.");
            return CreateFallbackResponse(culture, privacyResult.ContainsPii);
        }
    }

    private string GetEndpoint(bool isAzure)
    {
        if (isAzure)
        {
            var baseEndpoint = _settings.Endpoint?.TrimEnd('/') ?? "https://medicare.openai.azure.com";
            return $"{baseEndpoint}/openai/deployments/{_settings.Model}/chat/completions?api-version=2024-02-15-preview";
        }

        return !string.IsNullOrWhiteSpace(_settings.Endpoint) ? _settings.Endpoint : DefaultOpenAiEndpoint;
    }

    private void ConfigureAuthentication(HttpRequestMessage request, bool isAzure)
    {
        if (isAzure)
        {
            request.Headers.Add("api-key", _settings.ApiKey);
        }
        else
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.ApiKey);
        }
    }

    private StringContent CreatePayload(string prompt, bool isAzure)
    {
        const string systemInstructions = "You are the MediCare educational clinic guide. This is not a clinical service. You may only suggest one broad medical specialty for a symptom description and give general appointment navigation information. Never diagnose, assess severity, rule out an emergency, prescribe or recommend medicine, tests, or treatment, and never request identity or payment details. If symptoms sound urgent, advise immediate local emergency care without trying to evaluate them. Do not invent clinic, doctor, pricing, or availability facts. If asked outside this scope, say a clinician or the relevant website page can help. Reply in Egyptian Arabic when the user's message is Arabic; otherwise use concise English. Be calm and brief. Specialty must be one of Cardiology, Dermatology, Pediatrics, Orthopedics, General Internal Medicine, or null. The reply must state that the suggestion is not a diagnosis. Return a JSON object with string reply_text and recommended_specialization (one allowed specialty or null).";

        if (isAzure)
        {
            var azureBody = new
            {
                messages = new object[]
                {
                    new { role = "system", content = systemInstructions },
                    new { role = "user", content = prompt }
                },
                max_tokens = 300,
                temperature = 0.2,
                response_format = new { type = "json_object" }
            };
            return new StringContent(JsonSerializer.Serialize(azureBody), Encoding.UTF8, "application/json");
        }

        var openAiBody = new
        {
            model = _settings.Model,
            store = false,
            instructions = systemInstructions,
            input = prompt,
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
                            recommended_specialization = new
                            {
                                type = new[] { "string", "null" },
                                @enum = new string?[] { "Cardiology", "Dermatology", "Pediatrics", "Orthopedics", "General Internal Medicine", null }
                            }
                        },
                        required = new[] { "reply_text", "recommended_specialization" },
                        additionalProperties = false
                    }
                }
            }
        };

        return new StringContent(JsonSerializer.Serialize(openAiBody), Encoding.UTF8, "application/json");
    }

    private static async Task<OpenAiChatbotResult?> ParseOpenAiResponsesAsync(HttpResponseMessage response, CancellationToken token)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: token);
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

    private static async Task<OpenAiChatbotResult?> ParseAzureChatCompletionAsync(HttpResponseMessage response, CancellationToken token)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: token);
        if (!document.RootElement.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0) return null;

        var firstChoice = choices[0];
        if (!firstChoice.TryGetProperty("message", out var message) ||
            !message.TryGetProperty("content", out var contentElement)) return null;

        var text = contentElement.GetString();
        if (string.IsNullOrWhiteSpace(text)) return null;

        try
        {
            using var structured = JsonDocument.Parse(text);
            var root = structured.RootElement;
            var reply = root.TryGetProperty("reply_text", out var replyElement) ? replyElement.GetString() : null;
            var specialty = root.TryGetProperty("recommended_specialization", out var specialtyElement) && specialtyElement.ValueKind == JsonValueKind.String
                ? specialtyElement.GetString()
                : null;
            if (!string.IsNullOrWhiteSpace(reply)) return new OpenAiChatbotResult(reply, specialty);
        }
        catch (JsonException)
        {
            // Older Azure deployments may ignore JSON mode; retain their plain text reply without trusting a specialty value.
        }

        return new OpenAiChatbotResult(text, null);
    }

    private static bool IsAllowedSpecialty(string? value) => value is "Cardiology" or "Dermatology" or "Pediatrics" or "Orthopedics" or "General Internal Medicine";

    private static AiAssistantResponseDto CreateFallbackResponse(string culture, bool piiRedacted)
    {
        bool isArabic = culture.StartsWith("ar", StringComparison.OrdinalIgnoreCase);
        return new AiAssistantResponseDto
        {
            ReplyText = isArabic
                ? "تعذر الاتصال بمزود الذكاء الاصطناعي في الوقت الحالي. يمكنك تصفح أطباء العيادة حسب التخصص أو المحافظة مباشرة عبر الموقع."
                : "Unable to reach the external AI service at this time. You can directly browse clinic physicians by specialty or governorate on the portal.",
            ProviderUsed = "local_fallback",
            PiiRedacted = piiRedacted,
            Disclaimer = MedicalSafetyGuard.GetStandardDisclaimer(culture)
        };
    }
}
