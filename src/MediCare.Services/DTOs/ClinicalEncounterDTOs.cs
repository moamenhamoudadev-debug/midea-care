namespace MediCare.Services.DTOs;

public class PrescriptionItemDto
{
    public int Id { get; set; }
    public string MedicationName { get; set; } = string.Empty;
    public string Dosage { get; set; } = string.Empty;
    public string Frequency { get; set; } = string.Empty;
    public int DurationDays { get; set; }
    public string? Instructions { get; set; }
}

public class CreateEncounterDto
{
    public int AppointmentId { get; set; }
    public string Diagnosis { get; set; } = string.Empty;
    public string? Symptoms { get; set; }
    public string? VisitNotes { get; set; }
    public string? Notes { get; set; } // Prescription dispensing notes
    public string? BloodPressure { get; set; }
    public int? HeartRate { get; set; }
    public decimal? Temperature { get; set; }
    public decimal? BloodGlucose { get; set; }
    public decimal? WeightKg { get; set; }
    public List<PrescriptionItemDto> PrescriptionItems { get; set; } = new();
}

public class PrescriptionDetailsDto
{
    public int Id { get; set; }
    public int MedicalRecordId { get; set; }
    public int AppointmentId { get; set; }
    public DateTime PrescriptionDate { get; set; }
    public string DoctorName { get; set; } = string.Empty;
    public string DoctorLicense { get; set; } = string.Empty;
    public string Specialization { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public int? PatientAge { get; set; }
    public string? PatientGender { get; set; }
    public string? Notes { get; set; }
    public bool IsDispensed { get; set; } = false;
    public DateTime? DispensedAt { get; set; }
    public string? DispensedNotes { get; set; }
    public string VerificationToken { get; set; } = string.Empty;
    public int PrescriptionValidityDays { get; set; } = 30;
    public bool IsExpired { get; set; }
    public DateTime ValidUntil => PrescriptionDate.AddDays(PrescriptionValidityDays);
    public List<PrescriptionItemDto> Items { get; set; } = new();
}

public class PrescriptionVerificationDto
{
    public bool IsValid { get; set; }
    public bool IsExpired { get; set; }
    public DateTime? ValidUntil { get; set; }
    public string DoctorName { get; set; } = string.Empty;
    public string Specialization { get; set; } = string.Empty;
    public DateTime PrescriptionDate { get; set; }
    public string MaskedPatientName { get; set; } = string.Empty;
    public bool IsDispensed { get; set; }
    public DateTime? DispensedAt { get; set; }
}

public class PharmacistPrescriptionReviewDto
{
    public int PrescriptionId { get; set; }
    public string VerificationToken { get; set; } = string.Empty;
    public string DoctorName { get; set; } = string.Empty;
    public string Specialization { get; set; } = string.Empty;
    public string MaskedPatientName { get; set; } = string.Empty;
    public DateTime PrescriptionDate { get; set; }
    public bool IsExpired { get; set; }
    public DateTime? ValidUntil { get; set; }
    public bool IsDispensed { get; set; }
    public DateTime? DispensedAt { get; set; }
    public string? DispensedByName { get; set; }
    public string? PharmacyNotes { get; set; }
    public List<PrescriptionItemDto> Items { get; set; } = new();
}

public class MedicalRecordDetailsDto
{
    public int Id { get; set; }
    public int AppointmentId { get; set; }
    public DateTime AppointmentDate { get; set; }
    public TimeSpan AppointmentStartTime { get; set; }
    public string FormattedDate => AppointmentDate.ToString("yyyy-MM-dd");
    public string FormattedTime => $"{DateTime.Today.Add(AppointmentStartTime):hh:mm tt}";

    public int DoctorId { get; set; }
    public string DoctorName { get; set; } = string.Empty;
    public string Specialization { get; set; } = string.Empty;
    public string DoctorLicense { get; set; } = string.Empty;

    public int PatientId { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public string? PatientGender { get; set; }
    public int? PatientAge { get; set; }
    public string? PatientBloodGroup { get; set; }
    public string? EmergencyContact { get; set; }

    public string Diagnosis { get; set; } = string.Empty;
    public string? Symptoms { get; set; }
    public string? VisitNotes { get; set; }
    public string? AttachmentPath { get; set; }
    public string? BloodPressure { get; set; }
    public int? HeartRate { get; set; }
    public decimal? Temperature { get; set; }
    public decimal? BloodGlucose { get; set; }
    public decimal? WeightKg { get; set; }
    public DateTime CreatedAt { get; set; }

    public PrescriptionDetailsDto? Prescription { get; set; }
}

public class MedicalRecordTimelineDto
{
    public int Id { get; set; }
    public int AppointmentId { get; set; }
    public DateTime Date { get; set; }
    public TimeSpan Time { get; set; }
    public string DoctorName { get; set; } = string.Empty;
    public string Specialization { get; set; } = string.Empty;
    public string Diagnosis { get; set; } = string.Empty;
    public string? Symptoms { get; set; }
    public string? VisitNotes { get; set; }
    public string? AttachmentPath { get; set; }
    public int? PrescriptionId { get; set; }
}
