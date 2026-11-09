using FluentAssertions;
using MediCare.Data.Entities;
using MediCare.Data.Repositories;
using MediCare.Data.UnitOfWork;
using MediCare.Services.Common;
using MediCare.Services.Implementations;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace MediCare.Tests.Services;

public class PrescriptionServiceTests
{
    private readonly Mock<IUnitOfWork> _uowMock;
    private readonly Mock<IPrescriptionRepository> _prescriptionRepoMock;
    private readonly Mock<IClinicClock> _clinicClockMock;
    private readonly Mock<ILogger<PrescriptionService>> _loggerMock;
    private readonly PrescriptionService _service;

    public PrescriptionServiceTests()
    {
        _uowMock = new Mock<IUnitOfWork>();
        _prescriptionRepoMock = new Mock<IPrescriptionRepository>();
        _clinicClockMock = new Mock<IClinicClock>();
        _loggerMock = new Mock<ILogger<PrescriptionService>>();

        _uowMock.Setup(u => u.Prescriptions).Returns(_prescriptionRepoMock.Object);

        // Fixed clock for testing: 2026-11-15
        _clinicClockMock.Setup(c => c.Today).Returns(new DateTime(2026, 11, 15));

        _service = new PrescriptionService(
            _uowMock.Object,
            _clinicClockMock.Object,
            _loggerMock.Object);
    }

    private Prescription CreateSamplePrescription()
    {
        return new Prescription
        {
            Id = 42,
            MedicalRecordId = 10,
            DoctorId = 1,
            PatientId = 2,
            PrescriptionDate = new DateTime(2026, 11, 15, 11, 30, 0),
            Notes = "Take with food",
            Doctor = new Doctor
            {
                Id = 1,
                UserId = "doc_123",
                LicenseNumber = "MD-9988",
                User = new ApplicationUser { FullName = "Dr. Mona Zaki" },
                Specialization = new Specialization { Name = "Pediatrics" }
            },
            Patient = new Patient
            {
                Id = 2,
                UserId = "pat_456",
                DateOfBirth = new DateTime(2000, 11, 15), // Exactly 26 years old
                Gender = "Female",
                User = new ApplicationUser { FullName = "Nour Ali" }
            },
            Items = new List<PrescriptionItem>
            {
                new PrescriptionItem
                {
                    Id = 1,
                    MedicationName = "Augmentin 625mg",
                    Dosage = "1 tablet",
                    Frequency = "Every 12 hours",
                    DurationDays = 7,
                    Instructions = "After food"
                }
            }
        };
    }

    [Fact]
    public async Task GetPrescriptionForPrintAsync_OwningPatient_ReturnsCorrectDtoWithAgeCalculation()
    {
        // Arrange
        var prescription = CreateSamplePrescription();
        _prescriptionRepoMock.Setup(r => r.GetByIdWithDetailsAsync(42)).ReturnsAsync(prescription);

        // Act
        var result = await _service.GetPrescriptionForPrintAsync(42, "pat_456", isDoctor: false, isPatient: true, isAdmin: false);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.PatientName.Should().Be("Nour Ali");
        result.Value.PatientAge.Should().Be(26);
        result.Value.DoctorName.Should().Be("Dr. Mona Zaki");
        result.Value.Specialization.Should().Be("Pediatrics");
        result.Value.DoctorLicense.Should().Be("MD-9988");
        result.Value.Items.Should().HaveCount(1);
        result.Value.Items.First().MedicationName.Should().Be("Augmentin 625mg");
    }

    [Fact]
    public async Task GetPrescriptionForPrintAsync_AdminUser_ForbidsAccess()
    {
        // Arrange
        var prescription = CreateSamplePrescription();
        _prescriptionRepoMock.Setup(r => r.GetByIdWithDetailsAsync(42)).ReturnsAsync(prescription);

        // Act (Admin with arbitrary userId)
        var result = await _service.GetPrescriptionForPrintAsync(42, "admin_user", isDoctor: false, isPatient: false, isAdmin: true);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Forbidden");
    }

    [Fact]
    public async Task GetPrescriptionForPrintAsync_UnrelatedUser_ForbidsAccessWithIdor()
    {
        // Arrange
        var prescription = CreateSamplePrescription();
        _prescriptionRepoMock.Setup(r => r.GetByIdWithDetailsAsync(42)).ReturnsAsync(prescription);

        // Act (Attacker trying to view Nour's prescription)
        var result = await _service.GetPrescriptionForPrintAsync(42, "attacker_user", isDoctor: false, isPatient: true, isAdmin: false);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Forbidden");
    }

