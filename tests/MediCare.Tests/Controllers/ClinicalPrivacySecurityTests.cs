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
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace MediCare.Tests.Controllers;

public class ClinicalPrivacySecurityTests
{
    private static void SetUser(ControllerBase controller, string userId, string role)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Role, role)
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = principal }
        };
    }

    [Fact]
    public async Task MedicalRecords_Details_WhenAdmin_ReturnsForbid()
    {
        // Arrange
        var mockService = new Mock<IMedicalRecordService>();
        var mockEnv = new Mock<IWebHostEnvironment>();
        var mockLogger = new Mock<ILogger<MedicalRecordsController>>();
        var mockStorage = new Mock<IFileStorageService>();

        var controller = new MedicalRecordsController(mockService.Object, mockEnv.Object, mockLogger.Object, mockStorage.Object);
        SetUser(controller, "admin_user_1", "Admin");

        // Act
        var result = await controller.Details(10);

        // Assert (BIZ-09: Administrator must not read clinical record details)
        result.Should().BeOfType<ForbidResult>();
        mockService.Verify(s => s.GetRecordDetailsAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task MedicalRecords_PatientHistory_WhenAdmin_ReturnsForbid()
    {
        // Arrange
        var mockService = new Mock<IMedicalRecordService>();
        var mockEnv = new Mock<IWebHostEnvironment>();
        var mockLogger = new Mock<ILogger<MedicalRecordsController>>();
        var mockStorage = new Mock<IFileStorageService>();

        var controller = new MedicalRecordsController(mockService.Object, mockEnv.Object, mockLogger.Object, mockStorage.Object);
        SetUser(controller, "admin_user_1", "Admin");

        // Act
        var result = await controller.PatientHistory("pat_user_1");

        // Assert (BIZ-09: Administrator must not read patient clinical history)
        result.Should().BeOfType<ForbidResult>();
        mockService.Verify(s => s.GetPatientTimelineAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task Prescriptions_Print_WhenAdmin_ReturnsForbid()
    {
        // Arrange
        var mockService = new Mock<IPrescriptionService>();
        var mockLogger = new Mock<ILogger<PrescriptionsController>>();

        var controller = new PrescriptionsController(mockService.Object, mockLogger.Object);
        SetUser(controller, "admin_user_1", "Admin");

        // Act
        var result = await controller.Print(25);

        // Assert (BIZ-09: Administrator must not print/view patient prescriptions)
        result.Should().BeOfType<ForbidResult>();
        mockService.Verify(s => s.GetPrescriptionForPrintAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task Prescriptions_ByAppointment_WhenAdmin_ReturnsForbid()
    {
        // Arrange
        var mockService = new Mock<IPrescriptionService>();
        var mockLogger = new Mock<ILogger<PrescriptionsController>>();

        var controller = new PrescriptionsController(mockService.Object, mockLogger.Object);
        SetUser(controller, "admin_user_1", "Admin");

        // Act
        var result = await controller.ByAppointment(50);

        // Assert (BIZ-09: Administrator must not access prescription by appointment)
        result.Should().BeOfType<ForbidResult>();
        mockService.Verify(s => s.GetPrescriptionByAppointmentIdAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task Appointments_Details_WhenAdmin_RedactsClinicalFields()
    {
        // Arrange
        var mockApptService = new Mock<IAppointmentService>();
        var mockDocService = new Mock<IDoctorService>();
        var mockRecordService = new Mock<IMedicalRecordService>();
        var mockNotificationService = new Mock<INotificationService>();
        var mockEnv = new Mock<IWebHostEnvironment>();
        var mockLogger = new Mock<ILogger<AppointmentsController>>();
        var mockClock = new Mock<IClinicClock>();

        var apptDto = new AppointmentSummaryDto
        {
            Id = 77,
            DoctorId = 1,
            PatientId = 2,
            Status = AppointmentStatus.Completed,
            Diagnosis = "Confidential Patient Clinical Diagnosis",
            MedicalRecordId = 10,
            MedicalRecordAttachmentPath = "records/patient_scan_secret.pdf"
        };

        mockApptService.Setup(s => s.GetAppointmentByIdAsync(77))
            .ReturnsAsync(Result<AppointmentSummaryDto>.Success(apptDto));

        var controller = new AppointmentsController(
            mockApptService.Object, mockDocService.Object, mockRecordService.Object,
            mockNotificationService.Object, mockEnv.Object, mockLogger.Object, mockClock.Object);

        SetUser(controller, "admin_user_1", "Admin");

        // Act
        var result = await controller.Details(77);

        // Assert (BIZ-09: Administrative appointment overview must redact clinical diagnoses and attachments)
        result.Should().BeOfType<ViewResult>();
        var viewResult = (ViewResult)result;
        var model = viewResult.Model.Should().BeAssignableTo<AppointmentSummaryDto>().Subject;

        model.Diagnosis.Should().BeNull();
        model.MedicalRecordId.Should().BeNull();
        model.MedicalRecordAttachmentPath.Should().BeNull();
    }

    [Fact]
    public async Task Appointments_UploadAttachment_WhenAdmin_ReturnsForbid()
    {
        // Arrange
        var mockApptService = new Mock<IAppointmentService>();
        var mockDocService = new Mock<IDoctorService>();
        var mockRecordService = new Mock<IMedicalRecordService>();
        var mockNotificationService = new Mock<INotificationService>();
        var mockEnv = new Mock<IWebHostEnvironment>();
        var mockLogger = new Mock<ILogger<AppointmentsController>>();
        var mockClock = new Mock<IClinicClock>();

        var controller = new AppointmentsController(
            mockApptService.Object, mockDocService.Object, mockRecordService.Object,
            mockNotificationService.Object, mockEnv.Object, mockLogger.Object, mockClock.Object);

        SetUser(controller, "admin_user_1", "Admin");

        // Act
        var result = await controller.UploadAttachment(77, null);

        // Assert
        result.Should().BeOfType<ForbidResult>();
    }
}
