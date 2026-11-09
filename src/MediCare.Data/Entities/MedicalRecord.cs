namespace MediCare.Data.Entities;

public class MedicalRecord : BaseAuditableEntity
{
    public int AppointmentId { get; set; }
    public int DoctorId { get; set; }
    public int PatientId { get; set; }
    public string Diagnosis { get; set; } = string.Empty;
    public string? Symptoms { get; set; }
    public string? VisitNotes { get; set; }
    public string? AttachmentPath { get; set; }
    public bool IsDraft { get; set; } = false;

    public virtual Appointment Appointment { get; set; } = null!;
    public virtual Doctor Doctor { get; set; } = null!;
    public virtual Patient Patient { get; set; } = null!;
    public virtual Prescription? Prescription { get; set; }
}
