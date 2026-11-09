namespace MediCare.Services.Common;

public class SmtpSettings
{
    public const string SectionName = "Smtp";

    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 25;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string SenderEmail { get; set; } = "no-reply@medicare.com";
    public string SenderName { get; set; } = "MediCare Outpatient Clinic";
    public bool EnableSsl { get; set; } = false;
}
