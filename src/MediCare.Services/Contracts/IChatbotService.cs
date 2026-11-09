using MediCare.Services.DTOs;

namespace MediCare.Services.Contracts;

public interface IChatbotService
{
    Task<ChatbotResponseDto> ProcessMessageAsync(ChatbotRequestDto request);
}
