namespace MediCare.Services.Common;

/// <summary>
/// Configuration settings for the clinical AI assistant.
/// Supports OpenAI API and Azure OpenAI with secure environment variable loading.
/// </summary>
public sealed class AiAssistantSettings
{
    public const string SectionName = "AiAssistant";

    /// <summary>
    /// Provider selection: "OpenAI" (default) or "AzureOpenAI"
    /// </summary>
    public string Provider { get; set; } = "OpenAI";

    /// <summary>
    /// API Key loaded from User Secrets, environment variable OPENAI_API_KEY / AZURE_OPENAI_API_KEY.
    /// Never hardcoded or stored in Git repositories.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Model or Azure deployment name (e.g. gpt-4o-mini).
    /// </summary>
    public string Model { get; set; } = "gpt-4o-mini";

    /// <summary>
    /// Custom API endpoint. Required for Azure OpenAI (e.g., https://my-resource.openai.azure.com),
    /// optional for custom OpenAI proxies.
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// Request timeout in seconds.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 20;

    /// <summary>
    /// True when valid API credentials have been provided in the environment.
    /// </summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}
