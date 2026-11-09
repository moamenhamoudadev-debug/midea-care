namespace MediCare.Services.Contracts;

/// <summary>
/// Lightweight direct OpenAI client for clinic navigation and specialization guidance using the Responses API format.
/// <para>
/// Note: For full enterprise capabilities including patient consent gating, PII redaction guardrails,
/// prompt injection defense, and Azure OpenAI failover, use <see cref="IAiAssistantService"/>.
/// </para>
/// </summary>
public interface IOpenAiChatbotClient
{
    Task<OpenAiChatbotResult?> GetGuidanceAsync(string message, string culture, CancellationToken cancellationToken = default);
}

public sealed record OpenAiChatbotResult(string ReplyText, string? RecommendedSpecialization);
