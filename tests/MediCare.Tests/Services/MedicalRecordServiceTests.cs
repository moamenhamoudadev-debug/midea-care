using System.Linq.Expressions;
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
using Xunit;

namespace MediCare.Tests.Services;

public class MedicalRecordServiceTests
{
    private readonly Mock<IUnitOfWork> _uowMock;
    private readonly Mock<IAppointmentRepository> _appointmentRepoMock;
    private readonly Mock<IDoctorRepository> _doctorRepoMock;
    private readonly Mock<IRepository<Patient>> _patientRepoMock;
    private readonly Mock<IMedicalRecordRepository> _medicalRecordRepoMock;
    private readonly Mock<IPrescriptionRepository> _prescriptionRepoMock;
    private readonly Mock<IFileStorageService> _fileStorageMock;
    private readonly Mock<IClinicClock> _clinicClockMock;
    private readonly Mock<INotificationService> _notificationMock;
    private readonly Mock<ILogger<MedicalRecordService>> _loggerMock;
    private readonly MedicalRecordService _service;

    public MedicalRecordServiceTests()
    {
        _uowMock = new Mock<IUnitOfWork>();
        _appointmentRepoMock = new Mock<IAppointmentRepository>();
        _doctorRepoMock = new Mock<IDoctorRepository>();
        _patientRepoMock = new Mock<IRepository<Patient>>();
        _medicalRecordRepoMock = new Mock<IMedicalRecordRepository>();
        _prescriptionRepoMock = new Mock<IPrescriptionRepository>();
        _fileStorageMock = new Mock<IFileStorageService>();
        _clinicClockMock = new Mock<IClinicClock>();
        _notificationMock = new Mock<INotificationService>();
        _loggerMock = new Mock<ILogger<MedicalRecordService>>();

        _uowMock.Setup(u => u.Appointments).Returns(_appointmentRepoMock.Object);
        _uowMock.Setup(u => u.Doctors).Returns(_doctorRepoMock.Object);
        _uowMock.Setup(u => u.Patients).Returns(_patientRepoMock.Object);
        _uowMock.Setup(u => u.MedicalRecords).Returns(_medicalRecordRepoMock.Object);
        _uowMock.Setup(u => u.Prescriptions).Returns(_prescriptionRepoMock.Object);

        // Fixed clock: 2026-11-15 14:00:00
        _clinicClockMock.Setup(c => c.Now).Returns(new DateTime(2026, 11, 15, 14, 0, 0));
        _clinicClockMock.Setup(c => c.Today).Returns(new DateTime(2026, 11, 15));

        _service = new MedicalRecordService(
            _uowMock.Object,
            _fileStorageMock.Object,
            _clinicClockMock.Object,
            _notificationMock.Object,
            _loggerMock.Object);
    }

    private Appointment CreateSampleAppointment(AppointmentStatus status = AppointmentStatus.Confirmed, int doctorId = 1, int patientId = 1)
    {
        return new Appointment
        {
            Id = 100,
            DoctorId = doctorId,
            PatientId = patientId,
            AppointmentDate = new DateTime(2026, 11, 15),
            StartTime = new TimeSpan(10, 0, 0), // 10:00 AM (past compared to 14:00)
            EndTime = new TimeSpan(10, 30, 0),
            Status = status,
            PaymentStatus = PaymentStatus.Unpaid,
            ConsultationFee = 300,
            Doctor = new Doctor
            {
                Id = doctorId,
                UserId = "doc_user_1",
                User = new ApplicationUser { Id = "doc_user_1", FullName = "Dr. Ahmed Samy" },
                Specialization = new Specialization { Name = "Cardiology" }
            },
            Patient = new Patient
            {
                Id = patientId,
                UserId = "pat_user_1",
                User = new ApplicationUser { Id = "pat_user_1", FullName = "Omar Khaled" }
            }
        };
    }

    [Fact]
    public async Task ValidateEncounterAccessAsync_ValidDoctorAndConfirmedElapsedAppointment_ReturnsSuccess()
    {
        // Arrange
        var appt = CreateSampleAppointment();
        _appointmentRepoMock.Setup(r => r.GetByIdWithDetailsAsync(100)).ReturnsAsync(appt);
        _doctorRepoMock.Setup(r => r.FindAsync(It.IsAny<Expression<Func<Doctor, bool>>>()))
            .ReturnsAsync(new List<Doctor> { appt.Doctor! });

        // Act
        var result = await _service.ValidateEncounterAccessAsync(100, "doc_user_1");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.PatientName.Should().Be("Omar Khaled");
    }

