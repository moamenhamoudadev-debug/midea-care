using System.Linq.Expressions;
using System.Text;
using FluentAssertions;
using MediCare.Data.DTOs;
using MediCare.Data.Entities;
using MediCare.Data.Enums;
using MediCare.Data.Repositories;
using MediCare.Data.UnitOfWork;
using MediCare.Services.Common;
using MediCare.Services.Contracts;
using MediCare.Services.Implementations;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace MediCare.Tests.Services;

public class AdminServiceTests
{
    private readonly Mock<IUnitOfWork> _uowMock;
    private readonly Mock<IDoctorRepository> _doctorRepoMock;
    private readonly Mock<IAppointmentRepository> _appointmentRepoMock;
    private readonly Mock<IRepository<Patient>> _patientRepoMock;
    private readonly Mock<IRepository<Specialization>> _specRepoMock;
    private readonly Mock<IEmailService> _emailServiceMock;
    private readonly Mock<IClinicClock> _clinicClockMock;
    private readonly Mock<ILogger<AdminService>> _loggerMock;
    private readonly AdminService _service;

    public AdminServiceTests()
    {
        _uowMock = new Mock<IUnitOfWork>();
        _doctorRepoMock = new Mock<IDoctorRepository>();
        _appointmentRepoMock = new Mock<IAppointmentRepository>();
        _patientRepoMock = new Mock<IRepository<Patient>>();
        _specRepoMock = new Mock<IRepository<Specialization>>();
        _emailServiceMock = new Mock<IEmailService>();
        _clinicClockMock = new Mock<IClinicClock>();
        _loggerMock = new Mock<ILogger<AdminService>>();

        _uowMock.Setup(u => u.Doctors).Returns(_doctorRepoMock.Object);
        _uowMock.Setup(u => u.Appointments).Returns(_appointmentRepoMock.Object);
        _uowMock.Setup(u => u.Patients).Returns(_patientRepoMock.Object);
        _uowMock.Setup(u => u.Specializations).Returns(_specRepoMock.Object);

        // Fixed clock for testing: 2026-11-15
        _clinicClockMock.Setup(c => c.Today).Returns(new DateTime(2026, 11, 15));

        _service = new AdminService(
            _uowMock.Object,
            _emailServiceMock.Object,
            _clinicClockMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task GetPendingDoctorsAsync_ReturnsOnlyUnapprovedDoctors()
    {
        // Arrange
        var unapprovedDoctors = new List<Doctor>
        {
            new Doctor { Id = 10, UserId = "u1", IsApproved = false, LicenseNumber = "MD-001" },
            new Doctor { Id = 11, UserId = "u2", IsApproved = false, LicenseNumber = "MD-002" }
        };

        _doctorRepoMock.Setup(r => r.FindAsync(It.IsAny<Expression<Func<Doctor, bool>>>()))
            .ReturnsAsync(unapprovedDoctors);

        _doctorRepoMock.Setup(r => r.GetDoctorWithDetailsAsync(10))
            .ReturnsAsync(new Doctor
            {
                Id = 10,
                UserId = "u1",
                IsApproved = false,
                LicenseNumber = "MD-001",
                User = new ApplicationUser { FullName = "Dr. Pend One", Email = "one@med.com" }
            });

        _doctorRepoMock.Setup(r => r.GetDoctorWithDetailsAsync(11))
            .ReturnsAsync(new Doctor
            {
                Id = 11,
                UserId = "u2",
                IsApproved = false,
                LicenseNumber = "MD-002",
                User = new ApplicationUser { FullName = "Dr. Pend Two", Email = "two@med.com" }
            });

        // Act
        var result = await _service.GetPendingDoctorsAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value!.Select(d => d.LicenseNumber).Should().Contain(new[] { "MD-001", "MD-002" });
    }

    [Fact]
    public async Task ApproveDoctorAsync_UnapprovedDoctor_MarksApprovedAndSendsEmail()
    {
        // Arrange
        var doc = new Doctor
        {
            Id = 10,
            IsApproved = false,
            User = new ApplicationUser { Email = "dr.new@test.com", FullName = "Dr. New" }
        };
        _doctorRepoMock.Setup(r => r.GetDoctorWithDetailsAsync(10)).ReturnsAsync(doc);

        // Act
        var result = await _service.ApproveDoctorAsync(10);

        // Assert
        result.IsSuccess.Should().BeTrue();
        doc.IsApproved.Should().BeTrue();
        _doctorRepoMock.Verify(r => r.Update(doc), Times.Once);
        _uowMock.Verify(u => u.CommitAsync(), Times.Once);
        _emailServiceMock.Verify(e => e.SendEmailAsync(
            "dr.new@test.com",
            It.Is<string>(s => s.Contains("Approved")),
            It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task ApproveDoctorAsync_AlreadyApprovedDoctor_ReturnsFailure()
    {
        // Arrange
        var doc = new Doctor { Id = 10, IsApproved = true };
        _doctorRepoMock.Setup(r => r.GetDoctorWithDetailsAsync(10)).ReturnsAsync(doc);

        // Act
        var result = await _service.ApproveDoctorAsync(10);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("already approved");
        _uowMock.Verify(u => u.CommitAsync(), Times.Never);
    }

    [Fact]
    public async Task RejectDoctorAsync_UnapprovedDoctor_DeletesDoctorAndSendsRejectionEmail()
    {
        // Arrange
        var doc = new Doctor
        {
            Id = 15,
            IsApproved = false,
            User = new ApplicationUser { Email = "declined@med.com", FullName = "Dr. Declined" }
        };
        _doctorRepoMock.Setup(r => r.GetDoctorWithDetailsAsync(15)).ReturnsAsync(doc);

        // Act
        var result = await _service.RejectDoctorAsync(15, "Invalid Syndicate ID");

        // Assert
        result.IsSuccess.Should().BeTrue();
        _doctorRepoMock.Verify(r => r.Delete(doc), Times.Once);
        _uowMock.Verify(u => u.CommitAsync(), Times.Once);
        _emailServiceMock.Verify(e => e.SendEmailAsync(
            "declined@med.com",
            It.Is<string>(s => s.Contains("Application Update")),
            It.Is<string>(b => b.Contains("Invalid Syndicate ID"))), Times.Once);
    }

    [Fact]
    public async Task RejectDoctorAsync_AlreadyApprovedDoctor_ReturnsFailure()
    {
        // Arrange
        var doc = new Doctor { Id = 15, IsApproved = true };
        _doctorRepoMock.Setup(r => r.GetDoctorWithDetailsAsync(15)).ReturnsAsync(doc);

        // Act
        var result = await _service.RejectDoctorAsync(15, "Some reason");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Cannot reject an already approved doctor");
        _uowMock.Verify(u => u.CommitAsync(), Times.Never);
    }

    [Fact]
    public async Task RejectDoctorAsync_UnapprovedDoctor_AlsoDeletesAssociatedUserAccount_WhenUserManagerPresent()
    {
        // Arrange
        var userStore = new Mock<IUserStore<ApplicationUser>>();
        var userManagerMock = new Mock<UserManager<ApplicationUser>>(
            userStore.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        var serviceWithUserManager = new AdminService(
            _uowMock.Object,
            _emailServiceMock.Object,
            _clinicClockMock.Object,
            _loggerMock.Object,
            userManagerMock.Object);

        var docUser = new ApplicationUser { Id = "user-to-delete-123", Email = "declined@med.com", FullName = "Dr. Declined" };
        var doc = new Doctor
        {
            Id = 15,
            UserId = "user-to-delete-123",
            IsApproved = false,
            User = docUser
        };

        _doctorRepoMock.Setup(r => r.GetDoctorWithDetailsAsync(15)).ReturnsAsync(doc);
        userManagerMock.Setup(m => m.DeleteAsync(docUser)).ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await serviceWithUserManager.RejectDoctorAsync(15, "Invalid Syndicate ID");

        // Assert
        result.IsSuccess.Should().BeTrue();
        _doctorRepoMock.Verify(r => r.Delete(doc), Times.Once);
        _uowMock.Verify(u => u.CommitAsync(), Times.Once);
        userManagerMock.Verify(m => m.DeleteAsync(docUser), Times.Once);
    }

    [Fact]
    public async Task GetDashboardMetricsAsync_CalculatesCorrectAggregationsAndRevenue()
    {
        // Arrange
        var summaryData = new DashboardMetricsSummaryData
        {
            TotalAppointments = 5,
            ActiveDoctorsCount = 2,
            PendingDoctorsCount = 1,
            CompletedVisitsCount = 2,
            TotalRevenueCollected = 850m,
            TotalPendingRevenue = 650m,
            MonthlyTrends = new List<MonthlyTrendSummaryData>
            {
                new MonthlyTrendSummaryData { Year = 2026, Month = 11, MonthLabel = "Nov 2026", CompletedCount = 2, PaidRevenue = 850m }
            },
            Specializations = new List<SpecializationSummaryData>
            {
                new SpecializationSummaryData { SpecializationName = "Cardiology", AppointmentCount = 3 },
                new SpecializationSummaryData { SpecializationName = "Dermatology", AppointmentCount = 2 }
            },
            TopDoctors = new List<TopDoctorSummaryData>(),
            Demographics = new DemographicsSummaryData()
        };

        _appointmentRepoMock.Setup(r => r.GetDashboardMetricsSummaryAsync(It.IsAny<DateTime>()))
            .ReturnsAsync(summaryData);

        // Act
        var result = await _service.GetDashboardMetricsAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        var metrics = result.Value!;
        metrics.TotalAppointments.Should().Be(5);
        metrics.ActiveDoctorsCount.Should().Be(2);
        metrics.PendingDoctorsCount.Should().Be(1);
        metrics.CompletedVisitsCount.Should().Be(2);

        // Paid revenue: 850
        metrics.TotalRevenueCollected.Should().Be(850m);
        // Pending revenue excludes Cancelled: 650
        metrics.TotalPendingRevenue.Should().Be(650m);

        metrics.MonthlyTrends.Should().NotBeEmpty();
        metrics.SpecializationBreakdown.Should().HaveCount(2);

        _appointmentRepoMock.Verify(r => r.GetAllAsync(), Times.Never);
        _doctorRepoMock.Verify(r => r.GetAllAsync(), Times.Never);
    }

    [Fact]
    public async Task GetDashboardMetricsAsync_WhenDatabaseAggregationReturnsData_MapsDirectlyWithoutLoadingAllEntities()
    {
        // Arrange
        var dbSummary = new DashboardMetricsSummaryData
        {
            TotalAppointments = 1500,
            ActiveDoctorsCount = 42,
            PendingDoctorsCount = 5,
            CompletedVisitsCount = 1200,
            TotalRevenueCollected = 350000m,
            TotalPendingRevenue = 25000m,
            TotalRefundedRevenue = 5000m,
            RefundedAppointmentsCount = 15,
            MonthlyTrends = new List<MonthlyTrendSummaryData>
            {
                new MonthlyTrendSummaryData { Year = 2026, Month = 11, MonthLabel = "Nov 2026", CompletedCount = 100, PaidRevenue = 30000m }
            },
            Specializations = new List<SpecializationSummaryData>
            {
                new SpecializationSummaryData { SpecializationName = "Cardiology", AppointmentCount = 450 }
            },
            TopDoctors = new List<TopDoctorSummaryData>
            {
                new TopDoctorSummaryData { DoctorId = 1, DoctorName = "Dr. Ahmed", SpecializationName = "Cardiology", TotalAppointments = 200, TotalRevenue = 50000m }
            },
            Demographics = new DemographicsSummaryData
            {
                TotalPatients = 800,
                MaleCount = 400,
                FemaleCount = 400,
                AgeUnder18Count = 80,
                Age18To35Count = 320,
                Age36To50Count = 240,
                AgeOver50Count = 160
            }
        };

        _appointmentRepoMock.Setup(r => r.GetDashboardMetricsSummaryAsync(new DateTime(2026, 11, 15)))
            .ReturnsAsync(dbSummary);

        // Act
        var result = await _service.GetDashboardMetricsAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        var metrics = result.Value!;
        metrics.TotalAppointments.Should().Be(1500);
        metrics.ActiveDoctorsCount.Should().Be(42);
        metrics.PendingDoctorsCount.Should().Be(5);
        metrics.CompletedVisitsCount.Should().Be(1200);
        metrics.TotalRevenueCollected.Should().Be(350000m);
        metrics.TotalPendingRevenue.Should().Be(25000m);
        metrics.TotalRefundedRevenue.Should().Be(5000m);
        metrics.RefundedAppointmentsCount.Should().Be(15);
        metrics.PlatformCommissionEarned.Should().Be(35000m);
        metrics.NetDoctorPayouts.Should().Be(315000m);

        metrics.MonthlyTrends.Should().HaveCount(1);
        metrics.MonthlyTrends[0].MonthLabel.Should().Be("Nov 2026");
        metrics.SpecializationBreakdown.Should().HaveCount(1);
        metrics.SpecializationBreakdown[0].SpecializationName.Should().Be("Cardiology");
        metrics.TopDoctors.Should().HaveCount(1);
        metrics.TopDoctors[0].DoctorName.Should().Be("Dr. Ahmed");
        metrics.Demographics.TotalPatients.Should().Be(800);

        // Verify that entity-loading methods were NEVER called (0 entities materialized into memory)
        _appointmentRepoMock.Verify(r => r.GetAllAsync(), Times.Never);
        _doctorRepoMock.Verify(r => r.GetAllAsync(), Times.Never);
        _patientRepoMock.Verify(r => r.GetAllAsync(), Times.Never);
    }

    [Fact]
    public async Task ExportAppointmentsCsvAsync_ReturnsUtf8BomWithEscapedColumns()
    {
        // Arrange
        var appts = new List<Appointment>
        {
            new Appointment
            {
                Id = 1,
                AppointmentDate = new DateTime(2026, 11, 15),
                StartTime = new TimeSpan(10, 0, 0),
                DoctorId = 1,
                PatientId = 2,
                Status = AppointmentStatus.Completed,
                ConsultationFee = 350,
                PaymentStatus = PaymentStatus.Paid
            }
        };

        var doc = new Doctor
        {
            Id = 1,
            Specialization = new Specialization { Name = "General, Medicine" }, // Contains comma!
            User = new ApplicationUser { FullName = "Dr. John \"Jack\" Smith" } // Contains quotes!
        };

        var pat = new Patient
        {
            Id = 2,
            User = new ApplicationUser { FullName = "Jane Doe" }
        };

        _appointmentRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(appts);
        _doctorRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Doctor> { doc });
        _doctorRepoMock.Setup(r => r.GetDoctorWithDetailsAsync(1)).ReturnsAsync(doc);
        _patientRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Patient> { pat });

        // Act
        var result = await _service.ExportAppointmentsCsvAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();

        var bytes = result.Value!;
        // Verify UTF-8 BOM preamble (0xEF, 0xBB, 0xBF)
        bytes.Length.Should().BeGreaterThan(3);
        bytes[0].Should().Be(0xEF);
        bytes[1].Should().Be(0xBB);
        bytes[2].Should().Be(0xBF);

        var csvText = Encoding.UTF8.GetString(bytes);
        csvText.Should().Contain("AppointmentId,Date,Time,Doctor,Specialization,Patient,Status,Fee,PaymentStatus");
        // Escaped quotes: ""Jack""
        csvText.Should().Contain("\"Dr. John \"\"Jack\"\" Smith\"");
        // Escaped commas: "General, Medicine"
        csvText.Should().Contain("\"General, Medicine\"");
        csvText.Should().Contain("Jane Doe");
        csvText.Should().Contain("350.00");
    }

    [Fact]
    public async Task ExportAppointmentsCsvAsync_NeutralizesFormulaInjectionCharacters()
    {
        // Arrange: Patient name starts with '=' and Doctor name starts with '@' (Formula Injection payload)
        var appts = new List<Appointment>
        {
            new Appointment
            {
                Id = 101,
                AppointmentDate = new DateTime(2026, 10, 1),
                StartTime = new TimeSpan(9, 30, 0),
                DoctorId = 5,
                PatientId = 6,
                Status = AppointmentStatus.Confirmed,
                ConsultationFee = 400,
                PaymentStatus = PaymentStatus.Unpaid
            }
        };

        var doc = new Doctor
        {
            Id = 5,
            Specialization = new Specialization { Name = "+Surgery" },
            User = new ApplicationUser { FullName = "@AttackerDoc" }
        };

        var pat = new Patient
        {
            Id = 6,
            User = new ApplicationUser { FullName = "=cmd|' /C calc'!A0" }
        };

        _appointmentRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(appts);
        _doctorRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Doctor> { doc });
        _doctorRepoMock.Setup(r => r.GetDoctorWithDetailsAsync(5)).ReturnsAsync(doc);
        _patientRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Patient> { pat });

        // Act
        var result = await _service.ExportAppointmentsCsvAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        var csvText = Encoding.UTF8.GetString(result.Value!);
        // Leading '=' neutralized to "'="
        csvText.Should().Contain("'=cmd|' /C calc'!A0");
        // Leading '@' neutralized to "'@"
        csvText.Should().Contain("'@AttackerDoc");
        // Leading '+' neutralized to "'+Surgery"
        csvText.Should().Contain("'+Surgery");
    }

    [Fact]
    public async Task GetDashboardMetricsAsync_CalculatesTopDoctorsAndDemographicsCorrectly()
    {
        // Arrange
        var summaryData = new DashboardMetricsSummaryData
        {
            TopDoctors = new List<TopDoctorSummaryData>
            {
                new TopDoctorSummaryData { DoctorId = 1, DoctorName = "Dr. Ahmed", SpecializationName = "Cardiology", TotalAppointments = 2, TotalRevenue = 1000m },
                new TopDoctorSummaryData { DoctorId = 2, DoctorName = "Dr. Sara", SpecializationName = "Dermatology", TotalAppointments = 1, TotalRevenue = 300m }
            },
            Demographics = new DemographicsSummaryData
            {
                TotalPatients = 2,
                MaleCount = 1,
                FemaleCount = 1,
                AgeUnder18Count = 1,
                Age36To50Count = 1
            }
        };

        _appointmentRepoMock.Setup(r => r.GetDashboardMetricsSummaryAsync(It.IsAny<DateTime>()))
            .ReturnsAsync(summaryData);

        // Act
        var result = await _service.GetDashboardMetricsAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.TopDoctors.Should().HaveCount(2);
        result.Value.TopDoctors[0].DoctorName.Should().Be("Dr. Ahmed");
        result.Value.TopDoctors[0].TotalAppointments.Should().Be(2);
        result.Value.TopDoctors[0].TotalRevenue.Should().Be(1000m);

        result.Value.Demographics.TotalPatients.Should().Be(2);
        result.Value.Demographics.MaleCount.Should().Be(1);
        result.Value.Demographics.FemaleCount.Should().Be(1);
        result.Value.Demographics.AgeUnder18Count.Should().Be(1);
        result.Value.Demographics.Age36To50Count.Should().Be(1);

        _appointmentRepoMock.Verify(r => r.GetAllAsync(), Times.Never);
        _doctorRepoMock.Verify(r => r.GetAllAsync(), Times.Never);
        _patientRepoMock.Verify(r => r.GetAllAsync(), Times.Never);
    }

    [Fact]
    public async Task GetPatientsAsync_WithSearchTerm_ReturnsMatchingPatients()
    {
        // Arrange
        var patients = new List<Patient>
        {
            new Patient { Id = 1, UserId = "u1", Gender = "Male", DateOfBirth = new DateTime(1995, 1, 1), Allergies = "Penicillin", User = new ApplicationUser { FullName = "Mostafa Hassan", Email = "mostafa@test.com" } },
            new Patient { Id = 2, UserId = "u2", Gender = "Female", DateOfBirth = new DateTime(2000, 1, 1), Allergies = "None", User = new ApplicationUser { FullName = "Salma Khaled", Email = "salma@test.com" } }
        };

        _patientRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(patients);
        _appointmentRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Appointment>());

        // Act
        var result = await _service.GetPatientsAsync("Mostafa");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value![0].FullName.Should().Be("Mostafa Hassan");
        result.Value[0].Allergies.Should().Be("Penicillin");
    }

    [Fact]
    public async Task TogglePatientLockoutAsync_PatientNotFound_ReturnsFailure()
    {
        // Arrange
        _patientRepoMock.Setup(r => r.GetByIdAsync(999)).ReturnsAsync((Patient?)null);

        // Act
        var result = await _service.TogglePatientLockoutAsync(999, true);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task TogglePatientLockoutAsync_WhenUnlock_ClearsLockoutEndDateAndResetsAccessFailedCount()
    {
        // Arrange
        var userStore = new Mock<IUserStore<ApplicationUser>>();
        var userManagerMock = new Mock<UserManager<ApplicationUser>>(
            userStore.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        var serviceWithUserManager = new AdminService(
            _uowMock.Object,
            _emailServiceMock.Object,
            _clinicClockMock.Object,
            _loggerMock.Object,
            userManagerMock.Object);

        var patientUser = new ApplicationUser { Id = "user-patient-42", Email = "pat@medicare.com" };
        var patient = new Patient { Id = 42, UserId = "user-patient-42" };

        _patientRepoMock.Setup(r => r.GetByIdAsync(42)).ReturnsAsync(patient);
        userManagerMock.Setup(m => m.FindByIdAsync("user-patient-42")).ReturnsAsync(patientUser);
        userManagerMock.Setup(m => m.SetLockoutEndDateAsync(patientUser, null)).ReturnsAsync(IdentityResult.Success);
        userManagerMock.Setup(m => m.ResetAccessFailedCountAsync(patientUser)).ReturnsAsync(IdentityResult.Success);

        // Act: Admin unlocks the patient account (lockout: false)
        var result = await serviceWithUserManager.TogglePatientLockoutAsync(42, lockout: false);

        // Assert
        result.IsSuccess.Should().BeTrue();
        userManagerMock.Verify(m => m.SetLockoutEndDateAsync(patientUser, null), Times.Once);
        userManagerMock.Verify(m => m.ResetAccessFailedCountAsync(patientUser), Times.Once);
    }

    [Fact]
    public async Task CreatePharmacistAsync_ValidData_CreatesUserWithRoleAndMandatoryPasswordChangeClaim()
    {
        // Arrange
        var userStore = new Mock<IUserStore<ApplicationUser>>();
        var userManagerMock = new Mock<UserManager<ApplicationUser>>(
            userStore.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        var service = new AdminService(
            _uowMock.Object,
            _emailServiceMock.Object,
            _clinicClockMock.Object,
            _loggerMock.Object,
            userManagerMock.Object);

        var dto = new MediCare.Services.DTOs.CreatePharmacistDto
        {
            FullName = "Pharmacist Tarek",
            Email = "tarek@medicare.com",
            Password = "P@ssword123!"
        };

        userManagerMock.Setup(m => m.FindByEmailAsync("tarek@medicare.com"))
            .ReturnsAsync((ApplicationUser?)null);
        userManagerMock.Setup(m => m.CreateAsync(It.IsAny<ApplicationUser>(), "P@ssword123!"))
            .ReturnsAsync(IdentityResult.Success);
        userManagerMock.Setup(m => m.AddToRoleAsync(It.IsAny<ApplicationUser>(), "Pharmacist"))
            .ReturnsAsync(IdentityResult.Success);
        userManagerMock.Setup(m => m.AddClaimAsync(It.IsAny<ApplicationUser>(), It.Is<System.Security.Claims.Claim>(c => c.Type == "MustChangePassword")))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await service.CreatePharmacistAsync(dto, "admin-guid-1");

        // Assert
        result.IsSuccess.Should().BeTrue();
        userManagerMock.Verify(m => m.CreateAsync(It.Is<ApplicationUser>(u => u.Email == "tarek@medicare.com" && u.FullName == "Pharmacist Tarek"), "P@ssword123!"), Times.Once);
        userManagerMock.Verify(m => m.AddToRoleAsync(It.IsAny<ApplicationUser>(), "Pharmacist"), Times.Once);
        userManagerMock.Verify(m => m.AddClaimAsync(It.IsAny<ApplicationUser>(), It.Is<System.Security.Claims.Claim>(c => c.Type == "MustChangePassword" && c.Value == "true")), Times.Once);
    }

    [Fact]
    public async Task CreatePharmacistAsync_DuplicateEmail_ReturnsFailure()
    {
        // Arrange
        var userStore = new Mock<IUserStore<ApplicationUser>>();
        var userManagerMock = new Mock<UserManager<ApplicationUser>>(
            userStore.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        var service = new AdminService(
            _uowMock.Object,
            _emailServiceMock.Object,
            _clinicClockMock.Object,
            _loggerMock.Object,
            userManagerMock.Object);

        var dto = new MediCare.Services.DTOs.CreatePharmacistDto
        {
            FullName = "Pharmacist Existing",
            Email = "duplicate@medicare.com",
            Password = "P@ssword123!"
        };

        userManagerMock.Setup(m => m.FindByEmailAsync("duplicate@medicare.com"))
            .ReturnsAsync(new ApplicationUser { Email = "duplicate@medicare.com" });

        // Act
        var result = await service.CreatePharmacistAsync(dto, "admin-guid-1");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("already exists");
        userManagerMock.Verify(m => m.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CreatePharmacistAsync_WeakPassword_ReturnsFailureWithIdentityErrors()
    {
        // Arrange
        var userStore = new Mock<IUserStore<ApplicationUser>>();
        var userManagerMock = new Mock<UserManager<ApplicationUser>>(
            userStore.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        var service = new AdminService(
            _uowMock.Object,
            _emailServiceMock.Object,
            _clinicClockMock.Object,
            _loggerMock.Object,
            userManagerMock.Object);

        var dto = new MediCare.Services.DTOs.CreatePharmacistDto
        {
            FullName = "Pharmacist Weak",
            Email = "weak@medicare.com",
            Password = "weak"
        };

        userManagerMock.Setup(m => m.FindByEmailAsync("weak@medicare.com"))
            .ReturnsAsync((ApplicationUser?)null);
        userManagerMock.Setup(m => m.CreateAsync(It.IsAny<ApplicationUser>(), "weak"))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Password too short" }));

        // Act
        var result = await service.CreatePharmacistAsync(dto, "admin-guid-1");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Password too short");
        userManagerMock.Verify(m => m.AddToRoleAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task GetPharmacistsAsync_ReturnsMappedPharmacistsList()
    {
        // Arrange
        var userStore = new Mock<IUserStore<ApplicationUser>>();
        var userManagerMock = new Mock<UserManager<ApplicationUser>>(
            userStore.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        var pharmacists = new List<ApplicationUser>
        {
            new ApplicationUser
            {
                Id = "pharm-1",
                FullName = "Dr. Mohamed Said",
                Email = "m.said@medicare.com",
                PhoneNumber = "+201011112222",
                CreatedAt = new DateTime(2026, 1, 15),
                LockoutEnd = null
            },
            new ApplicationUser
            {
                Id = "pharm-2",
                FullName = "Dr. Laila Nour",
                Email = "laila@medicare.com",
                PhoneNumber = "+201022223333",
                CreatedAt = new DateTime(2026, 2, 20),
                LockoutEnd = DateTimeOffset.UtcNow.AddMinutes(10)
            }
        };

        userManagerMock.Setup(m => m.GetUsersInRoleAsync("Pharmacist"))
            .ReturnsAsync(pharmacists);

        var service = new AdminService(
            _uowMock.Object,
            _emailServiceMock.Object,
            _clinicClockMock.Object,
            _loggerMock.Object,
            userManagerMock.Object);

        // Act
        var result = await service.GetPharmacistsAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Count.Should().Be(2);

        var locked = result.Value.First(p => p.Id == "pharm-2");
        locked.FullName.Should().Be("Dr. Laila Nour");
        locked.IsLockedOut.Should().BeTrue();

        var active = result.Value.First(p => p.Id == "pharm-1");
        active.FullName.Should().Be("Dr. Mohamed Said");
        active.IsLockedOut.Should().BeFalse();
    }

    [Fact]
    public async Task CreateSpecializationAsync_ValidData_AddsEntityAndReturnsId()
    {
        // Arrange
        var dto = new MediCare.Services.DTOs.CreateSpecializationDto { Name = "Neurology", Description = "Brain and nerves" };
        _specRepoMock.Setup(r => r.FindAsync(It.IsAny<Expression<Func<Specialization, bool>>>()))
            .ReturnsAsync(new List<Specialization>());
        _uowMock.Setup(u => u.CommitAsync()).ReturnsAsync(1);

        // Act
        var result = await _service.CreateSpecializationAsync(dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _specRepoMock.Verify(r => r.AddAsync(It.Is<Specialization>(s => s.Name == "Neurology")), Times.Once);
        _uowMock.Verify(u => u.CommitAsync(), Times.Once);
    }

    [Fact]
    public async Task CreateSpecializationAsync_DuplicateName_ReturnsFailure()
    {
        // Arrange
        var dto = new MediCare.Services.DTOs.CreateSpecializationDto { Name = "Cardiology" };
        _specRepoMock.Setup(r => r.FindAsync(It.IsAny<Expression<Func<Specialization, bool>>>()))
            .ReturnsAsync(new List<Specialization> { new Specialization { Id = 1, Name = "Cardiology" } });

        // Act
        var result = await _service.CreateSpecializationAsync(dto);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("already exists");
    }

    [Fact]
    public async Task DeleteSpecializationAsync_WithAssignedDoctors_ReturnsFailure()
    {
        // Arrange
        var spec = new Specialization { Id = 5, Name = "Pediatrics" };
        _specRepoMock.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(spec);
        _doctorRepoMock.Setup(r => r.FindAsync(It.IsAny<Expression<Func<Doctor, bool>>>()))
            .ReturnsAsync(new List<Doctor> { new Doctor { Id = 10, SpecializationId = 5 } });

        // Act
        var result = await _service.DeleteSpecializationAsync(5);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Cannot delete specialization");
        _specRepoMock.Verify(r => r.Delete(It.IsAny<Specialization>()), Times.Never);
    }

    [Fact]
    public async Task DeleteSpecializationAsync_WithoutAssignedDoctors_DeletesSuccessfully()
    {
        // Arrange
        var spec = new Specialization { Id = 6, Name = "Empty Spec" };
        _specRepoMock.Setup(r => r.GetByIdAsync(6)).ReturnsAsync(spec);
        _doctorRepoMock.Setup(r => r.FindAsync(It.IsAny<Expression<Func<Doctor, bool>>>()))
            .ReturnsAsync(new List<Doctor>());
        _uowMock.Setup(u => u.CommitAsync()).ReturnsAsync(1);

        // Act
        var result = await _service.DeleteSpecializationAsync(6);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _specRepoMock.Verify(r => r.Delete(spec), Times.Once);
        _uowMock.Verify(u => u.CommitAsync(), Times.Once);
    }

    [Fact]
    public async Task ExportAppointmentsExcelAsync_ReturnsValidZipArchiveBytes()
    {
        // Arrange
        var appts = new List<Appointment>
        {
            new Appointment
            {
                Id = 1,
                AppointmentDate = new DateTime(2026, 10, 1),
                StartTime = new TimeSpan(9, 0, 0),
                DoctorId = 1,
                PatientId = 1,
                Status = AppointmentStatus.Confirmed,
                ConsultationFee = 350,
                PaymentStatus = PaymentStatus.Paid
            }
        };

        var doc = new Doctor { Id = 1, Specialization = new Specialization { Name = "Cardiology" }, User = new ApplicationUser { FullName = "Dr. Magdi" } };
        var pat = new Patient { Id = 1, User = new ApplicationUser { FullName = "Ahmed Tarek" } };

        _appointmentRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(appts);
        _doctorRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Doctor> { doc });
        _doctorRepoMock.Setup(r => r.GetDoctorWithDetailsAsync(1)).ReturnsAsync(doc);
        _patientRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Patient> { pat });

        // Act
        var result = await _service.ExportAppointmentsExcelAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Length.Should().BeGreaterThan(50);

        // Standard Zip / OOXML signature: 'PK\x03\x04'
        result.Value[0].Should().Be(0x50);
        result.Value[1].Should().Be(0x4B);
        result.Value[2].Should().Be(0x03);
        result.Value[3].Should().Be(0x04);
    }

    [Fact]
    public async Task ExportAppointmentsPdfAsync_ReturnsValidPdfBytes()
    {
        // Arrange
        var appts = new List<Appointment>
        {
            new Appointment
            {
                Id = 1,
                AppointmentDate = new DateTime(2026, 10, 1),
                StartTime = new TimeSpan(9, 0, 0),
                DoctorId = 1,
                PatientId = 1,
                Status = AppointmentStatus.Confirmed,
                ConsultationFee = 350,
                PaymentStatus = PaymentStatus.Paid
            }
        };

        var doc = new Doctor { Id = 1, Specialization = new Specialization { Name = "Cardiology" }, User = new ApplicationUser { FullName = "Dr. Magdi" } };
        var pat = new Patient { Id = 1, User = new ApplicationUser { FullName = "Ahmed Tarek" } };

        _appointmentRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(appts);
        _doctorRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Doctor> { doc });
        _doctorRepoMock.Setup(r => r.GetDoctorWithDetailsAsync(1)).ReturnsAsync(doc);
        _patientRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Patient> { pat });

        // Act
        var result = await _service.ExportAppointmentsPdfAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Length.Should().BeGreaterThan(100);

        var pdfString = System.Text.Encoding.ASCII.GetString(result.Value!);
        pdfString.Should().StartWith("%PDF-1.4");
        pdfString.Should().Contain("MediCare");
        pdfString.Should().Contain("%%EOF");
    }

    [Fact]
    public async Task GetDashboardMetricsAsync_CalculatesRevenueWithDiscountsAndRefundExclusion()
    {
        // Arrange
        var summaryData = new DashboardMetricsSummaryData
        {
            TotalRevenueCollected = 900m,
            TotalRefundedRevenue = 450m,
            RefundedAppointmentsCount = 1,
            TotalPendingRevenue = 300m,
            MonthlyTrends = new List<MonthlyTrendSummaryData>(),
            Specializations = new List<SpecializationSummaryData>(),
            TopDoctors = new List<TopDoctorSummaryData>(),
            Demographics = new DemographicsSummaryData()
        };

        _appointmentRepoMock.Setup(r => r.GetDashboardMetricsSummaryAsync(It.IsAny<DateTime>()))
            .ReturnsAsync(summaryData);

        // Act
        var result = await _service.GetDashboardMetricsAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        var metrics = result.Value!;

        // Collected revenue: 900
        metrics.TotalRevenueCollected.Should().Be(900m);

        // Refunded revenue: 450 (1 appointment)
        metrics.TotalRefundedRevenue.Should().Be(450m);
        metrics.RefundedAppointmentsCount.Should().Be(1);

        // Pending revenue: 300
        metrics.TotalPendingRevenue.Should().Be(300m);

        _appointmentRepoMock.Verify(r => r.GetAllAsync(), Times.Never);
    }

    [Fact]
    public async Task GetPatientsPagedAsync_ReturnsPagedResultsWithClampedSizesAndCorrectTotals()
    {
        // Arrange
        var patients = new List<Patient>
        {
            new Patient { Id = 1, UserId = "u1", Gender = "Male", DateOfBirth = new DateTime(1995, 1, 1), Allergies = "Penicillin", User = new ApplicationUser { FullName = "Adam Smith", Email = "adam@test.com" } },
            new Patient { Id = 2, UserId = "u2", Gender = "Female", DateOfBirth = new DateTime(2000, 1, 1), Allergies = "None", User = new ApplicationUser { FullName = "Beatrix Potter", Email = "beatrix@test.com" } },
            new Patient { Id = 3, UserId = "u3", Gender = "Male", DateOfBirth = new DateTime(1985, 5, 5), Allergies = "Dust", User = new ApplicationUser { FullName = "Charles Dickens", Email = "charles@test.com" } }
        };

        _patientRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(patients);
        _appointmentRepoMock.Setup(r => r.GetAppointmentCountsByPatientAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new Dictionary<int, int> { { 2, 5 } });

        // Act - Request page 2 with page size 1
        var result = await _service.GetPatientsPagedAsync(searchTerm: null, page: 2, pageSize: 1);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var paged = result.Value!;
        paged.TotalCount.Should().Be(3);
        paged.PageNumber.Should().Be(2);
        paged.PageSize.Should().Be(1);
        paged.TotalPages.Should().Be(3);
        paged.HasPreviousPage.Should().BeTrue();
        paged.HasNextPage.Should().BeTrue();
        paged.Items.Should().HaveCount(1);
        paged.Items[0].FullName.Should().Be("Beatrix Potter");
        paged.Items[0].TotalAppointments.Should().Be(5);
    }

    [Fact]
    public async Task GetPatientsPagedAsync_InvalidPageAndSize_ClampsSensibly()
    {
        // Arrange
        var patients = new List<Patient>
        {
            new Patient { Id = 1, UserId = "u1", Gender = "Male", DateOfBirth = new DateTime(1995, 1, 1), User = new ApplicationUser { FullName = "Adam Smith", Email = "adam@test.com" } }
        };

        _patientRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(patients);
        _appointmentRepoMock.Setup(r => r.GetAppointmentCountsByPatientAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new Dictionary<int, int> { { 1, 2 } });

        // Act - pass page 0 and pageSize 200
        var result = await _service.GetPatientsPagedAsync(searchTerm: null, page: 0, pageSize: 200);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var paged = result.Value!;
        paged.PageNumber.Should().Be(1); // clamped from 0 to 1
        paged.PageSize.Should().Be(100); // clamped from 200 to 100
        paged.Items.Should().HaveCount(1);
    }
}
