using FluentValidation;
using MediCare.Data.Entities;
using MediCare.Data.Enums;
using MediCare.Data.UnitOfWork;
using MediCare.Services.Common;
using MediCare.Services.Contracts;
using MediCare.Services.DTOs;

namespace MediCare.Services.Implementations;

public class ScheduleService : IScheduleService
{
    private readonly IUnitOfWork _uow;
    private readonly IValidator<WorkingHoursDto> _workingHoursValidator;
    private readonly IValidator<DoctorLeaveDto> _doctorLeaveValidator;
    private readonly IClinicClock _clinicClock;

    public ScheduleService(
        IUnitOfWork uow,
        IValidator<WorkingHoursDto> workingHoursValidator,
        IValidator<DoctorLeaveDto> doctorLeaveValidator,
        IClinicClock? clinicClock = null)
    {
        _uow = uow;
        _workingHoursValidator = workingHoursValidator;
        _doctorLeaveValidator = doctorLeaveValidator;
        _clinicClock = clinicClock ?? new ClinicClock();
    }

    public async Task<Result<List<WorkingHoursDto>>> GetWorkingHoursAsync(int doctorId)
    {
        var list = (await _uow.WorkingHours.FindAsync(w => w.DoctorId == doctorId))
            .OrderBy(w => w.DayOfWeek)
            .ThenBy(w => w.StartTime)
            .Select(w => new WorkingHoursDto
            {
                Id = w.Id,
                DoctorId = w.DoctorId,
                DayOfWeek = w.DayOfWeek,
                StartTime = w.StartTime,
                EndTime = w.EndTime
            })
            .ToList();

        return Result<List<WorkingHoursDto>>.Success(list);
    }

    public async Task<Result<WorkingHoursDto>> GetWorkingHoursByIdAsync(int id, int doctorId)
    {
        var entity = await _uow.WorkingHours.GetByIdAsync(id);
        if (entity == null || entity.DoctorId != doctorId)
        {
            return Result<WorkingHoursDto>.Failure("Working hours entry not found or unauthorized.");
        }

        return Result<WorkingHoursDto>.Success(new WorkingHoursDto
        {
            Id = entity.Id,
            DoctorId = entity.DoctorId,
            DayOfWeek = entity.DayOfWeek,
            StartTime = entity.StartTime,
            EndTime = entity.EndTime
        });
    }

    public async Task<Result<int>> AddWorkingHoursAsync(WorkingHoursDto dto, int doctorId)
    {
        dto.DoctorId = doctorId;
        var validationResult = await _workingHoursValidator.ValidateAsync(dto);
        if (!validationResult.IsValid)
        {
            return Result<int>.Failure(validationResult.Errors.First().ErrorMessage);
        }

        // Check for overlapping intervals on the same day for this doctor
        var existing = await _uow.WorkingHours.FindAsync(w => w.DoctorId == doctorId && w.DayOfWeek == dto.DayOfWeek);
        bool hasOverlap = existing.Any(w => dto.StartTime < w.EndTime && dto.EndTime > w.StartTime);
        if (hasOverlap)
        {
            return Result<int>.Failure("Working hours overlap with an existing schedule for this day.");
        }

        var entity = new WorkingHours
        {
            DoctorId = doctorId,
            DayOfWeek = dto.DayOfWeek,
            StartTime = dto.StartTime,
            EndTime = dto.EndTime
        };

        await _uow.WorkingHours.AddAsync(entity);
        await _uow.CommitAsync();

        return Result<int>.Success(entity.Id);
    }

