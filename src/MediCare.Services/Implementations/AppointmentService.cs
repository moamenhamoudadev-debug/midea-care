using MediCare.Data.Entities;
using MediCare.Data.Enums;
using MediCare.Data.UnitOfWork;
using MediCare.Services.Common;
using MediCare.Services.Contracts;
using MediCare.Services.DTOs;
using MediCare.Services.Factories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MediCare.Services.Implementations;

/// <summary>
/// Core appointment booking engine and finite state machine.
/// Implements transactional multi-layer double-booking prevention and enforces
/// lifecycle state invariants in Local Clinic Time.
/// </summary>
public class AppointmentService : IAppointmentService
{
    private readonly IUnitOfWork _uow;
    private readonly AppointmentFactory _appointmentFactory;
    private readonly INotificationService _notificationService;
    private readonly IClinicClock _clinicClock;
    private readonly ILogger<AppointmentService> _logger;
    private readonly IEmailService? _emailService;

    public AppointmentService(
        IUnitOfWork uow,
        AppointmentFactory appointmentFactory,
        INotificationService notificationService,
        IClinicClock clinicClock,
        ILogger<AppointmentService> logger,
        IEmailService? emailService = null)
    {
        _uow = uow;
        _appointmentFactory = appointmentFactory;
        _notificationService = notificationService;
        _clinicClock = clinicClock;
        _logger = logger;
        _emailService = emailService;
    }

    public async Task<Result<int>> BookAppointmentAsync(BookingRequestDto dto)
    {
        if (dto.Notes != null && dto.Notes.Length > 500)
        {
            return Result<int>.Failure("Symptoms / notes cannot exceed 500 characters.");
        }

        // 1. Validate Doctor eligibility
        var doctor = await _uow.Doctors.GetDoctorWithScheduleAndLeavesAsync(dto.DoctorId);
        if (doctor == null || !doctor.IsApproved)
        {
            return Result<int>.Failure("Doctor not found or doctor account is pending administrative approval.");
        }

        // 2. Validate Patient existence
        var patients = await _uow.Patients.FindAsync(p => p.Id == dto.PatientId);
        var patient = patients.FirstOrDefault();
        if (patient == null)
        {
            return Result<int>.Failure("Patient profile not found.");
        }

        // 3. Time validation (Clinic Wall-Clock Time)
        var targetDateTime = dto.AppointmentDate.Date.Add(dto.StartTime);
        if (targetDateTime <= _clinicClock.Now)
        {
            return Result<int>.Failure("Appointments cannot be scheduled in the past.");
        }

        if (targetDateTime < _clinicClock.Now.AddMinutes(30))
        {
            return Result<int>.Failure("Appointments must be booked at least 30 minutes in advance.");
        }

        if (dto.AppointmentDate.Date > _clinicClock.Now.Date.AddDays(30))
        {
            return Result<int>.Failure("Appointments can only be scheduled up to 30 days in advance.");
        }

        // 4. Validate Doctor Leaves
        bool onLeave = doctor.Leaves.Any(l => dto.AppointmentDate.Date >= l.StartDate.Date && dto.AppointmentDate.Date <= l.EndDate.Date);
        if (onLeave)
        {
            return Result<int>.Failure("Doctor is on leave on the selected date.");
        }

        // 5. Validate Doctor Working Hours
        int duration = 30;
        if (doctor.SlotDurationMinutes > 0)
        {
            duration = doctor.SlotDurationMinutes;
        }
        else
        {
            _logger.LogWarning("Doctor {DoctorId} has invalid or non-positive SlotDurationMinutes ({Duration}). Falling back to default 30-minute slot.",
                doctor.Id, doctor.SlotDurationMinutes);
        }
        var slotSpan = TimeSpan.FromMinutes(duration);
        var expectedEnd = dto.StartTime.Add(slotSpan);
        var shift = doctor.WorkingHours.FirstOrDefault(w =>
            w.DayOfWeek == dto.AppointmentDate.DayOfWeek &&
            dto.StartTime >= w.StartTime &&
            expectedEnd <= w.EndTime);

        if (shift == null)
        {
            return Result<int>.Failure("The selected time is outside the doctor's scheduled clinic hours.");
        }

        if ((dto.StartTime - shift.StartTime).Ticks % slotSpan.Ticks != 0)
        {
            return Result<int>.Failure("The selected appointment time does not align with the doctor's appointment slot schedule.");
        }

        // 6. Concurrency lock and transaction for slot and patient serialization
        var lockKeys = new[]
        {
            $"DoctorBooking_{dto.DoctorId}_{dto.AppointmentDate:yyyyMMdd}",
            $"PatientBooking_{dto.PatientId}_{dto.AppointmentDate:yyyyMMdd}"
        };
        await using var tx = await _uow.BeginTransactionAsync();
        await using var appLock = await _uow.AcquireApplicationLocksAsync(lockKeys, TimeSpan.FromSeconds(10));

        // 7. Validate Patient Double-Booking under lock
        bool patientHasConflict = await _uow.Appointments.HasPatientConflictAsync(dto.PatientId, dto.AppointmentDate, dto.StartTime, expectedEnd);
        if (patientHasConflict)
        {
            if (tx != null) await tx.RollbackAsync();
            return Result<int>.Failure("You already have an active appointment scheduled at this exact date and time.");
        }

        // 8. Service-level check for Doctor slot under lock (preventing interval overlaps)
        bool slotConflict = await _uow.Appointments.HasConflictAsync(dto.DoctorId, dto.AppointmentDate, dto.StartTime, expectedEnd);
        if (slotConflict)
        {
            if (tx != null) await tx.RollbackAsync();
            return Result<int>.Failure("The selected time slot is already booked. Please choose an alternative time.");
        }

        // 9. Create entity via AppointmentFactory
        var appointment = _appointmentFactory.Create(dto, doctor.ConsultationFee, duration);
        await _uow.Appointments.AddAsync(appointment);

        // 10. Persist and intercept DB-level Unique Index violation
        try
        {
            await _uow.CommitAsync();
            if (tx != null) await tx.CommitAsync();
        }
        catch (DbUpdateException ex)
        {
            if (tx != null) await tx.RollbackAsync();
            if (IsTruncationException(ex))
            {
                _logger.LogWarning(ex, "Payload truncation prevented booking for doctor {DoctorId} at {Date} {Time}",
                    dto.DoctorId, dto.AppointmentDate, dto.StartTime);
                return Result<int>.Failure("The entered symptoms or notes exceed the maximum allowed length (500 characters).");
            }

            _logger.LogWarning(ex, "Race condition detected: Filtered Unique Index prevented double-booking for doctor {DoctorId} at {Date} {Time}",
                dto.DoctorId, dto.AppointmentDate, dto.StartTime);
            return Result<int>.Failure("This slot was just booked by another patient. Please choose an alternative time.");
        }
        catch (Exception)
        {
            if (tx != null) await tx.RollbackAsync();
            throw;
        }

        // 10. Post-commit notifications
        var patientUser = patient ?? (await _uow.Patients.FindAsync(p => p.Id == dto.PatientId)).FirstOrDefault();
        var patientName = patientUser?.User?.FullName ?? "A patient";

        await _notificationService.SendNotificationAsync(
            doctor.UserId,
            "New Appointment Request",
            $"New reservation by {patientName} on {appointment.AppointmentDate:yyyy-MM-dd} at {DateTime.Today.Add(appointment.StartTime):hh:mm tt}.");

        await _notificationService.NotifySlotAvailabilityChangedAsync(doctor.Id, appointment.AppointmentDate);

        if (_emailService != null && !string.IsNullOrEmpty(patientUser?.User?.Email))
        {
            var emailSubject = "MediCare — Appointment Booking Request Received";
            var emailBody = $@"
                <div style='font-family: Arial, sans-serif; line-height: 1.6;'>
                    <h2>Appointment Request Received</h2>
                    <p>Dear {patientName},</p>
                    <p>Your appointment request with <strong>Dr. {doctor.User.FullName}</strong> has been received.</p>
                    <p><strong>Date:</strong> {appointment.AppointmentDate:yyyy-MM-dd}<br/>
                       <strong>Time:</strong> {DateTime.Today.Add(appointment.StartTime):hh:mm tt}<br/>
                       <strong>Consultation Fee:</strong> {appointment.ConsultationFee:F2} EGP</p>
                    <p>Status: <strong>Pending Doctor Confirmation</strong></p>
                    <p>Best regards,<br/>MediCare Outpatient Clinic</p>
                </div>";
            await _emailService.SendEmailAsync(patientUser.User.Email, emailSubject, emailBody);
        }

        return Result<int>.Success(appointment.Id);
    }

