namespace MediCare.Data.Entities;

public class PrescriptionItem : BaseAuditableEntity
{
    public int PrescriptionId { get; set; }
    public string MedicationName { get; set; } = string.Empty;
    public string Dosage { get; set; } = string.Empty;
    public string Frequency { get; set; } = string.Empty;
    public int DurationDays { get; set; }
    public string? Instructions { get; set; }

    public virtual Prescription Prescription { get; set; } = null!;
}
