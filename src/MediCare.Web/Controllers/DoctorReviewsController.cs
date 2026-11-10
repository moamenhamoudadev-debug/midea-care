using System;
using System.Security.Claims;
using System.Threading.Tasks;
using MediCare.Data.Enums;
using MediCare.Services.Contracts;
using MediCare.Services.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace MediCare.Web.Controllers;

[Authorize]
public class DoctorReviewsController : Controller
{
    private readonly IDoctorReviewService _reviewService;
    private readonly ILogger<DoctorReviewsController> _logger;

    public DoctorReviewsController(
        IDoctorReviewService reviewService,
        ILogger<DoctorReviewsController> logger)
    {
        _reviewService = reviewService;
        _logger = logger;
    }

    [HttpPost("/DoctorReviews/Submit")]
    [Authorize(Roles = "Patient")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit([FromForm] SubmitDoctorReviewDto dto)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Unauthorized();
        }

        var result = await _reviewService.SubmitReviewAsync(dto, currentUserId);

        var isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest" ||
                     Request.Headers["Accept"].ToString().Contains("application/json");

        if (!result.IsSuccess)
        {
            if (isAjax)
            {
                return Json(new { success = false, message = result.Error });
            }

            TempData["ErrorMessage"] = result.Error;
            return RedirectToAction("Details", "Doctors", new { id = dto.DoctorId });
        }

        if (isAjax)
        {
            return Json(new
            {
                success = true,
                message = "Review submitted successfully! Thank you for sharing your experience.",
                review = result.Value
            });
        }

        TempData["SuccessMessage"] = "Review submitted successfully! Thank you for sharing your feedback.";
        return Redirect($"/Doctors/Details/{dto.DoctorId}#reviews-section");
    }

    [HttpPost("/DoctorReviews/Helpful/{reviewId:int}")]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Helpful(int reviewId)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Json(new { success = false, message = "You must be signed in to mark a review as helpful." });
        }

        var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString();
        var result = await _reviewService.ToggleHelpfulVoteAsync(reviewId, currentUserId, clientIp);

        if (!result.IsSuccess)
        {
            return Json(new { success = false, message = result.Error });
        }

        return Json(new
        {
            success = true,
            helpfulCount = result.Value!.HelpfulCount,
            hasVoted = result.Value.HasVoted,
            message = result.Value.Message
        });
    }

    [HttpGet("/DoctorReviews/ListPartial/{doctorId:int}")]
    [AllowAnonymous]
    public async Task<IActionResult> ListPartial(
        int doctorId,
        [FromQuery] int? rating,
        [FromQuery] string? sort,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var hub = await _reviewService.GetDoctorReviewsHubAsync(
            doctorId,
            currentUserId,
            rating,
            sort ?? "recent",
            page,
            pageSize);

        return PartialView("_DoctorReviewsHubPartial", hub);
    }

    [HttpPost("/DoctorReviews/DoctorReply")]
    [Authorize(Roles = "Doctor")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DoctorReply([FromForm] DoctorReplyReviewDto dto)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Unauthorized();
        }

        var result = await _reviewService.DoctorReplyAsync(dto, currentUserId);

        var isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest" ||
                     Request.Headers["Accept"].ToString().Contains("application/json");

        if (!result.IsSuccess)
        {
            if (isAjax)
            {
                return Json(new { success = false, message = result.Error });
            }

            TempData["ErrorMessage"] = result.Error;
            return RedirectToAction("Reviews", "Doctor");
        }

        if (isAjax)
        {
            return Json(new
            {
                success = true,
                message = "Response submitted successfully.",
                review = result.Value
            });
        }

        TempData["SuccessMessage"] = "Your response has been published successfully.";
        return RedirectToAction("Reviews", "Doctor");
    }
}
