using FluentAssertions;
using MediCare.Services.Common;
using MediCare.Services.Contracts;
using MediCare.Services.DTOs;
using MediCare.Web.Controllers.Api;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace MediCare.Tests.Controllers;

public class AppointmentsApiControllerTests
{
    private readonly Mock<IAppointmentService> _appointmentServiceMock;
    private readonly AppointmentsApiController _controller;

    public AppointmentsApiControllerTests()
    {
        _appointmentServiceMock = new Mock<IAppointmentService>();
        _controller = new AppointmentsApiController(_appointmentServiceMock.Object);
    }

    [Theory]
    [InlineData(0, "2026-11-15", "10:00:00")]
    [InlineData(-1, "2026-11-15", "10:00:00")]
    [InlineData(1, "invalid-date", "10:00:00")]
    [InlineData(1, "2026-11-15", "invalid-time")]
    public async Task CheckConflict_ShouldReturnBadRequest_WhenPayloadIsInvalid(int doctorId, string date, string time)
    {
        // Arrange
        var request = new AppointmentsApiController.CheckConflictRequest
        {
            DoctorId = doctorId,
            AppointmentDate = date,
            StartTime = time
        };

        // Act
        var result = await _controller.CheckConflict(request);

        // Assert
        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task CheckConflict_ShouldReturnBadRequest_WhenServiceFails()
    {
        // Arrange
        var request = new AppointmentsApiController.CheckConflictRequest
        {
            DoctorId = 1,
            AppointmentDate = "2026-11-15",
            StartTime = "10:00:00"
        };

        _appointmentServiceMock.Setup(s => s.CheckConflictAsync(1, new DateTime(2026, 11, 15), new TimeSpan(10, 0, 0)))
            .ReturnsAsync(Result<ConflictCheckResponseDto>.Failure("Database connectivity issue"));

        // Act
        var result = await _controller.CheckConflict(request);

        // Assert
        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task CheckConflict_ShouldReturnConflict_WhenSlotIsAlreadyBooked()
    {
        // Arrange
        var request = new AppointmentsApiController.CheckConflictRequest
        {
            DoctorId = 1,
            AppointmentDate = "2026-11-15",
            StartTime = "10:00:00"
        };

        var conflictDto = new ConflictCheckResponseDto
        {
            HasConflict = true,
            Message = "The selected time slot is already booked."
        };

        _appointmentServiceMock.Setup(s => s.CheckConflictAsync(1, new DateTime(2026, 11, 15), new TimeSpan(10, 0, 0)))
            .ReturnsAsync(Result<ConflictCheckResponseDto>.Success(conflictDto));

        // Act
        var result = await _controller.CheckConflict(request);

        // Assert
        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task CheckConflict_ShouldReturnOk_WhenSlotIsAvailable()
    {
        // Arrange
        var request = new AppointmentsApiController.CheckConflictRequest
        {
            DoctorId = 1,
            AppointmentDate = "2026-11-15",
            StartTime = "10:00:00"
        };

        var availableDto = new ConflictCheckResponseDto
        {
            HasConflict = false,
            Message = "Slot is available."
        };

        _appointmentServiceMock.Setup(s => s.CheckConflictAsync(1, new DateTime(2026, 11, 15), new TimeSpan(10, 0, 0)))
            .ReturnsAsync(Result<ConflictCheckResponseDto>.Success(availableDto));

        // Act
        var result = await _controller.CheckConflict(request);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.StatusCode.Should().Be(200);
    }
}
