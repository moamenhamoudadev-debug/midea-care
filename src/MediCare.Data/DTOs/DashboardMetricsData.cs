namespace MediCare.Data.DTOs;

public class DashboardMetricsSummaryData
{
    public int TotalAppointments { get; set; }
    public int ActiveDoctorsCount { get; set; }
    public int PendingDoctorsCount { get; set; }
    public int CompletedVisitsCount { get; set; }
    public decimal TotalRevenueCollected { get; set; }
    public decimal TotalPendingRevenue { get; set; }
    public decimal TotalRefundedRevenue { get; set; }
    public int RefundedAppointmentsCount { get; set; }

    public List<MonthlyTrendSummaryData> MonthlyTrends { get; set; } = new();
    public List<SpecializationSummaryData> Specializations { get; set; } = new();
    public List<TopDoctorSummaryData> TopDoctors { get; set; } = new();
    public DemographicsSummaryData Demographics { get; set; } = new();
}

public class MonthlyTrendSummaryData
{
    public string MonthLabel { get; set; } = string.Empty;
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

public class SpecializationSummaryData
{
    public string SpecializationName { get; set; } = string.Empty;
    public int AppointmentCount { get; set; }
}

public class TopDoctorSummaryData
{
    public int DoctorId { get; set; }
    public string DoctorName { get; set; } = string.Empty;
    public string SpecializationName { get; set; } = string.Empty;
    public int TotalAppointments { get; set; }
    public decimal TotalRevenue { get; set; }
}

public class DemographicsSummaryData
{
    public int TotalPatients { get; set; }
    public int MaleCount { get; set; }
    public int FemaleCount { get; set; }
    public int AgeUnder18Count { get; set; }
    public int Age18To35Count { get; set; }
    public int Age36To50Count { get; set; }
    public int AgeOver50Count { get; set; }
}