    public async Task<Result> UpdateWorkingHoursAsync(WorkingHoursDto dto, int doctorId)
    {
        dto.DoctorId = doctorId;
        var validationResult = await _workingHoursValidator.ValidateAsync(dto);
        if (!validationResult.IsValid)
        {
            return Result.Failure(validationResult.Errors.First().ErrorMessage);
        }

        var entity = await _uow.WorkingHours.GetByIdAsync(dto.Id);
        if (entity == null || entity.DoctorId != doctorId)
        {
            return Result.Failure("Working hours entry not found or unauthorized.");
        }

        // Check for overlapping intervals on the same day excluding current entry
        var existing = await _uow.WorkingHours.FindAsync(w => w.DoctorId == doctorId && w.DayOfWeek == dto.DayOfWeek && w.Id != dto.Id);
        bool hasOverlap = existing.Any(w => dto.StartTime < w.EndTime && dto.EndTime > w.StartTime);
        if (hasOverlap)
        {
            return Result.Failure("Working hours overlap with an existing schedule for this day.");
        }

        // BIZ-11: Guard against unassigning future active appointments
        if (entity.DayOfWeek == dto.DayOfWeek)
        {
            var proposedShifts = existing
                .Select(w => (w.StartTime, w.EndTime))
                .Append((dto.StartTime, dto.EndTime))
                .ToList();

            var conflicts = await GetConflictingFutureAppointmentsAsync(doctorId, entity.DayOfWeek, proposedShifts);
            if (conflicts.Count > 0)
            {
                var dates = string.Join(", ", conflicts.Select(c => c.AppointmentDate.ToString("yyyy-MM-dd")).Distinct());
                return Result.Failure($"Cannot update working hours because {conflicts.Count} active appointment(s) scheduled on ({dates}) fall outside the new shift time window. Please cancel or reschedule them first.");
            }
        }
        else
        {
            var oldDayOtherShifts = (await _uow.WorkingHours.FindAsync(w =>
                w.DoctorId == doctorId && w.DayOfWeek == entity.DayOfWeek && w.Id != entity.Id))
                .Select(w => (w.StartTime, w.EndTime))
                .ToList();

            var conflicts = await GetConflictingFutureAppointmentsAsync(doctorId, entity.DayOfWeek, oldDayOtherShifts);
            if (conflicts.Count > 0)
            {
                var dates = string.Join(", ", conflicts.Select(c => c.AppointmentDate.ToString("yyyy-MM-dd")).Distinct());
                return Result.Failure($"Cannot change day of week because {conflicts.Count} active appointment(s) scheduled on ({dates}) would be left without an active doctor shift. Please cancel or reschedule them first.");
            }
        }

        entity.DayOfWeek = dto.DayOfWeek;
        entity.StartTime = dto.StartTime;
        entity.EndTime = dto.EndTime;

        _uow.WorkingHours.Update(entity);
        await _uow.CommitAsync();

        return Result.Success();
    }

    public async Task<Result> DeleteWorkingHoursAsync(int id, int doctorId)
    {
        var entity = await _uow.WorkingHours.GetByIdAsync(id);
        if (entity == null || entity.DoctorId != doctorId)
        {
            return Result.Failure("Working hours entry not found or unauthorized.");
        }

        // BIZ-11: Check if removing this shift orphans any future active appointments on this weekday
        var remainingShifts = (await _uow.WorkingHours.FindAsync(w =>
            w.DoctorId == doctorId && w.DayOfWeek == entity.DayOfWeek && w.Id != id))
            .Select(w => (w.StartTime, w.EndTime))
            .ToList();

        var conflicts = await GetConflictingFutureAppointmentsAsync(doctorId, entity.DayOfWeek, remainingShifts);
        if (conflicts.Count > 0)
        {
            var dates = string.Join(", ", conflicts.Select(c => c.AppointmentDate.ToString("yyyy-MM-dd")).Distinct());
            return Result.Failure($"Cannot delete working hours because there are {conflicts.Count} active appointment(s) scheduled on this day ({dates}). Please cancel or reschedule them first.");
        }

        _uow.WorkingHours.Delete(entity);
        await _uow.CommitAsync();

        return Result.Success();
    }

    private async Task<List<Appointment>> GetConflictingFutureAppointmentsAsync(
        int doctorId,
        DayOfWeek dayOfWeek,
        List<(TimeSpan Start, TimeSpan End)> allowedIntervals)
    {
        if (_uow.Appointments == null)
        {
            return new List<Appointment>();
        }

        var today = _clinicClock.Today;
        var activeAppointments = await _uow.Appointments.FindAsync(a =>
            a.DoctorId == doctorId &&
            a.AppointmentDate >= today &&
            (a.Status == AppointmentStatus.Pending || a.Status == AppointmentStatus.Confirmed));

        if (activeAppointments == null)
        {
            return new List<Appointment>();
        }

        var dayAppointments = activeAppointments
            .Where(a => a.AppointmentDate.DayOfWeek == dayOfWeek)
            .ToList();

        var conflicts = new List<Appointment>();
        foreach (var appt in dayAppointments)
        {
            bool isCovered = allowedIntervals.Any(shift =>
                appt.StartTime >= shift.Start && appt.EndTime <= shift.End);

            if (!isCovered)
            {
                conflicts.Add(appt);
            }
        }

        return conflicts;
    }

