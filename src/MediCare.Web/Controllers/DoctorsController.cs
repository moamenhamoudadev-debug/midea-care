using System.Security.Claims;
using System.Threading.Tasks;
using MediCare.Services.Contracts;
using MediCare.Services.DTOs;
using MediCare.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCare.Web.Controllers;

[Authorize]
public class DoctorsController : Controller
{
    private readonly IDoctorService _doctorService;
    private readonly IDoctorReviewService _doctorReviewService;

    public DoctorsController(
        IDoctorService doctorService,
        IDoctorReviewService doctorReviewService)
    {
        _doctorService = doctorService;
        _doctorReviewService = doctorReviewService;
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Index([FromQuery] DoctorFilterViewModel filter)
    {
        var filterDto = new DoctorFilterDto
        {
            SpecializationId = filter.SpecializationId,
            MaxFee = filter.MaxFee,
            AvailableDay = filter.AvailableDay,
            SearchTerm = filter.SearchTerm,
            Governorate = filter.Governorate,
            Page = filter.Page < 1 ? 1 : filter.Page,
            PageSize = 6
        };

        var pagedDoctors = await _doctorService.SearchDoctorsAsync(filterDto);
        var specializations = await _doctorService.GetSpecializationsAsync();

        var viewModel = new DoctorListViewModel
        {
            Filter = filter,
            Doctors = pagedDoctors,
            Specializations = specializations
        };

        return View(viewModel);
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Details(
        int id,
        [FromQuery] int? rating,
        [FromQuery] string? sort,
        [FromQuery] int page = 1)
    {
        var result = await _doctorService.GetDoctorDetailsAsync(id);
        if (!result.IsSuccess || result.Value == null)
        {
            TempData["ErrorMessage"] = "Doctor profile not found or is currently not approved.";
            return RedirectToAction(nameof(Index));
        }

        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var hub = await _doctorReviewService.GetDoctorReviewsHubAsync(
            id,
            currentUserId,
            rating,
            sort ?? "recent",
            page,
            10);

        ViewBag.ReviewsHub = hub;

        return View(result.Value);
    }
}
