using System.Text.RegularExpressions;

namespace MediCare.Services.Common;

public sealed record PrivacySanitizationResult(
    string SanitizedMessage,
    bool ContainsPii,
    List<string> DetectedPiiTypes);

/// <summary>
/// Enforces best-effort detection and redaction of direct personal identifiers
/// before any message can be transmitted to external AI providers.
/// Note: Best-effort redaction does not constitute guaranteed mathematical anonymization.
/// </summary>
public static class PatientDataPrivacyGuard
{
    // Egyptian National ID (14 digits starting with 2 or 3)
    private static readonly Regex NationalIdRegex = new(
        @"\b[23]\d{13}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Egyptian phone numbers (010, 011, 012, 015 followed by 8 digits, or with country code +20)
    private static readonly Regex EgyptianPhoneRegex = new(
        @"\b(?:\+?20|0)?1[0125]\d{8}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Generic international/standard phone numbers (10-14 digits)
    private static readonly Regex GeneralPhoneRegex = new(
        @"\b(?:\+?\d{1,3}[-.\s]?)?\(?\d{3}\)?[-.\s]?\d{3}[-.\s]?\d{4}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Email addresses
    private static readonly Regex EmailRegex = new(
        @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Potential 16-digit payment card sequences
    private static readonly Regex CardNumberRegex = new(
        @"\b(?:\d{4}[ -]?){4}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static PrivacySanitizationResult SanitizeInput(string? rawMessage)
    {
        if (string.IsNullOrWhiteSpace(rawMessage))
        {
            return new PrivacySanitizationResult(string.Empty, false, new List<string>());
        }

        var detectedTypes = new List<string>();
        string sanitized = rawMessage;

        if (NationalIdRegex.IsMatch(sanitized))
        {
            detectedTypes.Add("National ID");
            sanitized = NationalIdRegex.Replace(sanitized, "[REDACTED_NATIONAL_ID]");
        }

        if (EgyptianPhoneRegex.IsMatch(sanitized) || GeneralPhoneRegex.IsMatch(sanitized))
        {
            detectedTypes.Add("Phone Number");
            sanitized = EgyptianPhoneRegex.Replace(sanitized, "[REDACTED_PHONE]");
            sanitized = GeneralPhoneRegex.Replace(sanitized, "[REDACTED_PHONE]");
        }

        if (EmailRegex.IsMatch(sanitized))
        {
            detectedTypes.Add("Email Address");
            sanitized = EmailRegex.Replace(sanitized, "[REDACTED_EMAIL]");
        }

        if (CardNumberRegex.IsMatch(sanitized))
        {
            detectedTypes.Add("Payment Card Number");
            sanitized = CardNumberRegex.Replace(sanitized, "[REDACTED_CARD]");
        }

        bool hasPii = detectedTypes.Count > 0;
        return new PrivacySanitizationResult(sanitized, hasPii, detectedTypes);
    }
}
