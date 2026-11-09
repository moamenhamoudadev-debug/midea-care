namespace MediCare.Services.Common;

/// <summary>
/// Authoritative server-side constants for MediCare's published Payment Terms,
/// Cancellation Policy, and Consumer Protection disclosures.
/// </summary>
public static class PaymentTermsConstants
{
    /// <summary>
    /// Current authoritative terms version published by MediCare administration.
    /// The server strictly enforces this identifier; client-submitted version strings are never trusted.
    /// </summary>
    public const string ActiveVersion = "v1.0-2026.11";

    /// <summary>
    /// Canonical content identifier for the published terms text.
    /// Used for tamper-evidence and audit verification against Egyptian Consumer Protection standards.
    /// </summary>
    public const string CanonicalContentHash = "sha256:482fc3e9aeb070bffbf5da465fd4faf132ae763f4df73c04e29e4bbb4a602f4c";

    /// <summary>
    /// Effective publication date in UTC.
    /// </summary>
    public static readonly DateTime EffectiveDateUtc = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
}
