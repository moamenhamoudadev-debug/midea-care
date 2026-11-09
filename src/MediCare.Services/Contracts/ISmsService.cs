namespace MediCare.Services.Contracts;

public interface ISmsService
{
    Task<bool> SendSmsAsync(string phoneNumber, string message);
}