    public async Task<Result> ConfirmAppointmentAsync(int appointmentId, int doctorId)
    {
        var appointment = await _uow.Appointments.GetByIdWithDetailsAsync(appointmentId);
        if (appointment == null)
        {
            return Result.Failure("Appointment not found.");
        }

        if (appointment.DoctorId != doctorId)
        {
            _logger.LogWarning("Forbidden: Doctor {DoctorId} attempted to confirm appointment {ApptId} belonging to Doctor {OwnerId}",
                doctorId, appointmentId, appointment.DoctorId);
            return Result.Failure("Forbidden: You do not own this appointment.");
        }

        // Guard: only Pending -> Confirmed is valid
        if (appointment.Status != AppointmentStatus.Pending)
        {
            return Result.Failure($"Cannot confirm appointment. Current status is '{appointment.Status}'.");
        }

        appointment.Status = AppointmentStatus.Confirmed;
        _uow.Appointments.Update(appointment);

        try
        {
            await _uow.CommitAsync();
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Concurrency conflict: Appointment {ApptId} was modified concurrently during confirmation.", appointment.Id);
            return Result.Failure("This appointment was modified concurrently by another request. Please refresh the page and try again.");
        }

        // Notify patient
        await _notificationService.SendNotificationAsync(
            appointment.Patient.UserId,
            "Appointment Confirmed",
            $"Dr. {appointment.Doctor.User.FullName} confirmed your appointment on {appointment.AppointmentDate:yyyy-MM-dd} at {DateTime.Today.Add(appointment.StartTime):hh:mm tt}.");

        await _notificationService.NotifyAppointmentStatusChangedAsync(appointment.Id, "Confirmed", appointment.Patient.UserId);

        if (_emailService != null && !string.IsNullOrEmpty(appointment.Patient?.User?.Email))
        {
            var emailSubject = "MediCare — Appointment Confirmed";
            var emailBody = $@"
                <div style='font-family: Arial, sans-serif; line-height: 1.6;'>
                    <h2>Appointment Confirmed!</h2>
                    <p>Dear {appointment.Patient.User.FullName},</p>
                    <p><strong>Dr. {appointment.Doctor.User.FullName}</strong> has confirmed your appointment.</p>
                    <p><strong>Date:</strong> {appointment.AppointmentDate:yyyy-MM-dd}<br/>
                       <strong>Time:</strong> {DateTime.Today.Add(appointment.StartTime):hh:mm tt}</p>
                    <p>We look forward to seeing you.</p>
                    <p>Best regards,<br/>MediCare Outpatient Clinic</p>
                </div>";
            await _emailService.SendEmailAsync(appointment.Patient.User.Email, emailSubject, emailBody);
        }

        return Result.Success();
    }

