using MediCare.Data.DTOs;
using MediCare.Data.Entities;
using MediCare.Data.Enums;

namespace MediCare.Data.Repositories;

public interface IAppointmentRepository : IRepository<Appointment>
{
    Task<List<Appointment>> GetDoctorAppointmentsAsync(int doctorId, DateTime date);
    Task<List<Appointment>> GetDoctorAppointmentsWithDetailsAsync(int doctorId, AppointmentStatus? status = null, DateTime? date = null);
    Task<bool> HasConflictAsync(int doctorId, DateTime date, TimeSpan startTime, TimeSpan endTime);
    Task<bool> HasConflictAsync(int doctorId, DateTime date, TimeSpan startTime, TimeSpan endTime, int? excludeAppointmentId);
    Task<List<Appointment>> GetAppointmentsByMonthAsync(int month, int year);
    Task<Appointment?> GetByIdWithDetailsAsync(int id);
    Task<List<Appointment>> GetPatientAppointmentsAsync(int patientId);
    Task<List<Appointment>> GetDoctorAppointmentsRangeAsync(int doctorId, DateTime startDate, DateTime endDate);
    Task<bool> HasPatientConflictAsync(int patientId, DateTime date, TimeSpan startTime, TimeSpan endTime);
    Task<bool> HasPatientConflictAsync(int patientId, DateTime date, TimeSpan startTime, TimeSpan endTime, int? excludeAppointmentId);
    Task<List<Appointment>> GetDoctorActiveAppointmentsInDateRangeAsync(int doctorId, DateTime startDate, DateTime endDate);
    Task<Dictionary<int, int>> GetAppointmentCountsByPatientAsync(IEnumerable<int> patientIds);
    Task<List<Appointment>> GetPendingReminderAppointmentsWithDetailsAsync(DateTime minDate, DateTime maxDate);
    Task<bool> IsReminderPendingAsync(int appointmentId);
    Task<DashboardMetricsSummaryData> GetDashboardMetricsSummaryAsync(DateTime clinicToday);
}