    [Fact]
    public async Task GetPrescriptionForPrintAsync_AttendingDoctor_AllowsAccess()
    {
        // Arrange
        var prescription = CreateSamplePrescription();
        _prescriptionRepoMock.Setup(r => r.GetByIdWithDetailsAsync(42)).ReturnsAsync(prescription);

        // Act (Attending doctor with matching doc_123)
        var result = await _service.GetPrescriptionForPrintAsync(42, "doc_123", isDoctor: true, isPatient: false, isAdmin: false);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.DoctorName.Should().Be("Dr. Mona Zaki");
    }

    [Fact]
    public async Task GetPrescriptionForPrintAsync_NonAttendingDoctor_ForbidsAccessWithIdor()
    {
        // Arrange
        var prescription = CreateSamplePrescription();
        _prescriptionRepoMock.Setup(r => r.GetByIdWithDetailsAsync(42)).ReturnsAsync(prescription);

        // Act (Another doctor with different user ID attempting cross-doctor access)
        var result = await _service.GetPrescriptionForPrintAsync(42, "doc_999_other", isDoctor: true, isPatient: false, isAdmin: false);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Forbidden");
    }

    [Fact]
    public async Task GetPrescriptionForPrintAsync_NonExistentPrescription_ReturnsNotFound()
    {
        // Arrange
        _prescriptionRepoMock.Setup(r => r.GetByIdWithDetailsAsync(999)).ReturnsAsync((Prescription?)null);

        // Act
        var result = await _service.GetPrescriptionForPrintAsync(999, "any_user", false, false, false);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task VerifyPrescriptionByTokenAsync_ExistingPrescription_ReturnsMinimalPublicDetails()
    {
        // Arrange
        var token = "a1b2c3d4e5f60718293a4b5c6d7e8f90";
        var prescription = CreateSamplePrescription();
        prescription.VerificationToken = token;
        _prescriptionRepoMock.Setup(r => r.GetByTokenWithDetailsAsync(token)).ReturnsAsync(prescription);

        // Act
        var result = await _service.VerifyPrescriptionByTokenAsync(token);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.DoctorName.Should().Be("Dr. Mona Zaki");
        result.Value.MaskedPatientName.Should().Be("N**** A****"); // "Nadia Ali" masked
        result.Value.IsDispensed.Should().BeFalse();
    }

    [Fact]
    public async Task DispensePrescriptionAsync_ValidToken_SetsDispensedAndAudits()
    {
        // Arrange
        var token = "a1b2c3d4e5f60718293a4b5c6d7e8f90";
        var prescription = CreateSamplePrescription();
        prescription.VerificationToken = token;
        _prescriptionRepoMock.Setup(r => r.GetByTokenWithDetailsAsync(token)).ReturnsAsync(prescription);
        _clinicClockMock.Setup(c => c.Now).Returns(new DateTime(2026, 11, 15, 12, 0, 0));
        _uowMock.Setup(u => u.CommitAsync()).ReturnsAsync(1);

        // Act
        var result = await _service.DispensePrescriptionAsync(token, "pharm_user_1", "Misr Pharmacy Maadi");

        // Assert
        result.IsSuccess.Should().BeTrue();
        prescription.IsDispensed.Should().BeTrue();
        prescription.DispensedByUserId.Should().Be("pharm_user_1");
        prescription.PharmacyNotes.Should().Be("Misr Pharmacy Maadi");
        prescription.DispensedAt.Should().Be(new DateTime(2026, 11, 15, 12, 0, 0));
        _uowMock.Verify(u => u.Prescriptions.Update(prescription), Times.Once);
        _uowMock.Verify(u => u.CommitAsync(), Times.Once);
    }

    [Fact]
    public async Task DispensePrescriptionAsync_AlreadyDispensed_ReturnsFailure()
    {
        // Arrange
        var token = "a1b2c3d4e5f60718293a4b5c6d7e8f90";
        var prescription = CreateSamplePrescription();
        prescription.VerificationToken = token;
        prescription.IsDispensed = true;
        _prescriptionRepoMock.Setup(r => r.GetByTokenWithDetailsAsync(token)).ReturnsAsync(prescription);

        // Act
        var result = await _service.DispensePrescriptionAsync(token, "pharm_user_1", "Another Pharmacy");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("already been marked as dispensed");
        _uowMock.Verify(u => u.CommitAsync(), Times.Never);
    }
}