    public async Task<Result> RejectAppointmentAsync(int appointmentId, int doctorId)
    {
        var appointment = await _uow.Appointments.GetByIdWithDetailsAsync(appointmentId);
        if (appointment == null)
        {
            return Result.Failure("Appointment not found.");
        }

        if (appointment.DoctorId != doctorId)
        {
            _logger.LogWarning("Forbidden: Doctor {DoctorId} attempted to reject appointment {ApptId} belonging to Doctor {OwnerId}",
                doctorId, appointmentId, appointment.DoctorId);
            return Result.Failure("Forbidden: You do not own this appointment.");
        }

        // Guard: only Pending -> Rejected is valid
        if (appointment.Status != AppointmentStatus.Pending)
        {
            return Result.Failure($"Cannot reject appointment. Current status is '{appointment.Status}'.");
        }

        appointment.Status = AppointmentStatus.Rejected;
        bool wasPaid = appointment.PaymentStatus == PaymentStatus.Paid;
        if (wasPaid)
        {
            appointment.PaymentStatus = PaymentStatus.Refunded;
        }

        _uow.Appointments.Update(appointment);

        try
        {
            await _uow.CommitAsync();
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Concurrency conflict: Appointment {ApptId} was modified concurrently during rejection.", appointment.Id);
            return Result.Failure("This appointment was modified concurrently by another request. Please refresh the page and try again.");
        }

        var refundNotice = wasPaid ? " (A simulated refund has been credited to your account — demo mode, no real money was charged)." : string.Empty;

        // Notify patient
        await _notificationService.SendNotificationAsync(
            appointment.Patient.UserId,
            "Appointment Declined",
            $"Dr. {appointment.Doctor.User.FullName} was unable to accept your appointment on {appointment.AppointmentDate:yyyy-MM-dd}. The slot has been released.{refundNotice}");

        await _notificationService.NotifyAppointmentStatusChangedAsync(appointment.Id, "Rejected", appointment.Patient.UserId);
        await _notificationService.NotifySlotAvailabilityChangedAsync(appointment.DoctorId, appointment.AppointmentDate);

        if (_emailService != null && !string.IsNullOrEmpty(appointment.Patient?.User?.Email))
        {
            var emailSubject = "MediCare — Appointment Declined";
            var emailBody = $@"
                <div style='font-family: Arial, sans-serif; line-height: 1.6;'>
                    <h2>Appointment Declined</h2>
                    <p>Dear {appointment.Patient.User.FullName},</p>
                    <p>Dr. {appointment.Doctor.User.FullName} was unable to accept your appointment scheduled for {appointment.AppointmentDate:yyyy-MM-dd}. The slot has been released.</p>
                    {(wasPaid ? "<p><em>Note: A simulated refund of your consultation fee was recorded in the demo system (no actual financial charge occurred).</em></p>" : "")}
                    <p>Best regards,<br/>MediCare Outpatient Clinic</p>
                </div>";
            await _emailService.SendEmailAsync(appointment.Patient.User.Email, emailSubject, emailBody);
        }

        return Result.Success();
    }

    public async Task<Result> CancelAppointmentAsync(int appointmentId, string userId, bool isDoctorOrAdmin = false, bool isAdmin = false)
    {
        var appointment = await _uow.Appointments.GetByIdWithDetailsAsync(appointmentId);
        if (appointment == null)
        {
            return Result.Failure("Appointment not found.");
        }

        // Guard: terminal states cannot transition
        if (appointment.Status == AppointmentStatus.Completed ||
            appointment.Status == AppointmentStatus.Cancelled ||
            appointment.Status == AppointmentStatus.Rejected ||
            appointment.Status == AppointmentStatus.NoShow)
        {
            return Result.Failure($"Cannot cancel an appointment that is already {appointment.Status}.");
        }

        if (!isDoctorOrAdmin)
        {
            // Patient cancellation: ownership validation
            if (appointment.Patient.UserId != userId)
            {
                _logger.LogWarning("Forbidden: User {UserId} attempted to cancel appointment {ApptId} owned by patient {OwnerId}",
                    userId, appointmentId, appointment.Patient.UserId);
                return Result.Failure("Forbidden: You can only cancel your own appointments.");
            }

            // 2-hour cancellation rule
            var appointmentStart = appointment.AppointmentDate.Date.Add(appointment.StartTime);
            if (appointmentStart - _clinicClock.Now <= TimeSpan.FromHours(2))
            {
                return Result.Failure("Appointments cannot be cancelled less than 2 hours before the scheduled start time. Please contact the clinic directly.");
            }
        }
        else if (!isAdmin && appointment.Doctor.UserId != userId)
        {
            _logger.LogWarning("Forbidden: Doctor user {UserId} attempted to cancel appointment {ApptId} owned by Doctor user {OwnerId}",
                userId, appointmentId, appointment.Doctor.UserId);
            return Result.Failure("Forbidden: You do not own this appointment.");
        }

        appointment.Status = AppointmentStatus.Cancelled;
        bool wasPaid = appointment.PaymentStatus == PaymentStatus.Paid;
        if (wasPaid)
        {
            appointment.PaymentStatus = PaymentStatus.Refunded;
        }

        _uow.Appointments.Update(appointment);

        try
        {
            await _uow.CommitAsync();
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Concurrency conflict: Appointment {ApptId} was modified concurrently during cancellation.", appointment.Id);
            return Result.Failure("This appointment was modified concurrently by another request. Please refresh the page and try again.");
        }

        var refundNotice = wasPaid ? " (A simulated refund has been credited to your account — demo mode, no real money was charged)." : string.Empty;

        // Notify counterpart
        if (!isDoctorOrAdmin)
        {
            await _notificationService.SendNotificationAsync(
                appointment.Doctor.UserId,
                "Appointment Cancelled",
                $"{appointment.Patient.User.FullName} cancelled the appointment scheduled for {appointment.AppointmentDate:yyyy-MM-dd} at {DateTime.Today.Add(appointment.StartTime):hh:mm tt}. The slot is now open.");
        }
        else
        {
            await _notificationService.SendNotificationAsync(
                appointment.Patient.UserId,
                "Appointment Cancelled",
                $"Your appointment with Dr. {appointment.Doctor.User.FullName} on {appointment.AppointmentDate:yyyy-MM-dd} at {DateTime.Today.Add(appointment.StartTime):hh:mm tt} was cancelled by the clinic.{refundNotice}");
        }

        await _notificationService.NotifySlotAvailabilityChangedAsync(appointment.DoctorId, appointment.AppointmentDate);
        await _notificationService.NotifyAppointmentStatusChangedAsync(appointment.Id, "Cancelled", appointment.Patient.UserId);

        if (_emailService != null && !string.IsNullOrEmpty(appointment.Patient?.User?.Email))
        {
            var emailSubject = "MediCare — Appointment Cancelled";
            var emailBody = $@"
                <div style='font-family: Arial, sans-serif; line-height: 1.6;'>
                    <h2>Appointment Cancelled</h2>
                    <p>Dear {appointment.Patient.User.FullName},</p>
                    <p>Your appointment with Dr. {appointment.Doctor.User.FullName} scheduled for {appointment.AppointmentDate:yyyy-MM-dd} at {DateTime.Today.Add(appointment.StartTime):hh:mm tt} has been cancelled.</p>
                    {(wasPaid ? "<p><em>Note: A simulated refund of your consultation fee was recorded in the demo system (no actual financial charge occurred).</em></p>" : "")}
                    <p>Best regards,<br/>MediCare Outpatient Clinic</p>
                </div>";
            await _emailService.SendEmailAsync(appointment.Patient.User.Email, emailSubject, emailBody);
        }

        return Result.Success();
    }

