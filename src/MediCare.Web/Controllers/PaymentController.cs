using System.Security.Claims;
using MediCare.Data.Context;
using MediCare.Data.Entities;
using MediCare.Data.Enums;
using MediCare.Services.Contracts;
using MediCare.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MediCare.Web.Controllers;

[Authorize]
public class PaymentController : Controller
{
    private readonly IPaymentService _paymentService;
    private readonly ApplicationDbContext _db;
    private readonly ILogger<PaymentController> _logger;

    public PaymentController(
        IPaymentService paymentService,
        ApplicationDbContext db,
        ILogger<PaymentController> logger)
    {
        _paymentService = paymentService;
        _db = db;
        _logger = logger;
    }

    [HttpGet("/Payment/Result")]
    public async Task<IActionResult> Result(
        [FromQuery] string? merchantReference,
        [FromQuery(Name = "merchant_order_id")] string? merchantOrderId,
        [FromQuery] string? order,
        [FromQuery] string? id,
        [FromQuery] string? success,
        [FromQuery] string? pending,
        [FromQuery(Name = "data.message")] string? message,
        [FromQuery] int? appointmentId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Challenge();
        }

        var refCode = merchantReference ?? merchantOrderId;
        PaymentAttempt? attempt = null;

        if (!string.IsNullOrWhiteSpace(refCode))
        {
            attempt = await _db.PaymentAttempts
                .Include(x => x.Appointment)
                    .ThenInclude(x => x.Doctor)
                        .ThenInclude(x => x.User)
                .Include(x => x.Appointment)
                    .ThenInclude(x => x.Doctor)
                        .ThenInclude(x => x.Specialization)
                .Include(x => x.Appointment)
                    .ThenInclude(x => x.Patient)
                        .ThenInclude(x => x.User)
                .FirstOrDefaultAsync(x => x.MerchantReference == refCode);
        }
        else if (!string.IsNullOrWhiteSpace(order))
        {
            attempt = await _db.PaymentAttempts
                .Include(x => x.Appointment)
                    .ThenInclude(x => x.Doctor)
                        .ThenInclude(x => x.User)
                .Include(x => x.Appointment)
                    .ThenInclude(x => x.Doctor)
                        .ThenInclude(x => x.Specialization)
                .Include(x => x.Appointment)
                    .ThenInclude(x => x.Patient)
                        .ThenInclude(x => x.User)
                .FirstOrDefaultAsync(x => x.ProviderOrderId == order);
        }
        else if (appointmentId.HasValue)
        {
            attempt = await _db.PaymentAttempts
                .Include(x => x.Appointment)
                    .ThenInclude(x => x.Doctor)
                        .ThenInclude(x => x.User)
                .Include(x => x.Appointment)
                    .ThenInclude(x => x.Doctor)
                        .ThenInclude(x => x.Specialization)
                .Include(x => x.Appointment)
                    .ThenInclude(x => x.Patient)
                        .ThenInclude(x => x.User)
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync(x => x.AppointmentId == appointmentId.Value);
        }

        if (attempt == null)
        {
            return View(new PaymentResultViewModel
            {
                State = "NotFound",
                MerchantReference = refCode ?? "N/A"
            });
        }

        // Access check: appointment patient, doctor, or authorized admin
        var isPatientOwner = attempt.Appointment.Patient?.UserId == userId;
        var isDoctor = attempt.Appointment.Doctor?.UserId == userId;
        var isAdmin = User.IsInRole("Admin");

        if (!isPatientOwner && !isDoctor && !isAdmin)
        {
            _logger.LogWarning("Security IDOR: User {UserId} attempted unauthorized access to PaymentResult for attempt {AttemptId}", userId, attempt.Id);
            return Forbid();
        }

        var isPostCancelled = attempt.Appointment.Status is AppointmentStatus.Cancelled or AppointmentStatus.Rejected;
        var isPaid = attempt.Status == "Succeeded" || attempt.Appointment.PaymentStatus == PaymentStatus.Paid;
        var isExpired = attempt.Status == "Expired" || (attempt.Status == "Pending" && DateTime.UtcNow > attempt.ExpiresAt);
        var isFailed = attempt.Status == "Failed" || string.Equals(success, "false", StringComparison.OrdinalIgnoreCase);
        var isPending = attempt.Status == "Pending" && (string.Equals(pending, "true", StringComparison.OrdinalIgnoreCase) || string.Equals(success, "true", StringComparison.OrdinalIgnoreCase));

        string state;
        if (isPaid)
        {
            state = "Success";
        }
        else if (isFailed)
        {
            state = "Failed";
        }
        else if (isExpired)
        {
            state = "Expired";
        }
        else if (isPending)
        {
            state = "Processing";
        }
        else
        {
            state = "Processing";
        }

        var vm = new PaymentResultViewModel
        {
            State = state,
            MerchantReference = attempt.MerchantReference,
            ProviderTransactionId = attempt.ProviderTransactionId ?? id,
            ProviderOrderId = attempt.ProviderOrderId ?? order,
            AppointmentId = attempt.AppointmentId,
            AmountPaid = attempt.AmountCents / 100m,
            OriginalAmount = attempt.Appointment.ConsultationFee,
            DiscountAmount = attempt.DiscountAmount,
            PromoCode = attempt.PromoCode,
            Currency = attempt.Currency,
            PaymentMethodType = attempt.PaymentMethodType,
            PaymentMethodDetail = attempt.PaymentMethodDetail ?? attempt.Appointment.PaymentProvider,
            DoctorName = $"Dr. {attempt.Appointment.Doctor?.User?.FullName ?? "Physician"}",
            Specialization = attempt.Appointment.Doctor?.Specialization?.Name ?? "General Practice",
            ClinicLocation = "Dokki Medical District, Giza, Egypt",
            AppointmentDate = attempt.Appointment.AppointmentDate,
            StartTime = attempt.Appointment.StartTime,
            EndTime = attempt.Appointment.EndTime,
            ProcessedAt = attempt.CompletedAt ?? DateTime.UtcNow,
            FailureReason = message,
            TermsVersion = attempt.TermsVersion,
            IsPostCancellationPayment = isPostCancelled
        };

        return View(vm);
    }

    [HttpGet("/Appointments/Receipt/{appointmentId:int}")]
    [HttpGet("/Payment/Receipt/{appointmentId:int}")]
    public async Task<IActionResult> Receipt(int appointmentId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Challenge();
        }

        var result = await _paymentService.GetReceiptAsync(appointmentId, userId);
        if (!result.IsSuccess || result.Value == null)
        {
            TempData["ErrorMessage"] = result.Error ?? "Unable to load payment receipt.";
            return RedirectToAction("MyAppointments", "Appointments");
        }

        return View(result.Value);
    }
    [HttpGet("/Payment/History")]
    [Authorize(Roles = "Patient,Admin")]
    public async Task<IActionResult> History([FromQuery] MediCare.Services.DTOs.PaymentHistoryFilterDto filter, CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Challenge();
        }

        var isAdmin = User.IsInRole("Admin");
        var result = await _paymentService.GetPaymentHistoryAsync(filter, userId, isAdmin, cancellationToken);
        if (!result.IsSuccess || result.Value == null)
        {
            TempData["ErrorMessage"] = result.Error ?? "Unable to load payment history.";
            return RedirectToAction("MyAppointments", "Appointments");
        }

        return View(result.Value);
    }
}