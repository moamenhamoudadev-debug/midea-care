namespace MediCare.Services.Common;

/// <summary>
/// Configuration settings for medical prescriptions.
/// </summary>
public class PrescriptionSettings
{
    /// <summary>
    /// Default validity window (in days) from the prescription issue date during which
    /// a pharmacy is authorized to dispense the medication. Defaults to 30 days.
    /// </summary>
    public int PrescriptionValidityDays { get; set; } = 30;
}