    public async Task<Result> RescheduleAppointmentAsync(RescheduleRequestDto dto, string userId, bool isDoctorOrAdmin = false, bool isAdmin = false)
    {
        if (dto.Reason != null && dto.Reason.Length > 250)
        {
            return Result.Failure("Reason for rescheduling cannot exceed 250 characters.");
        }

        var appointment = await _uow.Appointments.GetByIdWithDetailsAsync(dto.AppointmentId);
        if (appointment == null)
        {
            return Result.Failure("Appointment not found.");
        }

        // Terminal status check
        if (appointment.Status == AppointmentStatus.Completed ||
            appointment.Status == AppointmentStatus.Cancelled ||
            appointment.Status == AppointmentStatus.Rejected ||
            appointment.Status == AppointmentStatus.NoShow)
        {
            return Result.Failure($"Cannot reschedule an appointment that is already {appointment.Status}.");
        }

        if (!isDoctorOrAdmin)
        {
            // Patient ownership validation
            if (appointment.Patient.UserId != userId)
            {
                _logger.LogWarning("Forbidden: User {UserId} attempted to reschedule appointment {ApptId} owned by patient {OwnerId}",
                    userId, dto.AppointmentId, appointment.Patient.UserId);
                return Result.Failure("Forbidden: You can only reschedule your own appointments.");
            }

            // 2-hour minimum lead time rule for rescheduling
            var currentAppointmentStart = appointment.AppointmentDate.Date.Add(appointment.StartTime);
            if (currentAppointmentStart - _clinicClock.Now <= TimeSpan.FromHours(2))
            {
                return Result.Failure("Appointments cannot be rescheduled less than 2 hours before the current scheduled start time. Please contact the clinic directly.");
            }
        }
        else if (!isAdmin && appointment.Doctor.UserId != userId)
        {
            _logger.LogWarning("Forbidden: Doctor user {UserId} attempted to reschedule appointment {ApptId} owned by Doctor user {OwnerId}",
                userId, dto.AppointmentId, appointment.Doctor.UserId);
            return Result.Failure("Forbidden: You do not own this appointment.");
        }

        // Validate new appointment date and time
        var newTargetDateTime = dto.NewAppointmentDate.Date.Add(dto.NewStartTime);
        if (newTargetDateTime <= _clinicClock.Now)
        {
            return Result.Failure("New appointment time cannot be in the past.");
        }

        if (newTargetDateTime < _clinicClock.Now.AddMinutes(30))
        {
            return Result.Failure("New appointment must be scheduled at least 30 minutes in advance.");
        }

        if (dto.NewAppointmentDate.Date > _clinicClock.Now.Date.AddDays(30))
        {
            return Result.Failure("Appointments can only be scheduled up to 30 days in advance.");
        }

        var doctor = await _uow.Doctors.GetDoctorWithScheduleAndLeavesAsync(appointment.DoctorId)
                     ?? await _uow.Doctors.GetDoctorWithScheduleAsync(appointment.DoctorId);
        if (doctor == null)
        {
            return Result.Failure("Doctor profile not found.");
        }

        // Validate Doctor leaves
        bool onLeave = doctor.Leaves.Any(l => dto.NewAppointmentDate.Date >= l.StartDate.Date && dto.NewAppointmentDate.Date <= l.EndDate.Date);
        if (onLeave)
        {
            return Result.Failure("Doctor is on leave on the selected new date.");
        }

        // Validate Doctor working hours
        int duration = doctor.SlotDurationMinutes > 0 ? doctor.SlotDurationMinutes : 30;
        var slotSpan = TimeSpan.FromMinutes(duration);
        var expectedEnd = dto.NewStartTime.Add(slotSpan);
        var shift = doctor.WorkingHours.FirstOrDefault(w =>
            w.DayOfWeek == dto.NewAppointmentDate.DayOfWeek &&
            dto.NewStartTime >= w.StartTime &&
            expectedEnd <= w.EndTime);

        if (shift == null)
        {
            return Result.Failure("The selected new time is outside the doctor's scheduled clinic hours.");
        }

        // Validate slot grid alignment
        if ((dto.NewStartTime - shift.StartTime).Ticks % slotSpan.Ticks != 0)
        {
            return Result.Failure("The selected new appointment time does not align with the doctor's appointment slot schedule.");
        }

        // Concurrency lock and transaction for rescheduling slot serialization
        var lockKeys = new List<string>
        {
            $"DoctorBooking_{appointment.DoctorId}_{dto.NewAppointmentDate:yyyyMMdd}",
            $"PatientBooking_{appointment.PatientId}_{dto.NewAppointmentDate:yyyyMMdd}"
        };

        if (dto.NewAppointmentDate.Date != appointment.AppointmentDate.Date)
        {
            lockKeys.Add($"DoctorBooking_{appointment.DoctorId}_{appointment.AppointmentDate:yyyyMMdd}");
            lockKeys.Add($"PatientBooking_{appointment.PatientId}_{appointment.AppointmentDate:yyyyMMdd}");
        }

        await using var tx = await _uow.BeginTransactionAsync();
        await using var appLock = await _uow.AcquireApplicationLocksAsync(lockKeys, TimeSpan.FromSeconds(10));

        var appointmentRepo = _uow.Appointments;

        // Check Doctor slot conflict with interval overlap (excluding this appointment)
        bool doctorConflict = appointmentRepo != null && await appointmentRepo.HasConflictAsync(
            appointment.DoctorId,
            dto.NewAppointmentDate,
            dto.NewStartTime,
            expectedEnd,
            excludeAppointmentId: appointment.Id);

        if (!doctorConflict && appointmentRepo != null)
        {
            var doctorAppointments = await appointmentRepo.FindAsync(a =>
                a.DoctorId == appointment.DoctorId &&
                a.AppointmentDate.Date == dto.NewAppointmentDate.Date &&
                a.StartTime < expectedEnd &&
                a.EndTime > dto.NewStartTime &&
                a.Id != appointment.Id &&
                a.Status != AppointmentStatus.Cancelled &&
                a.Status != AppointmentStatus.Rejected);

            if (doctorAppointments != null && doctorAppointments.Any())
            {
                doctorConflict = true;
            }
        }

        if (doctorConflict)
        {
            if (tx != null) await tx.RollbackAsync();
            return Result.Failure("The selected new time slot is already booked. Please choose an alternative time.");
        }

        // Check Patient conflict with interval overlap (excluding this appointment)
        bool patientConflict = appointmentRepo != null && await appointmentRepo.HasPatientConflictAsync(
            appointment.PatientId,
            dto.NewAppointmentDate,
            dto.NewStartTime,
            expectedEnd,
            excludeAppointmentId: appointment.Id);

        if (!patientConflict && appointmentRepo != null)
        {
            var patientAppointments = await appointmentRepo.FindAsync(a =>
                a.PatientId == appointment.PatientId &&
                a.AppointmentDate.Date == dto.NewAppointmentDate.Date &&
                a.StartTime < expectedEnd &&
                a.EndTime > dto.NewStartTime &&
                a.Id != appointment.Id &&
                a.Status != AppointmentStatus.Cancelled &&
                a.Status != AppointmentStatus.Rejected);

            if (patientAppointments != null && patientAppointments.Any())
            {
                patientConflict = true;
            }
        }

        if (patientConflict)
        {
            if (tx != null) await tx.RollbackAsync();
            return Result.Failure("You already have another active appointment scheduled at this exact new date and time.");
        }

        var oldDate = appointment.AppointmentDate;
        var oldStartTime = appointment.StartTime;
        var oldEndTime = appointment.EndTime;
        var oldReminderSent = appointment.ReminderSent;
        var oldNotes = appointment.Notes;

        appointment.AppointmentDate = dto.NewAppointmentDate.Date;
        appointment.StartTime = dto.NewStartTime;
        appointment.EndTime = expectedEnd;
        appointment.ReminderSent = false;
        if (!string.IsNullOrWhiteSpace(dto.Reason))
        {
            var rescheduleNote = $"Rescheduled: {dto.Reason.Trim()}";
            if (string.IsNullOrWhiteSpace(appointment.Notes))
            {
                appointment.Notes = rescheduleNote.Length > 500 ? rescheduleNote[..500] : rescheduleNote;
            }
            else
            {
                var combined = $"{appointment.Notes} | {rescheduleNote}";
                appointment.Notes = combined.Length > 500 ? combined[..500] : combined;
            }
        }

        appointmentRepo?.Update(appointment);

        try
        {
            await _uow.CommitAsync();
            if (tx != null) await tx.CommitAsync();
        }
        catch (DbUpdateConcurrencyException ex)
        {
            appointment.AppointmentDate = oldDate;
            appointment.StartTime = oldStartTime;
            appointment.EndTime = oldEndTime;
            appointment.ReminderSent = oldReminderSent;
            appointment.Notes = oldNotes;

            if (tx != null) await tx.RollbackAsync();
            _logger.LogWarning(ex, "Concurrency conflict: Appointment {ApptId} was modified concurrently during rescheduling.", appointment.Id);
            return Result.Failure("This appointment was modified concurrently by another request. Please refresh the page and try again.");
        }
        catch (DbUpdateException ex)
        {
            appointment.AppointmentDate = oldDate;
            appointment.StartTime = oldStartTime;
            appointment.EndTime = oldEndTime;
            appointment.ReminderSent = oldReminderSent;
            appointment.Notes = oldNotes;

            if (tx != null) await tx.RollbackAsync();
            if (IsTruncationException(ex))
            {
                _logger.LogWarning(ex, "Payload truncation prevented rescheduling for doctor {DoctorId} at {Date} {Time}",
                    appointment.DoctorId, dto.NewAppointmentDate, dto.NewStartTime);
                return Result.Failure("The entered rescheduling reason exceeds the maximum allowed length.");
            }

            _logger.LogWarning(ex, "Concurrency race condition prevented rescheduling for doctor {DoctorId} at {Date} {Time}",
                appointment.DoctorId, dto.NewAppointmentDate, dto.NewStartTime);
            return Result.Failure("This slot was just booked by another patient. Please choose an alternative time.");
        }
        catch (Exception)
        {
            appointment.AppointmentDate = oldDate;
            appointment.StartTime = oldStartTime;
            appointment.EndTime = oldEndTime;
            appointment.ReminderSent = oldReminderSent;
            appointment.Notes = oldNotes;

            if (tx != null) await tx.RollbackAsync();
            throw;
        }

        if (isDoctorOrAdmin)
        {
            await _notificationService.SendNotificationAsync(
                appointment.Patient.UserId,
                "Appointment Rescheduled",
                $"Your appointment with Dr. {appointment.Doctor.User.FullName} was rescheduled from {oldDate:yyyy-MM-dd} to {appointment.AppointmentDate:yyyy-MM-dd} at {DateTime.Today.Add(appointment.StartTime):hh:mm tt}.");
        }
        else
        {
            await _notificationService.SendNotificationAsync(
                appointment.Doctor.UserId,
                "Appointment Rescheduled",
                $"Appointment with {appointment.Patient.User.FullName} was rescheduled from {oldDate:yyyy-MM-dd} to {appointment.AppointmentDate:yyyy-MM-dd} at {DateTime.Today.Add(appointment.StartTime):hh:mm tt}.");
        }

        await _notificationService.NotifySlotAvailabilityChangedAsync(appointment.DoctorId, oldDate);
        await _notificationService.NotifySlotAvailabilityChangedAsync(appointment.DoctorId, appointment.AppointmentDate);
        await _notificationService.NotifyAppointmentStatusChangedAsync(appointment.Id, "Rescheduled", appointment.Patient.UserId);

        if (_emailService != null && !string.IsNullOrEmpty(appointment.Patient.User?.Email))
        {
            var emailSubject = "MediCare — Appointment Rescheduled Confirmation";
            var emailBody = $@"
                <div style='font-family: Arial, sans-serif; line-height: 1.6;'>
                    <h2>Appointment Rescheduled</h2>
                    <p>Dear {appointment.Patient.User.FullName},</p>
                    <p>Your appointment with Dr. {appointment.Doctor.User.FullName} has been rescheduled to <strong>{appointment.AppointmentDate:dddd, MMMM dd, yyyy} at {DateTime.Today.Add(appointment.StartTime):hh:mm tt}</strong>.</p>
                    <p>Best regards,<br/>MediCare Outpatient Clinic</p>
                </div>";
            await _emailService.SendEmailAsync(appointment.Patient.User.Email, emailSubject, emailBody);
        }

        return Result.Success();
    }

