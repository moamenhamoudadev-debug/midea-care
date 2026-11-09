using FluentAssertions;
using MediCare.Data.Entities;
using MediCare.Data.Enums;
using MediCare.Data.Repositories;
using MediCare.Data.UnitOfWork;
using MediCare.Services.Common;
using MediCare.Services.Contracts;
using MediCare.Services.DTOs;
using MediCare.Services.Implementations;
using Microsoft.Extensions.Logging;
using Moq;
using System.Linq.Expressions;
using Xunit;

namespace MediCare.Tests.Security;

public class DoctorTimelineAccessSecurityTests
{
    private readonly Mock<IUnitOfWork> _uowMock = new();
    private readonly Mock<IFileStorageService> _fileStorageMock = new();
    private readonly Mock<IClinicClock> _clinicClockMock = new();
    private readonly Mock<INotificationService> _notificationServiceMock = new();
    private readonly Mock<ILogger<MedicalRecordService>> _loggerMock = new();

    private readonly Patient _targetPatient = new()
    {
        Id = 10,
        UserId = "patient_user_1",
        User = new ApplicationUser { Id = "patient_user_1", FullName = "Ahmed Patient" }
    };

    private readonly Doctor _treatingDoctor = new()
    {
        Id = 5,
        UserId = "doctor_treating",
        User = new ApplicationUser { Id = "doctor_treating", FullName = "Dr. Treating" },
        Specialization = new Specialization { Name = "Cardiology" }
    };

    private readonly Doctor _unrelatedDoctor = new()
    {
        Id = 99,
        UserId = "doctor_unrelated",
        User = new ApplicationUser { Id = "doctor_unrelated", FullName = "Dr. Unrelated" },
        Specialization = new Specialization { Name = "Dermatology" }
    };

    public DoctorTimelineAccessSecurityTests()
    {
        var patRepoMock = new Mock<IRepository<Patient>>();
        patRepoMock.Setup(r => r.FindAsync(It.IsAny<Expression<Func<Patient, bool>>>()))
            .ReturnsAsync((Expression<Func<Patient, bool>> expr) =>
            {
                var func = expr.Compile();
                return new List<Patient> { _targetPatient }.Where(func).ToList();
            });
        _uowMock.Setup(u => u.Patients).Returns(patRepoMock.Object);

        var docRepoMock = new Mock<IDoctorRepository>();
        docRepoMock.Setup(r => r.FindAsync(It.IsAny<Expression<Func<Doctor, bool>>>()))
            .ReturnsAsync((Expression<Func<Doctor, bool>> expr) =>
            {
                var func = expr.Compile();
                var doctors = new List<Doctor> { _treatingDoctor, _unrelatedDoctor };
                return doctors.Where(func).ToList();
            });
        _uowMock.Setup(u => u.Doctors).Returns(docRepoMock.Object);

        var medRecordRepoMock = new Mock<IMedicalRecordRepository>();
        medRecordRepoMock.Setup(r => r.GetPatientHistoryAsync(_targetPatient.Id))
            .ReturnsAsync(new List<MedicalRecord>
            {
                new()
                {
                    Id = 1,
                    AppointmentId = 101,
                    Appointment = new Appointment
                    {
                        Id = 101,
                        AppointmentDate = new DateTime(2026, 10, 1),
                        StartTime = new TimeSpan(10, 0, 0)
                    },
                    Doctor = _treatingDoctor,
                    Diagnosis = "Hypertension",
                    Symptoms = "Headache",
                    VisitNotes = "Stable condition"
                }
            });
        _uowMock.Setup(u => u.MedicalRecords).Returns(medRecordRepoMock.Object);
    }