    [Fact]
    public async Task ValidateEncounterAccessAsync_DifferentDoctorAttemptsAccess_ReturnsForbidden()
    {
        // Arrange
        var appt = CreateSampleAppointment(doctorId: 1);
        _appointmentRepoMock.Setup(r => r.GetByIdWithDetailsAsync(100)).ReturnsAsync(appt);
        // User is doctor 2
        var attackerDoctor = new Doctor { Id = 2, UserId = "doc_user_2" };
        _doctorRepoMock.Setup(r => r.FindAsync(It.IsAny<Expression<Func<Doctor, bool>>>()))
            .ReturnsAsync(new List<Doctor> { attackerDoctor });

        // Act
        var result = await _service.ValidateEncounterAccessAsync(100, "doc_user_2");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Forbidden");
    }

    [Fact]
    public async Task ValidateEncounterAccessAsync_StatusNotConfirmed_ReturnsFailure()
    {
        // Arrange
        var appt = CreateSampleAppointment(status: AppointmentStatus.Pending);
        _appointmentRepoMock.Setup(r => r.GetByIdWithDetailsAsync(100)).ReturnsAsync(appt);
        _doctorRepoMock.Setup(r => r.FindAsync(It.IsAny<Expression<Func<Doctor, bool>>>()))
            .ReturnsAsync(new List<Doctor> { appt.Doctor! });

        // Act
        var result = await _service.ValidateEncounterAccessAsync(100, "doc_user_1");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Only Confirmed appointments can be completed");
    }

    [Fact]
    public async Task ValidateEncounterAccessAsync_ScheduledStartTimeInFuture_ReturnsFailure()
    {
        // Arrange (scheduled for 16:00 while clock is 14:00)
        var appt = CreateSampleAppointment();
        appt.StartTime = new TimeSpan(16, 0, 0);
        _appointmentRepoMock.Setup(r => r.GetByIdWithDetailsAsync(100)).ReturnsAsync(appt);
        _doctorRepoMock.Setup(r => r.FindAsync(It.IsAny<Expression<Func<Doctor, bool>>>()))
            .ReturnsAsync(new List<Doctor> { appt.Doctor! });

        // Act
        var result = await _service.ValidateEncounterAccessAsync(100, "doc_user_1");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("before the scheduled appointment start time");
    }

