namespace MediCare.Data.Entities;

public class Doctor : BaseAuditableEntity
{
    public string UserId { get; set; } = string.Empty;
    public int SpecializationId { get; set; }
    public string LicenseNumber { get; set; } = string.Empty;
    public decimal ConsultationFee { get; set; }
    public int SlotDurationMinutes { get; set; } = 30;
    public bool IsApproved { get; set; } = false;
    public string Governorate { get; set; } = "Cairo";
    public string? ProfileImageUrl { get; set; }
    public string? Bio { get; set; }

    public virtual ApplicationUser User { get; set; } = null!;
    public virtual Specialization Specialization { get; set; } = null!;
    public virtual ICollection<WorkingHours> WorkingHours { get; set; } = new List<WorkingHours>();
    public virtual ICollection<DoctorLeave> Leaves { get; set; } = new List<DoctorLeave>();
    public virtual ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();
    public virtual ICollection<MedicalRecord> MedicalRecords { get; set; } = new List<MedicalRecord>();
    public virtual ICollection<Prescription> Prescriptions { get; set; } = new List<Prescription>();
}
