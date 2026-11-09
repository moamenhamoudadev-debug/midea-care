namespace MediCare.Services.Common;

public sealed class OpenAiChatbotSettings
{
    public const string SectionName = "OpenAI";
    public string? ApiKey { get; set; }
    public string Model { get; set; } = "gpt-4o-mini";
}
