using System.Security.Claims;
using FluentAssertions;
using MediCare.Data.Entities;
using MediCare.Data.Enums;
using MediCare.Services.Common;
using MediCare.Services.Contracts;
using MediCare.Services.DTOs;
using MediCare.Services.Factories;
using MediCare.Web.Controllers;
using MediCare.Web.ViewModels;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace MediCare.Tests.Security;

/// <summary>
/// SEC-08 Security Audit Tests: Telehealth Room Security.
/// Validates cryptographically random room names and server-side type, status, and time-window guards.
/// </summary>
public class TelehealthRoomSecurityTests
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
        _webHostEnvironmentMock.SetupGet(e => e.EnvironmentName).Returns("Development");
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

    private static AppointmentSummaryDto CreateValidTelehealthAppt(int id = 100, int doctorId = 5, int patientId = 10)
    {
        return new AppointmentSummaryDto
        {
            Id = id,
            DoctorId = doctorId,
            DoctorName = "Dr. Ahmed Mostafa",
            PatientId = patientId,
            PatientName = "Ali Mahmoud",
            AppointmentDate = new DateTime(2026, 10, 20),
            StartTime = new TimeSpan(10, 0, 0),
            EndTime = new TimeSpan(10, 30, 0),
            Type = AppointmentType.Telemedicine,
            Status = AppointmentStatus.Confirmed,
            ConsultationFee = 350m,
            PaymentStatus = PaymentStatus.Paid
        };
    }

    [Fact]
    public void AppointmentFactory_Generates16ByteHexSecret_ForTelemedicine_AndNullForOthers()
    {
        var factory = new AppointmentFactory();

        var telehealthDto = new BookingRequestDto
        {
            DoctorId = 1,
            PatientId = 2,
            AppointmentDate = DateTime.Today.AddDays(1),
            StartTime = new TimeSpan(14, 0, 0),
            Type = AppointmentType.Telemedicine
        };

        var consultationDto = new BookingRequestDto
        {
            DoctorId = 1,
            PatientId = 2,
            AppointmentDate = DateTime.Today.AddDays(1),
            StartTime = new TimeSpan(14, 0, 0),
            Type = AppointmentType.Consultation
        };

        var appt1 = factory.Create(telehealthDto, 300m);
        var appt2 = factory.Create(consultationDto, 300m);

        appt1.TelehealthRoomSecret.Should().NotBeNullOrWhiteSpace();
        appt1.TelehealthRoomSecret!.Length.Should().Be(32, "16 cryptographically random bytes encoded as lowercase hex must be 32 characters");
        appt1.TelehealthRoomSecret.Should().MatchRegex("^[0-9a-f]{32}$");

        appt2.TelehealthRoomSecret.Should().BeNull("in-person appointments must not allocate a telehealth room secret");
    }

    [Fact]
    public void RoomName_IsMediCarePlusSecret_AndNeverContainsAppointmentOrDoctorId()
    {
        var secret = AppointmentFactory.GenerateTelehealthRoomSecret();
        var dto = new AppointmentSummaryDto
        {
            Id = 98765,
            DoctorId = 43210,
            TelehealthRoomSecret = secret
        };

        dto.TeleconsultationRoomName.Should().Be($"MediCare-{secret}");
        dto.TeleconsultationMeetingUrl.Should().Be($"https://meet.jit.si/MediCare-{secret}");

        // SEC-08: Absence of predictable appointment or doctor ID in room name
        dto.TeleconsultationRoomName.Should().NotContain("98765");
        dto.TeleconsultationRoomName.Should().NotContain("43210");
        dto.TeleconsultationRoomName.Should().NotContain("Appt");
        dto.TeleconsultationRoomName.Should().NotContain("-D");

        // When secret is null, room name and meeting URL must be empty
        dto.TelehealthRoomSecret = null;
        dto.TeleconsultationRoomName.Should().BeEmpty();
        dto.TeleconsultationMeetingUrl.Should().BeEmpty();
    }

    [Fact]
    public void AppointmentFactory_GeneratesGloballyUniqueCryptographicSecrets()
    {
        var secrets = new HashSet<string>();
        for (int i = 0; i < 200; i++)
        {
            var secret = AppointmentFactory.GenerateTelehealthRoomSecret();
            secrets.Add(secret).Should().BeTrue("each generated secret must be cryptographically unique");
        }
        secrets.Count.Should().Be(200);
    }

    [Fact]
    public async Task Telehealth_UnauthorizedUser_ReturnsForbid()
    {
        var appt = CreateValidTelehealthAppt();
        _apptServiceMock.Setup(s => s.GetAppointmentByIdAsync(100))
            .ReturnsAsync(Result<AppointmentSummaryDto>.Success(appt));

        // Attacker is patient 999 (not patient 10)
        _apptServiceMock.Setup(s => s.GetPatientIdByUserIdAsync("attacker_patient_user"))
            .ReturnsAsync(Result<int>.Success(999));

        var controller = CreateController("attacker_patient_user", "Patient");

        var result = await controller.Telehealth(100);

        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task Telehealth_NonTelehealthAppointment_ReturnsBadRequestWithFriendlyMessage()
    {
        var inPersonAppt = CreateValidTelehealthAppt();
        inPersonAppt.Type = AppointmentType.Consultation;

        _apptServiceMock.Setup(s => s.GetAppointmentByIdAsync(100))
            .ReturnsAsync(Result<AppointmentSummaryDto>.Success(inPersonAppt));
        _apptServiceMock.Setup(s => s.GetPatientIdByUserIdAsync("patient_10_user"))
            .ReturnsAsync(Result<int>.Success(10));

        var controller = CreateController("patient_10_user", "Patient");

        var result = await controller.Telehealth(100);

        var viewResult = result.Should().BeOfType<ViewResult>().Subject;
        viewResult.ViewName.Should().Be("TelehealthUnavailable");
        controller.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);

        var model = viewResult.Model.Should().BeOfType<TelehealthUnavailableViewModel>().Subject;
        model.Title.Should().Be("In-Person Consultation");
        model.Message.Should().Contain("in-person");
    }

    [Theory]
    [InlineData(AppointmentStatus.Pending)]
    [InlineData(AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.Completed)]
    [InlineData(AppointmentStatus.Rejected)]
    public async Task Telehealth_AppointmentNotConfirmed_ReturnsBadRequestWithFriendlyMessage(AppointmentStatus nonConfirmedStatus)
    {
        var appt = CreateValidTelehealthAppt();
        appt.Status = nonConfirmedStatus;

        _apptServiceMock.Setup(s => s.GetAppointmentByIdAsync(100))
            .ReturnsAsync(Result<AppointmentSummaryDto>.Success(appt));
        _apptServiceMock.Setup(s => s.GetPatientIdByUserIdAsync("patient_10_user"))
            .ReturnsAsync(Result<int>.Success(10));

        var controller = CreateController("patient_10_user", "Patient");

        var result = await controller.Telehealth(100);

        var viewResult = result.Should().BeOfType<ViewResult>().Subject;
        viewResult.ViewName.Should().Be("TelehealthUnavailable");
        controller.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);

        var model = viewResult.Model.Should().BeOfType<TelehealthUnavailableViewModel>().Subject;
        model.Message.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Telehealth_TooEarlyBeforeWindow_ReturnsForbiddenWithFriendlyMessage()
    {
        var appt = CreateValidTelehealthAppt();
        // Appt: 2026-10-20 10:00 - 10:30. Window opens at 09:45 (start - 15m).
        // Current time: 09:44 (16 minutes before start -> too early).
        var apptDate = appt.AppointmentDate.Date;
        var clockNow = apptDate.AddHours(9).AddMinutes(44);
        _clockMock.Setup(c => c.Now).Returns(clockNow);

        _apptServiceMock.Setup(s => s.GetAppointmentByIdAsync(100))
            .ReturnsAsync(Result<AppointmentSummaryDto>.Success(appt));
        _apptServiceMock.Setup(s => s.GetPatientIdByUserIdAsync("patient_10_user"))
            .ReturnsAsync(Result<int>.Success(10));

        var controller = CreateController("patient_10_user", "Patient");

        var result = await controller.Telehealth(100);

        var viewResult = result.Should().BeOfType<ViewResult>().Subject;
        viewResult.ViewName.Should().Be("TelehealthUnavailable");
        controller.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);

        var model = viewResult.Model.Should().BeOfType<TelehealthUnavailableViewModel>().Subject;
        model.Title.Should().Be("Consultation Room Not Yet Open");
        model.Message.Should().Contain("09:45 AM");
        model.WindowOpensAt.Should().Be(apptDate.AddHours(9).AddMinutes(45));
    }

    [Fact]
    public async Task Telehealth_TooLateAfterWindow_ReturnsForbiddenWithFriendlyMessage()
    {
        var appt = CreateValidTelehealthAppt();
        // Appt: 2026-10-20 10:00 - 10:30. Window closes at 11:00 (end + 30m).
        // Current time: 11:01 (31 minutes after end -> expired).
        var apptDate = appt.AppointmentDate.Date;
        var clockNow = apptDate.AddHours(11).AddMinutes(1);
        _clockMock.Setup(c => c.Now).Returns(clockNow);

        _apptServiceMock.Setup(s => s.GetAppointmentByIdAsync(100))
            .ReturnsAsync(Result<AppointmentSummaryDto>.Success(appt));
        _apptServiceMock.Setup(s => s.GetPatientIdByUserIdAsync("patient_10_user"))
            .ReturnsAsync(Result<int>.Success(10));

        var controller = CreateController("patient_10_user", "Patient");

        var result = await controller.Telehealth(100);

        var viewResult = result.Should().BeOfType<ViewResult>().Subject;
        viewResult.ViewName.Should().Be("TelehealthUnavailable");
        controller.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);

        var model = viewResult.Model.Should().BeOfType<TelehealthUnavailableViewModel>().Subject;
        model.Title.Should().Be("Consultation Window Closed");
        model.Message.Should().Contain("closed");
        model.WindowClosesAt.Should().Be(apptDate.AddHours(11).AddMinutes(0));
    }

    [Fact]
    public async Task Telehealth_InsideWindow_AtWindowOpen_ReturnsViewWithRoomSecret()
    {
        var appt = CreateValidTelehealthAppt();
        // Appt: 2026-10-20 10:00 - 10:30. Window opens at 09:45.
        // Current time: exactly 09:45.
        var apptDate = appt.AppointmentDate.Date;
        var clockNow = apptDate.AddHours(9).AddMinutes(45);
        _clockMock.Setup(c => c.Now).Returns(clockNow);

        var secret = "abcdef0123456789abcdef0123456789";
        _apptServiceMock.Setup(s => s.GetAppointmentByIdAsync(100))
            .ReturnsAsync(Result<AppointmentSummaryDto>.Success(appt));
        _apptServiceMock.Setup(s => s.GetPatientIdByUserIdAsync("patient_10_user"))
            .ReturnsAsync(Result<int>.Success(10));
        _apptServiceMock.Setup(s => s.EnsureTelehealthRoomSecretAsync(100))
            .ReturnsAsync(Result<string>.Success(secret));

        var controller = CreateController("patient_10_user", "Patient");

        var result = await controller.Telehealth(100);

        var viewResult = result.Should().BeOfType<ViewResult>().Subject;
        var model = viewResult.Model.Should().BeOfType<AppointmentSummaryDto>().Subject;
        model.TelehealthRoomSecret.Should().Be(secret);
        model.TeleconsultationRoomName.Should().Be($"MediCare-{secret}");
        model.TeleconsultationMeetingUrl.Should().Be($"https://meet.jit.si/MediCare-{secret}");
    }

    [Fact]
    public async Task Telehealth_InsideWindow_AtWindowClose_ReturnsViewWithRoomSecret()
    {
        var appt = CreateValidTelehealthAppt();
        // Appt: 2026-10-20 10:00 - 10:30. Window closes at 11:00.
        // Current time: exactly 11:00.
        var apptDate = appt.AppointmentDate.Date;
        var clockNow = apptDate.AddHours(11).AddMinutes(0);
        _clockMock.Setup(c => c.Now).Returns(clockNow);

        var secret = "11223344556677889900aabbccddeeff";
        _apptServiceMock.Setup(s => s.GetAppointmentByIdAsync(100))
            .ReturnsAsync(Result<AppointmentSummaryDto>.Success(appt));
        _apptServiceMock.Setup(s => s.GetPatientIdByUserIdAsync("patient_10_user"))
            .ReturnsAsync(Result<int>.Success(10));
        _apptServiceMock.Setup(s => s.EnsureTelehealthRoomSecretAsync(100))
            .ReturnsAsync(Result<string>.Success(secret));

        var controller = CreateController("patient_10_user", "Patient");

        var result = await controller.Telehealth(100);

        var viewResult = result.Should().BeOfType<ViewResult>().Subject;
        var model = viewResult.Model.Should().BeOfType<AppointmentSummaryDto>().Subject;
        model.TelehealthRoomSecret.Should().Be(secret);
    }

    [Fact]
    public async Task Telehealth_OutsideDevelopment_DoesNotCreateOrExposePublicJitsiRoom()
    {
        var appt = CreateValidTelehealthAppt();
        _webHostEnvironmentMock.SetupGet(e => e.EnvironmentName).Returns("Production");
        _apptServiceMock.Setup(s => s.GetAppointmentByIdAsync(100))
            .ReturnsAsync(Result<AppointmentSummaryDto>.Success(appt));
        _docServiceMock.Setup(s => s.GetDoctorIdByUserIdAsync("doctor-user-1"))
            .ReturnsAsync(Result<int>.Success(5));
        _clockMock.Setup(c => c.Now).Returns(appt.AppointmentDate.Date.AddHours(9).AddMinutes(45));

        var controller = CreateController("doctor-user-1", "Doctor");
        _webHostEnvironmentMock.SetupGet(e => e.EnvironmentName).Returns("Production");

        var result = await controller.Telehealth(100);

        var viewResult = result.Should().BeOfType<ViewResult>().Subject;
        var model = viewResult.Model.Should().BeOfType<AppointmentSummaryDto>().Subject;
        model.TelehealthRoomSecret.Should().BeNull();
        ((bool)controller.ViewBag.PublicJitsiDemoEnabled).Should().BeFalse();
        _apptServiceMock.Verify(s => s.EnsureTelehealthRoomSecretAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task Telehealth_BothDoctorAndPatient_CanAccessInsideWindow_WithIdenticalSecret()
    {
        var appt = CreateValidTelehealthAppt();
        // Current time: 10:15 (during the slot).
        var apptDate = appt.AppointmentDate.Date;
        var clockNow = apptDate.AddHours(10).AddMinutes(15);
        _clockMock.Setup(c => c.Now).Returns(clockNow);

        var sharedSecret = "fedcba9876543210fedcba9876543210";
        _apptServiceMock.Setup(s => s.GetAppointmentByIdAsync(100))
            .ReturnsAsync(Result<AppointmentSummaryDto>.Success(appt));
        _apptServiceMock.Setup(s => s.EnsureTelehealthRoomSecretAsync(100))
            .ReturnsAsync(Result<string>.Success(sharedSecret));

        // 1. Patient Access
        _apptServiceMock.Setup(s => s.GetPatientIdByUserIdAsync("patient_10_user"))
            .ReturnsAsync(Result<int>.Success(10));
        var patientController = CreateController("patient_10_user", "Patient");
        var patientResult = await patientController.Telehealth(100);
        var patientView = patientResult.Should().BeOfType<ViewResult>().Subject;
        var patientModel = patientView.Model.Should().BeOfType<AppointmentSummaryDto>().Subject;

        // 2. Doctor Access
        _docServiceMock.Setup(s => s.GetDoctorIdByUserIdAsync("doctor_5_user"))
            .ReturnsAsync(Result<int>.Success(5));
        var doctorController = CreateController("doctor_5_user", "Doctor");
        var doctorResult = await doctorController.Telehealth(100);
        var doctorView = doctorResult.Should().BeOfType<ViewResult>().Subject;
        var doctorModel = doctorView.Model.Should().BeOfType<AppointmentSummaryDto>().Subject;

        // Both join the exact same secret room
        patientModel.TelehealthRoomSecret.Should().Be(sharedSecret);
        doctorModel.TelehealthRoomSecret.Should().Be(sharedSecret);
        patientModel.TeleconsultationMeetingUrl.Should().Be(doctorModel.TeleconsultationMeetingUrl);
    }

    [Fact]
    public async Task Telehealth_AdminUser_ReturnsForbid_ToPreserveMedicalConfidentiality()
    {
        // SEC-12: Admins must be forbidden from joining private clinical telehealth consultations
        var appt = CreateValidTelehealthAppt();
        var apptDate = appt.AppointmentDate.Date;
        var clockNow = apptDate.AddHours(10).AddMinutes(15);
        _clockMock.Setup(c => c.Now).Returns(clockNow);

        _apptServiceMock.Setup(s => s.GetAppointmentByIdAsync(100))
            .ReturnsAsync(Result<AppointmentSummaryDto>.Success(appt));

        var adminController = CreateController("admin_user_1", "Admin");

        var result = await adminController.Telehealth(100);

        result.Should().BeOfType<ForbidResult>("administrators must never be permitted into patient-doctor telehealth rooms");
    }

    [Fact]
    public void AppointmentSummaryDto_ListQueries_LeaveSecretNullAndMeetingUrlEmpty()
    {
        // Leak audit: Verify that summary DTOs returned in list queries don't leak room secret or Jitsi URL
        var dto = new AppointmentSummaryDto
        {
            Id = 100,
            DoctorId = 5,
            PatientId = 10,
            Type = AppointmentType.Telemedicine,
            Status = AppointmentStatus.Confirmed,
            TelehealthRoomSecret = null
        };

        dto.TelehealthRoomSecret.Should().BeNull();
        dto.TeleconsultationRoomName.Should().BeEmpty();
        dto.TeleconsultationMeetingUrl.Should().BeEmpty();
        dto.TelehealthRoomName.Should().BeEmpty();
        dto.TelehealthUrl.Should().BeEmpty();
    }
}
