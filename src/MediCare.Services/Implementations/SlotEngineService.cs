using MediCare.Data.Entities;
using MediCare.Data.Enums;
using MediCare.Data.UnitOfWork;
using MediCare.Services.Common;
using MediCare.Services.Contracts;
using MediCare.Services.DTOs;

namespace MediCare.Services.Implementations;

/// <summary>
/// Slot calculation engine implementation.
/// Computes 30-minute intervals from Doctor WorkingHours, removing DoctorLeaves,
/// active non-terminal appointments, and historical elapsed slots in Local Clinic Time.
/// </summary>
public class SlotEngineService : ISlotEngineService
{
    private readonly IUnitOfWork _uow;
    private readonly IClinicClock _clinicClock;

    public SlotEngineService(IUnitOfWork uow, IClinicClock clinicClock)
    {
        _uow = uow;
        _clinicClock = clinicClock;
    }

    public async Task<List<SlotDto>> GetAvailableSlotsAsync(int doctorId, DateTime date)
    {
        var doctor = await _uow.Doctors.GetDoctorWithScheduleAndLeavesAsync(doctorId);
        if (doctor == null || !doctor.IsApproved)
        {
            return new List<SlotDto>();
        }

        var appointments = await _uow.Appointments.GetDoctorAppointmentsAsync(doctorId, date);
        return ComputeSlots(
            date,
            doctor.SlotDurationMinutes,
            doctor.WorkingHours,
            doctor.Leaves,
            appointments,
            _clinicClock.Now);
    }

    public async Task<List<SlotDto>> GetAvailableSlotsRangeAsync(int doctorId, DateTime startDate, DateTime endDate)
    {
        var doctor = await _uow.Doctors.GetDoctorWithScheduleAndLeavesAsync(doctorId);
        if (doctor == null || !doctor.IsApproved)
        {
            return new List<SlotDto>();
        }

        var appointments = await _uow.Appointments.GetDoctorAppointmentsRangeAsync(doctorId, startDate, endDate);

        var allSlots = new List<SlotDto>();
        for (var current = startDate.Date; current <= endDate.Date; current = current.AddDays(1))
        {
            var daySlots = ComputeSlots(
                current,
                doctor.SlotDurationMinutes,
                doctor.WorkingHours,
                doctor.Leaves,
                appointments,
                _clinicClock.Now);

            allSlots.AddRange(daySlots);
        }

        return allSlots;
    }

    public List<SlotDto> ComputeSlots(
        DateTime date,
        int slotDurationMinutes,
        IEnumerable<WorkingHours> workingHours,
        IEnumerable<DoctorLeave> leaves,
        IEnumerable<Appointment> existingAppointments,
        DateTime currentClinicTime)
    {
        var targetDate = date.Date;

        // 1. Doctor Leave check: if on leave, no slots are available
        bool isOnLeave = leaves.Any(l => targetDate >= l.StartDate.Date && targetDate <= l.EndDate.Date);
        if (isOnLeave)
        {
            return new List<SlotDto>();
        }

        // 2. Working hours for the given day of the week
        var dayShifts = workingHours
            .Where(w => w.DayOfWeek == targetDate.DayOfWeek)
            .OrderBy(w => w.StartTime)
            .ToList();

        if (!dayShifts.Any())
        {
            return new List<SlotDto>();
        }

        // 3. Filter existing active (non-terminal) appointments
        var activeAppointments = existingAppointments
            .Where(a => a.AppointmentDate.Date == targetDate &&
                        a.Status != AppointmentStatus.Cancelled &&
                        a.Status != AppointmentStatus.Rejected)
            .ToList();

        int duration = slotDurationMinutes > 0 ? slotDurationMinutes : 30;
        var slotSpan = TimeSpan.FromMinutes(duration);
        var slots = new List<SlotDto>();

        // 4. Generate discrete slot intervals per shift
        foreach (var shift in dayShifts)
        {
            var currentStart = shift.StartTime;
            while (currentStart + slotSpan <= shift.EndTime)
            {
                var currentEnd = currentStart + slotSpan;
                var slotDateTime = targetDate.Add(currentStart);

                // Slot is unavailable if in the past (clinic wall-clock time)
                bool isPast = slotDateTime <= currentClinicTime;

                // Slot is unavailable if overlapping with an active appointment
                bool isBooked = activeAppointments.Any(a => a.StartTime < currentEnd && a.EndTime > currentStart);

                slots.Add(new SlotDto
                {
                    Date = targetDate,
                    StartTime = currentStart,
                    EndTime = currentEnd,
                    FormattedTime = $"{DateTime.Today.Add(currentStart):hh:mm tt} - {DateTime.Today.Add(currentEnd):hh:mm tt}",
                    IsAvailable = !isPast && !isBooked
                });

                currentStart = currentEnd;
            }
        }

        return slots;
    }
}
