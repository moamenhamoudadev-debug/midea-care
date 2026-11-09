using System.Security.Claims;
using MediCare.Services.Common;
using MediCare.Services.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCare.Web.Controllers.Api;

[ApiController]
[Route("api/calendar")]
[Authorize]
public class CalendarApiController : ControllerBase
{
    private readonly ISlotEngineService _slotEngine;
    private readonly IAppointmentService _appointmentService;
    private readonly IDoctorService _doctorService;
    private readonly ILogger<CalendarApiController> _logger;
    private readonly IClinicClock _clinicClock;

    public CalendarApiController(
        ISlotEngineService slotEngine,
        IAppointmentService appointmentService,
        IDoctorService doctorService,
        ILogger<CalendarApiController> logger,
        IClinicClock? clinicClock = null)
    {
        _slotEngine = slotEngine;
        _appointmentService = appointmentService;
        _doctorService = doctorService;
        _logger = logger;
        _clinicClock = clinicClock ?? new ClinicClock();
    }

    /// <summary>
    /// GET /api/calendar/slots?doctorId=5&date=2026-11-15
    /// Computes available 30-minute intervals for doctor.
    /// </summary>
    [HttpGet("slots")]
    [AllowAnonymous]
    public async Task<IActionResult> GetSlots([FromQuery] int doctorId, [FromQuery] string date)
    {
        if (doctorId <= 0 || string.IsNullOrWhiteSpace(date))
        {
            return BadRequest(new
            {
                success = false,
                error = "Invalid query parameters.",
                errors = new[] { "Both doctorId and date parameters are required." }
            });
        }

        if (!DateTime.TryParse(date, out var targetDate))
        {
            return BadRequest(new
            {
                success = false,
                error = "Invalid query parameters.",
                errors = new[] { "The date parameter must be a valid ISO 8601 date string (YYYY-MM-DD)." }
            });
        }

        var doctorResult = await _doctorService.GetDoctorDetailsAsync(doctorId);
        if (!doctorResult.IsSuccess || doctorResult.Value == null)
        {
            return NotFound(new
            {
                success = false,
                error = "Doctor not found or doctor account is pending approval."
            });
        }

        var slots = await _slotEngine.GetAvailableSlotsAsync(doctorId, targetDate);

        var data = slots.Select(s => new
        {
            startTime = s.StartTime.ToString(@"hh\:mm\:ss"),
            endTime = s.EndTime.ToString(@"hh\:mm\:ss"),
            formattedTime = s.FormattedTime,
            isAvailable = s.IsAvailable
        });

        return Ok(new
        {
            success = true,
            data
        });
    }

    /// <summary>
    /// GET /api/calendar/doctor-events?start=...&end=...
    /// Scheduled events for the authenticated doctor's FullCalendar.
    /// </summary>
    [HttpGet("doctor-events")]
    [Authorize(Roles = "Doctor")]
    public async Task<IActionResult> GetDoctorEvents([FromQuery] string start, [FromQuery] string end)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        var doctorIdResult = await _doctorService.GetDoctorIdByUserIdAsync(userId);
        if (!doctorIdResult.IsSuccess)
        {
            return Forbid();
        }

        DateTime startDate = DateTime.TryParse(start, out var s) ? s : _clinicClock.Today.AddDays(-30);
        DateTime endDate = DateTime.TryParse(end, out var e) ? e : _clinicClock.Today.AddDays(30);

        var eventsResult = await _appointmentService.GetDoctorEventsAsync(doctorIdResult.Value, startDate, endDate);

        return Ok(new
        {
            success = true,
            data = eventsResult.Value ?? new()
        });
    }
}
