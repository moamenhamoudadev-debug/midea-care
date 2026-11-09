namespace MediCare.Services.Common;

public class TwilioSettings
{
    public bool Enabled { get; set; } = false;
    public string AccountSid { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
    public string FromPhoneNumber { get; set; } = "+10000000000";
}

public class SendGridSettings
{
    public bool Enabled { get; set; } = false;
    public string ApiKey { get; set; } = string.Empty;
    public string SenderEmail { get; set; } = "noreply@medicare.local";
    public string SenderName { get; set; } = "MediCare Clinic System";
}
