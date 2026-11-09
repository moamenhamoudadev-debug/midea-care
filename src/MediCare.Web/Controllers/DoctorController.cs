using System.Security.Claims;
using System.Text.Json;
using MediCare.Data.Enums;
using MediCare.Services.Contracts;
using MediCare.Services.DTOs;
using MediCare.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCare.Web.Controllers;

[Authorize(Roles = "Doctor")]
public class DoctorController : Controller
{
    private readonly IScheduleService _scheduleService;
    private readonly IDoctorService _doctorService;
    private readonly IAppointmentService _appointmentService;
    private readonly ILogger<DoctorController> _logger;

    public DoctorController(
        IScheduleService scheduleService,
        IDoctorService doctorService,
        IAppointmentService appointmentService,
        ILogger<DoctorController> logger)
    {
        _scheduleService = scheduleService;
        _doctorService = doctorService;
        _appointmentService = appointmentService;
        _logger = logger;
    }

    private async Task<DoctorDetailDto?> GetCurrentDoctorAsync()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return null;
        }

        var result = await _doctorService.GetDoctorByUserIdAsync(userId);
        return result.IsSuccess ? result.Value : null;
    }

    [HttpGet("/Doctor/Schedule")]
    public async Task<IActionResult> Schedule()
    {
        var doctor = await GetCurrentDoctorAsync();
        if (doctor == null)
        {
            _logger.LogWarning("Doctor profile not found for user {UserId}", User.FindFirstValue(ClaimTypes.NameIdentifier));
            return Forbid();
        }

        var hoursResult = await _scheduleService.GetWorkingHoursAsync(doctor.Id);
        var viewModel = new DoctorScheduleViewModel
        {
            DoctorId = doctor.Id,
            DoctorName = doctor.FullName,
            WorkingHours = hoursResult.Value ?? new List<WorkingHoursDto>()
        };

        return View(viewModel);
    }

    [HttpPost("/Doctor/Schedule/AddHours")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddWorkingHours(DoctorScheduleViewModel model)
    {
        var doctor = await GetCurrentDoctorAsync();
        if (doctor == null)
        {
            return Forbid();
        }

        if (!TimeSpan.TryParse(model.NewWorkingHours.StartTime, out var startTime) ||
            !TimeSpan.TryParse(model.NewWorkingHours.EndTime, out var endTime))
        {
            ModelState.AddModelError("", "Please enter valid times (e.g. 09:00, 17:00).");
        }
        else
        {
            var dto = new WorkingHoursDto
            {
                DoctorId = doctor.Id,
                DayOfWeek = model.NewWorkingHours.DayOfWeek,
                StartTime = startTime,
                EndTime = endTime
            };

            var result = await _scheduleService.AddWorkingHoursAsync(dto, doctor.Id);
            if (result.IsSuccess)
            {
                TempData["SuccessMessage"] = "Working hours added successfully.";
                return RedirectToAction(nameof(Schedule));
            }

            ModelState.AddModelError("", result.Error ?? "Failed to add working hours.");
        }

        var hoursResult = await _scheduleService.GetWorkingHoursAsync(doctor.Id);
        model.DoctorId = doctor.Id;
        model.DoctorName = doctor.FullName;
        model.WorkingHours = hoursResult.Value ?? new List<WorkingHoursDto>();
        return View(nameof(Schedule), model);
    }

    [HttpPost("/Doctor/Schedule/DeleteHours/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteWorkingHours(int id)
    {
        var doctor = await GetCurrentDoctorAsync();
        if (doctor == null)
        {
            return Forbid();
        }

        var result = await _scheduleService.DeleteWorkingHoursAsync(id, doctor.Id);
        if (result.IsSuccess)
        {
            TempData["SuccessMessage"] = "Working hours removed successfully.";
        }
        else
        {
            TempData["ErrorMessage"] = result.Error ?? "Failed to delete working hours.";
        }

        return RedirectToAction(nameof(Schedule));
    }

    [HttpGet("/Doctor/Leaves")]
    public async Task<IActionResult> Leaves()
    {
        var doctor = await GetCurrentDoctorAsync();
        if (doctor == null)
        {
            return Forbid();
        }

        var leavesResult = await _scheduleService.GetDoctorLeavesAsync(doctor.Id);
        var viewModel = new DoctorLeavesViewModel
        {
            DoctorId = doctor.Id,
            DoctorName = doctor.FullName,
            Leaves = leavesResult.Value ?? new List<DoctorLeaveDto>()
        };

        if (TempData["LeaveWarnings"] is string warningsJson)
        {
            try
            {
                viewModel.ConflictWarnings = JsonSerializer.Deserialize<List<LeaveConflictWarningDto>>(warningsJson)
                    ?? new List<LeaveConflictWarningDto>();
            }
            catch
            {
                // Ignore deserialization error
            }
        }

        return View(viewModel);
    }

    [HttpPost("/Doctor/Leaves/Add")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddLeave(DoctorLeavesViewModel model)
    {
        var doctor = await GetCurrentDoctorAsync();
        if (doctor == null)
        {
            return Forbid();
        }

        var dto = new DoctorLeaveDto
        {
            DoctorId = doctor.Id,
            StartDate = model.NewLeave.StartDate,
            EndDate = model.NewLeave.EndDate,
            Reason = model.NewLeave.Reason
        };

        var result = await _scheduleService.AddDoctorLeaveAsync(dto, doctor.Id);
        if (result.IsSuccess)
        {
            if (result.Value != null && result.Value.HasConflicts)
            {
                TempData["LeaveWarnings"] = JsonSerializer.Serialize(result.Value.AffectedAppointments);
                TempData["WarningMessage"] = $"Leave registered successfully. Notice: {result.Value.AffectedAppointments.Count} existing appointment(s) fall within this leave period. Please review below.";
            }
            else
            {
                TempData["SuccessMessage"] = "Leave period added successfully.";
            }

            return RedirectToAction(nameof(Leaves));
        }

        TempData["ErrorMessage"] = result.Error ?? "Failed to add leave.";
        ModelState.AddModelError("", result.Error ?? "Failed to add leave.");

        var leavesResult = await _scheduleService.GetDoctorLeavesAsync(doctor.Id);
        model.DoctorId = doctor.Id;
        model.DoctorName = doctor.FullName;
        model.Leaves = leavesResult.Value ?? new List<DoctorLeaveDto>();
        return View(nameof(Leaves), model);
    }

    [HttpPost("/Doctor/Leaves/Delete/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteLeave(int id)
    {
        var doctor = await GetCurrentDoctorAsync();
        if (doctor == null)
        {
            return Forbid();
        }

        var result = await _scheduleService.DeleteDoctorLeaveAsync(id, doctor.Id);
        if (result.IsSuccess)
        {
            TempData["SuccessMessage"] = "Leave cancelled successfully.";
        }
        else
        {
            TempData["ErrorMessage"] = result.Error ?? "Failed to delete leave.";
        }

        return RedirectToAction(nameof(Leaves));
    }

    [HttpGet("/Doctor/Appointments")]
    public async Task<IActionResult> Appointments([FromQuery] AppointmentStatus? status, [FromQuery] string? date)
    {
        var doctor = await GetCurrentDoctorAsync();
        if (doctor == null)
        {
            return Forbid();
        }

        DateTime? parsedDate = null;
        if (!string.IsNullOrEmpty(date) && DateTime.TryParse(date, out var d))
        {
            parsedDate = d;
        }

        var result = await _appointmentService.GetDoctorAppointmentsAsync(doctor.Id, status, parsedDate);
        ViewBag.CurrentStatus = status;
        ViewBag.CurrentDate = date;

        return View(result.Value ?? new List<AppointmentSummaryDto>());
    }

    [HttpPost("/Doctor/Appointments/Confirm/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmAppointment(int id)
    {
        var doctor = await GetCurrentDoctorAsync();
        if (doctor == null)
        {
            return Forbid();
        }

        var result = await _appointmentService.ConfirmAppointmentAsync(id, doctor.Id);
        if (result.IsSuccess)
        {
            TempData["SuccessMessage"] = "Appointment confirmed successfully.";
        }
        else
        {
            TempData["ErrorMessage"] = result.Error ?? "Failed to confirm appointment.";
        }

        return RedirectToAction(nameof(Appointments));
    }

    [HttpPost("/Doctor/Appointments/Reject/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RejectAppointment(int id)
    {
        var doctor = await GetCurrentDoctorAsync();
        if (doctor == null)
        {
            return Forbid();
        }

        var result = await _appointmentService.RejectAppointmentAsync(id, doctor.Id);
        if (result.IsSuccess)
        {
            TempData["SuccessMessage"] = "Appointment rejected and calendar slot released.";
        }
        else
        {
            TempData["ErrorMessage"] = result.Error ?? "Failed to reject appointment.";
        }

        return RedirectToAction(nameof(Appointments));
    }

    [HttpPost("/Doctor/Appointments/NoShow/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkNoShow(int id)
    {
        var doctor = await GetCurrentDoctorAsync();
        if (doctor == null)
        {
            return Forbid();
        }

        var result = await _appointmentService.MarkNoShowAsync(id, doctor.Id);
        if (result.IsSuccess)
        {
            TempData["SuccessMessage"] = "Appointment marked as No-Show.";
        }
        else
        {
            TempData["ErrorMessage"] = result.Error ?? "Failed to mark appointment as No-Show.";
        }

        return RedirectToAction(nameof(Appointments));
    }
}
