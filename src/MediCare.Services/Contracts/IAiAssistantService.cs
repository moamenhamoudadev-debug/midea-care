using MediCare.Services.DTOs;

namespace MediCare.Services.Contracts;

public interface IAiAssistantService
{
    Task<AiAssistantResponseDto> GenerateGuidanceAsync(AiAssistantRequestDto request, CancellationToken cancellationToken = default);
    bool IsConfigured { get; }
    string ProviderName { get; }
}
