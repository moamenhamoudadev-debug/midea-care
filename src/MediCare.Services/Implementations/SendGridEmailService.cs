using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MediCare.Services.Common;
using MediCare.Services.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MediCare.Services.Implementations;

public class SendGridEmailService : IEmailService
{
    private readonly SendGridSettings _settings;
    private readonly HttpClient _httpClient;
    private readonly ILogger<SendGridEmailService> _logger;

    public SendGridEmailService(
        IOptions<SendGridSettings> options,
        ILogger<SendGridEmailService> logger,
        HttpClient? httpClient = null)
    {
        _settings = options.Value ?? new SendGridSettings();
        _logger = logger;
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<bool> SendEmailAsync(string toEmail, string subject, string bodyHtml)
    {
        if (string.IsNullOrWhiteSpace(toEmail))
        {
            _logger.LogWarning("SendGrid email skipped: recipient address is empty.");
            return false;
        }

        if (!_settings.Enabled || string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            _logger.LogInformation("[SENDGRID SIMULATION] To: {ToEmail} | Subject: {Subject}", toEmail, subject);
            return true;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.sendgrid.com/v3/mail/send");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.ApiKey);

            var payload = new
            {
                personalizations = new[]
                {
                    new
                    {
                        to = new[] { new { email = toEmail } }
                    }
                },
                from = new
                {
                    email = _settings.SenderEmail,
                    name = _settings.SenderName
                },
                subject = subject,
                content = new[]
                {
                    new
                    {
                        type = "text/html",
                        value = bodyHtml
                    }
                }
            };

            var jsonContent = JsonSerializer.Serialize(payload);
            request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("SendGrid email sent successfully to {ToEmail}", toEmail);
                return true;
            }

            var errorBody = await response.Content.ReadAsStringAsync();
            _logger.LogError("SendGrid dispatch failed with status {StatusCode}: {ErrorBody}", response.StatusCode, errorBody);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error dispatching SendGrid email to {ToEmail}", toEmail);
            return false;
        }
    }
}