    [Fact]
    public async Task GetPatientTimeline_PatientViewingOwnHistory_Succeeds()
    {
        // Arrange
        var apptRepoMock = new Mock<IAppointmentRepository>();
        _uowMock.Setup(u => u.Appointments).Returns(apptRepoMock.Object);

        var service = new MedicalRecordService(
            _uowMock.Object, _fileStorageMock.Object, _clinicClockMock.Object,
            _notificationServiceMock.Object, _loggerMock.Object);

        // Act
        var result = await service.GetPatientTimelineAsync(
            patientUserId: "patient_user_1",
            requestingUserId: "patient_user_1",
            isDoctor: false,
            isAdmin: false);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetPatientTimeline_DifferentPatient_ReturnsForbidden()
    {
        // Arrange
        var apptRepoMock = new Mock<IAppointmentRepository>();
        _uowMock.Setup(u => u.Appointments).Returns(apptRepoMock.Object);

        var service = new MedicalRecordService(
            _uowMock.Object, _fileStorageMock.Object, _clinicClockMock.Object,
            _notificationServiceMock.Object, _loggerMock.Object);

        // Act: patient_user_2 tries to view patient_user_1's timeline
        var result = await service.GetPatientTimelineAsync(
            patientUserId: "patient_user_1",
            requestingUserId: "patient_user_2",
            isDoctor: false,
            isAdmin: false);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Forbidden");
    }

    [Fact]
    public async Task GetPatientTimeline_AdminUser_ReturnsForbidden()
    {
        // Arrange
        var apptRepoMock = new Mock<IAppointmentRepository>();
        _uowMock.Setup(u => u.Appointments).Returns(apptRepoMock.Object);

        var service = new MedicalRecordService(
            _uowMock.Object, _fileStorageMock.Object, _clinicClockMock.Object,
            _notificationServiceMock.Object, _loggerMock.Object);

        // Act
        var result = await service.GetPatientTimelineAsync(
            patientUserId: "patient_user_1",
            requestingUserId: "admin_user_99",
            isDoctor: false,
            isAdmin: true);

        // Assert (BIZ-09: Admins must not have read access to clinical records or timelines)
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Forbidden");
    }

    [Fact]
    public async Task GetPatientTimeline_TreatingDoctor_WithConfirmedAppointment_Succeeds()
    {
        // Arrange: Treating doctor has a Confirmed appointment with patient
        var appointments = new List<Appointment>
        {
            new()
            {
                Id = 101,
                DoctorId = _treatingDoctor.Id,
                PatientId = _targetPatient.Id,
                Status = AppointmentStatus.Confirmed
            }
        };

        var apptRepoMock = new Mock<IAppointmentRepository>();
        apptRepoMock.Setup(r => r.FindAsync(It.IsAny<Expression<Func<Appointment, bool>>>()))
            .ReturnsAsync((Expression<Func<Appointment, bool>> expr) => appointments.Where(expr.Compile()).ToList());
        _uowMock.Setup(u => u.Appointments).Returns(apptRepoMock.Object);

        var service = new MedicalRecordService(
            _uowMock.Object, _fileStorageMock.Object, _clinicClockMock.Object,
            _notificationServiceMock.Object, _loggerMock.Object);

        // Act
        var result = await service.GetPatientTimelineAsync(
            patientUserId: "patient_user_1",
            requestingUserId: "doctor_treating",
            isDoctor: true,
            isAdmin: false);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
    }

    [Fact]
    public async Task GetPatientTimeline_TreatingDoctor_WithCompletedAppointment_Succeeds()
    {
        // Arrange: Treating doctor has a Completed appointment with patient
        var appointments = new List<Appointment>
        {
            new()
            {
                Id = 102,
                DoctorId = _treatingDoctor.Id,
                PatientId = _targetPatient.Id,
                Status = AppointmentStatus.Completed
            }
        };

        var apptRepoMock = new Mock<IAppointmentRepository>();
        apptRepoMock.Setup(r => r.FindAsync(It.IsAny<Expression<Func<Appointment, bool>>>()))
            .ReturnsAsync((Expression<Func<Appointment, bool>> expr) => appointments.Where(expr.Compile()).ToList());
        _uowMock.Setup(u => u.Appointments).Returns(apptRepoMock.Object);

        var service = new MedicalRecordService(
            _uowMock.Object, _fileStorageMock.Object, _clinicClockMock.Object,
            _notificationServiceMock.Object, _loggerMock.Object);

        // Act
        var result = await service.GetPatientTimelineAsync(
            patientUserId: "patient_user_1",
            requestingUserId: "doctor_treating",
            isDoctor: true,
            isAdmin: false);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task GetPatientTimeline_DoctorWithOnlyCancelledAppointment_ReturnsForbidden()
    {
        // Arrange: Doctor only has a Cancelled appointment with the patient
        var appointments = new List<Appointment>
        {
            new()
            {
                Id = 103,
                DoctorId = _treatingDoctor.Id,
                PatientId = _targetPatient.Id,
                Status = AppointmentStatus.Cancelled
            }
        };

        var apptRepoMock = new Mock<IAppointmentRepository>();
        apptRepoMock.Setup(r => r.FindAsync(It.IsAny<Expression<Func<Appointment, bool>>>()))
            .ReturnsAsync((Expression<Func<Appointment, bool>> expr) => appointments.Where(expr.Compile()).ToList());
        _uowMock.Setup(u => u.Appointments).Returns(apptRepoMock.Object);

        var service = new MedicalRecordService(
            _uowMock.Object, _fileStorageMock.Object, _clinicClockMock.Object,
            _notificationServiceMock.Object, _loggerMock.Object);

        // Act
        var result = await service.GetPatientTimelineAsync(
            patientUserId: "patient_user_1",
            requestingUserId: "doctor_treating",
            isDoctor: true,
            isAdmin: false);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Forbidden");
    }

    [Fact]
    public async Task GetPatientTimeline_DoctorWhoNeverTreatedPatient_ReturnsForbidden()
    {
        // Arrange: Doctor has never had any appointment with this patient
        var apptRepoMock = new Mock<IAppointmentRepository>();
        apptRepoMock.Setup(r => r.FindAsync(It.IsAny<Expression<Func<Appointment, bool>>>()))
            .ReturnsAsync(new List<Appointment>()); // No appointments
        _uowMock.Setup(u => u.Appointments).Returns(apptRepoMock.Object);

        var service = new MedicalRecordService(
            _uowMock.Object, _fileStorageMock.Object, _clinicClockMock.Object,
            _notificationServiceMock.Object, _loggerMock.Object);

        // Act: doctor_unrelated tries to view patient_user_1's timeline
        var result = await service.GetPatientTimelineAsync(
            patientUserId: "patient_user_1",
            requestingUserId: "doctor_unrelated",
            isDoctor: true,
            isAdmin: false);

        // Assert: MUST return Forbidden
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Forbidden");
    }
}
