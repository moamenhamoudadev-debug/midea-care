using MailKit.Net.Smtp;
using MailKit.Security;
using MediCare.Services.Common;
using MediCare.Services.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace MediCare.Services.Implementations;

public class MailKitEmailService : IEmailService
{
    private readonly SmtpSettings _settings;
    private readonly ILogger<MailKitEmailService> _logger;

    public MailKitEmailService(IOptions<SmtpSettings> options, ILogger<MailKitEmailService> logger)
    {
        _settings = options.Value ?? new SmtpSettings();
        _logger = logger;
    }

    public async Task<bool> SendEmailAsync(string toEmail, string subject, string bodyHtml)
    {
        if (string.IsNullOrWhiteSpace(toEmail))
        {
            _logger.LogWarning("Email sending skipped: recipient address is empty.");
            return false;
        }

        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(_settings.SenderName, _settings.SenderEmail));
            message.To.Add(MailboxAddress.Parse(toEmail));
            message.Subject = subject;

            var bodyBuilder = new BodyBuilder
            {
                HtmlBody = bodyHtml
            };
            message.Body = bodyBuilder.ToMessageBody();

            using var client = new SmtpClient();

            // Allow tests / dev configurations without SSL if needed
            var secureSocketOptions = _settings.EnableSsl
                ? SecureSocketOptions.StartTls
                : SecureSocketOptions.Auto;

            // Connect
            await client.ConnectAsync(_settings.Host, _settings.Port, secureSocketOptions);

            // Authenticate if credentials supplied
            if (!string.IsNullOrEmpty(_settings.Username) && !string.IsNullOrEmpty(_settings.Password))
            {
                await client.AuthenticateAsync(_settings.Username, _settings.Password);
            }

            await client.SendAsync(message);
            await client.DisconnectAsync(true);

            _logger.LogInformation("Email successfully dispatched to {ToEmail} with subject '{Subject}'", toEmail, subject);
            return true;
        }
        catch (Exception ex)
        {
            // Do not break the core transactional workflow if email delivery fails
            _logger.LogError(ex, "Failed to deliver email to {ToEmail} with subject '{Subject}'. Error: {ErrorMessage}",
                toEmail, subject, ex.Message);
            return false;
        }
    }
}
