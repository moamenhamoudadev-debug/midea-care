using System.Security.Claims;
using MediCare.Services.Contracts;
using MediCare.Services.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCare.Web.Controllers.Api;

[ApiController]
[Route("api/notifications")]
[Authorize]
public class NotificationsApiController : ControllerBase
{
    private readonly INotificationService _notificationService;

    public NotificationsApiController(INotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    /// <summary>
    /// GET /api/notifications/unread
    /// Retrieves unread alerts for current authenticated user.
    /// </summary>
    [HttpGet("unread")]
    public async Task<IActionResult> GetUnread()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        var result = await _notificationService.GetUnreadNotificationsAsync(userId);
        return Ok(new
        {
            success = true,
            data = result.Value ?? new()
        });
    }

    /// <summary>
    /// GET /api/notifications/unread-count
    /// </summary>
    [HttpGet("unread-count")]
    public async Task<IActionResult> GetUnreadCount()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        var result = await _notificationService.GetUnreadCountAsync(userId);
        return Ok(new
        {
            success = true,
            count = result.Value
        });
    }

    /// <summary>
    /// GET /api/notifications/paged
    /// </summary>
    [HttpGet("paged")]
    public async Task<IActionResult> GetPaged([FromQuery] NotificationFilterDto filter)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        var result = await _notificationService.GetUserNotificationsPagedAsync(userId, filter);
        if (!result.IsSuccess)
        {
            return BadRequest(new { success = false, error = result.Error });
        }

        return Ok(new
        {
            success = true,
            data = result.Value
        });
    }

    /// <summary>
    /// POST /api/notifications/mark-read/{id}
    /// Marks an alert as read, validating ownership.
    /// </summary>
    [HttpPost("mark-read/{id:int}")]
    public async Task<IActionResult> MarkRead(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        var result = await _notificationService.MarkAsReadAsync(id, userId);
        if (!result.IsSuccess)
        {
            return StatusCode(403, new
            {
                success = false,
                error = result.Error
            });
        }

        return Ok(new
        {
            success = true,
            data = new
            {
                id,
                isRead = true
            }
        });
    }

    /// <summary>
    /// POST /api/notifications/mark-all-read
    /// </summary>
    [HttpPost("mark-all-read")]
    public async Task<IActionResult> MarkAllRead()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        var result = await _notificationService.MarkAllAsReadAsync(userId);
        if (!result.IsSuccess)
        {
            return BadRequest(new { success = false, error = result.Error });
        }

        return Ok(new
        {
            success = true,
            message = "All notifications marked as read."
        });
    }
}
