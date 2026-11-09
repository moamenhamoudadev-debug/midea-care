using System.Security.Claims;
using FluentAssertions;
using MediCare.Services.Common;
using MediCare.Services.Contracts;
using MediCare.Services.DTOs;
using MediCare.Web.Controllers.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace MediCare.Tests.Controllers;

public class NotificationsApiControllerTests
{
    private readonly Mock<INotificationService> _notificationServiceMock;
    private readonly NotificationsApiController _controller;

    public NotificationsApiControllerTests()
    {
        _notificationServiceMock = new Mock<INotificationService>();
        _controller = new NotificationsApiController(_notificationServiceMock.Object);
    }

    private void SetUserContext(string? userId)
    {
        var claims = new List<Claim>();
        if (!string.IsNullOrEmpty(userId))
        {
            claims.Add(new Claim(ClaimTypes.NameIdentifier, userId));
            claims.Add(new Claim(ClaimTypes.Role, "Patient"));
        }

        var identity = new ClaimsIdentity(claims, "TestAuth");
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
        };
    }

    [Fact]
    public async Task GetUnread_ShouldReturnUnauthorized_WhenUserClaimMissing()
    {
        // Arrange
        SetUserContext(null);

        // Act
        var result = await _controller.GetUnread();

        // Assert
        var unauthorizedResult = result.Should().BeOfType<UnauthorizedResult>().Subject;
        unauthorizedResult.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task GetUnread_ShouldReturnOk_WhenUserAuthenticated()
    {
        // Arrange
        SetUserContext("user_abc");
        var notifications = new List<NotificationDto>
        {
            new()
            {
                Id = 1,
                Title = "Appointment Confirmed",
                Message = "Your visit has been approved.",
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            }
        };

        _notificationServiceMock.Setup(n => n.GetUnreadNotificationsAsync("user_abc"))
            .ReturnsAsync(Result<List<NotificationDto>>.Success(notifications));

        // Act
        var result = await _controller.GetUnread();

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task MarkRead_ShouldReturnUnauthorized_WhenUserClaimMissing()
    {
        // Arrange
        SetUserContext(null);

        // Act
        var result = await _controller.MarkRead(5);

        // Assert
        var unauthorizedResult = result.Should().BeOfType<UnauthorizedResult>().Subject;
        unauthorizedResult.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task MarkRead_ShouldReturnForbidden_WhenOwnershipFails()
    {
        // Arrange
        SetUserContext("attacker_user");
        _notificationServiceMock.Setup(n => n.MarkAsReadAsync(10, "attacker_user"))
            .ReturnsAsync(Result.Failure("Forbidden: You do not own this notification."));

        // Act
        var result = await _controller.MarkRead(10);

        // Assert
        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task MarkRead_ShouldReturnOk_WhenOwnershipVerified()
    {
        // Arrange
        SetUserContext("owner_user");
        _notificationServiceMock.Setup(n => n.MarkAsReadAsync(10, "owner_user"))
            .ReturnsAsync(Result.Success());

        // Act
        var result = await _controller.MarkRead(10);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.StatusCode.Should().Be(200);
    }
}
