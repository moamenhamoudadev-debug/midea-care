using System.Net.Http.Headers;
using System.Text;
using MediCare.Services.Common;
using MediCare.Services.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MediCare.Services.Implementations;

public class TwilioSmsService : ISmsService
{
    private readonly TwilioSettings _settings;
    private readonly HttpClient _httpClient;
    private readonly ILogger<TwilioSmsService> _logger;

    public TwilioSmsService(
        IOptions<TwilioSettings> options,
        ILogger<TwilioSmsService> logger,
        HttpClient? httpClient = null)
    {
        _settings = options.Value ?? new TwilioSettings();
        _logger = logger;
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<bool> SendSmsAsync(string phoneNumber, string message)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            _logger.LogWarning("SMS dispatch skipped: phone number is empty.");
            return false;
        }

        if (!_settings.Enabled || string.IsNullOrWhiteSpace(_settings.AccountSid) || string.IsNullOrWhiteSpace(_settings.AuthToken))
        {
            _logger.LogInformation("[TWILIO SIMULATION] To: {PhoneNumber} | Message: {Message}", phoneNumber, message);
            return true;
        }

        try
        {
            var requestUrl = $"https://api.twilio.com/2010-04-01/Accounts/{_settings.AccountSid}/Messages.json";
            using var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);

            var authBytes = Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));

            var formParams = new Dictionary<string, string>
            {
                { "To", phoneNumber },
                { "From", _settings.FromPhoneNumber },
                { "Body", message }
            };

            request.Content = new FormUrlEncodedContent(formParams);

            var response = await _httpClient.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Twilio SMS sent successfully to {PhoneNumber}", phoneNumber);
                return true;
            }

            var errorBody = await response.Content.ReadAsStringAsync();
            _logger.LogError("Twilio SMS dispatch failed with status {StatusCode}: {ErrorBody}", response.StatusCode, errorBody);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error dispatching Twilio SMS to {PhoneNumber}", phoneNumber);
            return false;
        }
    }
}
