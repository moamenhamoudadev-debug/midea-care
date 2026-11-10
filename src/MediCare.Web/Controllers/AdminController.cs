using MediCare.Data.Enums;
using System.Security.Claims;
using MediCare.Services.Contracts;
using MediCare.Services.DTOs;
using MediCare.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCare.Web.Controllers;

[Authorize(Roles = "Admin")]
public class AdminController : Controller
{
    private readonly IAdminService _adminService;
    private readonly ILogger<AdminController> _logger;
    private readonly IPaymentService _paymentService;
    private readonly IPaymobRefundService? _refundService;
    private readonly IDoctorReviewService? _doctorReviewService;

    public AdminController(
        IAdminService adminService,
        ILogger<AdminController> logger,
        IPaymentService paymentService,
        IPaymobRefundService? refundService = null,
        IDoctorReviewService? doctorReviewService = null)
    {
        _adminService = adminService;
        _logger = logger;
        _paymentService = paymentService;
        _refundService = refundService;
        _doctorReviewService = doctorReviewService;
    }

    [HttpGet("/Admin")]
    [HttpGet("/Admin/Dashboard")]
    [HttpGet("/Admin/Index")]
    public async Task<IActionResult> Index()
    {
        var metricsResult = await _adminService.GetDashboardMetricsAsync();
        if (!metricsResult.IsSuccess)
        {
            TempData["ErrorMessage"] = metricsResult.Error ?? "Failed to load dashboard metrics.";
            return View(new AdminDashboardMetricsDto());
        }

        return View(metricsResult.Value);
    }

    [HttpGet("/Admin/Approvals")]
    public async Task<IActionResult> Approvals()
    {
        var pendingResult = await _adminService.GetPendingDoctorsAsync();
        if (!pendingResult.IsSuccess)
        {
            TempData["ErrorMessage"] = pendingResult.Error ?? "Failed to load pending doctor applications.";
            return View(new List<DoctorApprovalSummaryDto>());
        }

        return View(pendingResult.Value);
    }