    public async Task<Result> MarkNoShowAsync(int appointmentId, int doctorId)
    {
        var appointment = await _uow.Appointments.GetByIdWithDetailsAsync(appointmentId);
        if (appointment == null)
        {
            return Result.Failure("Appointment not found.");
        }

        if (appointment.DoctorId != doctorId)
        {
            _logger.LogWarning("Forbidden: Doctor {DoctorId} attempted to mark NoShow on appointment {ApptId}", doctorId, appointmentId);
            return Result.Failure("Forbidden: You do not own this appointment.");
        }

        if (appointment.Status != AppointmentStatus.Confirmed)
        {
            return Result.Failure($"Only Confirmed appointments can be marked as No-Show. Current status is '{appointment.Status}'.");
        }

        var appointmentStart = appointment.AppointmentDate.Date.Add(appointment.StartTime);
        if (_clinicClock.Now < appointmentStart)
        {
            return Result.Failure("Cannot mark an appointment as No-Show before its scheduled start time.");
        }

        appointment.Status = AppointmentStatus.NoShow;
        _uow.Appointments.Update(appointment);

        try
        {
            await _uow.CommitAsync();
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Concurrency conflict: Appointment {ApptId} was modified concurrently during NoShow update.", appointment.Id);
            return Result.Failure("This appointment was modified concurrently by another request. Please refresh the page and try again.");
        }

        return Result.Success();
    }

