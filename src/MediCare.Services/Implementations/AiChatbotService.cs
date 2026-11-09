using MediCare.Services.Contracts;
using MediCare.Services.DTOs;
using Microsoft.Extensions.Logging;

namespace MediCare.Services.Implementations;

public sealed class AiChatbotService : IChatbotService
{
    private readonly ChatbotService _fallback;
    private readonly IAiAssistantService _aiAssistant;
    private readonly ILogger<AiChatbotService> _logger;

    public AiChatbotService(
        ChatbotService fallback,
        IAiAssistantService aiAssistant,
        ILogger<AiChatbotService> logger)
    {
        _fallback = fallback;
        _aiAssistant = aiAssistant;
        _logger = logger;
    }

    public async Task<ChatbotResponseDto> ProcessMessageAsync(ChatbotRequestDto request)
    {
        // 1. Get fallback structure with quick replies and local doctors
        var fallback = await _fallback.ProcessMessageAsync(request);

        // 2. If it's an emergency, fallback immediately returns local emergency triage instructions
        if (fallback.IsEmergency)
        {
            return fallback;
        }

        // 3. If consent is not given, return fallback with a notice that consent is required for external AI
        if (!request.ConsentGiven)
        {
            fallback.ConsentRequired = true;
            fallback.EngineMode = "local_demo";
            return fallback;
        }

        // 4. Delegate to the configurable AI Assistant Service (OpenAI / Azure OpenAI)
        try
        {
            var aiRequest = new AiAssistantRequestDto
            {
                Message = request.Message,
                Culture = request.Culture,
                ConsentGiven = request.ConsentGiven
            };

            var aiResponse = await _aiAssistant.GenerateGuidanceAsync(aiRequest);

            fallback.ReplyText = aiResponse.ReplyText;
            fallback.RecommendedSpecialization = aiResponse.RecommendedSpecialization;
            fallback.EngineMode = aiResponse.ProviderUsed;
            fallback.IsEmergency = aiResponse.IsEmergency;
            fallback.PiiDetected = aiResponse.PiiRedacted;
            fallback.Disclaimer = aiResponse.Disclaimer;

            // Fetch matching doctors from local database for the recommended specialty
            if (!string.IsNullOrWhiteSpace(aiResponse.RecommendedSpecialization) && fallback.Doctors.Count == 0)
            {
                var doctorSuggestions = await _fallback.ProcessMessageAsync(new ChatbotRequestDto
                {
                    Message = aiResponse.RecommendedSpecialization,
                    Culture = request.Culture
                });
                fallback.Doctors = doctorSuggestions.Doctors;
            }

            return fallback;
        }
        catch (OperationCanceledException)
        {
            return fallback;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("AI assistant request encountered an issue. Using local fallback. Error type: {ErrorType}", ex.GetType().Name);
            return fallback;
        }
    }
}
