using MediCare.Services.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCare.Web.Controllers.Api;

[ApiController]
[Route("api/appointments")]
[Authorize]
public class AppointmentsApiController : ControllerBase
{
    private readonly IAppointmentService _appointmentService;

    public AppointmentsApiController(IAppointmentService appointmentService)
    {
        _appointmentService = appointmentService;
    }

    public class CheckConflictRequest
    {
        public int DoctorId { get; set; }
        public string AppointmentDate { get; set; } = string.Empty;
        public string StartTime { get; set; } = string.Empty;
    }

    /// <summary>
    /// POST /api/appointments/check-conflict
    /// Validates whether a candidate slot is currently free prior to booking modal.
    /// </summary>
    [HttpPost("check-conflict")]
    [Authorize(Roles = "Patient,Admin")]
    public async Task<IActionResult> CheckConflict([FromBody] CheckConflictRequest request)
    {
        if (request.DoctorId <= 0 ||
            !DateTime.TryParse(request.AppointmentDate, out var date) ||
            !TimeSpan.TryParse(request.StartTime, out var startTime))
        {
            return BadRequest(new
            {
                success = false,
                error = "Invalid conflict check payload."
            });
        }

        var result = await _appointmentService.CheckConflictAsync(request.DoctorId, date, startTime);
        if (!result.IsSuccess)
        {
            return BadRequest(new
            {
                success = false,
                error = result.Error
            });
        }

        if (result.Value != null && result.Value.HasConflict)
        {
            return StatusCode(409, new
            {
                success = false,
                error = "The selected time slot is already booked. Please choose an alternative time."
            });
        }

        return Ok(new
        {
            success = true,
            data = new
            {
                hasConflict = false,
                message = "Slot is available."
            }
        });
    }
}