    public async Task<Result<List<DoctorLeaveDto>>> GetDoctorLeavesAsync(int doctorId)
    {
        var list = (await _uow.DoctorLeaves.FindAsync(l => l.DoctorId == doctorId))
            .OrderByDescending(l => l.StartDate)
            .Select(l => new DoctorLeaveDto
            {
                Id = l.Id,
                DoctorId = l.DoctorId,
                StartDate = l.StartDate,
                EndDate = l.EndDate,
                Reason = l.Reason
            })
            .ToList();

        return Result<List<DoctorLeaveDto>>.Success(list);
    }

    public async Task<Result<List<LeaveConflictWarningDto>>> CheckLeaveConflictsAsync(int doctorId, DateTime startDate, DateTime endDate)
    {
        var activeAppointments = await _uow.Appointments.GetDoctorActiveAppointmentsInDateRangeAsync(doctorId, startDate, endDate);
        var warnings = activeAppointments.Select(a => new LeaveConflictWarningDto
        {
            AppointmentId = a.Id,
            AppointmentDate = a.AppointmentDate,
            StartTime = a.StartTime,
            PatientName = a.Patient.User.FullName,
            Status = a.Status.ToString()
        }).ToList();

        return Result<List<LeaveConflictWarningDto>>.Success(warnings);
    }

    public async Task<Result<DoctorLeaveCreateResultDto>> AddDoctorLeaveAsync(DoctorLeaveDto dto, int doctorId)
    {
        dto.DoctorId = doctorId;
        var validationResult = await _doctorLeaveValidator.ValidateAsync(dto);
        if (!validationResult.IsValid)
        {
            return Result<DoctorLeaveCreateResultDto>.Failure(validationResult.Errors.First().ErrorMessage);
        }

        var startDate = dto.StartDate.Date;
        var endDate = dto.EndDate.Date;

        var lockKeys = Enumerable.Range(0, (endDate - startDate).Days + 1)
            .Select(i => $"DoctorBooking_{doctorId}_{startDate.AddDays(i):yyyyMMdd}")
            .ToList();

        await using var tx = await _uow.BeginTransactionAsync();
        await using var appLock = await _uow.AcquireApplicationLocksAsync(lockKeys, TimeSpan.FromSeconds(10));

        // Check for overlapping doctor leaves
        var existingLeaves = await _uow.DoctorLeaves.FindAsync(l => l.DoctorId == doctorId);
        bool hasOverlappingLeave = existingLeaves.Any(l => startDate <= l.EndDate.Date && endDate >= l.StartDate.Date);
        if (hasOverlappingLeave)
        {
            if (tx != null) await tx.RollbackAsync();
            return Result<DoctorLeaveCreateResultDto>.Failure("Doctor already has a scheduled leave overlapping this date range.");
        }

        // Query active appointments in this range to reject conflicting leave scheduling
        var activeAppointments = await _uow.Appointments.GetDoctorActiveAppointmentsInDateRangeAsync(doctorId, startDate, endDate);
        if (activeAppointments != null && activeAppointments.Any())
        {
            if (tx != null) await tx.RollbackAsync();
            var dates = string.Join(", ", activeAppointments.Select(a => a.AppointmentDate.ToString("yyyy-MM-dd")).Distinct());
            return Result<DoctorLeaveCreateResultDto>.Failure(
                $"Cannot schedule leave: there are {activeAppointments.Count} active appointment(s) booked during this period ({dates}). Please reschedule or cancel affected appointments first.");
        }

        var leave = new DoctorLeave
        {
            DoctorId = doctorId,
            StartDate = startDate,
            EndDate = endDate,
            Reason = dto.Reason?.Trim()
        };

        await _uow.DoctorLeaves.AddAsync(leave);

        try
        {
            await _uow.CommitAsync();
            if (tx != null) await tx.CommitAsync();
        }
        catch (Exception)
        {
            if (tx != null) await tx.RollbackAsync();
            throw;
        }

        return Result<DoctorLeaveCreateResultDto>.Success(new DoctorLeaveCreateResultDto
        {
            LeaveId = leave.Id,
            AffectedAppointments = new List<LeaveConflictWarningDto>()
        });
    }

    public async Task<Result> DeleteDoctorLeaveAsync(int id, int doctorId)
    {
        var entity = await _uow.DoctorLeaves.GetByIdAsync(id);
        if (entity == null || entity.DoctorId != doctorId)
        {
            return Result.Failure("Leave entry not found or unauthorized.");
        }

        _uow.DoctorLeaves.Delete(entity);
        await _uow.CommitAsync();

        return Result.Success();
    }
}