    [Fact]
    public async Task SaveEncounterAsync_ValidData_AtomicallyCompletesAppointmentAndSavesPrescription()
    {
        // Arrange
        var appt = CreateSampleAppointment();
        _appointmentRepoMock.Setup(r => r.GetByIdWithDetailsAsync(100)).ReturnsAsync(appt);
        _doctorRepoMock.Setup(r => r.FindAsync(It.IsAny<Expression<Func<Doctor, bool>>>()))
            .ReturnsAsync(new List<Doctor> { appt.Doctor! });
        _medicalRecordRepoMock.Setup(r => r.GetByAppointmentIdWithDetailsAsync(100)).ReturnsAsync((MedicalRecord?)null);

        var dto = new CreateEncounterDto
        {
            AppointmentId = 100,
            Diagnosis = "Hypertension Stage 1",
            Symptoms = "Occasional headaches",
            VisitNotes = "Advised lower sodium intake",
            PrescriptionItems = new List<PrescriptionItemDto>
            {
                new PrescriptionItemDto
                {
                    MedicationName = "Concor",
                    Dosage = "5mg",
                    Frequency = "Once daily",
                    DurationDays = 30,
                    Instructions = "In the morning before food"
                }
            }
        };

        // Act
        var result = await _service.SaveEncounterAsync(dto, null, "doc_user_1", "C:\\fake\\webroot");

        // Assert
        result.IsSuccess.Should().BeTrue();

        // Check appointment status updated to Completed and Paid
        appt.Status.Should().Be(AppointmentStatus.Completed);
        appt.PaymentStatus.Should().Be(PaymentStatus.Paid);

        // Check entities added
        _medicalRecordRepoMock.Verify(r => r.AddAsync(It.Is<MedicalRecord>(m =>
            m.AppointmentId == 100 &&
            m.Diagnosis == "Hypertension Stage 1")), Times.Once);

        _prescriptionRepoMock.Verify(r => r.AddAsync(It.Is<Prescription>(p =>
            p.DoctorId == appt.DoctorId &&
            p.Items.Count == 1 &&
            p.Items.First().MedicationName == "Concor")), Times.Once);

        // Check commit called
        _uowMock.Verify(u => u.CommitAsync(), Times.Once);

        // Check notification sent to patient
        _notificationMock.Verify(n => n.SendNotificationAsync(
            "pat_user_1",
            It.Is<string>(s => s.Contains("Completed")),
            It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task SaveEncounterAsync_EmptyDiagnosis_ReturnsFailure()
    {
        // Arrange
        var dto = new CreateEncounterDto
        {
            AppointmentId = 100,
            Diagnosis = ""
        };

        // Act
        var result = await _service.SaveEncounterAsync(dto, null, "doc_user_1", "C:\\fake");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("diagnosis is required");
    }

    [Fact]
    public async Task SaveEncounterAsync_EncounterAlreadyExists_ReturnsFailure()
    {
        // Arrange
        var appt = CreateSampleAppointment();
        _appointmentRepoMock.Setup(r => r.GetByIdWithDetailsAsync(100)).ReturnsAsync(appt);
        _doctorRepoMock.Setup(r => r.FindAsync(It.IsAny<Expression<Func<Doctor, bool>>>()))
            .ReturnsAsync(new List<Doctor> { appt.Doctor! });
        _medicalRecordRepoMock.Setup(r => r.GetByAppointmentIdWithDetailsAsync(100))
            .ReturnsAsync(new MedicalRecord { Id = 1, AppointmentId = 100 });

        var dto = new CreateEncounterDto
        {
            AppointmentId = 100,
            Diagnosis = "Hypertension"
        };

        // Act
        var result = await _service.SaveEncounterAsync(dto, null, "doc_user_1", "C:\\fake");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("already been completed");
    }

    [Fact]
    public async Task GetRecordDetailsAsync_OwningPatient_AllowsAccess()
    {
        // Arrange
        var record = new MedicalRecord
        {
            Id = 5,
            AppointmentId = 100,
            Appointment = CreateSampleAppointment(),
            Doctor = new Doctor { Id = 1, UserId = "doc_user_1", User = new ApplicationUser { FullName = "Dr. Ahmed" } },
            Patient = new Patient { Id = 1, UserId = "pat_user_1", User = new ApplicationUser { FullName = "Omar Khaled" } },
            Diagnosis = "Asthma"
        };
        _medicalRecordRepoMock.Setup(r => r.GetByIdWithDetailsAsync(5)).ReturnsAsync(record);

        // Act (Patient viewing own record)
        var result = await _service.GetRecordDetailsAsync(5, "pat_user_1", isDoctor: false, isPatient: true, isAdmin: false);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Diagnosis.Should().Be("Asthma");
    }

    [Fact]
    public async Task GetRecordDetailsAsync_UnrelatedUser_ForbidsAccessWithIdorProtection()
    {
        // Arrange
        var record = new MedicalRecord
        {
            Id = 5,
            AppointmentId = 100,
            Appointment = CreateSampleAppointment(),
            Doctor = new Doctor { Id = 1, UserId = "doc_user_1" },
            Patient = new Patient { Id = 1, UserId = "pat_user_1" }
        };
        _medicalRecordRepoMock.Setup(r => r.GetByIdWithDetailsAsync(5)).ReturnsAsync(record);

        // Act (Attacker user_other trying to view)
        var result = await _service.GetRecordDetailsAsync(5, "user_other", isDoctor: false, isPatient: true, isAdmin: false);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Forbidden");
    }

    [Fact]
    public async Task SaveEncounterAsync_CommitFails_DeletesUploadedAttachmentAndRethrows()
    {
        // Arrange
        var appt = CreateSampleAppointment();
        _appointmentRepoMock.Setup(r => r.GetByIdWithDetailsAsync(100)).ReturnsAsync(appt);
        _doctorRepoMock.Setup(r => r.FindAsync(It.IsAny<Expression<Func<Doctor, bool>>>()))
            .ReturnsAsync(new List<Doctor> { appt.Doctor! });
        _medicalRecordRepoMock.Setup(r => r.GetByAppointmentIdWithDetailsAsync(100)).ReturnsAsync((MedicalRecord?)null);

        var fileMock = new Mock<Microsoft.AspNetCore.Http.IFormFile>();
        fileMock.Setup(f => f.Length).Returns(1024);
        fileMock.Setup(f => f.FileName).Returns("xray.pdf");

        var uploadedPath = "uploads/records/guid123.pdf";
        _fileStorageMock.Setup(f => f.SaveMedicalAttachmentAsync(fileMock.Object, "C:\\webroot"))
            .ReturnsAsync(Result<string>.Success(uploadedPath));

        _uowMock.Setup(u => u.CommitAsync()).ThrowsAsync(new Microsoft.EntityFrameworkCore.DbUpdateException("Database connection terminated", new Exception()));

        var dto = new CreateEncounterDto
        {
            AppointmentId = 100,
            Diagnosis = "Acute Bronchitis"
        };

        // Act & Assert
        var act = async () => await _service.SaveEncounterAsync(dto, fileMock.Object, "doc_user_1", "C:\\webroot");
        await act.Should().ThrowAsync<Microsoft.EntityFrameworkCore.DbUpdateException>();

        // Verify that the saved attachment was cleaned up on disk to prevent orphaned files
        _fileStorageMock.Verify(f => f.DeleteAttachment(uploadedPath, "C:\\webroot"), Times.Once);
    }

    [Fact]
    public async Task UploadPatientAttachmentAsync_ShouldSucceed_WhenPatientOwnsAppointment()
    {
        // Arrange
        var appt = CreateSampleAppointment();
        _appointmentRepoMock.Setup(r => r.GetByIdWithDetailsAsync(100)).ReturnsAsync(appt);

        var existingRecord = new MedicalRecord
        {
            Id = 55,
            AppointmentId = 100,
            DoctorId = 1,
            PatientId = 1,
            Diagnosis = "Initial checkup"
        };
        _medicalRecordRepoMock.Setup(r => r.GetByAppointmentIdWithDetailsAsync(100)).ReturnsAsync(existingRecord);

        var fileMock = new Mock<Microsoft.AspNetCore.Http.IFormFile>();
        fileMock.Setup(f => f.Length).Returns(2048);
        fileMock.Setup(f => f.FileName).Returns("blood_test.pdf");

        var uploadedPath = "uploads/records/blood_test_123.pdf";
        _fileStorageMock.Setup(f => f.SaveMedicalAttachmentAsync(fileMock.Object, "C:\\webroot"))
            .ReturnsAsync(Result<string>.Success(uploadedPath));

        _uowMock.Setup(u => u.CommitAsync()).ReturnsAsync(1);

        // Act
        var result = await _service.UploadPatientAttachmentAsync(100, fileMock.Object, "pat_user_1", "C:\\webroot");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(uploadedPath);
        existingRecord.AttachmentPath.Should().Be(uploadedPath);
        _uowMock.Verify(u => u.CommitAsync(), Times.Once);
    }

    [Fact]
    public async Task UploadPatientAttachmentAsync_ShouldFail_WhenUserDoesNotOwnAppointment()
    {
        // Arrange
        var appt = CreateSampleAppointment();
        _appointmentRepoMock.Setup(r => r.GetByIdWithDetailsAsync(100)).ReturnsAsync(appt);

        var fileMock = new Mock<Microsoft.AspNetCore.Http.IFormFile>();
        fileMock.Setup(f => f.Length).Returns(1024);

        // Act
        var result = await _service.UploadPatientAttachmentAsync(100, fileMock.Object, "attacker_user", "C:\\webroot");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Forbidden");
        _uowMock.Verify(u => u.CommitAsync(), Times.Never);
    }

    [Fact]
    public async Task ValidateEncounterAccess_ShouldSucceed_WhenOnlyDraftRecordExists()
    {
        // Arrange
        var appt = CreateSampleAppointment(AppointmentStatus.Confirmed);
        _appointmentRepoMock.Setup(r => r.GetByIdWithDetailsAsync(100)).ReturnsAsync(appt);
        _doctorRepoMock.Setup(d => d.FindAsync(It.IsAny<Expression<Func<Doctor, bool>>>()))
            .ReturnsAsync(new List<Doctor> { appt.Doctor });

        var draftRecord = new MedicalRecord
        {
            Id = 55,
            AppointmentId = 100,
            DoctorId = 1,
            PatientId = 1,
            Diagnosis = "Patient Uploaded Diagnostic / Laboratory Files",
            IsDraft = true
        };
        _medicalRecordRepoMock.Setup(r => r.GetByAppointmentIdWithDetailsAsync(100)).ReturnsAsync(draftRecord);

        // Act
        var result = await _service.ValidateEncounterAccessAsync(100, "doc_user_1");

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task SaveEncounter_ShouldUpdateSameRecordAndSetIsDraftFalse_WhenDraftRecordExists()
    {
        // Arrange
        var appt = CreateSampleAppointment(AppointmentStatus.Confirmed);
        _appointmentRepoMock.Setup(r => r.GetByIdWithDetailsAsync(100)).ReturnsAsync(appt);
        _doctorRepoMock.Setup(d => d.FindAsync(It.IsAny<Expression<Func<Doctor, bool>>>()))
            .ReturnsAsync(new List<Doctor> { appt.Doctor });

        var draftRecord = new MedicalRecord
        {
            Id = 55,
            AppointmentId = 100,
            DoctorId = 1,
            PatientId = 1,
            Diagnosis = "Patient Uploaded Diagnostic / Laboratory Files",
            AttachmentPath = "uploads/records/test.pdf",
            IsDraft = true
        };
        _medicalRecordRepoMock.Setup(r => r.GetByAppointmentIdWithDetailsAsync(100)).ReturnsAsync(draftRecord);
        _uowMock.Setup(u => u.CommitAsync()).ReturnsAsync(1);

        var dto = new CreateEncounterDto
        {
            AppointmentId = 100,
            Diagnosis = "Hypertension Stage 2",
            Symptoms = "Headache and dizziness",
            VisitNotes = "Recommended lifestyle changes"
        };

        // Act
        var result = await _service.SaveEncounterAsync(dto, null, "doc_user_1", "C:\\webroot");

        // Assert
        result.IsSuccess.Should().BeTrue();
        draftRecord.Diagnosis.Should().Be("Hypertension Stage 2");
        draftRecord.IsDraft.Should().BeFalse();
        draftRecord.AttachmentPath.Should().Be("uploads/records/test.pdf"); // Retains pre-visit upload
        _medicalRecordRepoMock.Verify(r => r.Update(draftRecord), Times.Once);
        _medicalRecordRepoMock.Verify(r => r.AddAsync(It.IsAny<MedicalRecord>()), Times.Never);
    }

    [Fact]
    public async Task ValidateEncounterAccess_ShouldFail_WhenCompletedRecordExists()
    {
        // Arrange
        var appt = CreateSampleAppointment(AppointmentStatus.Confirmed);
        _appointmentRepoMock.Setup(r => r.GetByIdWithDetailsAsync(100)).ReturnsAsync(appt);
        _doctorRepoMock.Setup(d => d.FindAsync(It.IsAny<Expression<Func<Doctor, bool>>>()))
            .ReturnsAsync(new List<Doctor> { appt.Doctor });

        var completedRecord = new MedicalRecord
        {
            Id = 55,
            AppointmentId = 100,
            DoctorId = 1,
            PatientId = 1,
            Diagnosis = "Final Diagnosed Condition",
            IsDraft = false
        };
        _medicalRecordRepoMock.Setup(r => r.GetByAppointmentIdWithDetailsAsync(100)).ReturnsAsync(completedRecord);

        // Act
        var result = await _service.ValidateEncounterAccessAsync(100, "doc_user_1");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("already been documented");
    }

    [Fact]
    public async Task UploadPatientAttachment_ShouldNotFlipIsDraftToTrue_WhenCompletedRecordExists()
    {
        // Arrange
        var appt = CreateSampleAppointment(AppointmentStatus.Completed);
        _appointmentRepoMock.Setup(r => r.GetByIdWithDetailsAsync(100)).ReturnsAsync(appt);

        var completedRecord = new MedicalRecord
        {
            Id = 55,
            AppointmentId = 100,
            DoctorId = 1,
            PatientId = 1,
            Diagnosis = "Final Completed Diagnosis",
            IsDraft = false
        };
        _medicalRecordRepoMock.Setup(r => r.GetByAppointmentIdWithDetailsAsync(100)).ReturnsAsync(completedRecord);

        var fileMock = new Mock<Microsoft.AspNetCore.Http.IFormFile>();
        fileMock.Setup(f => f.Length).Returns(2048);
        fileMock.Setup(f => f.FileName).Returns("followup_lab.pdf");

        var uploadedPath = "uploads/records/followup_lab.pdf";
        _fileStorageMock.Setup(f => f.SaveMedicalAttachmentAsync(fileMock.Object, "C:\\webroot"))
            .ReturnsAsync(Result<string>.Success(uploadedPath));
        _uowMock.Setup(u => u.CommitAsync()).ReturnsAsync(1);

        // Act
        var result = await _service.UploadPatientAttachmentAsync(100, fileMock.Object, "pat_user_1", "C:\\webroot");

        // Assert
        result.IsSuccess.Should().BeTrue();
        completedRecord.IsDraft.Should().BeFalse(); // Must remain false!
    }

    [Fact]
    public async Task GetRecordDetailsAsync_WhenRecordNotFound_ReturnsFailure()
    {
        _medicalRecordRepoMock.Setup(r => r.GetByIdWithDetailsAsync(999))
            .ReturnsAsync((MedicalRecord?)null);

        var result = await _service.GetRecordDetailsAsync(999, "any_user", isDoctor: false, isPatient: false, isAdmin: false);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Medical record not found");
    }

    [Fact]
    public async Task GetRecordDetailsAsync_WhenUserUnauthorized_ReturnsForbidden()
    {
        var record = new MedicalRecord
        {
            Id = 1,
            Doctor = new Doctor { UserId = "doc_1" },
            Patient = new Patient { UserId = "pat_1" }
        };
        _medicalRecordRepoMock.Setup(r => r.GetByIdWithDetailsAsync(1)).ReturnsAsync(record);

        var result = await _service.GetRecordDetailsAsync(1, "stranger_user", isDoctor: false, isPatient: false, isAdmin: false);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Forbidden");
    }

    [Fact]
    public async Task GetRecordDetailsAsync_WhenAdmin_ReturnsForbidden()
    {
        var record = new MedicalRecord
        {
            Id = 10,
            AppointmentId = 50,
            DoctorId = 1,
            PatientId = 2
        };
        _medicalRecordRepoMock.Setup(r => r.GetByIdWithDetailsAsync(10)).ReturnsAsync(record);

        var result = await _service.GetRecordDetailsAsync(10, "admin_user", isDoctor: false, isPatient: false, isAdmin: true);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Forbidden");
    }

    [Fact]
    public async Task GetRecordDetailsAsync_WhenPatient_ParsesVitalsAndPrescriptionSuccessfully()
    {
        var record = new MedicalRecord
        {
            Id = 10,
            AppointmentId = 50,
            Appointment = new Appointment { AppointmentDate = new DateTime(2026, 11, 15), StartTime = TimeSpan.FromHours(10) },
            DoctorId = 1,
            Doctor = new Doctor { LicenseNumber = "DOC-EGY-100", Specialization = new Specialization { Name = "Cardiology" }, User = new ApplicationUser { FullName = "Dr. Sameh" } },
            PatientId = 2,
            Patient = new Patient { UserId = "pat_user_1", DateOfBirth = new DateTime(1996, 5, 15), Gender = "Male", BloodGroup = "O+", EmergencyContact = "01000000000", User = new ApplicationUser { FullName = "Hassan Ali" } },
            Diagnosis = "Hypertension",
            Symptoms = "Headache",
            VisitNotes = "Standard consult [VITALS: BP: 120/80 | HR: 72 bpm | Temp: 36.8 °C | Glucose: 95 mg/dL | Weight: 74 kg]",
            CreatedAt = new DateTime(2026, 11, 15, 10, 30, 0),
            Prescription = new Prescription
            {
                Id = 88,
                MedicalRecordId = 10,
                PrescriptionDate = new DateTime(2026, 11, 15),
                Items = new List<PrescriptionItem>
                {
                    new() { Id = 1, MedicationName = "Amlodipine 5mg", Dosage = "1 tab", Frequency = "Once daily", DurationDays = 30 }
                }
            }
        };
        _medicalRecordRepoMock.Setup(r => r.GetByIdWithDetailsAsync(10)).ReturnsAsync(record);

        var result = await _service.GetRecordDetailsAsync(10, "pat_user_1", isDoctor: false, isPatient: true, isAdmin: false);

        result.IsSuccess.Should().BeTrue();
        result.Value!.BloodPressure.Should().Be("120/80");
        result.Value.HeartRate.Should().Be(72);
        result.Value.Temperature.Should().Be(36.8m);
        result.Value.BloodGlucose.Should().Be(95m);
        result.Value.WeightKg.Should().Be(74m);
        result.Value.Prescription.Should().NotBeNull();
        result.Value.Prescription!.Items.Should().ContainSingle();
    }

    [Fact]
    public async Task GetPatientTimelineAsync_WhenPatientNotFound_ReturnsFailure()
    {
        _patientRepoMock.Setup(p => p.FindAsync(It.IsAny<Expression<Func<Patient, bool>>>()))
            .ReturnsAsync(new List<Patient>());

        var result = await _service.GetPatientTimelineAsync("unknown_pat", "requesting_user", isDoctor: false, isAdmin: false);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Patient profile not found");
    }

    [Fact]
    public async Task GetPatientTimelineAsync_WhenUnauthorizedUser_ReturnsForbidden()
    {
        var patient = new Patient { Id = 5, UserId = "pat_5" };
        _patientRepoMock.Setup(p => p.FindAsync(It.IsAny<Expression<Func<Patient, bool>>>()))
            .ReturnsAsync(new List<Patient> { patient });

        var result = await _service.GetPatientTimelineAsync("pat_5", "stranger_user", isDoctor: false, isAdmin: false);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Forbidden");
    }

    [Fact]
    public async Task GetPatientTimelineAsync_WhenPatientViewsOwnHistory_ReturnsTimeline()
    {
        var patient = new Patient { Id = 5, UserId = "pat_5" };
        _patientRepoMock.Setup(p => p.FindAsync(It.IsAny<Expression<Func<Patient, bool>>>()))
            .ReturnsAsync(new List<Patient> { patient });

        var records = new List<MedicalRecord>
        {
            new()
            {
                Id = 1,
                AppointmentId = 10,
                Appointment = new Appointment { AppointmentDate = new DateTime(2026, 11, 1), StartTime = TimeSpan.FromHours(9) },
                Diagnosis = "Initial checkup",
                Doctor = new Doctor { User = new ApplicationUser { FullName = "Dr. Nadia" }, Specialization = new Specialization { Name = "Dermatology" } }
            }
        };
        _medicalRecordRepoMock.Setup(r => r.GetPatientHistoryAsync(5)).ReturnsAsync(records);

        var result = await _service.GetPatientTimelineAsync("pat_5", "pat_5", isDoctor: false, isAdmin: false);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle();
        result.Value!.First().Diagnosis.Should().Be("Initial checkup");
    }

    [Fact]
    public async Task SaveEncounterAsync_WhenDiagnosisExceeds500Characters_ReturnsFailure()
    {
        var dto = new CreateEncounterDto
        {
            AppointmentId = 1,
            Diagnosis = new string('A', 501)
        };

        var result = await _service.SaveEncounterAsync(dto, null, "doc_user_1", "C:\\temp");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("500 characters");
    }

    [Fact]
    public async Task SaveEncounterAsync_WhenPrescriptionItemInvalid_ReturnsFailure()
    {
        var dto = new CreateEncounterDto
        {
            AppointmentId = 1,
            Diagnosis = "Valid Diagnosis",
            PrescriptionItems = new List<PrescriptionItemDto>
            {
                new()
                {
                    MedicationName = new string('B', 151),
                    Dosage = "10mg",
                    Frequency = "Daily",
                    DurationDays = 7
                }
            }
        };

        var result = await _service.SaveEncounterAsync(dto, null, "doc_user_1", "C:\\temp");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("150 characters");
    }
}
