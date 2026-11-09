using MediCare.Data.Enums;

namespace MediCare.Services.DTOs;

public class DoctorApprovalSummaryDto
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string SpecializationName { get; set; } = string.Empty;
    public string LicenseNumber { get; set; } = string.Empty;
    public decimal ConsultationFee { get; set; }
    public DateTime RegisteredAt { get; set; }
    public string FormattedRegisteredAt => RegisteredAt.ToString("yyyy-MM-dd HH:mm");
}

public class MonthlyStatusMetricDto
{
    public string MonthLabel { get; set; } = string.Empty; // e.g. "2026-05" or "May 2026"
    public int Year { get; set; }
    public int Month { get; set; }
    public int CompletedCount { get; set; }
    public int CancelledCount { get; set; }
    public int NoShowCount { get; set; }
    public int RefundedCount { get; set; }
    public decimal PaidRevenue { get; set; }
    public decimal UnpaidRevenue { get; set; }
    public decimal RefundedRevenue { get; set; }
}

public class SpecializationMetricDto
{
    public string SpecializationName { get; set; } = string.Empty;
    public int AppointmentCount { get; set; }
}

public class AdminDashboardMetricsDto
{
    public int TotalAppointments { get; set; }
    public int ActiveDoctorsCount { get; set; }
    public int PendingDoctorsCount { get; set; }
    public int CompletedVisitsCount { get; set; }
    public decimal TotalRevenueCollected { get; set; }
    public decimal TotalPendingRevenue { get; set; }
    public decimal TotalRefundedRevenue { get; set; }
    public int RefundedAppointmentsCount { get; set; }
    public decimal PlatformCommissionRate { get; set; } = 0.10m; // 10% platform commission fee
    public decimal PlatformCommissionEarned => Math.Round(TotalRevenueCollected * PlatformCommissionRate, 2);
    public decimal NetDoctorPayouts => Math.Round(TotalRevenueCollected * (1.0m - PlatformCommissionRate), 2);

    public List<MonthlyStatusMetricDto> MonthlyTrends { get; set; } = new();
    public List<SpecializationMetricDto> SpecializationBreakdown { get; set; } = new();
    public List<TopDoctorMetricDto> TopDoctors { get; set; } = new();
    public PatientDemographicsDto Demographics { get; set; } = new();
}

public class TopDoctorMetricDto
{
    public int DoctorId { get; set; }
    public string DoctorName { get; set; } = string.Empty;
    public string SpecializationName { get; set; } = string.Empty;
    public int TotalAppointments { get; set; }
    public decimal TotalRevenue { get; set; }
}

public class PatientDemographicsDto
{
    public int TotalPatients { get; set; }
    public int MaleCount { get; set; }
    public int FemaleCount { get; set; }
    public int AgeUnder18Count { get; set; }
    public int Age18To35Count { get; set; }
    public int Age36To50Count { get; set; }
    public int AgeOver50Count { get; set; }
}

public class AdminPatientSummaryDto
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public DateTime DateOfBirth { get; set; }
    public string Gender { get; set; } = string.Empty;
    public string? BloodGroup { get; set; }
    public string? EmergencyContact { get; set; }
    public string? Allergies { get; set; }
    public string? MedicalHistory { get; set; }
    public bool IsLockedOut { get; set; }
    public DateTime CreatedAt { get; set; }
    public int TotalAppointments { get; set; }
}

public class CreateSpecializationDto
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class UpdateSpecializationDto
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}
