using MediCare.Data.Enums;
using MediCare.Services.Common;
using MediCare.Services.DTOs;

namespace MediCare.Services.Contracts;

public interface IAppointmentService
{
    Task<Result<int>> BookAppointmentAsync(BookingRequestDto dto);
    Task<Result> ConfirmAppointmentAsync(int appointmentId, int doctorId);
    Task<Result> RejectAppointmentAsync(int appointmentId, int doctorId);
    Task<Result> CancelAppointmentAsync(int appointmentId, string userId, bool isDoctorOrAdmin = false, bool isAdmin = false);
    Task<Result> RescheduleAppointmentAsync(RescheduleRequestDto dto, string userId, bool isDoctorOrAdmin = false, bool isAdmin = false);
    Task<Result> MarkNoShowAsync(int appointmentId, int doctorId);
    Task<Result> CompleteAppointmentAsync(int appointmentId, int doctorId);

    Task<Result<ConflictCheckResponseDto>> CheckConflictAsync(int doctorId, DateTime appointmentDate, TimeSpan startTime);
    Task<Result<List<AppointmentSummaryDto>>> GetPatientAppointmentsAsync(string patientUserId);
    Task<Result<List<AppointmentSummaryDto>>> GetDoctorAppointmentsAsync(int doctorId, AppointmentStatus? status = null, DateTime? date = null);
    Task<Result<List<CalendarEventDto>>> GetDoctorEventsAsync(int doctorId, DateTime start, DateTime end);
    Task<Result<AppointmentSummaryDto>> GetAppointmentByIdAsync(int appointmentId);
    Task<Result<int>> GetPatientIdByUserIdAsync(string userId);
    Task<Result> CallNextQueuePatientAsync(int appointmentId, string doctorUserId, bool isAdmin = false);
    Task<Result> CheckInAppointmentAsync(int appointmentId, string doctorUserId, bool isAdmin = false);
    Task<Result<string>> EnsureTelehealthRoomSecretAsync(int appointmentId);
}
