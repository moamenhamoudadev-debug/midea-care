using System.Security.Claims;
using MediCare.Data.Enums;
using MediCare.Services.Common;
using MediCare.Services.Contracts;
using MediCare.Services.DTOs;
using MediCare.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace MediCare.Web.Controllers;

[Authorize]
public class AppointmentsController : Controller
{
    private readonly IAppointmentService _appointmentService;
    private readonly IDoctorService _doctorService;
    private readonly IMedicalRecordService _medicalRecordService;
    private readonly INotificationService _notificationService;
    private readonly IWebHostEnvironment _webHostEnvironment;
    private readonly ILogger<AppointmentsController> _logger;
    private readonly IClinicClock _clinicClock;

    public AppointmentsController(
        IAppointmentService appointmentService,
        IDoctorService doctorService,
        IMedicalRecordService medicalRecordService,
        INotificationService notificationService,
        IWebHostEnvironment webHostEnvironment,
        ILogger<AppointmentsController> logger,
        IClinicClock clinicClock)
    {
        _appointmentService = appointmentService;
        _doctorService = doctorService;
        _medicalRecordService = medicalRecordService;
        _notificationService = notificationService;
        _webHostEnvironment = webHostEnvironment;
        _logger = logger;
        _clinicClock = clinicClock;
    }

    private string GetStorageRootPath()
    {
        var path = Path.Combine(_webHostEnvironment.ContentRootPath, "App_Data", "uploads", "records");
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
        return path;
    }

    private async Task<bool> IsUserAuthorizedForAppointmentAsync(AppointmentSummaryDto appt, string userId)
    {
        if (User.IsInRole("Admin"))
        {
            return true;
        }

        if (User.IsInRole("Doctor"))
        {
            var doctorIdResult = await _doctorService.GetDoctorIdByUserIdAsync(userId);
            return doctorIdResult.IsSuccess && appt.DoctorId == doctorIdResult.Value;
        }

        if (User.IsInRole("Patient"))
        {
            var patientIdResult = await _appointmentService.GetPatientIdByUserIdAsync(userId);
            return patientIdResult.IsSuccess && appt.PatientId == patientIdResult.Value;
        }

        return false;
    }

    [HttpGet("/Appointments/Book/{doctorId:int}")]
    [Authorize(Roles = "Patient")]
    public async Task<IActionResult> Book(int doctorId, [FromQuery] string? date)
    {
        var doctorResult = await _doctorService.GetDoctorDetailsAsync(doctorId);
        if (!doctorResult.IsSuccess || doctorResult.Value == null)
        {
            TempData["ErrorMessage"] = "Doctor not found or account is not approved.";
            return RedirectToAction("Index", "Doctors");
        }

        ViewBag.SelectedDate = date;
        return View(doctorResult.Value);
    }

    [HttpPost("/Appointments/Book")]
    [Authorize(Roles = "Patient")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BookAppointment(
        [FromForm] int doctorId,
        [FromForm] string appointmentDate,
        [FromForm] string startTime,
        [FromForm] string? notes,
        [FromForm] AppointmentType type = AppointmentType.Consultation)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Challenge();
        }

        var patientIdResult = await _appointmentService.GetPatientIdByUserIdAsync(userId);
        if (!patientIdResult.IsSuccess)
        {
            TempData["ErrorMessage"] = "Patient profile record not found. Please contact administration.";
            return RedirectToAction("Index", "Doctors");
        }

        if (notes != null && notes.Length > 500)
        {
            TempData["ErrorMessage"] = "Symptoms / notes cannot exceed 500 characters.";
            return RedirectToAction(nameof(Book), new { doctorId, date = appointmentDate });
        }

        if (!DateTime.TryParse(appointmentDate, out var date) ||
            !TimeSpan.TryParse(startTime, out var time))
        {
            TempData["ErrorMessage"] = "Invalid appointment date or time selected.";
            return RedirectToAction(nameof(Book), new { doctorId });
        }

        var requestDto = new BookingRequestDto
        {
            DoctorId = doctorId,
            PatientId = patientIdResult.Value,
            AppointmentDate = date.Date,
            StartTime = time,
            Notes = notes,
            Type = type
        };

        var result = await _appointmentService.BookAppointmentAsync(requestDto);
        if (!result.IsSuccess)
        {
            TempData["ErrorMessage"] = result.Error ?? "Failed to book appointment.";
            return RedirectToAction(nameof(Book), new { doctorId, date = date.ToString("yyyy-MM-dd") });
        }

