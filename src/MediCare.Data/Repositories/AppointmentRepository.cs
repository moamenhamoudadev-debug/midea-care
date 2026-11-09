using System.Globalization;
using MediCare.Data.Context;
using MediCare.Data.DTOs;
using MediCare.Data.Entities;
using MediCare.Data.Enums;
using Microsoft.EntityFrameworkCore;

namespace MediCare.Data.Repositories;

public class AppointmentRepository : Repository<Appointment>, IAppointmentRepository
{
    public AppointmentRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<List<Appointment>> GetDoctorAppointmentsAsync(int doctorId, DateTime date)
    {
        var targetDate = date.Date;
        return await _context.Appointments
            .AsNoTracking()
            .Where(a => a.DoctorId == doctorId && a.AppointmentDate.Date == targetDate)
            .OrderBy(a => a.StartTime)
            .ToListAsync();
    }

    public async Task<List<Appointment>> GetDoctorAppointmentsWithDetailsAsync(int doctorId, AppointmentStatus? status = null, DateTime? date = null)
    {
        var queryDate = date?.Date;
        return await _context.Appointments
            .AsNoTracking()
            .Include(a => a.Doctor).ThenInclude(d => d.User)
            .Include(a => a.Doctor).ThenInclude(d => d.Specialization)
            .Include(a => a.Patient).ThenInclude(p => p.User)
            .Where(a => a.DoctorId == doctorId
                     && (!status.HasValue || a.Status == status.Value)
                     && (!queryDate.HasValue || a.AppointmentDate.Date == queryDate.Value))
            .OrderByDescending(a => a.AppointmentDate)
            .ThenByDescending(a => a.StartTime)
            .ToListAsync();
    }

    public Task<bool> HasConflictAsync(int doctorId, DateTime date, TimeSpan startTime, TimeSpan endTime)
    {
        return HasConflictAsync(doctorId, date, startTime, endTime, null);
    }

    public async Task<bool> HasConflictAsync(int doctorId, DateTime date, TimeSpan startTime, TimeSpan endTime, int? excludeAppointmentId)
    {
        var targetDate = date.Date;
        return await _context.Appointments
            .AnyAsync(a => a.DoctorId == doctorId
                        && (!excludeAppointmentId.HasValue || a.Id != excludeAppointmentId.Value)
                        && a.AppointmentDate.Date == targetDate
                        && a.StartTime < endTime
                        && a.EndTime > startTime
                        && a.Status != AppointmentStatus.Cancelled
                        && a.Status != AppointmentStatus.Rejected);
    }

    public async Task<List<Appointment>> GetAppointmentsByMonthAsync(int month, int year)
    {
        return await _context.Appointments
            .AsNoTracking()
            .Where(a => a.AppointmentDate.Month == month && a.AppointmentDate.Year == year)
            .ToListAsync();
    }

    public async Task<Appointment?> GetByIdWithDetailsAsync(int id)
    {
        return await _context.Appointments
            .Include(a => a.Doctor).ThenInclude(d => d.User)
            .Include(a => a.Doctor).ThenInclude(d => d.Specialization)
            .Include(a => a.Patient).ThenInclude(p => p.User)
            .Include(a => a.MedicalRecord)
            .FirstOrDefaultAsync(a => a.Id == id);
    }

    public async Task<List<Appointment>> GetPatientAppointmentsAsync(int patientId)
    {
        return await _context.Appointments
            .AsNoTracking()
            .Include(a => a.Doctor).ThenInclude(d => d.User)
            .Include(a => a.Doctor).ThenInclude(d => d.Specialization)
            .Where(a => a.PatientId == patientId)
            .OrderByDescending(a => a.AppointmentDate)
            .ThenByDescending(a => a.StartTime)
            .ToListAsync();
    }

    public async Task<List<Appointment>> GetDoctorAppointmentsRangeAsync(int doctorId, DateTime startDate, DateTime endDate)
    {
        var start = startDate.Date;
        var end = endDate.Date;
        return await _context.Appointments
            .AsNoTracking()
            .Include(a => a.Patient).ThenInclude(p => p.User)
            .Where(a => a.DoctorId == doctorId && a.AppointmentDate.Date >= start && a.AppointmentDate.Date <= end)
            .OrderBy(a => a.AppointmentDate)
            .ThenBy(a => a.StartTime)
            .ToListAsync();
    }

