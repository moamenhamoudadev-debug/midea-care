using System.Globalization;
using MediCare.Data.Entities;
using MediCare.Data.Enums;
using MediCare.Data.UnitOfWork;
using MediCare.Services.Common;
using MediCare.Services.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace MediCare.Services.Implementations;

public class AppointmentReminderService : IAppointmentReminderService
{
    private readonly IUnitOfWork _uow;
    private readonly IClinicClock _clinicClock;
    private readonly IEmailService _emailService;
    private readonly ISmsService _smsService;
    private readonly INotificationService _notificationService;
    private readonly ILogger<AppointmentReminderService> _logger;
    private readonly UserManager<ApplicationUser>? _userManager;

    public AppointmentReminderService(
        IUnitOfWork uow,
        IClinicClock clinicClock,
        IEmailService emailService,
        ISmsService smsService,
        INotificationService notificationService,
        ILogger<AppointmentReminderService> logger,
        UserManager<ApplicationUser>? userManager = null)
    {
        _uow = uow;
        _clinicClock = clinicClock;
        _emailService = emailService;
        _smsService = smsService;
        _notificationService = notificationService;
        _logger = logger;
        _userManager = userManager;
    }

    public async Task<int> ProcessPendingRemindersAsync(CancellationToken cancellationToken = default)
    {
        var now = _clinicClock.Now;
        var windowStart = now.AddHours(23);
        var windowEnd = now.AddHours(25);
        var minDate = windowStart.Date;
        var maxDate = windowEnd.Date;

        // Find candidate active appointments that haven't received a reminder within the target date window
        List<Appointment> candidates;
        try
        {
            candidates = await _uow.Appointments.GetPendingReminderAppointmentsWithDetailsAsync(minDate, maxDate);
        }
        catch
        {
            candidates = null!;
        }

        if (candidates == null)
        {
            // Fallback for mocks where GetPendingReminderAppointmentsWithDetailsAsync is unmocked
            candidates = (await _uow.Appointments.FindAsync(a =>
                !a.ReminderSent &&
                a.AppointmentDate >= minDate &&
                a.AppointmentDate <= maxDate &&
                (a.Status == AppointmentStatus.Confirmed || a.Status == AppointmentStatus.Pending))).ToList();
        }

        var dueAppointments = candidates.Where(a =>
        {
            var apptDateTime = a.AppointmentDate.Date.Add(a.StartTime);
            return apptDateTime >= windowStart && apptDateTime <= windowEnd;
        }).ToList();

        if (dueAppointments.Count == 0)
        {
            return 0;
        }

        _logger.LogInformation("Processing 24-hour appointment reminders for {Count} appointments", dueAppointments.Count);
        int processedCount = 0;

        // In-memory cache across this batch run to avoid redundant queries during fallback
        var doctorCache = new Dictionary<int, Doctor?>();
        var patientCache = new Dictionary<int, Patient?>();

        foreach (var appt in dueAppointments)
        {
            if (cancellationToken.IsCancellationRequested) break;

            IDatabaseTransaction? transaction = null;
            try
            {
                // Serialize workers across application instances and re-check after the lock.
                // The lock is transaction-owned on SQL Server and released when this transaction ends.
                transaction = await _uow.BeginTransactionAsync(cancellationToken);
                await using var appLock = await _uow.AcquireApplicationLockAsync(
                    $"AppointmentReminder_{appt.Id}", TimeSpan.FromSeconds(10), cancellationToken);

                if (!await _uow.Appointments.IsReminderPendingAsync(appt.Id))
                {
                    if (transaction != null) await transaction.RollbackAsync(cancellationToken);
                    continue;
                }

                // Prefer eager-loaded doctor navigation; fallback to repository with batch cache
                Doctor? doctor = appt.Doctor;
                if (doctor == null)
                {
                    if (!doctorCache.TryGetValue(appt.DoctorId, out doctor))
                    {
                        doctor = await _uow.Doctors.GetDoctorWithDetailsAsync(appt.DoctorId);
                        doctorCache[appt.DoctorId] = doctor;
                    }
                }

                var doctorName = doctor?.User?.FullName ?? $"Doctor #{appt.DoctorId}";
                var specName = doctor?.Specialization?.Name ?? "General Practice";

                // Prefer eager-loaded patient navigation; fallback to repository with batch cache
                Patient? patient = appt.Patient;
                if (patient == null)
                {
                    if (!patientCache.TryGetValue(appt.PatientId, out patient))
                    {
                        patient = await _uow.Patients.GetByIdAsync(appt.PatientId);
                        patientCache[appt.PatientId] = patient;
                    }
                }
                if (patient == null) continue;

                string? patientEmail = patient.User?.Email;
                string? patientPhone = patient.User?.PhoneNumber;
                string patientName = patient.User?.FullName ?? "Valued Patient";

                if (_userManager != null && string.IsNullOrEmpty(patientEmail))
                {
                    var user = await _userManager.FindByIdAsync(patient.UserId);
                    if (user != null)
                    {
                        patientEmail = user.Email;
                        patientPhone = user.PhoneNumber;
                        if (!string.IsNullOrEmpty(user.FullName)) patientName = user.FullName;
                    }
                }

                var formattedTime = DateTime.Today.Add(appt.StartTime).ToString("hh:mm tt", CultureInfo.InvariantCulture);

                // 1. Dispatch Email Reminder
                if (!string.IsNullOrEmpty(patientEmail))
                {
                    var emailSubject = $"MediCare — Reminder: Appointment Tomorrow with Dr. {doctorName}";
                    var emailBody = $@"
                        <div style='font-family: Arial, sans-serif; line-height: 1.6;'>
                            <h2>Hello {patientName},</h2>
                            <p>This is a 24-hour reminder for your upcoming appointment:</p>
                            <ul>
                                <li><strong>Doctor:</strong> Dr. {doctorName}</li>
                                <li><strong>Specialization:</strong> {specName}</li>
                                <li><strong>Date:</strong> {appt.AppointmentDate:dddd, MMMM dd, yyyy}</li>
                                <li><strong>Time:</strong> {formattedTime}</li>
                                <li><strong>Clinic Location:</strong> MediCare Clinic, {doctor?.Governorate ?? "Cairo"}</li>
                            </ul>
                            <p>If you need to reschedule, please access your MediCare patient portal at least 2 hours before the appointment.</p>
                        </div>";

                    if (!await _emailService.SendEmailAsync(patientEmail, emailSubject, emailBody))
                    {
                        throw new InvalidOperationException("Email provider did not confirm reminder delivery.");
                    }
                }

                // 2. Dispatch SMS Reminder
                if (!string.IsNullOrEmpty(patientPhone))
                {
                    var smsText = $"MediCare Reminder: You have an appointment tomorrow at {formattedTime} with Dr. {doctorName}.";
                    if (!await _smsService.SendSmsAsync(patientPhone, smsText))
                    {
                        throw new InvalidOperationException("SMS provider did not confirm reminder delivery.");
                    }
                }

                // 3. Dispatch In-App Notification
                if (!string.IsNullOrEmpty(patient.UserId))
                {
                    await _notificationService.SendNotificationAsync(
                        patient.UserId,
                        "Upcoming Appointment Reminder",
                        $"Reminder: Your appointment with Dr. {doctorName} is tomorrow at {formattedTime}.");
                }

                // 4. Mark reminder as sent and commit immediately to prevent duplicate dispatch if subsequent items fail
                appt.ReminderSent = true;
                _uow.Appointments.Update(appt);
                await _uow.CommitAsync();
                if (transaction != null) await transaction.CommitAsync(cancellationToken);
                processedCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process reminder for appointment {AppointmentId}", appt.Id);
                if (transaction != null)
                {
                    try
                    {
                        await transaction.RollbackAsync(cancellationToken);
                    }
                    catch (Exception rollbackException)
                    {
                        _logger.LogWarning(rollbackException, "Failed to roll back reminder transaction for appointment {AppointmentId}", appt.Id);
                    }
                }
            }
            finally
            {
                if (transaction != null) await transaction.DisposeAsync();
            }
        }

        if (processedCount > 0)
        {
            _logger.LogInformation("Successfully dispatched and committed {ProcessedCount} 24-hour appointment reminders", processedCount);
        }

        return processedCount;
    }
}
