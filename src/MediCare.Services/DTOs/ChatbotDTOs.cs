namespace MediCare.Services.DTOs;

public class ChatbotRequestDto
{
    public string Message { get; set; } = string.Empty;
    public string? Culture { get; set; } = "en";

    /// <summary>
    /// Explicit, user-confirmed consent to transmit entered symptom text to the external AI provider.
    /// Default is false. When false, zero external provider calls are made.
    /// </summary>
    public bool ConsentGiven { get; set; } = false;
}

public class ChatbotResponseDto
{
    public string ReplyText { get; set; } = string.Empty;
    public string ReplyMessage => ReplyText;
    public string? RecommendedSpecialization { get; set; }
    public string? SpecialtySuggested => RecommendedSpecialization;
    public bool IsEmergency { get; set; }
    public string EngineMode { get; set; } = "rule_based"; // "openai", "azure_openai", "rule_based", "unconfigured"
    public string? EmergencyNotice { get; set; }
    public bool ConsentRequired { get; set; }
    public bool PiiDetected { get; set; }
    public string? Disclaimer { get; set; }
    public List<ChatbotDoctorCardDto> Doctors { get; set; } = new();
    public List<ChatbotDoctorCardDto> RecommendedDoctors => Doctors;
    public List<string> QuickReplies { get; set; } = new();
}

public class ChatbotDoctorCardDto
{
    public int Id { get; set; }
    public int DoctorId => Id;
    public string FullName { get; set; } = string.Empty;
    public string Specialization { get; set; } = string.Empty;
    public decimal Fee { get; set; }
    public decimal ConsultationFee => Fee;
    public string? AvatarUrl { get; set; }
    public string? ProfileImageUrl => AvatarUrl;
}

public class AiAssistantRequestDto
{
    public string Message { get; set; } = string.Empty;
    public string? Culture { get; set; } = "en";
    public bool ConsentGiven { get; set; } = false;
}

public class AiAssistantResponseDto
{
    public string ReplyText { get; set; } = string.Empty;
    public string? RecommendedSpecialization { get; set; }
    public bool IsEmergency { get; set; }
    public string ProviderUsed { get; set; } = "local"; // "openai", "azure_openai", "local"
    public bool IsConsentRequiredNotice { get; set; }
    public bool PiiRedacted { get; set; }
    public string? Disclaimer { get; set; }
    public List<string> QuickReplies { get; set; } = new();
}
