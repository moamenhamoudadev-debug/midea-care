using System.Security.Claims;
using FluentAssertions;
using MediCare.Services.Common;
using MediCare.Services.Contracts;
using MediCare.Services.DTOs;
using MediCare.Web.Controllers.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace MediCare.Tests.Controllers;

public class CalendarApiControllerTests
{
    private readonly Mock<ISlotEngineService> _slotEngineMock;
    private readonly Mock<IAppointmentService> _appointmentServiceMock;
    private readonly Mock<IDoctorService> _doctorServiceMock;
    private readonly Mock<ILogger<CalendarApiController>> _loggerMock;
    private readonly CalendarApiController _controller;

    public CalendarApiControllerTests()
    {
        _slotEngineMock = new Mock<ISlotEngineService>();
        _appointmentServiceMock = new Mock<IAppointmentService>();
        _doctorServiceMock = new Mock<IDoctorService>();
        _loggerMock = new Mock<ILogger<CalendarApiController>>();

        _controller = new CalendarApiController(
            _slotEngineMock.Object,
            _appointmentServiceMock.Object,
            _doctorServiceMock.Object,
            _loggerMock.Object);
    }

    private void SetUserContext(string? userId, string role = "Doctor")
    {
        var claims = new List<Claim>();
        if (!string.IsNullOrEmpty(userId))
        {
            claims.Add(new Claim(ClaimTypes.NameIdentifier, userId));
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var identity = new ClaimsIdentity(claims, "TestAuth");
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
        };
    }

    [Theory]
    [InlineData(0, "2026-11-15")]
    [InlineData(-5, "2026-11-15")]
    [InlineData(1, "")]
    [InlineData(1, "   ")]
    public async Task GetSlots_ShouldReturnBadRequest_WhenParametersAreInvalid(int doctorId, string date)
    {
        // Act
        var result = await _controller.GetSlots(doctorId, date);

        // Assert
        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task GetSlots_ShouldReturnBadRequest_WhenDateCannotBeParsed()
    {
        // Act
        var result = await _controller.GetSlots(5, "invalid-date-string");

        // Assert
        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task GetSlots_ShouldReturnNotFound_WhenDoctorDoesNotExist()
    {
        // Arrange
        _doctorServiceMock.Setup(d => d.GetDoctorDetailsAsync(99))
            .ReturnsAsync(Result<DoctorDetailDto>.Failure("Doctor not found"));

        // Act
        var result = await _controller.GetSlots(99, "2026-11-15");

        // Assert
        var notFound = result.Should().BeOfType<NotFoundObjectResult>().Subject;
        notFound.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task GetSlots_ShouldReturnOk_WithAvailableSlots_WhenValidRequest()
    {
        // Arrange
        var doctorDto = new DoctorDetailDto { Id = 3, FullName = "Dr. Layla" };
        _doctorServiceMock.Setup(d => d.GetDoctorDetailsAsync(3))
            .ReturnsAsync(Result<DoctorDetailDto>.Success(doctorDto));

        var slots = new List<SlotDto>
        {
            new()
            {
                StartTime = new TimeSpan(9, 0, 0),
                EndTime = new TimeSpan(9, 30, 0),
                FormattedTime = "09:00 AM - 09:30 AM",
                IsAvailable = true
            },
            new()
            {
                StartTime = new TimeSpan(9, 30, 0),
                EndTime = new TimeSpan(10, 0, 0),
                FormattedTime = "09:30 AM - 10:00 AM",
                IsAvailable = false
            }
        };

        _slotEngineMock.Setup(s => s.GetAvailableSlotsAsync(3, new DateTime(2026, 11, 15)))
            .ReturnsAsync(slots);

        // Act
        var result = await _controller.GetSlots(3, "2026-11-15");

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task GetDoctorEvents_ShouldReturnUnauthorized_WhenUserClaimMissing()
    {
        // Arrange
        SetUserContext(null);

        // Act
        var result = await _controller.GetDoctorEvents("2026-11-01", "2026-11-30");

        // Assert
        var unauthorizedResult = result.Should().BeOfType<UnauthorizedResult>().Subject;
        unauthorizedResult.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task GetDoctorEvents_ShouldReturnForbid_WhenDoctorProfileMissing()
    {
        // Arrange
        SetUserContext("doc_user_without_profile");
        _doctorServiceMock.Setup(d => d.GetDoctorIdByUserIdAsync("doc_user_without_profile"))
            .ReturnsAsync(Result<int>.Failure("Profile not found"));

        // Act
        var result = await _controller.GetDoctorEvents("2026-11-01", "2026-11-30");

        // Assert
        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task GetDoctorEvents_ShouldReturnOk_WhenDoctorIsAuthenticated()
    {
        // Arrange
        SetUserContext("doc_user_1");
        _doctorServiceMock.Setup(d => d.GetDoctorIdByUserIdAsync("doc_user_1"))
            .ReturnsAsync(Result<int>.Success(1));

        var events = new List<CalendarEventDto>
        {
            new()
            {
                Id = "101",
                Title = "Consultation: Patient A",
                Start = "2026-11-15T09:00:00",
                End = "2026-11-15T09:30:00",
                Status = "Confirmed",
                Color = "#198754",
                Url = "/Appointments/Details/101"
            }
        };

        _appointmentServiceMock.Setup(a => a.GetDoctorEventsAsync(1, It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(Result<List<CalendarEventDto>>.Success(events));

        // Act
        var result = await _controller.GetDoctorEvents("2026-11-01", "2026-11-30");

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.StatusCode.Should().Be(200);
    }
}