        TempData["SuccessMessage"] = "Your appointment has been successfully scheduled and is pending doctor confirmation!";
        return RedirectToAction(nameof(MyAppointments));
    }

    [HttpGet("/Appointments/MyAppointments")]
    [Authorize(Roles = "Patient")]
    public async Task<IActionResult> MyAppointments()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Challenge();
        }

        var result = await _appointmentService.GetPatientAppointmentsAsync(userId);
        var appointments = result.Value ?? new List<AppointmentSummaryDto>();

        return View(appointments);
    }

    [HttpPost("/Appointments/Cancel/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Challenge();
        }

        bool isDoctorOrAdmin = User.IsInRole("Doctor") || User.IsInRole("Admin");
        bool isAdmin = User.IsInRole("Admin");
        var result = await _appointmentService.CancelAppointmentAsync(id, userId, isDoctorOrAdmin, isAdmin);

        if (result.IsSuccess)
        {
            TempData["SuccessMessage"] = "Appointment cancelled successfully.";
        }
        else
        {
            TempData["ErrorMessage"] = result.Error ?? "Failed to cancel appointment.";
        }

        if (User.IsInRole("Doctor"))
        {
            return RedirectToAction("Appointments", "Doctor");
        }

        return RedirectToAction(nameof(MyAppointments));
    }

    [HttpGet("/Appointments/Details/{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Challenge();
        }

        var apptResult = await _appointmentService.GetAppointmentByIdAsync(id);
        if (!apptResult.IsSuccess || apptResult.Value == null)
        {
            return NotFound();
        }

        var appt = apptResult.Value;
        bool isAuthorized = await IsUserAuthorizedForAppointmentAsync(appt, userId);

        if (!isAuthorized)
        {
            _logger.LogWarning("Security IDOR: User {UserId} attempted unauthorized access to appointment {AppointmentId}", userId, id);
            return Forbid();
        }

        // BIZ-09: Redact clinical data from administrator view
        if (User.IsInRole("Admin"))
        {
            appt.MedicalRecordId = null;
            appt.MedicalRecordAttachmentPath = null;
            appt.Diagnosis = null;
        }

        return View(appt);
    }

    [HttpGet("/Appointments/Telehealth/{id:int}")]
    public async Task<IActionResult> Telehealth(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Challenge();
        }

        var apptResult = await _appointmentService.GetAppointmentByIdAsync(id);
        if (!apptResult.IsSuccess || apptResult.Value == null)
        {
            return NotFound();
        }

        var appt = apptResult.Value;

        // SEC-12: Patient Confidentiality & Privilege Separation:
        // Live telehealth video consultation is restricted strictly to the appointment's own Patient and assigned Doctor.
        // Administrators are excluded to uphold medical privacy.
        bool isParticipant = false;
        if (User.IsInRole("Doctor"))
        {
            var doctorIdResult = await _doctorService.GetDoctorIdByUserIdAsync(userId);
            isParticipant = doctorIdResult.IsSuccess && appt.DoctorId == doctorIdResult.Value;
        }
        else if (User.IsInRole("Patient"))
        {
            var patientIdResult = await _appointmentService.GetPatientIdByUserIdAsync(userId);
            isParticipant = patientIdResult.IsSuccess && appt.PatientId == patientIdResult.Value;
        }

        if (!isParticipant)
        {
            _logger.LogWarning("Security IDOR/Privacy: User {UserId} denied access to private Telehealth room for appointment {AppointmentId}", userId, id);
            return Forbid();
        }

        // SEC-08 Guard 1: Type check (must be Telemedicine)
        if (appt.Type != AppointmentType.Telemedicine)
        {
            _logger.LogWarning("Security Guard: User {UserId} attempted telehealth access to non-telehealth appointment {AppointmentId} (Type: {Type})",
                userId, id, appt.Type);
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return View("TelehealthUnavailable", new TelehealthUnavailableViewModel
            {
                AppointmentId = id,
                Title = "In-Person Consultation",
                Message = "This appointment is scheduled for an in-person clinic consultation, not a virtual video consultation."
            });
        }

        // SEC-08 Guard 2: Status check (must be Confirmed)
        if (appt.Status != AppointmentStatus.Confirmed)
        {
            _logger.LogWarning("Security Guard: User {UserId} attempted telehealth access to appointment {AppointmentId} with invalid status {Status}",
                userId, id, appt.Status);
            Response.StatusCode = StatusCodes.Status400BadRequest;
            var statusMessage = appt.Status switch
            {
                AppointmentStatus.Pending => "This appointment is currently pending physician confirmation. Telehealth rooms are only accessible once confirmed.",
                AppointmentStatus.Completed => "This telehealth consultation has already been completed.",
                AppointmentStatus.Cancelled => "This appointment has been cancelled.",
                AppointmentStatus.Rejected => "This appointment was declined.",
                _ => $"This appointment is not active (Status: {appt.Status})."
            };
            return View("TelehealthUnavailable", new TelehealthUnavailableViewModel
            {
                AppointmentId = id,
                Title = "Session Unavailable",
                Message = statusMessage
            });
        }

        // SEC-08 Guard 3: Time window check (start - 15m to end + 30m)
        var slotStart = appt.AppointmentDate.Date.Add(appt.StartTime);
        var slotEnd = appt.AppointmentDate.Date.Add(appt.EndTime);
        var windowOpen = slotStart.AddMinutes(-15);
        var windowClose = slotEnd.AddMinutes(30);
        var now = _clinicClock.Now;

        if (now < windowOpen)
        {
            _logger.LogInformation("Telehealth early access attempt for appointment {AppointmentId} by user {UserId}. Opens at {WindowOpen}",
                id, userId, windowOpen);
            Response.StatusCode = StatusCodes.Status403Forbidden;
            return View("TelehealthUnavailable", new TelehealthUnavailableViewModel
            {
                AppointmentId = id,
                Title = "Consultation Room Not Yet Open",
                Message = $"Your telehealth session opens at {windowOpen:hh:mm tt} on {appt.FormattedDate} (15 minutes prior to appointment time). Please return then.",
                WindowOpensAt = windowOpen,
                WindowClosesAt = windowClose
            });
        }

        if (now > windowClose)
        {
            _logger.LogInformation("Telehealth late access attempt for appointment {AppointmentId} by user {UserId}. Closed at {WindowClose}",
                id, userId, windowClose);
            Response.StatusCode = StatusCodes.Status403Forbidden;
            return View("TelehealthUnavailable", new TelehealthUnavailableViewModel
            {
                AppointmentId = id,
                Title = "Consultation Window Closed",
                Message = $"This telehealth consultation window has closed. The session was open until {windowClose:hh:mm tt}.",
                WindowOpensAt = windowOpen,
                WindowClosesAt = windowClose
            });
        }

        // Public Jitsi rooms are an educational Development-only integration.
        // Non-Development environments retain the local camera preview but never expose a public room URL.
        ViewBag.PublicJitsiDemoEnabled = _webHostEnvironment.IsDevelopment();
        if (_webHostEnvironment.IsDevelopment())
        {
            // SEC-08: Ensure cryptographically random room secret exists (lazy backfill if missing)
            var secretResult = await _appointmentService.EnsureTelehealthRoomSecretAsync(id);
            if (!secretResult.IsSuccess || string.IsNullOrEmpty(secretResult.Value))
            {
                _logger.LogError("Failed to resolve telehealth room secret for appointment {AppointmentId}", id);
                return StatusCode(StatusCodes.Status500InternalServerError, "Failed to initialize demo telehealth room.");
            }

            appt.TelehealthRoomSecret = secretResult.Value;
        }
        else
        {
            appt.TelehealthRoomSecret = null;
        }
        return View(appt);
    }

    [HttpGet("/Appointments/Reschedule/{id:int}")]
    public async Task<IActionResult> Reschedule(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Challenge();
        }

        var apptResult = await _appointmentService.GetAppointmentByIdAsync(id);
        if (!apptResult.IsSuccess || apptResult.Value == null)
        {
            return NotFound();
        }

        var appt = apptResult.Value;
        bool isAuthorized = await IsUserAuthorizedForAppointmentAsync(appt, userId);

        if (!isAuthorized)
        {
            _logger.LogWarning("Security IDOR: User {UserId} denied access to reschedule appointment {AppointmentId}", userId, id);
            return Forbid();
        }

        var doctorResult = await _doctorService.GetDoctorDetailsAsync(appt.DoctorId);

        var viewModel = new MediCare.Web.ViewModels.RescheduleAppointmentViewModel
        {
            AppointmentId = appt.Id,
            DoctorId = appt.DoctorId,
            DoctorName = appt.DoctorName,
            SpecializationName = appt.SpecializationName,
            CurrentDate = appt.AppointmentDate,
            CurrentStartTime = appt.StartTime,
            NewAppointmentDate = appt.AppointmentDate > _clinicClock.Today ? appt.AppointmentDate.AddDays(1) : _clinicClock.Today.AddDays(1),
            DoctorDetails = doctorResult.Value
        };

        return View(viewModel);
    }

    [HttpPost("/Appointments/Reschedule")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RescheduleAppointment(MediCare.Web.ViewModels.RescheduleAppointmentViewModel model)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Challenge();
        }

        if (!ModelState.IsValid)
        {
            await PopulateRescheduleViewModelAsync(model);
            return View("Reschedule", model);
        }

        if (!TimeSpan.TryParse(model.NewStartTime, out var time))
        {
            ModelState.AddModelError(nameof(model.NewStartTime), "Invalid time specified for rescheduling.");
            await PopulateRescheduleViewModelAsync(model);
            return View("Reschedule", model);
        }

        var dto = new RescheduleRequestDto
        {
            AppointmentId = model.AppointmentId,
            NewAppointmentDate = model.NewAppointmentDate.Date,
            NewStartTime = time,
            Reason = model.Reason
        };

        bool isDoctorOrAdmin = User.IsInRole("Doctor") || User.IsInRole("Admin");
        bool isAdmin = User.IsInRole("Admin");
        var result = await _appointmentService.RescheduleAppointmentAsync(dto, userId, isDoctorOrAdmin, isAdmin);

        if (result.IsSuccess)
        {
            TempData["SuccessMessage"] = "Appointment has been successfully rescheduled!";
            if (User.IsInRole("Doctor"))
            {
                return RedirectToAction("Appointments", "Doctor");
            }
            return RedirectToAction(nameof(MyAppointments));
        }

        ModelState.AddModelError(string.Empty, result.Error ?? "Failed to reschedule appointment.");
        TempData["ErrorMessage"] = result.Error ?? "Failed to reschedule appointment.";
        await PopulateRescheduleViewModelAsync(model);
        return View("Reschedule", model);
    }

    private async Task PopulateRescheduleViewModelAsync(MediCare.Web.ViewModels.RescheduleAppointmentViewModel model)
    {
        var apptResult = await _appointmentService.GetAppointmentByIdAsync(model.AppointmentId);
        if (apptResult.IsSuccess && apptResult.Value != null)
        {
            var appt = apptResult.Value;
            model.DoctorId = appt.DoctorId;
            model.DoctorName = appt.DoctorName;
            model.SpecializationName = appt.SpecializationName;
            model.CurrentDate = appt.AppointmentDate;
            model.CurrentStartTime = appt.StartTime;

            var doctorResult = await _doctorService.GetDoctorDetailsAsync(appt.DoctorId);
            model.DoctorDetails = doctorResult.Value;
        }
    }

    [HttpPost("/Appointments/UploadAttachment/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadAttachment(int id, IFormFile? attachment)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Challenge();
        }

        if (User.IsInRole("Admin"))
        {
            _logger.LogWarning("Security Guard: Admin {UserId} attempted to upload medical attachment for appointment {AppointmentId}", userId, id);
            return Forbid();
        }

        if (attachment == null || attachment.Length == 0)
        {
            TempData["ErrorMessage"] = "Please select a valid medical file (PDF, PNG, JPG) to upload.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var result = await _medicalRecordService.UploadPatientAttachmentAsync(
            id,
            attachment,
            userId,
            GetStorageRootPath());

        if (result.IsSuccess)
        {
            TempData["SuccessMessage"] = "Medical lab / radiology attachment uploaded successfully!";
        }
        else
        {
            TempData["ErrorMessage"] = result.Error ?? "Failed to upload medical attachment.";
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("/Appointments/CallNextQueue/{id:int}")]
    [Authorize(Roles = "Doctor,Admin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CallNextQueue(int id)
    {
        var doctorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(doctorUserId))
        {
            return Challenge();
        }

        var isAdmin = User.IsInRole("Admin");
        var result = await _appointmentService.CallNextQueuePatientAsync(id, doctorUserId, isAdmin);
        if (result.IsSuccess)
        {
            TempData["SuccessMessage"] = "تم استدعاء المريض بنجاح وإرسال إشعار فوري للشاشة ولحساب المريض!";
        }
        else
        {
            TempData["ErrorMessage"] = result.Error ?? "فشل استدعاء المريض.";
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("/Appointments/CheckIn/{id:int}")]
    [Authorize(Roles = "Doctor,Admin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CheckIn(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Challenge();
        }

        var apptResult = await _appointmentService.GetAppointmentByIdAsync(id);
        if (!apptResult.IsSuccess || apptResult.Value == null)
        {
            return NotFound();
        }

        var appt = apptResult.Value;
        var isAdmin = User.IsInRole("Admin");

        var result = await _appointmentService.CheckInAppointmentAsync(id, userId, isAdmin);
        if (result.IsSuccess)
        {
            TempData["SuccessMessage"] = $"تم تسجيل حضور المريض {appt.PatientName} بالاستقبال (رقم الطابور #{appt.QueueNumber}) وتأكيد جاهزيته للكشف!";
        }
        else
        {
            if (result.Error?.StartsWith("Forbidden", StringComparison.OrdinalIgnoreCase) == true)
            {
                _logger.LogWarning("Security IDOR: User {UserId} denied CheckIn for Appointment {AppointmentId}: {Error}",
                    userId, id, result.Error);
                return Forbid();
            }

            if (result.Error?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true)
            {
                return NotFound();
            }

            TempData["ErrorMessage"] = result.Error ?? "فشل تسجيل حضور المريض.";
        }
        return RedirectToAction(nameof(Details), new { id });
    }
}
