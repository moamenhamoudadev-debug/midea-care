using System.Security.Claims;
using FluentAssertions;
using MediCare.Data.Enums;
using MediCare.Services.Common;
using MediCare.Services.Contracts;
using MediCare.Services.DTOs;
using MediCare.Web.Controllers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace MediCare.Tests.Controllers;

public class AppointmentsControllerTests : IDisposable
{
    private readonly Mock<IAppointmentService> _appointmentServiceMock;
    private readonly Mock<IDoctorService> _doctorServiceMock;
    private readonly Mock<IMedicalRecordService> _medicalRecordServiceMock;
    private readonly Mock<INotificationService> _notificationServiceMock;
    private readonly Mock<IWebHostEnvironment> _webHostEnvironmentMock;
    private readonly Mock<ILogger<AppointmentsController>> _loggerMock;
    private readonly Mock<IClinicClock> _clinicClockMock;
    private readonly AppointmentsController _controller;
    private readonly string _testContentRoot;

    public AppointmentsControllerTests()
    {
        _appointmentServiceMock = new Mock<IAppointmentService>();
        _doctorServiceMock = new Mock<IDoctorService>();
        _medicalRecordServiceMock = new Mock<IMedicalRecordService>();
        _notificationServiceMock = new Mock<INotificationService>();
        _webHostEnvironmentMock = new Mock<IWebHostEnvironment>();
        _loggerMock = new Mock<ILogger<AppointmentsController>>();
        _clinicClockMock = new Mock<IClinicClock>();

        _testContentRoot = Path.Combine(Path.GetTempPath(), $"medicare_appt_ctrl_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testContentRoot);
        _webHostEnvironmentMock.Setup(w => w.ContentRootPath).Returns(_testContentRoot);

        _controller = new AppointmentsController(
            _appointmentServiceMock.Object,
            _doctorServiceMock.Object,
            _medicalRecordServiceMock.Object,
            _notificationServiceMock.Object,
            _webHostEnvironmentMock.Object,
            _loggerMock.Object,
            _clinicClockMock.Object);

        var httpContext = new DefaultHttpContext();
        var tempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());
        _controller.TempData = tempData;
    }

    public void Dispose()
    {
        if (Directory.Exists(_testContentRoot))
        {
            try
            {
                Directory.Delete(_testContentRoot, recursive: true);
            }
            catch
            {
                // Ignore cleanup error
            }
        }
    }

