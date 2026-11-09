using System.Security.Claims;
using FluentAssertions;
using MediCare.Services.Common;
using MediCare.Services.Contracts;
using MediCare.Services.DTOs;
using MediCare.Web.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace MediCare.Tests.Security;

public class AppointmentCheckInSecurityTests
{
    private readonly Mock<IAppointmentService> _apptServiceMock = new();
    private readonly Mock<IDoctorService> _docServiceMock = new();
    private readonly Mock<IMedicalRecordService> _medRecordServiceMock = new();
    private readonly Mock<INotificationService> _notifServiceMock = new();
    private readonly Mock<IWebHostEnvironment> _webHostEnvironmentMock = new();
    private readonly Mock<ILogger<AppointmentsController>> _loggerMock = new();
    private readonly Mock<IClinicClock> _clockMock = new();

    private AppointmentsController CreateController(string userId, string role)
    {
        var controller = new AppointmentsController(
            _apptServiceMock.Object,
            _docServiceMock.Object,
            _medRecordServiceMock.Object,
            _notifServiceMock.Object,
            _webHostEnvironmentMock.Object,
            _loggerMock.Object,
            _clockMock.Object);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Role, role)
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
        };
        controller.TempData = new TempDataDictionary(
            controller.HttpContext,
            Mock.Of<ITempDataProvider>());

        return controller;
    }

    [Fact]
    public async Task CheckIn_WhenDoctorOwnsAppointment_SucceedsAndRedirects()
    {
        // Arrange: Doctor with Id = 5 owns the appointment with Id = 100
        var apptDetails = new AppointmentSummaryDto
        {
            Id = 100,
            DoctorId = 5,
            PatientName = "Karim Tarek",
            QueueNumber = 3,
            Status = Data.Enums.AppointmentStatus.Confirmed
        };

        _apptServiceMock.Setup(s => s.GetAppointmentByIdAsync(100))
            .ReturnsAsync(Result<AppointmentSummaryDto>.Success(apptDetails));

        _apptServiceMock.Setup(s => s.CheckInAppointmentAsync(100, "doctor_5_user", false))
            .ReturnsAsync(Result.Success());

        var controller = CreateController("doctor_5_user", "Doctor");

        // Act
        var result = await controller.CheckIn(100);

        // Assert
        result.Should().BeOfType<RedirectToActionResult>();
        var redirect = (RedirectToActionResult)result;
        redirect.ActionName.Should().Be(nameof(AppointmentsController.Details));
        controller.TempData["SuccessMessage"].Should().NotBeNull();
    }

    [Fact]
    public async Task CheckIn_WhenAdminUser_SucceedsForAnyAppointment()
    {
        // Arrange: Admin user checks in appointment for Doctor 5
        var apptDetails = new AppointmentSummaryDto
        {
            Id = 100,
            DoctorId = 5,
            PatientName = "Karim Tarek",
            QueueNumber = 3,
            Status = Data.Enums.AppointmentStatus.Confirmed
        };

        _apptServiceMock.Setup(s => s.GetAppointmentByIdAsync(100))
            .ReturnsAsync(Result<AppointmentSummaryDto>.Success(apptDetails));

        _apptServiceMock.Setup(s => s.CheckInAppointmentAsync(100, "admin_user_1", true))
            .ReturnsAsync(Result.Success());

        var controller = CreateController("admin_user_1", "Admin");

        // Act
        var result = await controller.CheckIn(100);

        // Assert
        result.Should().BeOfType<RedirectToActionResult>();
        var redirect = (RedirectToActionResult)result;
        redirect.ActionName.Should().Be(nameof(AppointmentsController.Details));
    }

    [Fact]
    public async Task CheckIn_WhenUnrelatedDoctor_ReturnsForbid()
    {
        // Arrange: Doctor with Id = 99 attempts to check in an appointment owned by Doctor Id = 5
        var apptDetails = new AppointmentSummaryDto
        {
            Id = 100,
            DoctorId = 5,
            PatientName = "Karim Tarek",
            QueueNumber = 3
        };

        _apptServiceMock.Setup(s => s.GetAppointmentByIdAsync(100))
            .ReturnsAsync(Result<AppointmentSummaryDto>.Success(apptDetails));

        _apptServiceMock.Setup(s => s.CheckInAppointmentAsync(100, "doctor_99_user", false))
            .ReturnsAsync(Result.Failure("Forbidden: You do not own this appointment."));

        var controller = CreateController("doctor_99_user", "Doctor");

        // Act
        var result = await controller.CheckIn(100);

        // Assert: MUST return ForbidResult
        result.Should().BeOfType<ForbidResult>("A doctor cannot check in an appointment belonging to another doctor");
    }

    [Fact]
    public async Task CheckIn_WhenAppointmentNotFound_ReturnsNotFound()
    {
        // Arrange: Appointment does not exist
        _apptServiceMock.Setup(s => s.GetAppointmentByIdAsync(999))
            .ReturnsAsync(Result<AppointmentSummaryDto>.Failure("Appointment not found."));

        var controller = CreateController("doctor_1_user", "Doctor");

        // Act
        var result = await controller.CheckIn(999);

        // Assert
        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task CheckIn_WhenAlreadyCheckedIn_SetsErrorMessageAndRedirects()
    {
        // Arrange: Appointment already checked in
        var apptDetails = new AppointmentSummaryDto
        {
            Id = 100,
            DoctorId = 5,
            PatientName = "Karim Tarek",
            QueueNumber = 3,
            Status = Data.Enums.AppointmentStatus.Confirmed
        };

        _apptServiceMock.Setup(s => s.GetAppointmentByIdAsync(100))
            .ReturnsAsync(Result<AppointmentSummaryDto>.Success(apptDetails));

        _apptServiceMock.Setup(s => s.CheckInAppointmentAsync(100, "doctor_5_user", false))
            .ReturnsAsync(Result.Failure("Patient has already been checked in for this appointment."));

        var controller = CreateController("doctor_5_user", "Doctor");

        // Act
        var result = await controller.CheckIn(100);

        // Assert
        result.Should().BeOfType<RedirectToActionResult>();
        controller.TempData["ErrorMessage"].Should().Be("Patient has already been checked in for this appointment.");
    }

    [Fact]
    public async Task CheckIn_WhenStatusNotConfirmed_SetsErrorMessageAndRedirects()
    {
        // Arrange: Appointment not confirmed
        var apptDetails = new AppointmentSummaryDto
        {
            Id = 100,
            DoctorId = 5,
            PatientName = "Karim Tarek",
            QueueNumber = 3,
            Status = Data.Enums.AppointmentStatus.Pending
        };

        _apptServiceMock.Setup(s => s.GetAppointmentByIdAsync(100))
            .ReturnsAsync(Result<AppointmentSummaryDto>.Success(apptDetails));

        _apptServiceMock.Setup(s => s.CheckInAppointmentAsync(100, "doctor_5_user", false))
            .ReturnsAsync(Result.Failure("Only confirmed appointments can be checked in."));

        var controller = CreateController("doctor_5_user", "Doctor");

        // Act
        var result = await controller.CheckIn(100);

        // Assert
        result.Should().BeOfType<RedirectToActionResult>();
        controller.TempData["ErrorMessage"].Should().Be("Only confirmed appointments can be checked in.");
    }

    [Fact]
    public void CheckIn_HasStrictAuthorizeAndAntiforgeryAttributes()
    {
        var method = typeof(AppointmentsController).GetMethods()
            .First(m => m.Name == "CheckIn" && m.GetCustomAttributes(typeof(HttpPostAttribute), false).Any());

        var authAttr = method.GetCustomAttributes(typeof(AuthorizeAttribute), false).FirstOrDefault() as AuthorizeAttribute;
        authAttr.Should().NotBeNull("CheckIn POST must have [Authorize]");
        authAttr!.Roles.Should().Contain("Doctor");
        authAttr.Roles.Should().Contain("Admin");

        var antiforgery = method.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), false);
        antiforgery.Should().NotBeEmpty("CheckIn POST must have [ValidateAntiForgeryToken]");
    }
}
