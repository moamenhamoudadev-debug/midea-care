namespace MediCare.Data.Entities;

public class Prescription : BaseAuditableEntity
{
    public int MedicalRecordId { get; set; }
    public int DoctorId { get; set; }
    public int PatientId { get; set; }
    public DateTime PrescriptionDate { get; set; }
    public string? Notes { get; set; }

    // Security & Verification
    public string VerificationToken { get; set; } = string.Empty;

    // Dispensing details
    public bool IsDispensed { get; set; } = false;
    public DateTime? DispensedAt { get; set; }
    public string? DispensedByUserId { get; set; }
    public string? PharmacyNotes { get; set; }

    public virtual MedicalRecord MedicalRecord { get; set; } = null!;
    public virtual Doctor Doctor { get; set; } = null!;
    public virtual Patient Patient { get; set; } = null!;
    public virtual ApplicationUser? DispensedByUser { get; set; }
    public virtual ICollection<PrescriptionItem> Items { get; set; } = new List<PrescriptionItem>();
}