    public async Task<Result> CompleteAppointmentAsync(int appointmentId, int doctorId)
    {
        var appointment = await _uow.Appointments.GetByIdWithDetailsAsync(appointmentId);
        if (appointment == null)
        {
            return Result.Failure("Appointment not found.");
        }

        if (appointment.DoctorId != doctorId)
        {
            return Result.Failure("Forbidden: You do not own this appointment.");
        }

        if (appointment.Status != AppointmentStatus.Confirmed)
        {
            return Result.Failure($"Only Confirmed appointments can be marked as Completed. Current status is '{appointment.Status}'.");
        }

        var appointmentStart = appointment.AppointmentDate.Date.Add(appointment.StartTime);
        if (_clinicClock.Now < appointmentStart)
        {
            return Result.Failure("Cannot complete an appointment before its scheduled start time.");
        }

        appointment.Status = AppointmentStatus.Completed;
        _uow.Appointments.Update(appointment);

        try
        {
            await _uow.CommitAsync();
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Concurrency conflict: Appointment {ApptId} was modified concurrently during completion.", appointment.Id);
            return Result.Failure("This appointment was modified concurrently by another request. Please refresh the page and try again.");
        }

        return Result.Success();
    }

    public async Task<Result<ConflictCheckResponseDto>> CheckConflictAsync(int doctorId, DateTime appointmentDate, TimeSpan startTime)
    {
        var doctor = await _uow.Doctors.GetByIdAsync(doctorId);
        int duration = 30;
        if (doctor != null && doctor.SlotDurationMinutes > 0)
        {
            duration = doctor.SlotDurationMinutes;
        }
        else
        {
            _logger.LogWarning("Doctor {DoctorId} has invalid or null SlotDurationMinutes ({Duration}). Falling back to default 30-minute slot.",
                doctorId, doctor?.SlotDurationMinutes);
        }
        var endTime = startTime.Add(TimeSpan.FromMinutes(duration));
        bool hasConflict = await _uow.Appointments.HasConflictAsync(doctorId, appointmentDate, startTime, endTime);
        return Result<ConflictCheckResponseDto>.Success(new ConflictCheckResponseDto
        {
            HasConflict = hasConflict,
            Message = hasConflict ? "The selected time slot is already booked." : "Slot is available."
        });
    }

    public async Task<Result<List<AppointmentSummaryDto>>> GetPatientAppointmentsAsync(string patientUserId)
    {
        var patient = (await _uow.Patients.FindAsync(p => p.UserId == patientUserId)).FirstOrDefault();
        if (patient == null)
        {
            return Result<List<AppointmentSummaryDto>>.Failure("Patient record not found.");
        }

        var appointments = await _uow.Appointments.GetPatientAppointmentsAsync(patient.Id);
        var summaries = appointments.Select(a =>
        {
            var apptStart = a.AppointmentDate.Date.Add(a.StartTime);
            bool canCancel = (a.Status == AppointmentStatus.Pending || a.Status == AppointmentStatus.Confirmed) &&
                             (apptStart - _clinicClock.Now).TotalHours > 2;

            return new AppointmentSummaryDto
            {
                Id = a.Id,
                DoctorId = a.DoctorId,
                DoctorName = a.Doctor?.User?.FullName ?? "Physician",
                SpecializationName = a.Doctor?.Specialization?.Name ?? "General",
                PatientId = a.PatientId,
                PatientName = a.Patient?.User?.FullName ?? "Patient",
                AppointmentDate = a.AppointmentDate,
                StartTime = a.StartTime,
                EndTime = a.EndTime,
                Status = a.Status,
                ConsultationFee = a.ConsultationFee,
                PaymentStatus = a.PaymentStatus,
                Type = a.Type,
                Notes = a.Notes,
                CanCancel = canCancel,
                CheckedInAt = a.CheckedInAt
            };
        }).ToList();

        return Result<List<AppointmentSummaryDto>>.Success(summaries);
    }

