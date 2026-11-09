namespace MediCare.Data.Entities;

public class Patient : BaseAuditableEntity
{
    public string UserId { get; set; } = string.Empty;
    public DateTime DateOfBirth { get; set; }
    public string Gender { get; set; } = string.Empty;
    public string? BloodGroup { get; set; }
    public string? EmergencyContact { get; set; }
    public string? Allergies { get; set; }
    public string? MedicalHistory { get; set; }

    public virtual ApplicationUser User { get; set; } = null!;
    public virtual ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();
    public virtual ICollection<MedicalRecord> MedicalRecords { get; set; } = new List<MedicalRecord>();
    public virtual ICollection<Prescription> Prescriptions { get; set; } = new List<Prescription>();
}
