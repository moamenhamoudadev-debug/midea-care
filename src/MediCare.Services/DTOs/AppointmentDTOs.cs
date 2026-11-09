using MediCare.Data.Enums;

namespace MediCare.Services.DTOs;

public class BookingRequestDto
{
    public int DoctorId { get; set; }
    public int PatientId { get; set; }
    public DateTime AppointmentDate { get; set; }
    public TimeSpan StartTime { get; set; }
    public AppointmentType Type { get; set; } = AppointmentType.Consultation;
    public string? Notes { get; set; }
}

public class AppointmentSummaryDto
{
    public int Id { get; set; }
    public int DoctorId { get; set; }
    public string DoctorName { get; set; } = string.Empty;
    public string SpecializationName { get; set; } = string.Empty;
    public int PatientId { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public string? PatientPhoneNumber { get; set; }
    public DateTime AppointmentDate { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public string FormattedTime => $"{DateTime.Today.Add(StartTime):hh:mm tt} - {DateTime.Today.Add(EndTime):hh:mm tt}";
    public string FormattedDate => AppointmentDate.ToString("ddd, MMM dd, yyyy");
    public AppointmentStatus Status { get; set; }
    public decimal ConsultationFee { get; set; }
    public PaymentStatus PaymentStatus { get; set; }
    public AppointmentType Type { get; set; }
    public string? Notes { get; set; }
    public bool CanCancel { get; set; }
    public string? DoctorPhoneNumber { get; set; }
    public string? Governorate { get; set; }
    public int QueueNumber { get; set; } = 1;
    public int CurrentServingQueueNumber { get; set; } = 1;
    public string? MedicalRecordAttachmentPath { get; set; }
    public int? MedicalRecordId { get; set; }
    public string? Diagnosis { get; set; }
    public DateTime? CheckedInAt { get; set; }
    public bool IsCheckedIn => CheckedInAt.HasValue;
    public string? TelehealthRoomSecret { get; set; }
    public string TeleconsultationRoomName => !string.IsNullOrEmpty(TelehealthRoomSecret) ? $"MediCare-{TelehealthRoomSecret}" : string.Empty;
    public string TeleconsultationMeetingUrl => !string.IsNullOrEmpty(TeleconsultationRoomName) ? $"https://meet.jit.si/{TeleconsultationRoomName}" : string.Empty;
    public string TelehealthRoomName => TeleconsultationRoomName;
    public string TelehealthUrl => TeleconsultationMeetingUrl;
}

public class CalendarEventDto
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Start { get; set; } = string.Empty;
    public string End { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
}

public class ConflictCheckRequestDto
{
    public int DoctorId { get; set; }
    public DateTime AppointmentDate { get; set; }
    public TimeSpan StartTime { get; set; }
}

public class ConflictCheckResponseDto
{
    public bool HasConflict { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class RescheduleRequestDto
{
    public int AppointmentId { get; set; }
    public DateTime NewAppointmentDate { get; set; }
    public TimeSpan NewStartTime { get; set; }
    public string? Reason { get; set; }
}
