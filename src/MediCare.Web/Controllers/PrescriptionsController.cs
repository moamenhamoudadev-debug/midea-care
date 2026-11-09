using System.Security.Claims;
using MediCare.Services.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MediCare.Web.Controllers;

[Authorize]
public class PrescriptionsController : Controller
{
    private readonly IPrescriptionService _prescriptionService;
    private readonly ILogger<PrescriptionsController> _logger;

    public PrescriptionsController(
        IPrescriptionService prescriptionService,
        ILogger<PrescriptionsController> logger)
    {
        _prescriptionService = prescriptionService;
        _logger = logger;
    }

    [HttpGet("/Prescriptions/Print/{id:int}")]
    public async Task<IActionResult> Print(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Challenge();
        }

        if (User.IsInRole("Admin"))
        {
            _logger.LogWarning("Security IDOR/Privacy: Admin {UserId} forbidden from viewing Prescription {PrescriptionId}", userId, id);
            return Forbid();
        }

        bool isDoctor = User.IsInRole("Doctor");
        bool isPatient = User.IsInRole("Patient");
        bool isAdmin = false;

        var result = await _prescriptionService.GetPrescriptionForPrintAsync(id, userId, isDoctor, isPatient, isAdmin);
        if (!result.IsSuccess)
        {
            if (result.Error?.StartsWith("Forbidden", StringComparison.OrdinalIgnoreCase) == true)
            {
                _logger.LogWarning("Security IDOR: User {UserId} forbidden from accessing Prescription {PrescriptionId}: {Error}",
                    userId, id, result.Error);
                return Forbid();
            }

            return NotFound();
        }

        return View(result.Value);
    }

    [HttpGet("/Prescriptions/ByAppointment/{appointmentId:int}")]
    public async Task<IActionResult> ByAppointment(int appointmentId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Challenge();
        }

        if (User.IsInRole("Admin"))
        {
            _logger.LogWarning("Security IDOR/Privacy: Admin {UserId} forbidden from viewing Prescription for Appointment {ApptId}", userId, appointmentId);
            return Forbid();
        }

        bool isDoctor = User.IsInRole("Doctor");
        bool isPatient = User.IsInRole("Patient");
        bool isAdmin = false;

        var result = await _prescriptionService.GetPrescriptionByAppointmentIdAsync(appointmentId, userId, isDoctor, isPatient, isAdmin);
        if (!result.IsSuccess)
        {
            if (result.Error?.StartsWith("Forbidden", StringComparison.OrdinalIgnoreCase) == true)
            {
                _logger.LogWarning("Security IDOR: User {UserId} forbidden from accessing Prescription for Appointment {ApptId}: {Error}",
                    userId, appointmentId, result.Error);
                return Forbid();
            }

            TempData["ErrorMessage"] = "No prescription record found for this appointment.";
            return RedirectToAction("MyAppointments", "Appointments");
        }

        return RedirectToAction(nameof(Print), new { id = result.Value!.Id });
    }

    [HttpGet("/Prescriptions/Verify")]
    [AllowAnonymous]
    [EnableRateLimiting("PrescriptionVerificationPolicy")]
    public async Task<IActionResult> Verify([FromQuery] string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length != 32)
        {
            return View("VerifyInvalid");
        }

        var result = await _prescriptionService.VerifyPrescriptionByTokenAsync(token);
        if (!result.IsSuccess || result.Value == null || !result.Value.IsValid)
        {
            return View("VerifyInvalid");
        }

        ViewBag.Token = token;
        return View(result.Value);
    }

    [HttpGet("/Prescriptions/Dispense")]
    [Authorize(Roles = "Pharmacist,Admin")]
    [EnableRateLimiting("PrescriptionDispensePolicy")]
    public async Task<IActionResult> Dispense([FromQuery] string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length != 32)
        {
            TempData["ErrorMessage"] = "رمز التحقق غير صالح.";
            return RedirectToAction("Index", "Home");
        }

        var result = await _prescriptionService.GetPrescriptionForPharmacistAsync(token);
        if (!result.IsSuccess || result.Value == null)
        {
            TempData["ErrorMessage"] = result.Error ?? "الروشتة غير موجودة.";
            return RedirectToAction("Index", "Home");
        }

        return View("PharmacistDispense", result.Value);
    }

    [HttpPost("/Prescriptions/Dispense")]
    [Authorize(Roles = "Pharmacist,Admin")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("PrescriptionDispensePolicy")]
    public async Task<IActionResult> Dispense([FromForm] string token, [FromForm] string? pharmacyNotes)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Challenge();
        }

        if (string.IsNullOrWhiteSpace(token) || token.Length != 32)
        {
            TempData["ErrorMessage"] = "رمز التحقق غير صالح.";
            return RedirectToAction("Index", "Home");
        }

        var result = await _prescriptionService.DispensePrescriptionAsync(token, userId, pharmacyNotes);
        if (result.IsSuccess)
        {
            TempData["SuccessMessage"] = "تم تسجيل صرف الروشتة رسمياً بنجاح وتوثيق تاريخ ووقت الصرف.";
        }
        else
        {
            TempData["ErrorMessage"] = result.Error ?? "فشل تأكيد صرف الروشتة.";
        }

        return RedirectToAction(nameof(Dispense), new { token });
    }
}