    public Task<bool> HasPatientConflictAsync(int patientId, DateTime date, TimeSpan startTime, TimeSpan endTime)
    {
        return HasPatientConflictAsync(patientId, date, startTime, endTime, null);
    }

    public async Task<bool> HasPatientConflictAsync(int patientId, DateTime date, TimeSpan startTime, TimeSpan endTime, int? excludeAppointmentId)
    {
        var targetDate = date.Date;
        return await _context.Appointments
            .AnyAsync(a => a.PatientId == patientId
                        && (!excludeAppointmentId.HasValue || a.Id != excludeAppointmentId.Value)
                        && a.AppointmentDate.Date == targetDate
                        && a.StartTime < endTime
                        && a.EndTime > startTime
                        && a.Status != AppointmentStatus.Cancelled
                        && a.Status != AppointmentStatus.Rejected);
    }

    public async Task<List<Appointment>> GetDoctorActiveAppointmentsInDateRangeAsync(int doctorId, DateTime startDate, DateTime endDate)
    {
        var start = startDate.Date;
        var end = endDate.Date;
        return await _context.Appointments
            .AsNoTracking()
            .Include(a => a.Patient).ThenInclude(p => p.User)
            .Where(a => a.DoctorId == doctorId
                     && a.AppointmentDate.Date >= start
                     && a.AppointmentDate.Date <= end
                     && (a.Status == AppointmentStatus.Pending || a.Status == AppointmentStatus.Confirmed))
            .OrderBy(a => a.AppointmentDate)
            .ThenBy(a => a.StartTime)
            .ToListAsync();
    }