    public async Task<Result<List<AppointmentSummaryDto>>> GetDoctorAppointmentsAsync(int doctorId, AppointmentStatus? status = null, DateTime? date = null)
    {
        var list = await _uow.Appointments.GetDoctorAppointmentsWithDetailsAsync(doctorId, status, date);
        if (list == null)
        {
            var queryDate = date?.Date;
            list = (await _uow.Appointments.FindAsync(a =>
                a.DoctorId == doctorId &&
                (!status.HasValue || a.Status == status.Value) &&
                (!queryDate.HasValue || a.AppointmentDate.Date == queryDate.Value)))
                .OrderByDescending(a => a.AppointmentDate)
                .ThenByDescending(a => a.StartTime)
                .ToList();
        }

        var result = list.Select(full => new AppointmentSummaryDto
        {
            Id = full.Id,
            DoctorId = full.DoctorId,
            DoctorName = full.Doctor?.User?.FullName ?? string.Empty,
            SpecializationName = full.Doctor?.Specialization?.Name ?? string.Empty,
            PatientId = full.PatientId,
            PatientName = full.Patient?.User?.FullName ?? "Patient",
            PatientPhoneNumber = full.Patient?.User?.PhoneNumber,
            AppointmentDate = full.AppointmentDate,
            StartTime = full.StartTime,
            EndTime = full.EndTime,
            Status = full.Status,
            ConsultationFee = full.ConsultationFee,
            PaymentStatus = full.PaymentStatus,
            Type = full.Type,
            Notes = full.Notes,
            CanCancel = (full.Status == AppointmentStatus.Pending || full.Status == AppointmentStatus.Confirmed),
            CheckedInAt = full.CheckedInAt
        }).ToList();

        return Result<List<AppointmentSummaryDto>>.Success(result);
    }

    public async Task<Result<List<CalendarEventDto>>> GetDoctorEventsAsync(int doctorId, DateTime start, DateTime end)
    {
        var appointments = await _uow.Appointments.GetDoctorAppointmentsRangeAsync(doctorId, start, end);
        var events = appointments.Select(a =>
        {
            var color = a.Status switch
            {
                AppointmentStatus.Confirmed => "#198754",
                AppointmentStatus.Pending => "#ffc107",
                AppointmentStatus.Completed => "#0d6efd",
                AppointmentStatus.Cancelled => "#6c757d",
                AppointmentStatus.Rejected => "#dc3545",
                AppointmentStatus.NoShow => "#d63384",
                _ => "#0dcaf0"
            };

            var startIso = a.AppointmentDate.Date.Add(a.StartTime).ToString("yyyy-MM-ddTHH:mm:ss");
            var endIso = a.AppointmentDate.Date.Add(a.EndTime).ToString("yyyy-MM-ddTHH:mm:ss");

            return new CalendarEventDto
            {
                Id = a.Id.ToString(),
                Title = $"{a.Type}: {a.Patient?.User?.FullName ?? "Patient"}",
                Start = startIso,
                End = endIso,
                Status = a.Status.ToString(),
                Color = color,
                Url = $"/Appointments/Details/{a.Id}"
            };
        }).ToList();

        return Result<List<CalendarEventDto>>.Success(events);
    }

    public async Task<Result<int>> GetPatientIdByUserIdAsync(string userId)
    {
        var patients = await _uow.Patients.FindAsync(p => p.UserId == userId);
        var patient = patients.FirstOrDefault();
        if (patient == null)
        {
            return Result<int>.Failure("Patient profile not found.");
        }

        return Result<int>.Success(patient.Id);
    }

    public async Task<Result<AppointmentSummaryDto>> GetAppointmentByIdAsync(int appointmentId)
    {
        var full = await _uow.Appointments.GetByIdWithDetailsAsync(appointmentId);
        if (full == null)
        {
            return Result<AppointmentSummaryDto>.Failure("Appointment not found.");
        }

        var apptStart = full.AppointmentDate.Date.Add(full.StartTime);
        bool canCancel = (full.Status == AppointmentStatus.Pending || full.Status == AppointmentStatus.Confirmed) &&
                         (apptStart - _clinicClock.Now).TotalHours > 2;

        // Dynamic Queue Calculation for doctor on this date
        var dayAppointments = (await _uow.Appointments.FindAsync(a =>
            a.DoctorId == full.DoctorId &&
            a.AppointmentDate.Date == full.AppointmentDate.Date &&
            a.Status != AppointmentStatus.Cancelled &&
            a.Status != AppointmentStatus.Rejected))
            .OrderBy(a => a.StartTime)
            .ToList();

        int queueIndex = dayAppointments.FindIndex(a => a.Id == full.Id);
        int queueNumber = queueIndex >= 0 ? queueIndex + 1 : 1;

        int currentServingIndex = dayAppointments.FindIndex(a => a.Status != AppointmentStatus.Completed);
        int currentServingNumber = currentServingIndex >= 0 ? currentServingIndex + 1 : (dayAppointments.Count > 0 ? dayAppointments.Count : 1);

        var dto = new AppointmentSummaryDto
        {
            Id = full.Id,
            DoctorId = full.DoctorId,
            DoctorName = full.Doctor?.User?.FullName ?? "Physician",
            SpecializationName = full.Doctor?.Specialization?.Name ?? "General",
            PatientId = full.PatientId,
            PatientName = full.Patient?.User?.FullName ?? "Patient",
            PatientPhoneNumber = full.Patient?.User?.PhoneNumber,
            AppointmentDate = full.AppointmentDate,
            StartTime = full.StartTime,
            EndTime = full.EndTime,
            Status = full.Status,
            ConsultationFee = full.ConsultationFee,
            PaymentStatus = full.PaymentStatus,
            Type = full.Type,
            Notes = full.Notes,
            CanCancel = canCancel,
            DoctorPhoneNumber = full.Doctor?.User?.PhoneNumber,
            Governorate = full.Doctor?.Governorate ?? "Cairo",
            QueueNumber = queueNumber,
            CurrentServingQueueNumber = currentServingNumber,
            CheckedInAt = full.CheckedInAt,
            MedicalRecordAttachmentPath = full.MedicalRecord?.AttachmentPath,
            MedicalRecordId = full.MedicalRecord?.Id,
            Diagnosis = full.MedicalRecord?.Diagnosis
        };

        return Result<AppointmentSummaryDto>.Success(dto);
    }