    [HttpPost("/Admin/ApproveDoctor/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApproveDoctor(int id)
    {
        var result = await _adminService.ApproveDoctorAsync(id);
        if (result.IsSuccess)
        {
            TempData["SuccessMessage"] = "Doctor account has been approved and welcoming email dispatched.";
        }
        else
        {
            TempData["ErrorMessage"] = result.Error ?? "Failed to approve doctor.";
        }

        return RedirectToAction(nameof(Approvals));
    }

    [HttpPost("/Admin/RejectDoctor/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RejectDoctor(int id, [FromForm] string? reason)
    {
        var result = await _adminService.RejectDoctorAsync(id, reason);
        if (result.IsSuccess)
        {
            TempData["SuccessMessage"] = "Doctor application rejected and status email dispatched.";
        }
        else
        {
            TempData["ErrorMessage"] = result.Error ?? "Failed to reject doctor.";
        }

        return RedirectToAction(nameof(Approvals));
    }

    [HttpGet("/Admin/Reports")]
    public async Task<IActionResult> Reports()
    {
        var metricsResult = await _adminService.GetDashboardMetricsAsync();
        if (!metricsResult.IsSuccess)
        {
            TempData["ErrorMessage"] = metricsResult.Error ?? "Failed to load analytics report.";
            return View(new AdminDashboardMetricsDto());
        }

        return View(metricsResult.Value);
    }

    [HttpGet("/Admin/ExportAppointmentsCsv")]
    public async Task<IActionResult> ExportAppointmentsCsv()
    {
        var csvResult = await _adminService.ExportAppointmentsCsvAsync();
        if (!csvResult.IsSuccess || csvResult.Value == null)
        {
            TempData["ErrorMessage"] = csvResult.Error ?? "Failed to generate CSV export.";
            return RedirectToAction(nameof(Reports));
        }

        var fileName = $"medicare-appointments-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv";
        return File(csvResult.Value, "text/csv; charset=utf-8", fileName);
    }

    [HttpGet("/Admin/ExportAppointmentsExcel")]
    public async Task<IActionResult> ExportAppointmentsExcel()
    {
        var excelResult = await _adminService.ExportAppointmentsExcelAsync();
        if (!excelResult.IsSuccess || excelResult.Value == null)
        {
            TempData["ErrorMessage"] = excelResult.Error ?? "Failed to generate Excel export.";
            return RedirectToAction(nameof(Reports));
        }

        var fileName = $"medicare-appointments-{DateTime.UtcNow:yyyyMMdd-HHmm}.xlsx";
        return File(excelResult.Value, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    [HttpGet("/Admin/ExportAppointmentsPdf")]
    public async Task<IActionResult> ExportAppointmentsPdf()
    {
        var pdfResult = await _adminService.ExportAppointmentsPdfAsync();
        if (!pdfResult.IsSuccess || pdfResult.Value == null)
        {
            TempData["ErrorMessage"] = pdfResult.Error ?? "Failed to generate PDF report.";
            return RedirectToAction(nameof(Reports));
        }

        var fileName = $"medicare-appointments-{DateTime.UtcNow:yyyyMMdd-HHmm}.pdf";
        return File(pdfResult.Value, "application/pdf", fileName);
    }

    [HttpGet("/Admin/Patients")]
    public async Task<IActionResult> Patients([FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        ViewData["CurrentSearch"] = search;
        var pagedResult = await _adminService.GetPatientsPagedAsync(search, page, pageSize);
        if (!pagedResult.IsSuccess || pagedResult.Value == null)
        {
            TempData["ErrorMessage"] = pagedResult.Error ?? "Failed to load patient records.";
            return View(new List<AdminPatientSummaryDto>());
        }

        ViewBag.PagedPatients = pagedResult.Value;
        ViewBag.CurrentPage = pagedResult.Value.PageNumber;
        ViewBag.TotalPages = pagedResult.Value.TotalPages;
        ViewBag.TotalCount = pagedResult.Value.TotalCount;
        ViewBag.PageSize = pagedResult.Value.PageSize;

        return View(pagedResult.Value.Items);
    }

    [HttpPost("/Admin/TogglePatientLockout/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TogglePatientLockout(int id, [FromForm] bool lockout)
    {
        var result = await _adminService.TogglePatientLockoutAsync(id, lockout);
        if (result.IsSuccess)
        {
            TempData["SuccessMessage"] = lockout
                ? "Patient account has been locked out from accessing the system."
                : "Patient account lockout has been removed.";
        }
        else
        {
            TempData["ErrorMessage"] = result.Error ?? "Failed to update patient lockout status.";
        }

        return RedirectToAction(nameof(Patients));
    }

    [HttpGet("/Admin/CreatePharmacist")]
    public IActionResult CreatePharmacist()
    {
        return View(new CreatePharmacistViewModel());
    }

    [HttpPost("/Admin/CreatePharmacist")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreatePharmacist(CreatePharmacistViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var adminId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "unknown";
        var dto = new CreatePharmacistDto
        {
            FullName = model.FullName,
            Email = model.Email,
            Password = model.Password
        };

        var result = await _adminService.CreatePharmacistAsync(dto, adminId);
        if (result.IsSuccess)
        {
            TempData["SuccessMessage"] = $"Pharmacist account for {model.Email} has been created successfully with role 'Pharmacist'.";
            return RedirectToAction(nameof(Index));
        }

        ModelState.AddModelError(string.Empty, result.Error ?? "Failed to create pharmacist account.");
        return View(model);
    }

    [HttpGet("/Admin/Pharmacists")]
    public async Task<IActionResult> Pharmacists()
    {
        var result = await _adminService.GetPharmacistsAsync();
        if (!result.IsSuccess)
        {
            TempData["ErrorMessage"] = result.Error ?? "Failed to load pharmacist staff.";
            return View(new List<MediCare.Services.DTOs.PharmacistSummaryDto>());
        }

        return View(result.Value);
    }

    [HttpGet("/Admin/Specializations")]
    public async Task<IActionResult> Specializations()
    {
        var specsResult = await _adminService.GetAllSpecializationsAsync();
        if (!specsResult.IsSuccess)
        {
            TempData["ErrorMessage"] = specsResult.Error ?? "Failed to load specializations.";
            return View(new List<SpecializationDto>());
        }

        return View(specsResult.Value);
    }

    [HttpPost("/Admin/CreateSpecialization")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateSpecialization([FromForm] CreateSpecializationDto dto)
    {
        var result = await _adminService.CreateSpecializationAsync(dto);
        if (result.IsSuccess)
        {
            TempData["SuccessMessage"] = $"Specialization '{dto.Name}' added successfully.";
        }
        else
        {
            TempData["ErrorMessage"] = result.Error ?? "Failed to add specialization.";
        }

        return RedirectToAction(nameof(Specializations));
    }

    [HttpPost("/Admin/UpdateSpecialization/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateSpecialization(int id, [FromForm] UpdateSpecializationDto dto)
    {
        var result = await _adminService.UpdateSpecializationAsync(id, dto);
        if (result.IsSuccess)
        {
            TempData["SuccessMessage"] = $"Specialization updated successfully.";
        }
        else
        {
            TempData["ErrorMessage"] = result.Error ?? "Failed to update specialization.";
        }

        return RedirectToAction(nameof(Specializations));
    }

    [HttpPost("/Admin/DeleteSpecialization/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteSpecialization(int id)
    {
        var result = await _adminService.DeleteSpecializationAsync(id);
        if (result.IsSuccess)
        {
            TempData["SuccessMessage"] = "Specialization deleted successfully.";
        }
        else
        {
            TempData["ErrorMessage"] = result.Error ?? "Failed to delete specialization.";
        }

        return RedirectToAction(nameof(Specializations));
    }

    [HttpGet("/Admin/Payments")]
    public async Task<IActionResult> Payments([FromQuery] PaymentHistoryFilterDto filter, CancellationToken cancellationToken = default)
    {
        var result = await _paymentService.GetAdminFinancialDashboardAsync(filter, cancellationToken);
        if (!result.IsSuccess || result.Value == null)
        {
            TempData["ErrorMessage"] = result.Error ?? "Unable to load financial dashboard.";
            return RedirectToAction("Index");
        }

        var adminUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.Identity?.Name ?? "admin";
        if (_refundService != null)
        {
            result.Value.RefundQueue = await _refundService.GetRefundQueueAsync(null, adminUserId, cancellationToken);
            result.Value.PendingRefundReviewCount = result.Value.RefundQueue.Count(r => r.Status == "UnderReview" || r.Status == "Requested");
            result.Value.PendingRefundAmount = result.Value.RefundQueue.Where(r => r.Status == "UnderReview" || r.Status == "Requested").Sum(r => r.AmountEgp);
        }

        return View(result.Value);
    }

    [HttpPost("/Admin/Payments/Reconcile")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reconcile([FromForm] AdminResolveReconciliationDto dto, CancellationToken cancellationToken = default)
    {
        var adminUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.Identity?.Name ?? "admin";
        var result = await _paymentService.ResolveReconciliationAsync(dto, adminUserId, cancellationToken);
        if (!result.IsSuccess)
        {
            TempData["ErrorMessage"] = result.Error ?? "Failed to resolve reconciliation.";
        }
        else
        {
            TempData["SuccessMessage"] = $"Payment transaction #{dto.AttemptId} reconciliation successfully resolved ({dto.Resolution}).";
        }

        return RedirectToAction("Payments");
    }

    [HttpPost("/Admin/Payments/Refund")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestRefund([FromForm] CreateRefundRequestDto dto, CancellationToken cancellationToken = default)
    {
        if (_refundService == null)
        {
            TempData["ErrorMessage"] = "Refund service is not configured.";
            return RedirectToAction(nameof(Payments));
        }

        var adminUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.Identity?.Name ?? "admin";
        var result = await _refundService.RequestRefundAsync(dto, adminUserId, cancellationToken);

        if (!result.Success)
        {
            TempData["ErrorMessage"] = result.Message;
        }
        else
        {
            TempData["SuccessMessage"] = result.Message;
        }

        return RedirectToAction(nameof(Payments));
    }

    [HttpPost("/Admin/Payments/Refund/Approve")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApproveRefund([FromForm] ApproveRefundRequestDto dto, CancellationToken cancellationToken = default)
    {
        if (_refundService == null)
        {
            TempData["ErrorMessage"] = "Refund service is not configured.";
            return RedirectToAction(nameof(Payments));
        }

        var adminUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.Identity?.Name ?? "admin";
        var result = await _refundService.ApproveAndExecuteRefundAsync(dto.RefundId, adminUserId, dto.ApproverNote, cancellationToken);

        if (!result.Success)
        {
            TempData["ErrorMessage"] = result.Message;
        }
        else
        {
            TempData["SuccessMessage"] = result.Message;
        }

        return RedirectToAction(nameof(Payments));
    }

    [HttpPost("/Admin/Payments/Refund/Reject")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RejectRefund([FromForm] RejectRefundRequestDto dto, CancellationToken cancellationToken = default)
    {
        if (_refundService == null)
        {
            TempData["ErrorMessage"] = "Refund service is not configured.";
            return RedirectToAction(nameof(Payments));
        }

        var adminUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.Identity?.Name ?? "admin";
        var result = await _refundService.RejectRefundAsync(dto.RefundId, adminUserId, dto.RejectionReason, cancellationToken);

        if (!result.Success)
        {
            TempData["ErrorMessage"] = result.Message;
        }
        else
        {
            TempData["SuccessMessage"] = result.Message;
        }

        return RedirectToAction(nameof(Payments));
    }

    [HttpGet("/Admin/Reviews")]
    public async Task<IActionResult> Reviews(
        [FromQuery] ReviewModerationStatus? status,
        [FromQuery] int? doctorId,
        [FromQuery] int page = 1)
    {
        if (_doctorReviewService == null)
        {
            TempData["ErrorMessage"] = "Doctor review service is not configured.";
            return RedirectToAction(nameof(Index));
        }

        var pagedReviews = await _doctorReviewService.GetAdminReviewQueueAsync(status, doctorId, page, 20);

        var viewModel = new AdminReviewsViewModel
        {
            Reviews = pagedReviews,
            StatusFilter = status,
            DoctorIdFilter = doctorId,
            TotalApprovedReviews = pagedReviews.Items.Count(r => r.ModerationStatus == ReviewModerationStatus.Approved),
            TotalHiddenReviews = pagedReviews.Items.Count(r => r.ModerationStatus == ReviewModerationStatus.Hidden),
            TotalDemoReviews = pagedReviews.Items.Count(r => r.IsDemo),
            TotalVerifiedReviews = pagedReviews.Items.Count(r => r.IsVerifiedAppointment)
        };

        return View(viewModel);
    }

    [HttpPost("/Admin/ModerateReview")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ModerateReview([FromForm] AdminModerateReviewDto dto)
    {
        if (_doctorReviewService == null)
        {
            TempData["ErrorMessage"] = "Doctor review service is not configured.";
            return RedirectToAction(nameof(Reviews));
        }

        var adminUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.Identity?.Name ?? "admin";
        var result = await _doctorReviewService.AdminModerateReviewAsync(dto, adminUserId);

        if (result.IsSuccess)
        {
            TempData["SuccessMessage"] = $"Review moderation status updated to {dto.NewStatus}.";
        }
        else
        {
            TempData["ErrorMessage"] = result.Error ?? "Failed to update review moderation status.";
        }

        return RedirectToAction(nameof(Reviews));
    }

    [HttpPost("/Admin/PurgeDemoReviews")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PurgeDemoReviews()
    {
        if (_doctorReviewService == null)
        {
            TempData["ErrorMessage"] = "Doctor review service is not configured.";
            return RedirectToAction(nameof(Reviews));
        }

        var count = await _doctorReviewService.PurgeDemoReviewsAsync();
        TempData["SuccessMessage"] = $"Successfully purged {count} demo reviews from the database.";
        return RedirectToAction(nameof(Reviews));
    }
}