    public async Task<Dictionary<int, int>> GetAppointmentCountsByPatientAsync(IEnumerable<int> patientIds)
    {
        var idList = patientIds.Distinct().ToList();
        if (idList.Count == 0) return new Dictionary<int, int>();

        return await _context.Appointments
            .AsNoTracking()
            .Where(a => idList.Contains(a.PatientId))
            .GroupBy(a => a.PatientId)
            .Select(g => new { PatientId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.PatientId, x => x.Count);
    }

    public async Task<List<Appointment>> GetPendingReminderAppointmentsWithDetailsAsync(DateTime minDate, DateTime maxDate)
    {
        var min = minDate.Date;
        var max = maxDate.Date;
        return await _context.Appointments
            .Include(a => a.Doctor).ThenInclude(d => d.User)
            .Include(a => a.Doctor).ThenInclude(d => d.Specialization)
            .Include(a => a.Patient).ThenInclude(p => p.User)
            .Where(a => !a.ReminderSent
                     && a.AppointmentDate.Date >= min
                     && a.AppointmentDate.Date <= max
                     && (a.Status == AppointmentStatus.Confirmed || a.Status == AppointmentStatus.Pending))
            .ToListAsync();
    }

    public Task<bool> IsReminderPendingAsync(int appointmentId)
    {
        return _context.Appointments
            .AsNoTracking()
            .AnyAsync(a => a.Id == appointmentId
                        && !a.ReminderSent
                        && (a.Status == AppointmentStatus.Confirmed || a.Status == AppointmentStatus.Pending));
    }

    public async Task<DashboardMetricsSummaryData> GetDashboardMetricsSummaryAsync(DateTime clinicToday)
    {
        var apptsQuery = _context.Appointments.AsNoTracking();

        // 1. Core KPIs
        var totalAppointments = await apptsQuery.CountAsync();
        var completedVisitsCount = await apptsQuery.CountAsync(a => a.Status == AppointmentStatus.Completed);
        var refundedAppointmentsCount = await apptsQuery.CountAsync(a => a.PaymentStatus == PaymentStatus.Refunded);

        var totalRevenueCollected = await apptsQuery
            .Where(a => a.PaymentStatus == PaymentStatus.Paid)
            .SumAsync(a => (decimal?)(a.PaidAmount ?? (a.DiscountAmount.HasValue && a.DiscountAmount.Value > 0m
                ? (a.ConsultationFee > a.DiscountAmount.Value ? a.ConsultationFee - a.DiscountAmount.Value : 0m)
                : a.ConsultationFee))) ?? 0m;

        var totalRefundedRevenue = await apptsQuery
            .Where(a => a.PaymentStatus == PaymentStatus.Refunded)
            .SumAsync(a => (decimal?)(a.PaidAmount ?? (a.DiscountAmount.HasValue && a.DiscountAmount.Value > 0m
                ? (a.ConsultationFee > a.DiscountAmount.Value ? a.ConsultationFee - a.DiscountAmount.Value : 0m)
                : a.ConsultationFee))) ?? 0m;

        var totalPendingRevenue = await apptsQuery
            .Where(a => a.PaymentStatus == PaymentStatus.Unpaid
                     && a.Status != AppointmentStatus.Cancelled
                     && a.Status != AppointmentStatus.Rejected)
            .SumAsync(a => (decimal?)a.ConsultationFee) ?? 0m;

        // 2. Doctor counts
        var activeDoctorsCount = await _context.Doctors.AsNoTracking().CountAsync(d => d.IsApproved);
        var pendingDoctorsCount = await _context.Doctors.AsNoTracking().CountAsync(d => !d.IsApproved);

        // 3. Monthly trends (past 12 months)
        var past12Months = Enumerable.Range(0, 12)
            .Select(i => clinicToday.AddMonths(-i))
            .OrderBy(d => d)
            .Select(d => new { Year = d.Year, Month = d.Month, Label = d.ToString("MMM yyyy", CultureInfo.InvariantCulture) })
            .ToList();

        var startMonth = past12Months.First();
        var startDate = new DateTime(startMonth.Year, startMonth.Month, 1);

        var monthlyGrouped = await apptsQuery
            .Where(a => a.AppointmentDate >= startDate)
            .GroupBy(a => new { a.AppointmentDate.Year, a.AppointmentDate.Month })
            .Select(g => new
            {
                g.Key.Year,
                g.Key.Month,
                CompletedCount = g.Count(a => a.Status == AppointmentStatus.Completed),
                CancelledCount = g.Count(a => a.Status == AppointmentStatus.Cancelled),
                NoShowCount = g.Count(a => a.Status == AppointmentStatus.NoShow),
                RefundedCount = g.Count(a => a.PaymentStatus == PaymentStatus.Refunded),
                UnpaidRevenue = g.Sum(a => a.PaymentStatus == PaymentStatus.Unpaid ? a.ConsultationFee : 0m),
                PaidRevenue = g.Sum(a => a.PaymentStatus == PaymentStatus.Paid
                    ? (a.PaidAmount ?? (a.DiscountAmount.HasValue && a.DiscountAmount.Value > 0m
                        ? (a.ConsultationFee > a.DiscountAmount.Value ? a.ConsultationFee - a.DiscountAmount.Value : 0m)
                        : a.ConsultationFee))
                    : 0m),
                RefundedRevenue = g.Sum(a => a.PaymentStatus == PaymentStatus.Refunded
                    ? (a.PaidAmount ?? (a.DiscountAmount.HasValue && a.DiscountAmount.Value > 0m
                        ? (a.ConsultationFee > a.DiscountAmount.Value ? a.ConsultationFee - a.DiscountAmount.Value : 0m)
                        : a.ConsultationFee))
                    : 0m)
            })
            .ToListAsync();

        var monthlyDict = monthlyGrouped.ToDictionary(g => (g.Year, g.Month));
        var trends = new List<MonthlyTrendSummaryData>(12);
        foreach (var m in past12Months)
        {
            if (monthlyDict.TryGetValue((m.Year, m.Month), out var data))
            {
                trends.Add(new MonthlyTrendSummaryData
                {
                    Year = m.Year,
                    Month = m.Month,
                    MonthLabel = m.Label,
                    CompletedCount = data.CompletedCount,
                    CancelledCount = data.CancelledCount,
                    NoShowCount = data.NoShowCount,
                    RefundedCount = data.RefundedCount,
                    PaidRevenue = data.PaidRevenue,
                    UnpaidRevenue = data.UnpaidRevenue,
                    RefundedRevenue = data.RefundedRevenue
                });
            }
            else
            {
                trends.Add(new MonthlyTrendSummaryData
                {
                    Year = m.Year,
                    Month = m.Month,
                    MonthLabel = m.Label,
                    CompletedCount = 0,
                    CancelledCount = 0,
                    NoShowCount = 0,
                    RefundedCount = 0,
                    PaidRevenue = 0m,
                    UnpaidRevenue = 0m,
                    RefundedRevenue = 0m
                });
            }
        }

        // 4. Specialization Breakdown (pure SQL count per specialization)
        var specBreakdown = await _context.Specializations.AsNoTracking()
            .Select(s => new SpecializationSummaryData
            {
                SpecializationName = s.Name,
                AppointmentCount = _context.Appointments.Count(a => a.Doctor.SpecializationId == s.Id)
            })
            .OrderByDescending(s => s.AppointmentCount)
            .ToListAsync();

        // 5. Top 5 Doctors (SQL TOP 5)
        var topDoctors = await _context.Doctors.AsNoTracking()
            .Where(d => d.Appointments.Any())
            .Select(d => new TopDoctorSummaryData
            {
                DoctorId = d.Id,
                DoctorName = d.User != null && d.User.FullName != null ? d.User.FullName : ("Dr. #" + d.Id),
                SpecializationName = d.Specialization != null ? d.Specialization.Name : "General",
                TotalAppointments = d.Appointments.Count(),
                TotalRevenue = d.Appointments
                    .Where(a => a.PaymentStatus == PaymentStatus.Paid)
                    .Sum(a => (decimal?)(a.PaidAmount ?? (a.DiscountAmount.HasValue && a.DiscountAmount.Value > 0m
                        ? (a.ConsultationFee > a.DiscountAmount.Value ? a.ConsultationFee - a.DiscountAmount.Value : 0m)
                        : a.ConsultationFee))) ?? 0m
            })
            .OrderByDescending(d => d.TotalAppointments)
            .ThenByDescending(d => d.TotalRevenue)
            .Take(5)
            .ToListAsync();

        // 6. Demographics
        var date18 = clinicToday.AddYears(-18);
        var date36 = clinicToday.AddYears(-36);
        var date51 = clinicToday.AddYears(-51);

        var patientsQuery = _context.Patients.AsNoTracking();
        var totalPatients = await patientsQuery.CountAsync();
        var maleCount = await patientsQuery.CountAsync(p => p.Gender == "Male" || p.Gender == "M");
        var femaleCount = await patientsQuery.CountAsync(p => p.Gender == "Female" || p.Gender == "F");

        var under18 = await patientsQuery.CountAsync(p => p.DateOfBirth > date18);
        var age18To35 = await patientsQuery.CountAsync(p => p.DateOfBirth <= date18 && p.DateOfBirth > date36);
        var age36To50 = await patientsQuery.CountAsync(p => p.DateOfBirth <= date36 && p.DateOfBirth > date51);
        var over50 = await patientsQuery.CountAsync(p => p.DateOfBirth <= date51);

        return new DashboardMetricsSummaryData
        {
            TotalAppointments = totalAppointments,
            ActiveDoctorsCount = activeDoctorsCount,
            PendingDoctorsCount = pendingDoctorsCount,
            CompletedVisitsCount = completedVisitsCount,
            TotalRevenueCollected = totalRevenueCollected,
            TotalPendingRevenue = totalPendingRevenue,
            TotalRefundedRevenue = totalRefundedRevenue,
            RefundedAppointmentsCount = refundedAppointmentsCount,
            MonthlyTrends = trends,
            Specializations = specBreakdown,
            TopDoctors = topDoctors,
            Demographics = new DemographicsSummaryData
            {
                TotalPatients = totalPatients,
                MaleCount = maleCount,
                FemaleCount = femaleCount,
                AgeUnder18Count = under18,
                Age18To35Count = age18To35,
                Age36To50Count = age36To50,
                AgeOver50Count = over50
            }
        };
    }
}