    public async Task<Result> CallNextQueuePatientAsync(int appointmentId, string doctorUserId, bool isAdmin = false)
    {
        var appointment = await _uow.Appointments.GetByIdWithDetailsAsync(appointmentId);
        if (appointment == null)
        {
            return Result.Failure("Appointment not found.");
        }

        if (!isAdmin && appointment.Doctor?.UserId != doctorUserId)
        {
            return Result.Failure("Forbidden: You do not own this appointment.");
        }

        if (appointment.Status != AppointmentStatus.Confirmed)
        {
            return Result.Failure("Only confirmed appointments can be called for queue.");
        }

        if (appointment.AppointmentDate.Date != _clinicClock.Today)
        {
            return Result.Failure("Cannot call patients for appointments not scheduled for today.");
        }

        if (appointment.Type == AppointmentType.Telemedicine)
        {
            return Result.Failure("Telemedicine appointments cannot be called into physical queue.");
        }

        if (!appointment.CheckedInAt.HasValue)
        {
            return Result.Failure("Patient has not checked in at reception yet.");
        }

        if (!string.IsNullOrEmpty(appointment.Patient?.UserId))
        {
            await _notificationService.SendNotificationAsync(
                appointment.Patient.UserId,
                "حان دورك للكشف الآن! (Your Turn Now)",
                $"د. {appointment.Doctor?.User?.FullName ?? "الطبيب المعالج"} بانتظارك في غرفة الكشف لموعدك رقم #{appointment.Id}. يرجى التوجه للعيادة فوراً.");

            await _notificationService.NotifyAppointmentStatusChangedAsync(appointment.Id, "Calling", appointment.Patient.UserId);
        }

        return Result.Success();
    }

    public async Task<Result> CheckInAppointmentAsync(int appointmentId, string doctorUserId, bool isAdmin = false)
    {
        var appointment = await _uow.Appointments.GetByIdWithDetailsAsync(appointmentId);
        if (appointment == null)
        {
            return Result.Failure("Appointment not found.");
        }

        if (!isAdmin && appointment.Doctor?.UserId != doctorUserId)
        {
            return Result.Failure("Forbidden: You do not own this appointment.");
        }

        if (appointment.Status != AppointmentStatus.Confirmed)
        {
            return Result.Failure("Only confirmed appointments can be checked in.");
        }

        if (appointment.AppointmentDate.Date != _clinicClock.Today)
        {
            return Result.Failure("Check-in is only allowed on the scheduled appointment date.");
        }

        if (appointment.Type == AppointmentType.Telemedicine)
        {
            return Result.Failure("Telemedicine appointments do not require physical clinic check-in.");
        }

        if (appointment.CheckedInAt.HasValue)
        {
            return Result.Failure("Patient has already been checked in for this appointment.");
        }

        appointment.CheckedInAt = _clinicClock.Now;
        appointment.UpdatedAt = _clinicClock.Now;

        _uow.Appointments.Update(appointment);
        await _uow.CommitAsync();

        if (!string.IsNullOrEmpty(appointment.Patient?.UserId))
        {
            await _notificationService.SendNotificationAsync(
                appointment.Patient.UserId,
                "تم تسجيل وصولك بالعيادة (Checked In)",
                $"تم تسجيل وصولك بنجاح في عيادة د. {appointment.Doctor?.User?.FullName ?? "الطبيب المعالج"}. يرجى الانتظار في صالة الاستقبال حتى يتم استدعاء دورك.");

            await _notificationService.NotifyAppointmentStatusChangedAsync(appointment.Id, "CheckedIn", appointment.Patient.UserId);
        }

        return Result.Success();
    }

    public async Task<Result<string>> EnsureTelehealthRoomSecretAsync(int appointmentId)
    {
        var appt = await _uow.Appointments.GetByIdAsync(appointmentId);
        if (appt == null)
        {
            return Result<string>.Failure("Appointment not found.");
        }

        if (appt.Type != AppointmentType.Telemedicine)
        {
            return Result<string>.Failure("Appointment is not a telehealth appointment.");
        }

        if (!string.IsNullOrEmpty(appt.TelehealthRoomSecret))
        {
            return Result<string>.Success(appt.TelehealthRoomSecret);
        }

        var secret = AppointmentFactory.GenerateTelehealthRoomSecret();
        appt.TelehealthRoomSecret = secret;
        _uow.Appointments.Update(appt);

        try
        {
            await _uow.CommitAsync();
            return Result<string>.Success(secret);
        }
        catch (DbUpdateConcurrencyException)
        {
            var reloaded = await _uow.Appointments.GetByIdAsync(appointmentId);
            if (reloaded != null && !string.IsNullOrEmpty(reloaded.TelehealthRoomSecret))
            {
                return Result<string>.Success(reloaded.TelehealthRoomSecret);
            }
            return Result<string>.Success(secret);
        }
    }

    private static bool IsTruncationException(DbUpdateException ex)
    {
        if (ex.InnerException is Microsoft.Data.SqlClient.SqlException sqlEx)
        {
            return sqlEx.Number == 8152 || sqlEx.Number == 2628;
        }

        var message = $"{ex.Message} {ex.InnerException?.Message}";
        return message.Contains("truncate", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("truncation", StringComparison.OrdinalIgnoreCase);
    }
}
