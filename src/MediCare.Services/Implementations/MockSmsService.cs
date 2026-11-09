using MediCare.Services.Contracts;
using Microsoft.Extensions.Logging;

namespace MediCare.Services.Implementations;

public class MockSmsService : ISmsService
{
    private readonly ILogger<MockSmsService> _logger;

    public MockSmsService(ILogger<MockSmsService> logger)
    {
        _logger = logger;
    }

    public Task<bool> SendSmsAsync(string phoneNumber, string message)
    {
        _logger.LogInformation("[MOCK SMS DISPATCH] To: {PhoneNumber} | Message: {Message}", phoneNumber, message);
        return Task.FromResult(true);
    }
}