    private void SetUserContext(string? userId, string role)
    {
        var claims = new List<Claim>();
        if (!string.IsNullOrEmpty(userId))
        {
            claims.Add(new Claim(ClaimTypes.NameIdentifier, userId));
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var identity = new ClaimsIdentity(claims, string.IsNullOrEmpty(userId) ? null : "TestAuth");
        var httpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };
        _controller.TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());
    }

    // =========================================================================
    // 1. UploadAttachment Security & Validation Tests
    // =========================================================================

    [Fact]
    public async Task UploadAttachment_WhenAnonymous_ReturnsChallenge()
    {
        SetUserContext(null, "");

        var result = await _controller.UploadAttachment(10, null);

        result.Should().BeOfType<ChallengeResult>();
    }

    [Fact]
    public async Task UploadAttachment_WhenFileNullOrEmpty_SetsErrorAndRedirects()
    {
        SetUserContext("patient_user_1", "Patient");

        var result = await _controller.UploadAttachment(10, null);

        result.Should().BeOfType<RedirectToActionResult>()
            .Which.ActionName.Should().Be(nameof(AppointmentsController.Details));
        _controller.TempData["ErrorMessage"].Should().Be("Please select a valid medical file (PDF, PNG, JPG) to upload.");
    }

    [Fact]
    public async Task UploadAttachment_WhenServiceFails_SetsErrorAndRedirects()
    {
        SetUserContext("patient_user_1", "Patient");
        var formFileMock = new Mock<IFormFile>();
        formFileMock.Setup(f => f.Length).Returns(1024);

        _medicalRecordServiceMock.Setup(m => m.UploadPatientAttachmentAsync(10, formFileMock.Object, "patient_user_1", It.IsAny<string>()))
            .ReturnsAsync(Result<string>.Failure("Invalid file signature. File header does not match extension."));

        var result = await _controller.UploadAttachment(10, formFileMock.Object);

        result.Should().BeOfType<RedirectToActionResult>()
            .Which.ActionName.Should().Be(nameof(AppointmentsController.Details));
        _controller.TempData["ErrorMessage"]!.ToString().Should().Contain("Invalid file signature");
    }

    [Fact]
    public async Task UploadAttachment_WhenServiceSucceeds_SetsSuccessMessageAndRedirects()
    {
        SetUserContext("patient_user_1", "Patient");
        var formFileMock = new Mock<IFormFile>();
        formFileMock.Setup(f => f.Length).Returns(2048);

        _medicalRecordServiceMock.Setup(m => m.UploadPatientAttachmentAsync(10, formFileMock.Object, "patient_user_1", It.IsAny<string>()))
            .ReturnsAsync(Result<string>.Success("record_attachment_10.pdf"));

        var result = await _controller.UploadAttachment(10, formFileMock.Object);

        result.Should().BeOfType<RedirectToActionResult>()
            .Which.ActionName.Should().Be(nameof(AppointmentsController.Details));
        _controller.TempData["SuccessMessage"]!.ToString().Should().Contain("uploaded successfully");
    }

    // =========================================================================
    // 2. CheckIn Security IDOR & Role Tests
    // =========================================================================

    [Fact]
    public async Task CheckIn_WhenAnonymous_ReturnsChallenge()
    {
        SetUserContext(null, "");

        var result = await _controller.CheckIn(15);

        result.Should().BeOfType<ChallengeResult>();
    }

    [Fact]
    public async Task CheckIn_WhenAppointmentNotFound_ReturnsNotFound()
    {
        SetUserContext("doc_user_1", "Doctor");
        _appointmentServiceMock.Setup(a => a.GetAppointmentByIdAsync(999))
            .ReturnsAsync(Result<AppointmentSummaryDto>.Failure("Appointment not found."));

        var result = await _controller.CheckIn(999);

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task CheckIn_WhenDoctorDoesNotOwnAppointment_ReturnsForbid_IDORPrevented()
    {
        SetUserContext("doc_user_1", "Doctor");

        var apptDto = new AppointmentSummaryDto
        {
            Id = 15,
            DoctorId = 2, // Owned by Doctor 2
            PatientName = "Ahmed Aly"
        };
        _appointmentServiceMock.Setup(a => a.GetAppointmentByIdAsync(15))
            .ReturnsAsync(Result<AppointmentSummaryDto>.Success(apptDto));
        _appointmentServiceMock.Setup(a => a.CheckInAppointmentAsync(15, "doc_user_1", false))
            .ReturnsAsync(Result.Failure("Forbidden: You do not own this appointment."));

        // doc_user_1 maps to Doctor 1
        _doctorServiceMock.Setup(d => d.GetDoctorIdByUserIdAsync("doc_user_1"))
            .ReturnsAsync(Result<int>.Success(1));

        var result = await _controller.CheckIn(15);

        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task CheckIn_WhenDoctorOwnsAppointment_SucceedsAndRedirects()
    {
        SetUserContext("doc_user_1", "Doctor");

        var apptDto = new AppointmentSummaryDto
        {
            Id = 15,
            DoctorId = 1, // Owned by Doctor 1
            PatientName = "Ahmed Aly",
            QueueNumber = 3
        };
        _appointmentServiceMock.Setup(a => a.GetAppointmentByIdAsync(15))
            .ReturnsAsync(Result<AppointmentSummaryDto>.Success(apptDto));
        _appointmentServiceMock.Setup(a => a.CheckInAppointmentAsync(15, "doc_user_1", false))
            .ReturnsAsync(Result.Success());

        _doctorServiceMock.Setup(d => d.GetDoctorIdByUserIdAsync("doc_user_1"))
            .ReturnsAsync(Result<int>.Success(1));

        var result = await _controller.CheckIn(15);

        result.Should().BeOfType<RedirectToActionResult>()
            .Which.ActionName.Should().Be(nameof(AppointmentsController.Details));
        _controller.TempData["SuccessMessage"]!.ToString().Should().Contain("Ahmed Aly");
    }

    [Fact]
    public async Task CheckIn_WhenAdminUser_CanCheckInAnyDoctorAppointment()
    {
        SetUserContext("admin_user_1", "Admin");

        var apptDto = new AppointmentSummaryDto
        {
            Id = 15,
            DoctorId = 99,
            PatientName = "Ahmed Aly",
            QueueNumber = 5
        };
        _appointmentServiceMock.Setup(a => a.GetAppointmentByIdAsync(15))
            .ReturnsAsync(Result<AppointmentSummaryDto>.Success(apptDto));
        _appointmentServiceMock.Setup(a => a.CheckInAppointmentAsync(15, "admin_user_1", true))
            .ReturnsAsync(Result.Success());

        var result = await _controller.CheckIn(15);

        result.Should().BeOfType<RedirectToActionResult>()
            .Which.ActionName.Should().Be(nameof(AppointmentsController.Details));
        _controller.TempData["SuccessMessage"]!.ToString().Should().Contain("Ahmed Aly");
    }

    // =========================================================================
    // 3. CallNextQueue Tests
    // =========================================================================

    [Fact]
    public async Task CallNextQueue_WhenAnonymous_ReturnsChallenge()
    {
        SetUserContext(null, "");

        var result = await _controller.CallNextQueue(20);

        result.Should().BeOfType<ChallengeResult>();
    }

    [Fact]
    public async Task CallNextQueue_WhenServiceSucceeds_SetsSuccessMessageAndRedirects()
    {
        SetUserContext("doc_user_1", "Doctor");

        _appointmentServiceMock.Setup(a => a.CallNextQueuePatientAsync(20, "doc_user_1", false))
            .ReturnsAsync(Result.Success());

        var result = await _controller.CallNextQueue(20);

        result.Should().BeOfType<RedirectToActionResult>()
            .Which.ActionName.Should().Be(nameof(AppointmentsController.Details));
        _controller.TempData["SuccessMessage"]!.ToString().Should().Contain("تم استدعاء المريض");
    }

    [Fact]
    public async Task CallNextQueue_WhenServiceFails_SetsErrorMessageAndRedirects()
    {
        SetUserContext("doc_user_1", "Doctor");

        _appointmentServiceMock.Setup(a => a.CallNextQueuePatientAsync(20, "doc_user_1", false))
            .ReturnsAsync(Result.Failure("Forbidden: You do not own this appointment."));

        var result = await _controller.CallNextQueue(20);

        result.Should().BeOfType<RedirectToActionResult>()
            .Which.ActionName.Should().Be(nameof(AppointmentsController.Details));
        _controller.TempData["ErrorMessage"]!.ToString().Should().Contain("Forbidden");
    }

    [Fact]
    public void Book_GetAndPostActions_MustBeRestrictedToPatientRole()
    {
        var getMethod = typeof(AppointmentsController).GetMethod(nameof(AppointmentsController.Book));
        getMethod.Should().NotBeNull();
        var getAuth = getMethod!.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), false).FirstOrDefault() as Microsoft.AspNetCore.Authorization.AuthorizeAttribute;
        getAuth.Should().NotBeNull("GET /Appointments/Book/{doctorId} must have [Authorize]");
        getAuth!.Roles.Should().Be("Patient", "Booking an appointment is exclusively reserved for patients");

        var postMethod = typeof(AppointmentsController).GetMethod(nameof(AppointmentsController.BookAppointment));
        postMethod.Should().NotBeNull();
        var postAuth = postMethod!.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), false).FirstOrDefault() as Microsoft.AspNetCore.Authorization.AuthorizeAttribute;
        postAuth.Should().NotBeNull("POST /Appointments/Book must have [Authorize]");
        postAuth!.Roles.Should().Be("Patient", "Booking submission is exclusively reserved for patients");
    }
}
