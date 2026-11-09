using FluentAssertions;
using MediCare.Data.Entities;
using MediCare.Data.Enums;
using MediCare.Data.Repositories;
using MediCare.Data.UnitOfWork;
using MediCare.Services.Common;
using MediCare.Services.Contracts;
using MediCare.Services.DTOs;
using MediCare.Services.Factories;
using MediCare.Services.Implementations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace MediCare.Tests.Services;

public class AppointmentServiceTests
{
    private readonly Mock<IUnitOfWork> _uowMock;
    private readonly Mock<IDoctorRepository> _doctorRepoMock;
    private readonly Mock<IAppointmentRepository> _appointmentRepoMock;
    private readonly Mock<IRepository<Patient>> _patientRepoMock;
    private readonly Mock<INotificationService> _notificationServiceMock;
    private readonly Mock<IEmailService> _emailServiceMock;
    private readonly Mock<IClinicClock> _clinicClockMock;
    private readonly Mock<ILogger<AppointmentService>> _loggerMock;
    private readonly AppointmentFactory _factory;
    private readonly AppointmentService _service;

    public AppointmentServiceTests()
    {
        _uowMock = new Mock<IUnitOfWork>();
        _doctorRepoMock = new Mock<IDoctorRepository>();
        _appointmentRepoMock = new Mock<IAppointmentRepository>();
        _patientRepoMock = new Mock<IRepository<Patient>>();
        _notificationServiceMock = new Mock<INotificationService>();
        _emailServiceMock = new Mock<IEmailService>();
        _clinicClockMock = new Mock<IClinicClock>();
        _loggerMock = new Mock<ILogger<AppointmentService>>();
        _factory = new AppointmentFactory();

        _uowMock.Setup(u => u.Doctors).Returns(_doctorRepoMock.Object);
        _uowMock.Setup(u => u.Appointments).Returns(_appointmentRepoMock.Object);
        _uowMock.Setup(u => u.Patients).Returns(_patientRepoMock.Object);

        // Fixed clinic clock for testing: 2026-11-15 08:00:00 (Sunday)
        _clinicClockMock.Setup(c => c.Now).Returns(new DateTime(2026, 11, 15, 8, 0, 0));
        _clinicClockMock.Setup(c => c.Today).Returns(new DateTime(2026, 11, 15));

        _service = new AppointmentService(
            _uowMock.Object,
            _factory,
            _notificationServiceMock.Object,
            _clinicClockMock.Object,
            _loggerMock.Object,
            _emailServiceMock.Object);
    }

    private Doctor CreateValidDoctor(int doctorId = 1)
    {
        return new Doctor
        {
            Id = doctorId,
            UserId = "doc_user_1",
            IsApproved = true,
            ConsultationFee = 300,
            SlotDurationMinutes = 30,
            User = new ApplicationUser { Id = "doc_user_1", FullName = "Dr. Ahmed Mahmoud" },
            WorkingHours = new List<WorkingHours>
            {
                new()
                {
                    DoctorId = doctorId,
                    DayOfWeek = DayOfWeek.Sunday,
                    StartTime = new TimeSpan(9, 0, 0),
                    EndTime = new TimeSpan(17, 0, 0)
                }
            },
            Leaves = new List<DoctorLeave>()
        };
    }

    private Patient CreateValidPatient(int patientId = 1)
    {
        return new Patient
        {
            Id = patientId,
            UserId = "pat_user_1",
            User = new ApplicationUser { Id = "pat_user_1", FullName = "Omar Khaled" }
        };
    }

    [Fact]
    public async Task BookAppointment_ShouldSucceed_WhenValidRequest()
    {
        // Arrange: Valid appointment at 10:00 AM on 2026-11-15 (Sunday)
        var doctor = CreateValidDoctor();
        var patient = CreateValidPatient();

        _doctorRepoMock.Setup(d => d.GetDoctorWithScheduleAndLeavesAsync(1)).ReturnsAsync(doctor);
        _patientRepoMock.Setup(p => p.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Patient, bool>>>()))
            .ReturnsAsync(new List<Patient> { patient });
        _patientRepoMock.Setup(p => p.GetByIdAsync(1)).ReturnsAsync(patient);
        _appointmentRepoMock.Setup(a => a.HasPatientConflictAsync(1, new DateTime(2026, 11, 15), new TimeSpan(10, 0, 0), It.IsAny<TimeSpan>()))
            .ReturnsAsync(false);
        _appointmentRepoMock.Setup(a => a.HasConflictAsync(1, new DateTime(2026, 11, 15), new TimeSpan(10, 0, 0), It.IsAny<TimeSpan>()))
            .ReturnsAsync(false);
        _uowMock.Setup(u => u.CommitAsync()).ReturnsAsync(1);

        var dto = new BookingRequestDto
        {
            DoctorId = 1,
            PatientId = 1,
            AppointmentDate = new DateTime(2026, 11, 15),
            StartTime = new TimeSpan(10, 0, 0),
            Type = AppointmentType.Consultation,
            Notes = "Checkup"
        };

        // Act
        var result = await _service.BookAppointmentAsync(dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _uowMock.Verify(u => u.Appointments.AddAsync(It.Is<Appointment>(a =>
            a.DoctorId == 1 &&
            a.PatientId == 1 &&
            a.Status == AppointmentStatus.Pending &&
            a.ConsultationFee == 300 &&
            a.PaymentStatus == PaymentStatus.Unpaid)), Times.Once);
        _notificationServiceMock.Verify(n => n.SendNotificationAsync(
            "doc_user_1",
            "New Appointment Request",
            It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task BookAppointment_ShouldFail_WhenDoctorNotApproved()
    {
        // Arrange: Doctor is not approved
        var doctor = CreateValidDoctor();
        doctor.IsApproved = false;

        _doctorRepoMock.Setup(d => d.GetDoctorWithScheduleAndLeavesAsync(1)).ReturnsAsync(doctor);

        var dto = new BookingRequestDto
        {
            DoctorId = 1,
            PatientId = 1,
            AppointmentDate = new DateTime(2026, 11, 15),
            StartTime = new TimeSpan(10, 0, 0)
        };

        // Act
        var result = await _service.BookAppointmentAsync(dto);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("pending administrative approval");
    }

    [Fact]
    public async Task BookAppointment_ShouldFail_WhenLeadTimeUnder30Minutes()
    {
        // Arrange: Clock is 08:00, booking at 08:20 (only 20 min in advance)
        var doctor = CreateValidDoctor();
        var patient = CreateValidPatient();

        _doctorRepoMock.Setup(d => d.GetDoctorWithScheduleAndLeavesAsync(1)).ReturnsAsync(doctor);
        _patientRepoMock.Setup(p => p.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Patient, bool>>>()))
            .ReturnsAsync(new List<Patient> { patient });
        _patientRepoMock.Setup(p => p.GetByIdAsync(1)).ReturnsAsync(patient);

        var dto = new BookingRequestDto
        {
            DoctorId = 1,
            PatientId = 1,
            AppointmentDate = new DateTime(2026, 11, 15),
            StartTime = new TimeSpan(8, 20, 0)
        };

        // Act
        var result = await _service.BookAppointmentAsync(dto);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("at least 30 minutes in advance");
    }

    [Fact]
    public async Task BookAppointment_ShouldFail_WhenDateIsMoreThan30DaysInAdvance()
    {
        // Arrange
        var doctor = CreateValidDoctor();
        var patient = CreateValidPatient();

        _doctorRepoMock.Setup(d => d.GetDoctorWithScheduleAndLeavesAsync(1)).ReturnsAsync(doctor);
        _patientRepoMock.Setup(p => p.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Patient, bool>>>()))
            .ReturnsAsync(new List<Patient> { patient });
        _patientRepoMock.Setup(p => p.GetByIdAsync(1)).ReturnsAsync(patient);

        var dto = new BookingRequestDto
        {
            DoctorId = 1,
            PatientId = 1,
            AppointmentDate = new DateTime(2026, 12, 20), // 35 days in advance from clock (2026-11-15)
            StartTime = new TimeSpan(10, 0, 0)
        };

        // Act
        var result = await _service.BookAppointmentAsync(dto);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("30 days");
    }

    [Fact]
    public async Task BookAppointment_ShouldFail_WhenDoctorOnLeave()
    {
        // Arrange: Doctor has leave on Nov 15
        var doctor = CreateValidDoctor();
        doctor.Leaves.Add(new DoctorLeave
        {
            StartDate = new DateTime(2026, 11, 15),
            EndDate = new DateTime(2026, 11, 15),
            Reason = "Conference"
        });
        var patient = CreateValidPatient();

        _doctorRepoMock.Setup(d => d.GetDoctorWithScheduleAndLeavesAsync(1)).ReturnsAsync(doctor);
        _patientRepoMock.Setup(p => p.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Patient, bool>>>()))
            .ReturnsAsync(new List<Patient> { patient });
        _patientRepoMock.Setup(p => p.GetByIdAsync(1)).ReturnsAsync(patient);

        var dto = new BookingRequestDto
        {
            DoctorId = 1,
            PatientId = 1,
            AppointmentDate = new DateTime(2026, 11, 15),
            StartTime = new TimeSpan(10, 0, 0)
        };

        // Act
        var result = await _service.BookAppointmentAsync(dto);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Doctor is on leave");
    }

    [Fact]
    public async Task BookAppointment_ShouldFail_WhenOutsideDoctorWorkingHours()
    {
        // Arrange: Doctor works 09:00 - 17:00, booking requested at 18:00
        var doctor = CreateValidDoctor();
        var patient = CreateValidPatient();

        _doctorRepoMock.Setup(d => d.GetDoctorWithScheduleAndLeavesAsync(1)).ReturnsAsync(doctor);
        _patientRepoMock.Setup(p => p.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Patient, bool>>>()))
            .ReturnsAsync(new List<Patient> { patient });
        _patientRepoMock.Setup(p => p.GetByIdAsync(1)).ReturnsAsync(patient);

        var dto = new BookingRequestDto
        {
            DoctorId = 1,
            PatientId = 1,
            AppointmentDate = new DateTime(2026, 11, 15),
            StartTime = new TimeSpan(18, 0, 0)
        };

        // Act
        var result = await _service.BookAppointmentAsync(dto);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("outside the doctor's scheduled clinic hours");
    }

    [Fact]
    public async Task BookAppointment_ShouldFail_WhenStartTimeDoesNotAlignWithDoctorSlotSchedule()
    {
        // Arrange: Shift starts at 09:00:00, slot duration is 30 mins.
        // Patient attempts booking at 09:15:00 (unaligned with the 30-min grid)
        var doctor = CreateValidDoctor();
        var patient = CreateValidPatient();

        _doctorRepoMock.Setup(d => d.GetDoctorWithScheduleAndLeavesAsync(1)).ReturnsAsync(doctor);
        _patientRepoMock.Setup(p => p.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Patient, bool>>>()))
            .ReturnsAsync(new List<Patient> { patient });
        _patientRepoMock.Setup(p => p.GetByIdAsync(1)).ReturnsAsync(patient);

        var dto = new BookingRequestDto
        {
            DoctorId = 1,
            PatientId = 1,
            AppointmentDate = new DateTime(2026, 11, 15),
            StartTime = new TimeSpan(9, 15, 0),
            Type = AppointmentType.Consultation
        };

        // Act
        var result = await _service.BookAppointmentAsync(dto);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("align with the doctor's appointment slot schedule");
        _uowMock.Verify(u => u.CommitAsync(), Times.Never);
    }

    [Fact]
    public async Task BookAppointment_ShouldFail_WhenPatientHasDoubleBooking()
    {
        // Arrange: Patient already booked at same time
        var doctor = CreateValidDoctor();
        var patient = CreateValidPatient();

        _doctorRepoMock.Setup(d => d.GetDoctorWithScheduleAndLeavesAsync(1)).ReturnsAsync(doctor);
        _patientRepoMock.Setup(p => p.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Patient, bool>>>()))
            .ReturnsAsync(new List<Patient> { patient });
        _patientRepoMock.Setup(p => p.GetByIdAsync(1)).ReturnsAsync(patient);
        _appointmentRepoMock.Setup(a => a.HasPatientConflictAsync(1, new DateTime(2026, 11, 15), new TimeSpan(10, 0, 0), It.IsAny<TimeSpan>()))
            .ReturnsAsync(true);

        var dto = new BookingRequestDto
        {
            DoctorId = 1,
            PatientId = 1,
            AppointmentDate = new DateTime(2026, 11, 15),
            StartTime = new TimeSpan(10, 0, 0)
        };

        // Act
        var result = await _service.BookAppointmentAsync(dto);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("already have an active appointment scheduled");
    }

    [Fact]
    public async Task BookAppointment_ShouldCatchDbUpdateException_AndReturnFriendlyConflictMessage()
    {
        // Arrange: DbUpdateException thrown during commit (Filtered Unique Index violation simulation)
        var doctor = CreateValidDoctor();
        var patient = CreateValidPatient();

        _doctorRepoMock.Setup(d => d.GetDoctorWithScheduleAndLeavesAsync(1)).ReturnsAsync(doctor);
        _patientRepoMock.Setup(p => p.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Patient, bool>>>()))
            .ReturnsAsync(new List<Patient> { patient });
        _patientRepoMock.Setup(p => p.GetByIdAsync(1)).ReturnsAsync(patient);
        _appointmentRepoMock.Setup(a => a.HasPatientConflictAsync(1, new DateTime(2026, 11, 15), new TimeSpan(10, 0, 0), It.IsAny<TimeSpan>()))
            .ReturnsAsync(false);
        _appointmentRepoMock.Setup(a => a.HasConflictAsync(1, new DateTime(2026, 11, 15), new TimeSpan(10, 0, 0), It.IsAny<TimeSpan>()))
            .ReturnsAsync(false);

        _uowMock.Setup(u => u.CommitAsync()).ThrowsAsync(new DbUpdateException("Violation of unique index", (Exception)null!));

        var dto = new BookingRequestDto
        {
            DoctorId = 1,
            PatientId = 1,
            AppointmentDate = new DateTime(2026, 11, 15),
            StartTime = new TimeSpan(10, 0, 0)
        };

        // Act
        var result = await _service.BookAppointmentAsync(dto);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("This slot was just booked by another patient");
    }

    [Fact]
    public async Task ConfirmAppointment_ShouldTransitionFromPendingToConfirmed_AndNotifyPatient()
    {
        // Arrange
        var appointment = new Appointment
        {
            Id = 10,
            DoctorId = 1,
            Status = AppointmentStatus.Pending,
            AppointmentDate = new DateTime(2026, 11, 16),
            StartTime = new TimeSpan(10, 0, 0),
            Doctor = CreateValidDoctor(1),
            Patient = CreateValidPatient(1)
        };

        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(10)).ReturnsAsync(appointment);
        _uowMock.Setup(u => u.CommitAsync()).ReturnsAsync(1);

        // Act
        var result = await _service.ConfirmAppointmentAsync(10, doctorId: 1);

        // Assert
        result.IsSuccess.Should().BeTrue();
        appointment.Status.Should().Be(AppointmentStatus.Confirmed);
        _uowMock.Verify(u => u.Appointments.Update(appointment), Times.Once);
        _notificationServiceMock.Verify(n => n.SendNotificationAsync(
            "pat_user_1",
            "Appointment Confirmed",
            It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task ConfirmAppointment_ShouldFail_WhenDoctorDoesNotOwnAppointment()
    {
        // Arrange: Doctor 2 attempts to confirm Doctor 1's appointment
        var appointment = new Appointment
        {
            Id = 10,
            DoctorId = 1,
            Status = AppointmentStatus.Pending
        };

        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(10)).ReturnsAsync(appointment);

        // Act
        var result = await _service.ConfirmAppointmentAsync(10, doctorId: 2);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Forbidden");
    }

    [Fact]
    public async Task ConfirmAppointment_ShouldFail_WhenStatusIsNotPending()
    {
        // Arrange: Already Confirmed
        var appointment = new Appointment
        {
            Id = 10,
            DoctorId = 1,
            Status = AppointmentStatus.Confirmed
        };

        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(10)).ReturnsAsync(appointment);

        // Act
        var result = await _service.ConfirmAppointmentAsync(10, doctorId: 1);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Cannot confirm appointment");
    }

    [Fact]
    public async Task CancelAppointment_ShouldSucceed_WhenPatientCancelsMoreThan2HoursInAdvance()
    {
        // Arrange: Clock is 08:00, appointment is at 11:00 (3 hours in advance: 3 > 2)
        var appointment = new Appointment
        {
            Id = 15,
            DoctorId = 1,
            PatientId = 1,
            Status = AppointmentStatus.Pending,
            AppointmentDate = new DateTime(2026, 11, 15),
            StartTime = new TimeSpan(11, 0, 0),
            Doctor = CreateValidDoctor(1),
            Patient = CreateValidPatient(1)
        };

        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(15)).ReturnsAsync(appointment);
        _uowMock.Setup(u => u.CommitAsync()).ReturnsAsync(1);

        // Act
        var result = await _service.CancelAppointmentAsync(15, "pat_user_1", isDoctorOrAdmin: false);

        // Assert
        result.IsSuccess.Should().BeTrue();
        appointment.Status.Should().Be(AppointmentStatus.Cancelled);
        _notificationServiceMock.Verify(n => n.NotifySlotAvailabilityChangedAsync(1, new DateTime(2026, 11, 15)), Times.Once);
    }

    [Fact]
    public async Task CancelAppointment_ShouldFail_WhenPatientCancelsLessThan2HoursInAdvance()
    {
        // Arrange: Clock is 08:00, appointment is at 09:30 (1.5 hours in advance: 1.5 <= 2)
        var appointment = new Appointment
        {
            Id = 15,
            DoctorId = 1,
            PatientId = 1,
            Status = AppointmentStatus.Confirmed,
            AppointmentDate = new DateTime(2026, 11, 15),
            StartTime = new TimeSpan(9, 30, 0),
            Doctor = CreateValidDoctor(1),
            Patient = CreateValidPatient(1)
        };

        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(15)).ReturnsAsync(appointment);

        // Act
        var result = await _service.CancelAppointmentAsync(15, "pat_user_1", isDoctorOrAdmin: false);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("cannot be cancelled less than 2 hours before");
    }

    [Fact]
    public async Task CancelAppointment_ShouldFail_WhenAlreadyInTerminalState()
    {
        // Arrange: Status is already Completed
        var appointment = new Appointment
        {
            Id = 15,
            Status = AppointmentStatus.Completed,
            AppointmentDate = new DateTime(2026, 11, 15),
            StartTime = new TimeSpan(14, 0, 0)
        };

        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(15)).ReturnsAsync(appointment);

        // Act
        var result = await _service.CancelAppointmentAsync(15, "pat_user_1", isDoctorOrAdmin: false);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("already Completed");
    }

    [Fact]
    public async Task CancelAppointment_ShouldFail_WhenDifferentDoctorAttemptsCancel_WithoutAdminRole()
    {
        // Arrange: Appointment owned by Doctor 1 (UserId: "doc_user_1")
        var appointment = new Appointment
        {
            Id = 15,
            DoctorId = 1,
            PatientId = 1,
            Status = AppointmentStatus.Confirmed,
            AppointmentDate = new DateTime(2026, 11, 15),
            StartTime = new TimeSpan(14, 0, 0),
            Doctor = CreateValidDoctor(1), // UserId: "doc_user_1"
            Patient = CreateValidPatient(1)
        };

        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(15)).ReturnsAsync(appointment);

        // Act: Doctor 2 (UserId: "other_doc_user") attempts to cancel
        var result = await _service.CancelAppointmentAsync(15, "other_doc_user", isDoctorOrAdmin: true, isAdmin: false);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Forbidden");
        _uowMock.Verify(u => u.CommitAsync(), Times.Never);
    }

    [Fact]
    public async Task CancelAppointment_ShouldSucceed_WhenOwningDoctorCancels()
    {
        // Arrange
        var appointment = new Appointment
        {
            Id = 15,
            DoctorId = 1,
            PatientId = 1,
            Status = AppointmentStatus.Confirmed,
            AppointmentDate = new DateTime(2026, 11, 15),
            StartTime = new TimeSpan(14, 0, 0),
            Doctor = CreateValidDoctor(1), // UserId: "doc_user_1"
            Patient = CreateValidPatient(1)
        };

        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(15)).ReturnsAsync(appointment);
        _uowMock.Setup(u => u.CommitAsync()).ReturnsAsync(1);

        // Act: Owning doctor cancels
        var result = await _service.CancelAppointmentAsync(15, "doc_user_1", isDoctorOrAdmin: true, isAdmin: false);

        // Assert
        result.IsSuccess.Should().BeTrue();
        appointment.Status.Should().Be(AppointmentStatus.Cancelled);
        _uowMock.Verify(u => u.CommitAsync(), Times.Once);
    }

    [Fact]
    public async Task CancelAppointment_ShouldSucceed_WhenAdminCancelsAnyDoctorAppointment()
    {
        // Arrange
        var appointment = new Appointment
        {
            Id = 15,
            DoctorId = 1,
            PatientId = 1,
            Status = AppointmentStatus.Confirmed,
            AppointmentDate = new DateTime(2026, 11, 15),
            StartTime = new TimeSpan(14, 0, 0),
            Doctor = CreateValidDoctor(1), // UserId: "doc_user_1"
            Patient = CreateValidPatient(1)
        };

        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(15)).ReturnsAsync(appointment);
        _uowMock.Setup(u => u.CommitAsync()).ReturnsAsync(1);

        // Act: Admin (different UserId) cancels with isAdmin = true
        var result = await _service.CancelAppointmentAsync(15, "admin_user_99", isDoctorOrAdmin: true, isAdmin: true);

        // Assert
        result.IsSuccess.Should().BeTrue();
        appointment.Status.Should().Be(AppointmentStatus.Cancelled);
        _uowMock.Verify(u => u.CommitAsync(), Times.Once);
    }

    [Fact]
    public async Task MarkNoShow_ShouldSucceed_WhenConfirmedAndAfterStartTime()
    {
        // Arrange: Clock is 11:30, appointment was scheduled at 10:00 (started in past)
        _clinicClockMock.Setup(c => c.Now).Returns(new DateTime(2026, 11, 15, 11, 30, 0));

        var appointment = new Appointment
        {
            Id = 20,
            DoctorId = 1,
            Status = AppointmentStatus.Confirmed,
            AppointmentDate = new DateTime(2026, 11, 15),
            StartTime = new TimeSpan(10, 0, 0)
        };

        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(20)).ReturnsAsync(appointment);
        _uowMock.Setup(u => u.CommitAsync()).ReturnsAsync(1);

        // Act
        var result = await _service.MarkNoShowAsync(20, doctorId: 1);

        // Assert
        result.IsSuccess.Should().BeTrue();
        appointment.Status.Should().Be(AppointmentStatus.NoShow);
    }

    [Fact]
    public async Task MarkNoShow_ShouldFail_WhenBeforeScheduledStartTime()
    {
        // Arrange: Clock is 08:00, appointment is at 10:00 (not started yet)
        var appointment = new Appointment
        {
            Id = 20,
            DoctorId = 1,
            Status = AppointmentStatus.Confirmed,
            AppointmentDate = new DateTime(2026, 11, 15),
            StartTime = new TimeSpan(10, 0, 0)
        };

        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(20)).ReturnsAsync(appointment);

        // Act
        var result = await _service.MarkNoShowAsync(20, doctorId: 1);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("before its scheduled start time");
    }

    [Fact]
    public async Task GetAppointmentById_ShouldReturnSuccess_WhenFound()
    {
        // Arrange
        var appointment = new Appointment
        {
            Id = 42,
            DoctorId = 1,
            PatientId = 1,
            Status = AppointmentStatus.Confirmed,
            AppointmentDate = new DateTime(2026, 11, 15),
            StartTime = new TimeSpan(14, 0, 0),
            EndTime = new TimeSpan(14, 30, 0),
            ConsultationFee = 350,
            Doctor = CreateValidDoctor(1),
            Patient = CreateValidPatient(1)
        };

        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(42)).ReturnsAsync(appointment);

        // Act
        var result = await _service.GetAppointmentByIdAsync(42);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Id.Should().Be(42);
        result.Value.DoctorName.Should().Be("Dr. Ahmed Mahmoud");
        result.Value.PatientName.Should().Be("Omar Khaled");
        result.Value.CanCancel.Should().BeTrue();
    }

    [Fact]
    public async Task GetAppointmentById_ShouldReturnFailure_WhenNotFound()
    {
        // Arrange
        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(999)).ReturnsAsync((Appointment?)null);

        // Act
        var result = await _service.GetAppointmentByIdAsync(999);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task BookAppointment_ShouldAwaitAndDispatchEmail_WhenPatientHasEmail()
    {
        // Arrange
        var doctor = CreateValidDoctor();
        var patient = new Patient
        {
            Id = 1,
            UserId = "pat_user_1",
            User = new ApplicationUser { Id = "pat_user_1", FullName = "Omar Khaled", Email = "omar@patient.com" }
        };

        _doctorRepoMock.Setup(d => d.GetDoctorWithScheduleAndLeavesAsync(1)).ReturnsAsync(doctor);
        _patientRepoMock.Setup(p => p.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Patient, bool>>>()))
            .ReturnsAsync(new List<Patient> { patient });
        _patientRepoMock.Setup(p => p.GetByIdAsync(1)).ReturnsAsync(patient);
        _appointmentRepoMock.Setup(a => a.HasPatientConflictAsync(1, new DateTime(2026, 11, 15), new TimeSpan(10, 0, 0), It.IsAny<TimeSpan>()))
            .ReturnsAsync(false);
        _appointmentRepoMock.Setup(a => a.HasConflictAsync(1, new DateTime(2026, 11, 15), new TimeSpan(10, 0, 0), It.IsAny<TimeSpan>()))
            .ReturnsAsync(false);
        _uowMock.Setup(u => u.CommitAsync()).ReturnsAsync(1);
        _emailServiceMock.Setup(e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);

        var dto = new BookingRequestDto
        {
            DoctorId = 1,
            PatientId = 1,
            AppointmentDate = new DateTime(2026, 11, 15),
            StartTime = new TimeSpan(10, 0, 0),
            Type = AppointmentType.Consultation
        };

        // Act
        var result = await _service.BookAppointmentAsync(dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _emailServiceMock.Verify(e => e.SendEmailAsync(
            "omar@patient.com",
            It.Is<string>(s => s.Contains("Received")),
            It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task ConfirmAppointment_ShouldAwaitAndDispatchEmail_WhenPatientHasEmail()
    {
        // Arrange
        var appointment = new Appointment
        {
            Id = 10,
            DoctorId = 1,
            Status = AppointmentStatus.Pending,
            AppointmentDate = new DateTime(2026, 11, 16),
            StartTime = new TimeSpan(10, 0, 0),
            Doctor = CreateValidDoctor(1),
            Patient = new Patient
            {
                Id = 1,
                UserId = "pat_user_1",
                User = new ApplicationUser { Id = "pat_user_1", FullName = "Omar Khaled", Email = "omar@patient.com" }
            }
        };

        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(10)).ReturnsAsync(appointment);
        _uowMock.Setup(u => u.CommitAsync()).ReturnsAsync(1);
        _emailServiceMock.Setup(e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);

        // Act
        var result = await _service.ConfirmAppointmentAsync(10, doctorId: 1);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _emailServiceMock.Verify(e => e.SendEmailAsync(
            "omar@patient.com",
            It.Is<string>(s => s.Contains("Confirmed")),
            It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task CancelAppointment_ShouldAwaitAndDispatchEmail_WhenPatientHasEmail()
    {
        // Arrange
        var appointment = new Appointment
        {
            Id = 15,
            DoctorId = 1,
            PatientId = 1,
            Status = AppointmentStatus.Pending,
            AppointmentDate = new DateTime(2026, 11, 15),
            StartTime = new TimeSpan(11, 0, 0),
            Doctor = CreateValidDoctor(1),
            Patient = new Patient
            {
                Id = 1,
                UserId = "pat_user_1",
                User = new ApplicationUser { Id = "pat_user_1", FullName = "Omar Khaled", Email = "omar@patient.com" }
            }
        };

        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(15)).ReturnsAsync(appointment);
        _uowMock.Setup(u => u.CommitAsync()).ReturnsAsync(1);
        _emailServiceMock.Setup(e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);

        // Act
        var result = await _service.CancelAppointmentAsync(15, "pat_user_1", isDoctorOrAdmin: false);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _emailServiceMock.Verify(e => e.SendEmailAsync(
            "omar@patient.com",
            It.Is<string>(s => s.Contains("Cancelled")),
            It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task RescheduleAppointment_ShouldSucceed_WhenValidAndWithinRules()
    {
        // Arrange
        var doctor = CreateValidDoctor(1);
        var patient = CreateValidPatient(1);
        var appointment = new Appointment
        {
            Id = 10,
            DoctorId = 1,
            PatientId = 1,
            Status = AppointmentStatus.Confirmed,
            AppointmentDate = new DateTime(2026, 11, 15),
            StartTime = new TimeSpan(14, 0, 0), // 6h away from 08:00
            EndTime = new TimeSpan(14, 30, 0),
            Doctor = doctor,
            Patient = patient
        };

        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(10)).ReturnsAsync(appointment);
        _doctorRepoMock.Setup(d => d.GetDoctorWithScheduleAndLeavesAsync(1)).ReturnsAsync(doctor);
        _appointmentRepoMock.Setup(a => a.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Appointment, bool>>>()))
            .ReturnsAsync(new List<Appointment>());
        _uowMock.Setup(u => u.CommitAsync()).ReturnsAsync(1);
        _emailServiceMock.Setup(e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);

        var dto = new RescheduleRequestDto
        {
            AppointmentId = 10,
            NewAppointmentDate = new DateTime(2026, 11, 22), // Sunday
            NewStartTime = new TimeSpan(10, 0, 0),
            Reason = "Schedule adjustment"
        };

        // Act
        var result = await _service.RescheduleAppointmentAsync(dto, "pat_user_1", isDoctorOrAdmin: false);

        // Assert
        result.IsSuccess.Should().BeTrue();
        appointment.AppointmentDate.Should().Be(new DateTime(2026, 11, 22));
        appointment.StartTime.Should().Be(new TimeSpan(10, 0, 0));
        appointment.Notes.Should().Contain("Rescheduled: Schedule adjustment");
        _uowMock.Verify(u => u.CommitAsync(), Times.Once);
        _notificationServiceMock.Verify(n => n.NotifySlotAvailabilityChangedAsync(1, new DateTime(2026, 11, 15)), Times.Once);
        _notificationServiceMock.Verify(n => n.NotifySlotAvailabilityChangedAsync(1, new DateTime(2026, 11, 22)), Times.Once);
    }

    [Fact]
    public async Task RescheduleAppointment_ShouldFail_WhenLessThan2HoursFromCurrentStart()
    {
        // Arrange: appointment starts at 09:30, clock is 08:00 (1.5 hours difference)
        var doctor = CreateValidDoctor(1);
        var patient = CreateValidPatient(1);
        var appointment = new Appointment
        {
            Id = 11,
            DoctorId = 1,
            PatientId = 1,
            Status = AppointmentStatus.Confirmed,
            AppointmentDate = new DateTime(2026, 11, 15),
            StartTime = new TimeSpan(9, 30, 0),
            Doctor = doctor,
            Patient = patient
        };

        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(11)).ReturnsAsync(appointment);

        var dto = new RescheduleRequestDto
        {
            AppointmentId = 11,
            NewAppointmentDate = new DateTime(2026, 11, 22),
            NewStartTime = new TimeSpan(10, 0, 0)
        };

        // Act
        var result = await _service.RescheduleAppointmentAsync(dto, "pat_user_1", isDoctorOrAdmin: false);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("less than 2 hours before the current scheduled start time");
        _uowMock.Verify(u => u.CommitAsync(), Times.Never);
    }

    [Fact]
    public async Task RescheduleAppointment_ShouldFail_WhenPatientDoesNotOwnAppointment()
    {
        // Arrange
        var doctor = CreateValidDoctor(1);
        var patient = CreateValidPatient(1);
        var appointment = new Appointment
        {
            Id = 12,
            DoctorId = 1,
            PatientId = 1,
            Status = AppointmentStatus.Confirmed,
            AppointmentDate = new DateTime(2026, 11, 15),
            StartTime = new TimeSpan(14, 0, 0),
            Doctor = doctor,
            Patient = patient
        };

        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(12)).ReturnsAsync(appointment);

        var dto = new RescheduleRequestDto
        {
            AppointmentId = 12,
            NewAppointmentDate = new DateTime(2026, 11, 22),
            NewStartTime = new TimeSpan(10, 0, 0)
        };

        // Act: Different user tries to reschedule
        var result = await _service.RescheduleAppointmentAsync(dto, "other_unauthorized_user", isDoctorOrAdmin: false);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Forbidden: You can only reschedule your own appointments.");
        _uowMock.Verify(u => u.CommitAsync(), Times.Never);
    }

    [Fact]
    public async Task RescheduleAppointment_ShouldFail_WhenNewSlotHasConflict()
    {
        // Arrange
        var doctor = CreateValidDoctor(1);
        var patient = CreateValidPatient(1);
        var appointment = new Appointment
        {
            Id = 13,
            DoctorId = 1,
            PatientId = 1,
            Status = AppointmentStatus.Pending,
            AppointmentDate = new DateTime(2026, 11, 15),
            StartTime = new TimeSpan(14, 0, 0),
            Doctor = doctor,
            Patient = patient
        };

        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(13)).ReturnsAsync(appointment);
        _doctorRepoMock.Setup(d => d.GetDoctorWithScheduleAndLeavesAsync(1)).ReturnsAsync(doctor);

        // Conflict on new slot
        var conflictingAppt = new Appointment
        {
            Id = 99,
            DoctorId = 1,
            AppointmentDate = new DateTime(2026, 11, 22),
            StartTime = new TimeSpan(10, 0, 0),
            Status = AppointmentStatus.Confirmed
        };
        _appointmentRepoMock.Setup(a => a.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Appointment, bool>>>()))
            .ReturnsAsync(new List<Appointment> { conflictingAppt });

        var dto = new RescheduleRequestDto
        {
            AppointmentId = 13,
            NewAppointmentDate = new DateTime(2026, 11, 22),
            NewStartTime = new TimeSpan(10, 0, 0)
        };

        // Act
        var result = await _service.RescheduleAppointmentAsync(dto, "pat_user_1", isDoctorOrAdmin: false);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("already booked");
        _uowMock.Verify(u => u.CommitAsync(), Times.Never);
    }

    [Fact]
    public async Task RescheduleAppointment_ShouldFail_WhenNewStartTimeDoesNotAlignWithDoctorSlotSchedule()
    {
        // Arrange: Doctor has 30-min slots starting on the hour and half-hour.
        // Reschedule target is 10:15 (unaligned)
        var doctor = CreateValidDoctor(1);
        var patient = CreateValidPatient(1);
        var appointment = new Appointment
        {
            Id = 13,
            DoctorId = 1,
            PatientId = 1,
            Status = AppointmentStatus.Pending,
            AppointmentDate = new DateTime(2026, 11, 15),
            StartTime = new TimeSpan(14, 0, 0),
            Doctor = doctor,
            Patient = patient
        };

        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(13)).ReturnsAsync(appointment);
        _doctorRepoMock.Setup(d => d.GetDoctorWithScheduleAndLeavesAsync(1)).ReturnsAsync(doctor);

        var dto = new RescheduleRequestDto
        {
            AppointmentId = 13,
            NewAppointmentDate = new DateTime(2026, 11, 22),
            NewStartTime = new TimeSpan(10, 15, 0) // Unaligned with 30-min grid
        };

        // Act
        var result = await _service.RescheduleAppointmentAsync(dto, "pat_user_1", isDoctorOrAdmin: false);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("align with the doctor's appointment slot schedule");
        _uowMock.Verify(u => u.CommitAsync(), Times.Never);
    }

    [Fact]
    public async Task RescheduleAppointment_ShouldFail_WhenNewSlotIntervalOverlapsExistingAppointment()
    {
        // Arrange
        var doctor = CreateValidDoctor(1);
        var patient = CreateValidPatient(1);
        var appointment = new Appointment
        {
            Id = 13,
            DoctorId = 1,
            PatientId = 1,
            Status = AppointmentStatus.Pending,
            AppointmentDate = new DateTime(2026, 11, 15),
            StartTime = new TimeSpan(14, 0, 0),
            Doctor = doctor,
            Patient = patient
        };

        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(13)).ReturnsAsync(appointment);
        _doctorRepoMock.Setup(d => d.GetDoctorWithScheduleAndLeavesAsync(1)).ReturnsAsync(doctor);

        // Existing appointment for doctor is 60-min: 10:00 to 11:00
        var existingOverlappingAppt = new Appointment
        {
            Id = 99,
            DoctorId = 1,
            AppointmentDate = new DateTime(2026, 11, 22),
            StartTime = new TimeSpan(10, 0, 0),
            EndTime = new TimeSpan(11, 0, 0),
            Status = AppointmentStatus.Confirmed
        };

        _appointmentRepoMock.Setup(a => a.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Appointment, bool>>>()))
            .ReturnsAsync((System.Linq.Expressions.Expression<Func<Appointment, bool>> expr) =>
            {
                var list = new List<Appointment> { existingOverlappingAppt };
                return list.Where(expr.Compile()).ToList();
            });

        // Reschedule request is for 10:30 to 11:00 (aligned with 30-min slots, but inside 10:00-11:00!)
        var dto = new RescheduleRequestDto
        {
            AppointmentId = 13,
            NewAppointmentDate = new DateTime(2026, 11, 22),
            NewStartTime = new TimeSpan(10, 30, 0)
        };

        // Act
        var result = await _service.RescheduleAppointmentAsync(dto, "pat_user_1", isDoctorOrAdmin: false);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("already booked");
        _uowMock.Verify(u => u.CommitAsync(), Times.Never);
    }

    [Fact]
    public async Task RescheduleAppointment_ShouldFail_WhenDifferentDoctorAttemptsReschedule_WithoutAdminRole()
    {
        // Arrange
        var doctor = CreateValidDoctor(1); // UserId: "doc_user_1"
        var patient = CreateValidPatient(1);
        var appointment = new Appointment
        {
            Id = 13,
            DoctorId = 1,
            PatientId = 1,
            AppointmentDate = new DateTime(2026, 11, 15),
            StartTime = new TimeSpan(10, 0, 0),
            Status = AppointmentStatus.Confirmed,
            Doctor = doctor,
            Patient = patient
        };

        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(13)).ReturnsAsync(appointment);

        var dto = new RescheduleRequestDto
        {
            AppointmentId = 13,
            NewAppointmentDate = new DateTime(2026, 11, 22),
            NewStartTime = new TimeSpan(10, 0, 0)
        };

        // Act: Doctor 2 (UserId: "other_doc_user") attempts to reschedule
        var result = await _service.RescheduleAppointmentAsync(dto, "other_doc_user", isDoctorOrAdmin: true, isAdmin: false);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Forbidden");
        _uowMock.Verify(u => u.CommitAsync(), Times.Never);
    }

    [Fact]
    public async Task RescheduleAppointment_ShouldSucceed_WhenOwningDoctorReschedules()
    {
        // Arrange
        var doctor = CreateValidDoctor(1); // UserId: "doc_user_1"
        var patient = CreateValidPatient(1);
        var appointment = new Appointment
        {
            Id = 13,
            DoctorId = 1,
            PatientId = 1,
            AppointmentDate = new DateTime(2026, 11, 15),
            StartTime = new TimeSpan(10, 0, 0),
            Status = AppointmentStatus.Confirmed,
            Doctor = doctor,
            Patient = patient
        };

        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(13)).ReturnsAsync(appointment);
        _doctorRepoMock.Setup(d => d.GetDoctorWithScheduleAndLeavesAsync(1)).ReturnsAsync(doctor);
        _appointmentRepoMock.Setup(a => a.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Appointment, bool>>>()))
            .ReturnsAsync(new List<Appointment>());
        _uowMock.Setup(u => u.CommitAsync()).ReturnsAsync(1);

        var dto = new RescheduleRequestDto
        {
            AppointmentId = 13,
            NewAppointmentDate = new DateTime(2026, 11, 22),
            NewStartTime = new TimeSpan(10, 0, 0)
        };

        // Act: Owning doctor reschedules
        var result = await _service.RescheduleAppointmentAsync(dto, "doc_user_1", isDoctorOrAdmin: true, isAdmin: false);

        // Assert
        result.IsSuccess.Should().BeTrue();
        appointment.AppointmentDate.Should().Be(new DateTime(2026, 11, 22));
        _uowMock.Verify(u => u.CommitAsync(), Times.Once);
    }

    [Fact]
    public async Task RescheduleAppointment_ShouldSucceed_WhenAdminReschedulesAnyDoctorAppointment()
    {
        // Arrange
        var doctor = CreateValidDoctor(1); // UserId: "doc_user_1"
        var patient = CreateValidPatient(1);
        var appointment = new Appointment
        {
            Id = 13,
            DoctorId = 1,
            PatientId = 1,
            AppointmentDate = new DateTime(2026, 11, 15),
            StartTime = new TimeSpan(10, 0, 0),
            Status = AppointmentStatus.Confirmed,
            Doctor = doctor,
            Patient = patient
        };

        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(13)).ReturnsAsync(appointment);
        _doctorRepoMock.Setup(d => d.GetDoctorWithScheduleAndLeavesAsync(1)).ReturnsAsync(doctor);
        _appointmentRepoMock.Setup(a => a.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Appointment, bool>>>()))
            .ReturnsAsync(new List<Appointment>());
        _uowMock.Setup(u => u.CommitAsync()).ReturnsAsync(1);

        var dto = new RescheduleRequestDto
        {
            AppointmentId = 13,
            NewAppointmentDate = new DateTime(2026, 11, 22),
            NewStartTime = new TimeSpan(10, 0, 0)
        };

        // Act: Admin (UserId: "admin_user_99") reschedules with isAdmin = true
        var result = await _service.RescheduleAppointmentAsync(dto, "admin_user_99", isDoctorOrAdmin: true, isAdmin: true);

        // Assert
        result.IsSuccess.Should().BeTrue();
        appointment.AppointmentDate.Should().Be(new DateTime(2026, 11, 22));
        _uowMock.Verify(u => u.CommitAsync(), Times.Once);
    }

    [Fact]
    public async Task CallNextQueuePatientAsync_ShouldSucceed_WhenDoctorOwnsAppointment()
    {
        // Arrange
        var doctor = CreateValidDoctor(1);
        var patient = CreateValidPatient(1);
        var appointment = new Appointment
        {
            Id = 42,
            DoctorId = 1,
            PatientId = 1,
            AppointmentDate = _clinicClockMock.Object.Today,
            CheckedInAt = _clinicClockMock.Object.Now,
            Status = AppointmentStatus.Confirmed,
            Doctor = doctor,
            Patient = patient
        };

        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(42)).ReturnsAsync(appointment);

        // Act
        var result = await _service.CallNextQueuePatientAsync(42, "doc_user_1", isAdmin: false);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _notificationServiceMock.Verify(n => n.SendNotificationAsync(
            "pat_user_1",
            It.Is<string>(s => s.Contains("دورك")),
            It.IsAny<string>()), Times.Once);
        _notificationServiceMock.Verify(n => n.NotifyAppointmentStatusChangedAsync(42, "Calling", "pat_user_1"), Times.Once);
    }

    [Fact]
    public async Task CallNextQueuePatientAsync_ShouldFail_WhenDoctorDoesNotOwnAppointment()
    {
        // Arrange
        var doctor = CreateValidDoctor(1);
        var patient = CreateValidPatient(1);
        var appointment = new Appointment
        {
            Id = 42,
            DoctorId = 1,
            PatientId = 1,
            Doctor = doctor,
            Patient = patient
        };

        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(42)).ReturnsAsync(appointment);

        // Act
        var result = await _service.CallNextQueuePatientAsync(42, "different_doctor_user", isAdmin: false);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Forbidden");
        _notificationServiceMock.Verify(n => n.SendNotificationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CallNextQueuePatientAsync_ShouldSucceed_WhenAdminCalls()
    {
        // Arrange
        var doctor = CreateValidDoctor(1);
        var patient = CreateValidPatient(1);
        var appointment = new Appointment
        {
            Id = 42,
            DoctorId = 1,
            PatientId = 1,
            AppointmentDate = _clinicClockMock.Object.Today,
            CheckedInAt = _clinicClockMock.Object.Now,
            Status = AppointmentStatus.Confirmed,
            Doctor = doctor,
            Patient = patient
        };

        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(42)).ReturnsAsync(appointment);

        // Act
        var result = await _service.CallNextQueuePatientAsync(42, "admin_user", isAdmin: true);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _notificationServiceMock.Verify(n => n.SendNotificationAsync(
            "pat_user_1",
            It.Is<string>(s => s.Contains("دورك")),
            It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task GetDoctorAppointments_ShouldFetchViaSingleQuery_WithoutNPlusOneLoops()
    {
        // Arrange
        var doctor = CreateValidDoctor(1);
        var patient = CreateValidPatient(1);
        var appointments = new List<Appointment>
        {
            new Appointment { Id = 101, DoctorId = 1, PatientId = 1, Doctor = doctor, Patient = patient, AppointmentDate = new DateTime(2026, 11, 15), StartTime = new TimeSpan(9, 0, 0), EndTime = new TimeSpan(9, 30, 0), Status = AppointmentStatus.Confirmed },
            new Appointment { Id = 102, DoctorId = 1, PatientId = 1, Doctor = doctor, Patient = patient, AppointmentDate = new DateTime(2026, 11, 15), StartTime = new TimeSpan(9, 30, 0), EndTime = new TimeSpan(10, 0, 0), Status = AppointmentStatus.Confirmed },
            new Appointment { Id = 103, DoctorId = 1, PatientId = 1, Doctor = doctor, Patient = patient, AppointmentDate = new DateTime(2026, 11, 15), StartTime = new TimeSpan(10, 0, 0), EndTime = new TimeSpan(10, 30, 0), Status = AppointmentStatus.Confirmed }
        };

        _appointmentRepoMock.Setup(r => r.GetDoctorAppointmentsWithDetailsAsync(1, null, null))
            .ReturnsAsync(appointments);

        // Act
        var result = await _service.GetDoctorAppointmentsAsync(1);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(3);
        _appointmentRepoMock.Verify(r => r.GetDoctorAppointmentsWithDetailsAsync(1, null, null), Times.Once);
        _appointmentRepoMock.Verify(r => r.GetByIdWithDetailsAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task CompleteAppointmentAsync_WhenDoctorDoesNotOwn_ReturnsForbidden()
    {
        var appt = new Appointment { Id = 201, DoctorId = 1, Status = AppointmentStatus.Confirmed };
        _appointmentRepoMock.Setup(r => r.GetByIdWithDetailsAsync(201)).ReturnsAsync(appt);

        var result = await _service.CompleteAppointmentAsync(201, doctorId: 2);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Forbidden");
    }

    [Fact]
    public async Task CompleteAppointmentAsync_WhenStatusNotConfirmed_ReturnsFailure()
    {
        var appt = new Appointment { Id = 202, DoctorId = 1, Status = AppointmentStatus.Pending };
        _appointmentRepoMock.Setup(r => r.GetByIdWithDetailsAsync(202)).ReturnsAsync(appt);

        var result = await _service.CompleteAppointmentAsync(202, doctorId: 1);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Only Confirmed appointments");
    }

    [Fact]
    public async Task CompleteAppointmentAsync_WhenBeforeStartTime_ReturnsFailure()
    {
        // Clock is 2026-11-15 08:00
        var appt = new Appointment
        {
            Id = 203,
            DoctorId = 1,
            AppointmentDate = new DateTime(2026, 11, 15),
            StartTime = TimeSpan.FromHours(10), // in future
            Status = AppointmentStatus.Confirmed
        };
        _appointmentRepoMock.Setup(r => r.GetByIdWithDetailsAsync(203)).ReturnsAsync(appt);

        var result = await _service.CompleteAppointmentAsync(203, doctorId: 1);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("before its scheduled start time");
    }

    [Fact]
    public async Task CompleteAppointmentAsync_WhenValidAndElapsed_SetsCompletedAndCommits()
    {
        // Clock is 2026-11-15 08:00
        var appt = new Appointment
        {
            Id = 204,
            DoctorId = 1,
            AppointmentDate = new DateTime(2026, 11, 15),
            StartTime = TimeSpan.FromHours(7), // in past
            Status = AppointmentStatus.Confirmed
        };
        _appointmentRepoMock.Setup(r => r.GetByIdWithDetailsAsync(204)).ReturnsAsync(appt);

        var result = await _service.CompleteAppointmentAsync(204, doctorId: 1);

        result.IsSuccess.Should().BeTrue();
        appt.Status.Should().Be(AppointmentStatus.Completed);
        _uowMock.Verify(u => u.CommitAsync(), Times.Once);
    }

    [Fact]
    public async Task MarkNoShowAsync_WhenDoctorDoesNotOwn_ReturnsForbidden()
    {
        var appt = new Appointment { Id = 205, DoctorId = 1, Status = AppointmentStatus.Confirmed };
        _appointmentRepoMock.Setup(r => r.GetByIdWithDetailsAsync(205)).ReturnsAsync(appt);

        var result = await _service.MarkNoShowAsync(205, doctorId: 3);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Forbidden");
    }

    [Fact]
    public async Task MarkNoShowAsync_WhenValidAndElapsed_SetsNoShowAndCommits()
    {
        // Clock is 2026-11-15 08:00
        var appt = new Appointment
        {
            Id = 206,
            DoctorId = 1,
            AppointmentDate = new DateTime(2026, 11, 15),
            StartTime = TimeSpan.FromHours(7), // in past
            Status = AppointmentStatus.Confirmed
        };
        _appointmentRepoMock.Setup(r => r.GetByIdWithDetailsAsync(206)).ReturnsAsync(appt);

        var result = await _service.MarkNoShowAsync(206, doctorId: 1);

        result.IsSuccess.Should().BeTrue();
        appt.Status.Should().Be(AppointmentStatus.NoShow);
        _uowMock.Verify(u => u.CommitAsync(), Times.Once);
    }

    [Fact]
    public async Task RejectAppointmentAsync_WhenDoctorDoesNotOwn_ReturnsForbidden()
    {
        var appt = new Appointment { Id = 207, DoctorId = 1, Status = AppointmentStatus.Pending };
        _appointmentRepoMock.Setup(r => r.GetByIdWithDetailsAsync(207)).ReturnsAsync(appt);

        var result = await _service.RejectAppointmentAsync(207, doctorId: 2);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Forbidden");
    }

    [Fact]
    public async Task RejectAppointmentAsync_WhenValid_SetsRejectedAndNotifies()
    {
        var doctor = CreateValidDoctor(1);
        var patient = CreateValidPatient(1);
        var appt = new Appointment
        {
            Id = 208,
            DoctorId = 1,
            PatientId = 1,
            Doctor = doctor,
            Patient = patient,
            AppointmentDate = new DateTime(2026, 11, 16),
            StartTime = TimeSpan.FromHours(10),
            Status = AppointmentStatus.Pending
        };
        _appointmentRepoMock.Setup(r => r.GetByIdWithDetailsAsync(208)).ReturnsAsync(appt);

        var result = await _service.RejectAppointmentAsync(208, doctorId: 1);

        result.IsSuccess.Should().BeTrue();
        appt.Status.Should().Be(AppointmentStatus.Rejected);
        _notificationServiceMock.Verify(n => n.NotifyAppointmentStatusChangedAsync(208, "Rejected", patient.UserId), Times.Once);
        _notificationServiceMock.Verify(n => n.NotifySlotAvailabilityChangedAsync(1, appt.AppointmentDate), Times.Once);
    }

    [Fact]
    public async Task CheckConflictAsync_ReturnsAccurateConflictDto()
    {
        var doctor = new Doctor { Id = 1, SlotDurationMinutes = 45 };
        _doctorRepoMock.Setup(d => d.GetByIdAsync(1)).ReturnsAsync(doctor);

        _appointmentRepoMock.Setup(r => r.HasConflictAsync(1, new DateTime(2026, 11, 16), TimeSpan.FromHours(10), TimeSpan.FromHours(10).Add(TimeSpan.FromMinutes(45))))
            .ReturnsAsync(true);

        var conflictResult = await _service.CheckConflictAsync(1, new DateTime(2026, 11, 16), TimeSpan.FromHours(10));

        conflictResult.IsSuccess.Should().BeTrue();
        conflictResult.Value!.HasConflict.Should().BeTrue();

        _appointmentRepoMock.Setup(r => r.HasConflictAsync(1, new DateTime(2026, 11, 16), TimeSpan.FromHours(11), TimeSpan.FromHours(11).Add(TimeSpan.FromMinutes(45))))
            .ReturnsAsync(false);

        var availableResult = await _service.CheckConflictAsync(1, new DateTime(2026, 11, 16), TimeSpan.FromHours(11));

        availableResult.IsSuccess.Should().BeTrue();
        availableResult.Value!.HasConflict.Should().BeFalse();
    }

    [Fact]
    public async Task GetPatientAppointmentsAsync_WhenPatientNotFound_ReturnsFailure()
    {
        _patientRepoMock.Setup(p => p.FindAsync(It.IsAny<System.Linq.Expressions.Expression<System.Func<Patient, bool>>>()))
            .ReturnsAsync(new List<Patient>());

        var result = await _service.GetPatientAppointmentsAsync("unknown-pat-user");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Patient record not found");
    }

    [Fact]
    public async Task BookAppointment_ShouldFail_WhenPatientNotFound()
    {
        var doctor = CreateValidDoctor();
        _doctorRepoMock.Setup(d => d.GetDoctorWithScheduleAndLeavesAsync(1))
            .ReturnsAsync(doctor);
        _patientRepoMock.Setup(p => p.FindAsync(It.IsAny<System.Linq.Expressions.Expression<System.Func<Patient, bool>>>()))
            .ReturnsAsync(new List<Patient>());

        var dto = new BookingRequestDto
        {
            DoctorId = 1,
            PatientId = 999,
            AppointmentDate = new DateTime(2026, 11, 15),
            StartTime = new TimeSpan(10, 0, 0),
            Type = AppointmentType.Consultation
        };

        var result = await _service.BookAppointmentAsync(dto);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Patient profile not found");
    }

    [Fact]
    public async Task BookAppointment_ShouldFail_WhenTargetDateTimeInPast()
    {
        var doctor = CreateValidDoctor();
        var patient = CreateValidPatient();
        _doctorRepoMock.Setup(d => d.GetDoctorWithScheduleAndLeavesAsync(1))
            .ReturnsAsync(doctor);
        _patientRepoMock.Setup(p => p.FindAsync(It.IsAny<System.Linq.Expressions.Expression<System.Func<Patient, bool>>>()))
            .ReturnsAsync(new List<Patient> { patient });

        var dto = new BookingRequestDto
        {
            DoctorId = 1,
            PatientId = 1,
            AppointmentDate = new DateTime(2026, 11, 14),
            StartTime = new TimeSpan(10, 0, 0),
            Type = AppointmentType.Consultation
        };

        var result = await _service.BookAppointmentAsync(dto);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("cannot be scheduled in the past");
    }

    [Fact]
    public async Task BookAppointment_ShouldFail_WhenDoctorSlotConflictDetected()
    {
        var doctor = CreateValidDoctor();
        var patient = CreateValidPatient();
        _doctorRepoMock.Setup(d => d.GetDoctorWithScheduleAndLeavesAsync(1))
            .ReturnsAsync(doctor);
        _patientRepoMock.Setup(p => p.FindAsync(It.IsAny<System.Linq.Expressions.Expression<System.Func<Patient, bool>>>()))
            .ReturnsAsync(new List<Patient> { patient });
        _appointmentRepoMock.Setup(a => a.HasPatientConflictAsync(1, new DateTime(2026, 11, 15), new TimeSpan(10, 0, 0), It.IsAny<TimeSpan>()))
            .ReturnsAsync(false);
        _appointmentRepoMock.Setup(a => a.HasConflictAsync(1, new DateTime(2026, 11, 15), new TimeSpan(10, 0, 0), It.IsAny<TimeSpan>()))
            .ReturnsAsync(true);

        var dto = new BookingRequestDto
        {
            DoctorId = 1,
            PatientId = 1,
            AppointmentDate = new DateTime(2026, 11, 15),
            StartTime = new TimeSpan(10, 0, 0),
            Type = AppointmentType.Consultation
        };

        var result = await _service.BookAppointmentAsync(dto);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("selected time slot is already booked");
    }

    [Fact]
    public async Task ConfirmAppointment_ShouldFail_WhenAppointmentNotFound()
    {
        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(999))
            .ReturnsAsync((Appointment?)null);

        var result = await _service.ConfirmAppointmentAsync(999, 1);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Appointment not found");
    }

    [Fact]
    public async Task RejectAppointment_ShouldFail_WhenAppointmentNotFound()
    {
        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(999))
            .ReturnsAsync((Appointment?)null);

        var result = await _service.RejectAppointmentAsync(999, 1);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Appointment not found");
    }

    [Fact]
    public async Task RejectAppointment_ShouldFail_WhenStatusIsNotPending()
    {
        var appt = new Appointment
        {
            Id = 50,
            DoctorId = 1,
            Status = AppointmentStatus.Confirmed
        };
        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(50))
            .ReturnsAsync(appt);

        var result = await _service.RejectAppointmentAsync(50, 1);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Cannot reject appointment. Current status is 'Confirmed'");
    }

    [Fact]
    public async Task CancelAppointment_ShouldFail_WhenAppointmentNotFound()
    {
        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(999))
            .ReturnsAsync((Appointment?)null);

        var result = await _service.CancelAppointmentAsync(999, "any-user");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Appointment not found");
    }

    [Fact]
    public async Task RescheduleAppointment_ShouldFail_WhenAppointmentNotFound()
    {
        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(999))
            .ReturnsAsync((Appointment?)null);

        var dto = new RescheduleRequestDto
        {
            AppointmentId = 999,
            NewAppointmentDate = new DateTime(2026, 11, 16),
            NewStartTime = new TimeSpan(10, 0, 0)
        };

        var result = await _service.RescheduleAppointmentAsync(dto, "user-1");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Appointment not found");
    }

    [Theory]
    [InlineData(AppointmentStatus.Completed)]
    [InlineData(AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.Rejected)]
    [InlineData(AppointmentStatus.NoShow)]
    public async Task RescheduleAppointment_ShouldFail_WhenTerminalStatus(AppointmentStatus terminalStatus)
    {
        var appt = new Appointment
        {
            Id = 60,
            Status = terminalStatus
        };
        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(60))
            .ReturnsAsync(appt);

        var dto = new RescheduleRequestDto
        {
            AppointmentId = 60,
            NewAppointmentDate = new DateTime(2026, 11, 16),
            NewStartTime = new TimeSpan(10, 0, 0)
        };

        var result = await _service.RescheduleAppointmentAsync(dto, "user-1");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain($"Cannot reschedule an appointment that is already {terminalStatus}");
    }

    [Fact]
    public async Task RescheduleAppointment_ShouldFail_WhenNewDateTimeInPast()
    {
        var doctor = CreateValidDoctor();
        var patient = CreateValidPatient();
        var appt = new Appointment
        {
            Id = 61,
            DoctorId = 1,
            PatientId = 1,
            Status = AppointmentStatus.Confirmed,
            AppointmentDate = new DateTime(2026, 11, 16),
            StartTime = new TimeSpan(14, 0, 0),
            Patient = patient,
            Doctor = doctor
        };
        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(61))
            .ReturnsAsync(appt);

        var dto = new RescheduleRequestDto
        {
            AppointmentId = 61,
            NewAppointmentDate = new DateTime(2026, 11, 14),
            NewStartTime = new TimeSpan(10, 0, 0)
        };

        var result = await _service.RescheduleAppointmentAsync(dto, patient.UserId);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("New appointment time cannot be in the past");
    }

    [Fact]
    public async Task RescheduleAppointment_ShouldFail_WhenNewLeadTimeUnder30Minutes()
    {
        var doctor = CreateValidDoctor();
        var patient = CreateValidPatient();
        var appt = new Appointment
        {
            Id = 62,
            DoctorId = 1,
            PatientId = 1,
            Status = AppointmentStatus.Confirmed,
            AppointmentDate = new DateTime(2026, 11, 16),
            StartTime = new TimeSpan(14, 0, 0),
            Patient = patient,
            Doctor = doctor
        };
        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(62))
            .ReturnsAsync(appt);

        var dto = new RescheduleRequestDto
        {
            AppointmentId = 62,
            NewAppointmentDate = new DateTime(2026, 11, 15),
            NewStartTime = new TimeSpan(8, 15, 0)
        };

        var result = await _service.RescheduleAppointmentAsync(dto, patient.UserId);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("at least 30 minutes in advance");
    }

    [Fact]
    public async Task RescheduleAppointment_ShouldFail_WhenNewDateMoreThan30DaysInAdvance()
    {
        var doctor = CreateValidDoctor();
        var patient = CreateValidPatient();
        var appt = new Appointment
        {
            Id = 63,
            DoctorId = 1,
            PatientId = 1,
            Status = AppointmentStatus.Confirmed,
            AppointmentDate = new DateTime(2026, 11, 16),
            StartTime = new TimeSpan(14, 0, 0),
            Patient = patient,
            Doctor = doctor
        };
        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(63))
            .ReturnsAsync(appt);

        var dto = new RescheduleRequestDto
        {
            AppointmentId = 63,
            NewAppointmentDate = new DateTime(2026, 12, 28),
            NewStartTime = new TimeSpan(10, 0, 0)
        };

        var result = await _service.RescheduleAppointmentAsync(dto, patient.UserId);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("up to 30 days in advance");
    }

    [Fact]
    public async Task RescheduleAppointment_ShouldFail_WhenDoctorNotFound()
    {
        var patient = CreateValidPatient();
        var appt = new Appointment
        {
            Id = 64,
            DoctorId = 999,
            PatientId = 1,
            Status = AppointmentStatus.Confirmed,
            AppointmentDate = new DateTime(2026, 11, 16),
            StartTime = new TimeSpan(14, 0, 0),
            Patient = patient
        };
        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(64))
            .ReturnsAsync(appt);
        _doctorRepoMock.Setup(d => d.GetDoctorWithScheduleAndLeavesAsync(999))
            .ReturnsAsync((Doctor?)null);
        _doctorRepoMock.Setup(d => d.GetDoctorWithScheduleAsync(999))
            .ReturnsAsync((Doctor?)null);

        var dto = new RescheduleRequestDto
        {
            AppointmentId = 64,
            NewAppointmentDate = new DateTime(2026, 11, 18),
            NewStartTime = new TimeSpan(10, 0, 0)
        };

        var result = await _service.RescheduleAppointmentAsync(dto, patient.UserId);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Doctor profile not found");
    }

    [Fact]
    public async Task RescheduleAppointment_ShouldFail_WhenDoctorOnLeave()
    {
        var doctor = CreateValidDoctor();
        doctor.Leaves.Add(new DoctorLeave
        {
            DoctorId = 1,
            StartDate = new DateTime(2026, 11, 20),
            EndDate = new DateTime(2026, 11, 25)
        });
        var patient = CreateValidPatient();
        var appt = new Appointment
        {
            Id = 65,
            DoctorId = 1,
            PatientId = 1,
            Status = AppointmentStatus.Confirmed,
            AppointmentDate = new DateTime(2026, 11, 16),
            StartTime = new TimeSpan(14, 0, 0),
            Patient = patient,
            Doctor = doctor
        };
        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(65))
            .ReturnsAsync(appt);
        _doctorRepoMock.Setup(d => d.GetDoctorWithScheduleAndLeavesAsync(1))
            .ReturnsAsync(doctor);

        var dto = new RescheduleRequestDto
        {
            AppointmentId = 65,
            NewAppointmentDate = new DateTime(2026, 11, 22),
            NewStartTime = new TimeSpan(10, 0, 0)
        };

        var result = await _service.RescheduleAppointmentAsync(dto, patient.UserId);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Doctor is on leave on the selected new date");
    }

    [Fact]
    public async Task RescheduleAppointment_ShouldCatchDbUpdateException_AndReturnFriendlyConflictMessage()
    {
        var doctor = CreateValidDoctor();
        var patient = CreateValidPatient();
        var appt = new Appointment
        {
            Id = 67,
            DoctorId = 1,
            PatientId = 1,
            Status = AppointmentStatus.Confirmed,
            AppointmentDate = new DateTime(2026, 11, 16),
            StartTime = new TimeSpan(14, 0, 0),
            Patient = patient,
            Doctor = doctor
        };
        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(67))
            .ReturnsAsync(appt);
        _doctorRepoMock.Setup(d => d.GetDoctorWithScheduleAndLeavesAsync(1))
            .ReturnsAsync(doctor);

        _appointmentRepoMock.Setup(a => a.FindAsync(It.IsAny<System.Linq.Expressions.Expression<System.Func<Appointment, bool>>>()))
            .ReturnsAsync(new List<Appointment>());

        _uowMock.Setup(u => u.CommitAsync())
            .ThrowsAsync(new DbUpdateException("Duplicate key index violation", new Exception()));

        var dto = new RescheduleRequestDto
        {
            AppointmentId = 67,
            NewAppointmentDate = new DateTime(2026, 11, 22),
            NewStartTime = new TimeSpan(10, 0, 0),
            Reason = "Patient change of plans"
        };

        var result = await _service.RescheduleAppointmentAsync(dto, patient.UserId);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("This slot was just booked by another patient");
    }

    [Fact]
    public async Task GetDoctorEventsAsync_ShouldReturnColorCodesPerStatus()
    {
        var appointments = new List<Appointment>
        {
            new() { Id = 1, Status = AppointmentStatus.Confirmed, AppointmentDate = new DateTime(2026, 11, 15), StartTime = new TimeSpan(9, 0, 0), EndTime = new TimeSpan(9, 30, 0), Type = AppointmentType.Consultation },
            new() { Id = 2, Status = AppointmentStatus.Pending, AppointmentDate = new DateTime(2026, 11, 15), StartTime = new TimeSpan(10, 0, 0), EndTime = new TimeSpan(10, 30, 0), Type = AppointmentType.FollowUp },
            new() { Id = 3, Status = AppointmentStatus.Completed, AppointmentDate = new DateTime(2026, 11, 15), StartTime = new TimeSpan(11, 0, 0), EndTime = new TimeSpan(11, 30, 0), Type = AppointmentType.FollowUp },
            new() { Id = 4, Status = AppointmentStatus.Cancelled, AppointmentDate = new DateTime(2026, 11, 15), StartTime = new TimeSpan(12, 0, 0), EndTime = new TimeSpan(12, 30, 0), Type = AppointmentType.Telemedicine },
            new() { Id = 5, Status = AppointmentStatus.Rejected, AppointmentDate = new DateTime(2026, 11, 15), StartTime = new TimeSpan(13, 0, 0), EndTime = new TimeSpan(13, 30, 0), Type = AppointmentType.Consultation },
            new() { Id = 6, Status = AppointmentStatus.NoShow, AppointmentDate = new DateTime(2026, 11, 15), StartTime = new TimeSpan(14, 0, 0), EndTime = new TimeSpan(14, 30, 0), Type = AppointmentType.Consultation }
        };

        _appointmentRepoMock.Setup(a => a.GetDoctorAppointmentsRangeAsync(1, It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(appointments);

        var result = await _service.GetDoctorEventsAsync(1, DateTime.Today, DateTime.Today.AddDays(7));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(6);
        result.Value!.First(e => e.Id == "1").Color.Should().Be("#198754");
        result.Value!.First(e => e.Id == "2").Color.Should().Be("#ffc107");
        result.Value!.First(e => e.Id == "3").Color.Should().Be("#0d6efd");
        result.Value!.First(e => e.Id == "4").Color.Should().Be("#6c757d");
        result.Value!.First(e => e.Id == "5").Color.Should().Be("#dc3545");
        result.Value!.First(e => e.Id == "6").Color.Should().Be("#d63384");
    }

    [Fact]
    public async Task GetPatientIdByUserIdAsync_ShouldReturnSuccess_WhenFound()
    {
        var patient = CreateValidPatient(42);
        _patientRepoMock.Setup(p => p.FindAsync(It.IsAny<System.Linq.Expressions.Expression<System.Func<Patient, bool>>>()))
            .ReturnsAsync(new List<Patient> { patient });

        var result = await _service.GetPatientIdByUserIdAsync("pat_user_1");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
    }

    [Fact]
    public async Task GetPatientIdByUserIdAsync_ShouldReturnFailure_WhenNotFound()
    {
        _patientRepoMock.Setup(p => p.FindAsync(It.IsAny<System.Linq.Expressions.Expression<System.Func<Patient, bool>>>()))
            .ReturnsAsync(new List<Patient>());

        var result = await _service.GetPatientIdByUserIdAsync("unknown_user");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Patient profile not found");
    }

    [Fact]
    public async Task CallNextQueuePatientAsync_ShouldFail_WhenAppointmentNotFound()
    {
        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(999))
            .ReturnsAsync((Appointment?)null);

        var result = await _service.CallNextQueuePatientAsync(999, "doc_user_1");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Appointment not found");
    }

    [Fact]
    public async Task ConfirmAppointmentAsync_ShouldReturnFriendlyFailure_WhenConcurrencyConflictOccurs()
    {
        var appt = new Appointment
        {
            Id = 10,
            DoctorId = 1,
            Status = AppointmentStatus.Pending,
            PaymentStatus = PaymentStatus.Unpaid,
            Patient = new Patient { UserId = "pat-1" },
            Doctor = CreateValidDoctor(1)
        };
        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(10)).ReturnsAsync(appt);
        _uowMock.Setup(u => u.CommitAsync()).ThrowsAsync(new DbUpdateConcurrencyException("Concurrency conflict"));

        var result = await _service.ConfirmAppointmentAsync(10, 1);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("This appointment was modified concurrently by another request. Please refresh the page and try again.");
    }

    [Fact]
    public async Task RejectAppointmentAsync_ShouldReturnFriendlyFailure_WhenConcurrencyConflictOccurs()
    {
        var appt = new Appointment
        {
            Id = 11,
            DoctorId = 1,
            Status = AppointmentStatus.Pending,
            PaymentStatus = PaymentStatus.Unpaid,
            Patient = new Patient { UserId = "pat-1" },
            Doctor = CreateValidDoctor(1)
        };
        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(11)).ReturnsAsync(appt);
        _uowMock.Setup(u => u.CommitAsync()).ThrowsAsync(new DbUpdateConcurrencyException("Concurrency conflict"));

        var result = await _service.RejectAppointmentAsync(11, 1);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("This appointment was modified concurrently by another request. Please refresh the page and try again.");
    }

    [Fact]
    public async Task CancelAppointmentAsync_ShouldReturnFriendlyFailure_WhenConcurrencyConflictOccurs()
    {
        var appt = new Appointment
        {
            Id = 12,
            DoctorId = 1,
            PatientId = 2,
            AppointmentDate = _clinicClockMock.Object.Today.AddDays(2),
            StartTime = new TimeSpan(10, 0, 0),
            EndTime = new TimeSpan(10, 30, 0),
            Status = AppointmentStatus.Confirmed,
            PaymentStatus = PaymentStatus.Paid,
            Patient = new Patient { UserId = "pat-1" },
            Doctor = CreateValidDoctor(1)
        };
        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(12)).ReturnsAsync(appt);
        _uowMock.Setup(u => u.CommitAsync()).ThrowsAsync(new DbUpdateConcurrencyException("Concurrency conflict"));

        var result = await _service.CancelAppointmentAsync(12, "pat-1", false, false);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("This appointment was modified concurrently by another request. Please refresh the page and try again.");
    }

    [Fact]
    public async Task RescheduleAppointmentAsync_ShouldReturnFriendlyFailure_WhenConcurrencyConflictOccurs()
    {
        var appt = new Appointment
        {
            Id = 13,
            DoctorId = 1,
            PatientId = 2,
            AppointmentDate = _clinicClockMock.Object.Today.AddDays(2),
            StartTime = new TimeSpan(10, 0, 0),
            EndTime = new TimeSpan(10, 30, 0),
            Status = AppointmentStatus.Confirmed,
            PaymentStatus = PaymentStatus.Paid,
            Patient = new Patient { UserId = "pat-1" },
            Doctor = CreateValidDoctor(1)
        };
        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(13)).ReturnsAsync(appt);
        _doctorRepoMock.Setup(d => d.GetDoctorWithScheduleAndLeavesAsync(1)).ReturnsAsync(CreateValidDoctor(1));
        _doctorRepoMock.Setup(d => d.GetDoctorWithScheduleAsync(1)).ReturnsAsync(CreateValidDoctor(1));
        _appointmentRepoMock.Setup(a => a.HasConflictAsync(1, It.IsAny<DateTime>(), It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>())).ReturnsAsync(false);
        _appointmentRepoMock.Setup(a => a.HasPatientConflictAsync(2, It.IsAny<DateTime>(), It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>())).ReturnsAsync(false);
        _uowMock.Setup(u => u.CommitAsync()).ThrowsAsync(new DbUpdateConcurrencyException("Concurrency conflict"));

        var dto = new RescheduleRequestDto
        {
            AppointmentId = 13,
            NewAppointmentDate = _clinicClockMock.Object.Today.AddDays(7),
            NewStartTime = new TimeSpan(11, 0, 0)
        };
        var result = await _service.RescheduleAppointmentAsync(dto, "pat-1", false, false);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("This appointment was modified concurrently by another request. Please refresh the page and try again.");
    }

    [Fact]
    public async Task CompleteAppointmentAsync_ShouldReturnFriendlyFailure_WhenConcurrencyConflictOccurs()
    {
        var appt = new Appointment
        {
            Id = 14,
            DoctorId = 1,
            AppointmentDate = _clinicClockMock.Object.Today,
            StartTime = new TimeSpan(8, 0, 0),
            EndTime = new TimeSpan(8, 30, 0),
            Status = AppointmentStatus.Confirmed,
            PaymentStatus = PaymentStatus.Paid
        };
        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(14)).ReturnsAsync(appt);
        _uowMock.Setup(u => u.CommitAsync()).ThrowsAsync(new DbUpdateConcurrencyException("Concurrency conflict"));

        var result = await _service.CompleteAppointmentAsync(14, 1);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("This appointment was modified concurrently by another request. Please refresh the page and try again.");
    }

    [Fact]
    public async Task CheckInAppointmentAsync_ShouldFail_WhenAppointmentNotFound()
    {
        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(999))
            .ReturnsAsync((Appointment?)null);

        var result = await _service.CheckInAppointmentAsync(999, "doc_user_1", false);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Appointment not found");
    }

    [Fact]
    public async Task CheckInAppointmentAsync_ShouldFail_WhenDoctorDoesNotOwnAppointment()
    {
        var doctor = CreateValidDoctor(1);
        var patient = CreateValidPatient(1);
        var appt = new Appointment
        {
            Id = 50,
            DoctorId = 1,
            PatientId = 1,
            Status = AppointmentStatus.Confirmed,
            Doctor = doctor,
            Patient = patient
        };
        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(50)).ReturnsAsync(appt);

        var result = await _service.CheckInAppointmentAsync(50, "different_doctor_user", false);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Forbidden");
        _uowMock.Verify(u => u.CommitAsync(), Times.Never);
    }

    [Fact]
    public async Task CheckInAppointmentAsync_ShouldFail_WhenAppointmentNotConfirmed()
    {
        var doctor = CreateValidDoctor(1);
        var patient = CreateValidPatient(1);
        var appt = new Appointment
        {
            Id = 50,
            DoctorId = 1,
            PatientId = 1,
            Status = AppointmentStatus.Pending, // Pending cannot be checked in
            Doctor = doctor,
            Patient = patient
        };
        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(50)).ReturnsAsync(appt);

        var result = await _service.CheckInAppointmentAsync(50, "doc_user_1", false);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Only confirmed appointments can be checked in");
        _uowMock.Verify(u => u.CommitAsync(), Times.Never);
    }

    [Fact]
    public async Task CheckInAppointmentAsync_ShouldFail_WhenAlreadyCheckedIn()
    {
        var doctor = CreateValidDoctor(1);
        var patient = CreateValidPatient(1);
        var appt = new Appointment
        {
            Id = 50,
            DoctorId = 1,
            PatientId = 1,
            AppointmentDate = _clinicClockMock.Object.Today,
            Status = AppointmentStatus.Confirmed,
            CheckedInAt = DateTime.UtcNow.AddMinutes(-10),
            Doctor = doctor,
            Patient = patient
        };
        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(50)).ReturnsAsync(appt);

        var result = await _service.CheckInAppointmentAsync(50, "doc_user_1", false);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("already been checked in");
        _uowMock.Verify(u => u.CommitAsync(), Times.Never);
    }

    [Fact]
    public async Task CheckInAppointmentAsync_ShouldSucceed_AndPersistCheckedInAt_WhenDoctorOwnsAppointment()
    {
        var doctor = CreateValidDoctor(1);
        var patient = CreateValidPatient(1);
        var appt = new Appointment
        {
            Id = 50,
            DoctorId = 1,
            PatientId = 1,
            AppointmentDate = _clinicClockMock.Object.Today,
            Status = AppointmentStatus.Confirmed,
            CheckedInAt = null,
            Doctor = doctor,
            Patient = patient
        };
        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(50)).ReturnsAsync(appt);
        _uowMock.Setup(u => u.CommitAsync()).ReturnsAsync(1);

        var result = await _service.CheckInAppointmentAsync(50, "doc_user_1", false);

        result.IsSuccess.Should().BeTrue();
        appt.CheckedInAt.Should().NotBeNull();
        appt.CheckedInAt.Should().Be(_clinicClockMock.Object.Now);
        _appointmentRepoMock.Verify(r => r.Update(appt), Times.Once);
        _uowMock.Verify(u => u.CommitAsync(), Times.Once);
        _notificationServiceMock.Verify(n => n.SendNotificationAsync(
            "pat_user_1",
            It.Is<string>(s => s.Contains("Checked In") || s.Contains("وصولك")),
            It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task CheckInAppointmentAsync_ShouldSucceed_WhenAdminChecksIn()
    {
        var doctor = CreateValidDoctor(1);
        var patient = CreateValidPatient(1);
        var appt = new Appointment
        {
            Id = 50,
            DoctorId = 1,
            PatientId = 1,
            AppointmentDate = _clinicClockMock.Object.Today,
            Status = AppointmentStatus.Confirmed,
            CheckedInAt = null,
            Doctor = doctor,
            Patient = patient
        };
        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(50)).ReturnsAsync(appt);
        _uowMock.Setup(u => u.CommitAsync()).ReturnsAsync(1);

        var result = await _service.CheckInAppointmentAsync(50, "admin_user_x", isAdmin: true);

        result.IsSuccess.Should().BeTrue();
        appt.CheckedInAt.Should().NotBeNull();
        _uowMock.Verify(u => u.CommitAsync(), Times.Once);
    }

    [Fact]
    public async Task CheckInAppointmentAsync_ShouldFail_WhenAppointmentDateIsNotToday()
    {
        var doctor = CreateValidDoctor(1);
        var patient = CreateValidPatient(1);
        var appt = new Appointment
        {
            Id = 51,
            DoctorId = 1,
            PatientId = 1,
            AppointmentDate = _clinicClockMock.Object.Today.AddDays(1),
            Status = AppointmentStatus.Confirmed,
            Doctor = doctor,
            Patient = patient
        };
        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(51)).ReturnsAsync(appt);

        var result = await _service.CheckInAppointmentAsync(51, "doc_user_1", isAdmin: false);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("scheduled appointment date");
        _uowMock.Verify(u => u.CommitAsync(), Times.Never);
    }

    [Fact]
    public async Task CheckInAppointmentAsync_ShouldFail_WhenTypeIsTelemedicine()
    {
        var doctor = CreateValidDoctor(1);
        var patient = CreateValidPatient(1);
        var appt = new Appointment
        {
            Id = 52,
            DoctorId = 1,
            PatientId = 1,
            AppointmentDate = _clinicClockMock.Object.Today,
            Type = AppointmentType.Telemedicine,
            Status = AppointmentStatus.Confirmed,
            Doctor = doctor,
            Patient = patient
        };
        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(52)).ReturnsAsync(appt);

        var result = await _service.CheckInAppointmentAsync(52, "doc_user_1", isAdmin: false);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Telemedicine");
        _uowMock.Verify(u => u.CommitAsync(), Times.Never);
    }

    [Fact]
    public async Task CallNextQueuePatientAsync_ShouldFail_WhenNotCheckedIn()
    {
        var doctor = CreateValidDoctor(1);
        var patient = CreateValidPatient(1);
        var appt = new Appointment
        {
            Id = 53,
            DoctorId = 1,
            PatientId = 1,
            AppointmentDate = _clinicClockMock.Object.Today,
            Status = AppointmentStatus.Confirmed,
            CheckedInAt = null,
            Doctor = doctor,
            Patient = patient
        };
        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(53)).ReturnsAsync(appt);

        var result = await _service.CallNextQueuePatientAsync(53, "doc_user_1", isAdmin: false);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not checked in");
    }

    [Fact]
    public async Task CallNextQueuePatientAsync_ShouldFail_WhenTelemedicine()
    {
        var doctor = CreateValidDoctor(1);
        var patient = CreateValidPatient(1);
        var appt = new Appointment
        {
            Id = 54,
            DoctorId = 1,
            PatientId = 1,
            AppointmentDate = _clinicClockMock.Object.Today,
            Status = AppointmentStatus.Confirmed,
            CheckedInAt = _clinicClockMock.Object.Now,
            Type = AppointmentType.Telemedicine,
            Doctor = doctor,
            Patient = patient
        };
        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(54)).ReturnsAsync(appt);

        var result = await _service.CallNextQueuePatientAsync(54, "doc_user_1", isAdmin: false);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Telemedicine");
    }

    [Fact]
    public async Task CallNextQueuePatientAsync_ShouldFail_WhenDateIsNotToday()
    {
        var doctor = CreateValidDoctor(1);
        var patient = CreateValidPatient(1);
        var appt = new Appointment
        {
            Id = 55,
            DoctorId = 1,
            PatientId = 1,
            AppointmentDate = _clinicClockMock.Object.Today.AddDays(2),
            Status = AppointmentStatus.Confirmed,
            CheckedInAt = _clinicClockMock.Object.Now,
            Doctor = doctor,
            Patient = patient
        };
        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(55)).ReturnsAsync(appt);

        var result = await _service.CallNextQueuePatientAsync(55, "doc_user_1", isAdmin: false);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not scheduled for today");
    }

    [Fact]
    public async Task EnsureTelehealthRoomSecretAsync_WhenAppointmentNotFound_ReturnsFailure()
    {
        _appointmentRepoMock.Setup(a => a.GetByIdAsync(999)).ReturnsAsync((Appointment?)null);

        var result = await _service.EnsureTelehealthRoomSecretAsync(999);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Appointment not found.");
    }

    [Fact]
    public async Task EnsureTelehealthRoomSecretAsync_WhenNotTelemedicine_ReturnsFailure()
    {
        var appt = new Appointment
        {
            Id = 55,
            Type = AppointmentType.Consultation
        };
        _appointmentRepoMock.Setup(a => a.GetByIdAsync(55)).ReturnsAsync(appt);

        var result = await _service.EnsureTelehealthRoomSecretAsync(55);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Appointment is not a telehealth appointment.");
    }

    [Fact]
    public async Task EnsureTelehealthRoomSecretAsync_WhenSecretAlreadyExists_ReturnsExistingSecretWithoutRecommitting()
    {
        var appt = new Appointment
        {
            Id = 56,
            Type = AppointmentType.Telemedicine,
            TelehealthRoomSecret = "existing_secret_12345"
        };
        _appointmentRepoMock.Setup(a => a.GetByIdAsync(56)).ReturnsAsync(appt);

        var result = await _service.EnsureTelehealthRoomSecretAsync(56);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("existing_secret_12345");
        _uowMock.Verify(u => u.CommitAsync(), Times.Never);
    }

    [Fact]
    public async Task EnsureTelehealthRoomSecretAsync_WhenSecretMissing_GeneratesAndCommitsSecret()
    {
        var appt = new Appointment
        {
            Id = 57,
            Type = AppointmentType.Telemedicine,
            TelehealthRoomSecret = null
        };
        _appointmentRepoMock.Setup(a => a.GetByIdAsync(57)).ReturnsAsync(appt);
        _uowMock.Setup(u => u.CommitAsync()).ReturnsAsync(1);

        var result = await _service.EnsureTelehealthRoomSecretAsync(57);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNullOrWhiteSpace();
        result.Value!.Length.Should().Be(32);
        appt.TelehealthRoomSecret.Should().Be(result.Value);
        _appointmentRepoMock.Verify(a => a.Update(appt), Times.Once);
        _uowMock.Verify(u => u.CommitAsync(), Times.Once);
    }

    #region BIZ-02 & BIZ-06 Concurrency & Rescheduling Integrity Tests

    [Fact]
    public async Task CreateAppointmentAsync_WhenSlotConflictUnderLock_RollsBackAndReturnsFailure()
    {
        // Arrange
        var doctor = CreateValidDoctor(1);
        var patient = CreateValidPatient(1);
        _doctorRepoMock.Setup(d => d.GetDoctorWithScheduleAndLeavesAsync(1)).ReturnsAsync(doctor);
        _patientRepoMock.Setup(p => p.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Patient, bool>>>()))
            .ReturnsAsync(new List<Patient> { patient });

        var txMock = new Mock<IDatabaseTransaction>();
        _uowMock.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(txMock.Object);

        // Pre-check finds doctor slot conflict
        _appointmentRepoMock.Setup(a => a.HasPatientConflictAsync(1, It.IsAny<DateTime>(), It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>()))
            .ReturnsAsync(false);
        _appointmentRepoMock.Setup(a => a.HasConflictAsync(1, It.IsAny<DateTime>(), It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>()))
            .ReturnsAsync(true);

        var dto = new BookingRequestDto
        {
            DoctorId = 1,
            PatientId = 1,
            AppointmentDate = new DateTime(2026, 11, 22),
            StartTime = new TimeSpan(10, 0, 0),
            Type = AppointmentType.Consultation
        };

        // Act
        var result = await _service.BookAppointmentAsync(dto);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("already booked");
        txMock.Verify(t => t.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
        _uowMock.Verify(u => u.CommitAsync(), Times.Never);
    }

    [Fact]
    public async Task RescheduleAppointmentAsync_WhenCommitFails_RestoresOriginalAppointmentProperties()
    {
        // Arrange
        var doctor = CreateValidDoctor(1);
        var patient = CreateValidPatient(1);
        var originalDate = new DateTime(2026, 11, 15);
        var originalStartTime = new TimeSpan(14, 0, 0);
        var originalEndTime = new TimeSpan(14, 30, 0);
        var originalNotes = "Original notes";

        var appointment = new Appointment
        {
            Id = 15,
            DoctorId = 1,
            PatientId = 1,
            Status = AppointmentStatus.Pending,
            AppointmentDate = originalDate,
            StartTime = originalStartTime,
            EndTime = originalEndTime,
            ReminderSent = true,
            Notes = originalNotes,
            Doctor = doctor,
            Patient = patient
        };

        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(15)).ReturnsAsync(appointment);
        _doctorRepoMock.Setup(d => d.GetDoctorWithScheduleAndLeavesAsync(1)).ReturnsAsync(doctor);

        _appointmentRepoMock.Setup(a => a.HasConflictAsync(1, It.IsAny<DateTime>(), It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>(), 15))
            .ReturnsAsync(false);
        _appointmentRepoMock.Setup(a => a.HasPatientConflictAsync(1, It.IsAny<DateTime>(), It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>(), 15))
            .ReturnsAsync(false);

        var txMock = new Mock<IDatabaseTransaction>();
        _uowMock.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(txMock.Object);

        // Simulate database exception during commit (e.g. concurrency violation)
        _uowMock.Setup(u => u.CommitAsync())
            .ThrowsAsync(new Microsoft.EntityFrameworkCore.DbUpdateException("Database error during reschedule"));

        var dto = new RescheduleRequestDto
        {
            AppointmentId = 15,
            NewAppointmentDate = new DateTime(2026, 11, 22),
            NewStartTime = new TimeSpan(11, 0, 0),
            Reason = "Patient requested change"
        };

        // Act
        var result = await _service.RescheduleAppointmentAsync(dto, "pat_user_1", isDoctorOrAdmin: false);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("slot was just booked");

        // Verify tracked entity properties were reverted to original
        appointment.AppointmentDate.Should().Be(originalDate);
        appointment.StartTime.Should().Be(originalStartTime);
        appointment.EndTime.Should().Be(originalEndTime);
        appointment.ReminderSent.Should().BeTrue();
        appointment.Notes.Should().Be(originalNotes);

        txMock.Verify(t => t.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RescheduleAppointmentAsync_WhenTargetSlotHasIntervalConflictViaRepo_ReturnsFailure()
    {
        // Arrange
        var doctor = CreateValidDoctor(1);
        var patient = CreateValidPatient(1);
        var appointment = new Appointment
        {
            Id = 16,
            DoctorId = 1,
            PatientId = 1,
            Status = AppointmentStatus.Pending,
            AppointmentDate = new DateTime(2026, 11, 15),
            StartTime = new TimeSpan(14, 0, 0),
            EndTime = new TimeSpan(14, 30, 0),
            Doctor = doctor,
            Patient = patient
        };

        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(16)).ReturnsAsync(appointment);
        _doctorRepoMock.Setup(d => d.GetDoctorWithScheduleAndLeavesAsync(1)).ReturnsAsync(doctor);

        // Repo detects conflict for the new slot
        _appointmentRepoMock.Setup(a => a.HasConflictAsync(1, new DateTime(2026, 11, 22), new TimeSpan(11, 0, 0), new TimeSpan(11, 30, 0), 16))
            .ReturnsAsync(true);

        var dto = new RescheduleRequestDto
        {
            AppointmentId = 16,
            NewAppointmentDate = new DateTime(2026, 11, 22),
            NewStartTime = new TimeSpan(11, 0, 0)
        };

        // Act
        var result = await _service.RescheduleAppointmentAsync(dto, "pat_user_1", isDoctorOrAdmin: false);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("already booked");
        _uowMock.Verify(u => u.CommitAsync(), Times.Never);
    }

    #region Phase 6 Batch E Verified Integrity & Refund Tests

    [Fact]
    public async Task CancelAppointmentAsync_WhenPaid_TransitionsToRefundedAndSendsDemoRefundNotice()
    {
        // Arrange
        var doctor = CreateValidDoctor(1);
        var patient = CreateValidPatient(1);
        patient.User.Email = "omar@test.com";

        var appointment = new Appointment
        {
            Id = 88,
            DoctorId = 1,
            PatientId = 1,
            Status = AppointmentStatus.Confirmed,
            PaymentStatus = PaymentStatus.Paid,
            PaidAmount = 450m,
            ConsultationFee = 500m,
            AppointmentDate = new DateTime(2026, 11, 20),
            StartTime = new TimeSpan(10, 0, 0),
            Doctor = doctor,
            Patient = patient
        };

        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(88)).ReturnsAsync(appointment);
        _uowMock.Setup(u => u.CommitAsync()).ReturnsAsync(1);

        // Act: Doctor/Admin cancels appointment -> patient receives cancellation notification with demo refund notice
        var result = await _service.CancelAppointmentAsync(88, "doc_user_1", isDoctorOrAdmin: true, isAdmin: false);

        // Assert
        result.IsSuccess.Should().BeTrue();
        appointment.Status.Should().Be(AppointmentStatus.Cancelled);
        appointment.PaymentStatus.Should().Be(PaymentStatus.Refunded);

        // Notification to patient must include demo refund wording
        _notificationServiceMock.Verify(n => n.SendNotificationAsync(
            "pat_user_1",
            It.Is<string>(s => s.Contains("Cancelled")),
            It.Is<string>(msg => msg.Contains("demo") || msg.Contains("refund"))), Times.Once);

        _emailServiceMock.Verify(e => e.SendEmailAsync(
            "omar@test.com",
            It.Is<string>(s => s.Contains("Cancelled")),
            It.Is<string>(body => body.Contains("demo refund") || body.Contains("simulated"))), Times.Once);
    }

    [Fact]
    public async Task CancelAppointmentAsync_WhenUnpaid_LeavesPaymentStatusUnpaid()
    {
        // Arrange
        var doctor = CreateValidDoctor(1);
        var patient = CreateValidPatient(1);
        var appointment = new Appointment
        {
            Id = 89,
            DoctorId = 1,
            PatientId = 1,
            Status = AppointmentStatus.Pending,
            PaymentStatus = PaymentStatus.Unpaid,
            ConsultationFee = 500m,
            AppointmentDate = new DateTime(2026, 11, 20),
            StartTime = new TimeSpan(10, 0, 0),
            Doctor = doctor,
            Patient = patient
        };

        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(89)).ReturnsAsync(appointment);
        _uowMock.Setup(u => u.CommitAsync()).ReturnsAsync(1);

        // Act
        var result = await _service.CancelAppointmentAsync(89, "pat_user_1", isDoctorOrAdmin: false);

        // Assert
        result.IsSuccess.Should().BeTrue();
        appointment.Status.Should().Be(AppointmentStatus.Cancelled);
        appointment.PaymentStatus.Should().Be(PaymentStatus.Unpaid);
    }

    [Fact]
    public async Task RejectAppointmentAsync_WhenPaid_TransitionsToRefundedAndSendsDemoRefundEmail()
    {
        // Arrange
        var doctor = CreateValidDoctor(1);
        var patient = CreateValidPatient(1);
        patient.User.Email = "omar@test.com";

        var appointment = new Appointment
        {
            Id = 90,
            DoctorId = 1,
            PatientId = 1,
            Status = AppointmentStatus.Pending,
            PaymentStatus = PaymentStatus.Paid,
            ConsultationFee = 500m,
            PaidAmount = 500m,
            AppointmentDate = new DateTime(2026, 11, 20),
            StartTime = new TimeSpan(10, 0, 0),
            Doctor = doctor,
            Patient = patient
        };

        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(90)).ReturnsAsync(appointment);
        _uowMock.Setup(u => u.CommitAsync()).ReturnsAsync(1);

        // Act: Doctor rejects pending appointment
        var result = await _service.RejectAppointmentAsync(90, doctorId: 1);

        // Assert
        result.IsSuccess.Should().BeTrue();
        appointment.Status.Should().Be(AppointmentStatus.Rejected);
        appointment.PaymentStatus.Should().Be(PaymentStatus.Refunded);

        _notificationServiceMock.Verify(n => n.SendNotificationAsync(
            "pat_user_1",
            It.Is<string>(s => s.Contains("Declined")),
            It.Is<string>(msg => msg.Contains("demo") || msg.Contains("refund"))), Times.Once);

        _emailServiceMock.Verify(e => e.SendEmailAsync(
            "omar@test.com",
            It.Is<string>(s => s.Contains("Declined")),
            It.Is<string>(body => body.Contains("demo refund") || body.Contains("simulated"))), Times.Once);
    }

    [Fact]
    public async Task RescheduleAppointmentAsync_WhenInitiatedByDoctor_NotifiesPatientAndSendsEmail()
    {
        // Arrange
        var doctor = CreateValidDoctor(1);
        var patient = CreateValidPatient(1);
        patient.User.Email = "omar@test.com";

        var appointment = new Appointment
        {
            Id = 91,
            DoctorId = 1,
            PatientId = 1,
            Status = AppointmentStatus.Confirmed,
            AppointmentDate = new DateTime(2026, 11, 15),
            StartTime = new TimeSpan(10, 0, 0),
            EndTime = new TimeSpan(10, 30, 0),
            Doctor = doctor,
            Patient = patient
        };

        _appointmentRepoMock.Setup(a => a.GetByIdWithDetailsAsync(91)).ReturnsAsync(appointment);
        _doctorRepoMock.Setup(d => d.GetDoctorWithScheduleAndLeavesAsync(1)).ReturnsAsync(doctor);
        _appointmentRepoMock.Setup(a => a.HasConflictAsync(1, new DateTime(2026, 11, 22), new TimeSpan(11, 0, 0), new TimeSpan(11, 30, 0), 91))
            .ReturnsAsync(false);
        _appointmentRepoMock.Setup(a => a.HasPatientConflictAsync(1, new DateTime(2026, 11, 22), new TimeSpan(11, 0, 0), new TimeSpan(11, 30, 0), 91))
            .ReturnsAsync(false);

        var txMock = new Mock<IDatabaseTransaction>();
        _uowMock.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(txMock.Object);
        _uowMock.Setup(u => u.AcquireApplicationLocksAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<IAsyncDisposable>());
        _uowMock.Setup(u => u.CommitAsync()).ReturnsAsync(1);

        var dto = new RescheduleRequestDto
        {
            AppointmentId = 91,
            NewAppointmentDate = new DateTime(2026, 11, 22),
            NewStartTime = new TimeSpan(11, 0, 0),
            Reason = "Doctor clinic shift adjustment"
        };

        // Act: Doctor reschedules
        var result = await _service.RescheduleAppointmentAsync(dto, "doc_user_1", isDoctorOrAdmin: true, isAdmin: false);

        // Assert
        result.IsSuccess.Should().BeTrue();
        appointment.AppointmentDate.Should().Be(new DateTime(2026, 11, 22));
        appointment.StartTime.Should().Be(new TimeSpan(11, 0, 0));

        // Patient must be notified because doctor initiated reschedule
        _notificationServiceMock.Verify(n => n.SendNotificationAsync(
            "pat_user_1",
            It.Is<string>(s => s.Contains("Rescheduled")),
            It.Is<string>(msg => msg.Contains("rescheduled"))), Times.Once);

        _emailServiceMock.Verify(e => e.SendEmailAsync(
            "omar@test.com",
            It.Is<string>(s => s.Contains("Rescheduled")),
            It.Is<string>(b => b.Contains("rescheduled"))), Times.Once);
    }

    [Fact]
    public async Task BookAppointmentAsync_AcquiresCompositeDoctorAndPatientApplicationLocksInTransaction()
    {
        // Arrange
        var doctor = CreateValidDoctor(1);
        var patient = CreateValidPatient(1);
        patient.User.Email = "omar@test.com";

        _doctorRepoMock.Setup(d => d.GetDoctorWithScheduleAndLeavesAsync(1)).ReturnsAsync(doctor);
        _patientRepoMock.Setup(p => p.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Patient, bool>>>()))
            .ReturnsAsync(new List<Patient> { patient });
        _patientRepoMock.Setup(p => p.GetByIdAsync(1)).ReturnsAsync(patient);
        _appointmentRepoMock.Setup(a => a.HasConflictAsync(1, new DateTime(2026, 11, 22), new TimeSpan(10, 0, 0), It.IsAny<TimeSpan>(), null))
            .ReturnsAsync(false);
        _appointmentRepoMock.Setup(a => a.HasPatientConflictAsync(1, new DateTime(2026, 11, 22), new TimeSpan(10, 0, 0), It.IsAny<TimeSpan>(), null))
            .ReturnsAsync(false);

        var txMock = new Mock<IDatabaseTransaction>();
        _uowMock.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(txMock.Object);

        List<string>? capturedLockKeys = null;
        _uowMock.Setup(u => u.AcquireApplicationLocksAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<string>, TimeSpan, CancellationToken>((keys, timeout, ct) => capturedLockKeys = keys.ToList())
            .ReturnsAsync(Mock.Of<IAsyncDisposable>());

        _uowMock.Setup(u => u.CommitAsync()).ReturnsAsync(1);

        var request = new BookingRequestDto
        {
            DoctorId = 1,
            PatientId = 1,
            AppointmentDate = new DateTime(2026, 11, 22),
            StartTime = new TimeSpan(10, 0, 0),
            Type = AppointmentType.Consultation
        };

        // Act
        var result = await _service.BookAppointmentAsync(request);

        // Assert
        result.IsSuccess.Should().BeTrue();
        capturedLockKeys.Should().NotBeNull();
        capturedLockKeys.Should().Contain("DoctorBooking_1_20261122");
        capturedLockKeys.Should().Contain("PatientBooking_1_20261122");
        _uowMock.Verify(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        txMock.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BookAppointmentAsync_WhenNotesExceed500Characters_ReturnsFailure()
    {
        // Arrange
        var request = new BookingRequestDto
        {
            DoctorId = 1,
            PatientId = 1,
            AppointmentDate = new DateTime(2026, 11, 22),
            StartTime = new TimeSpan(10, 0, 0),
            Notes = new string('N', 501)
        };

        // Act
        var result = await _service.BookAppointmentAsync(request);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("500 characters");
    }

    [Fact]
    public async Task RescheduleAppointmentAsync_WhenReasonExceeds250Characters_ReturnsFailure()
    {
        // Arrange
        var request = new RescheduleRequestDto
        {
            AppointmentId = 1,
            NewAppointmentDate = new DateTime(2026, 11, 22),
            NewStartTime = new TimeSpan(10, 0, 0),
            Reason = new string('R', 251)
        };

        // Act
        var result = await _service.RescheduleAppointmentAsync(request, "user_1");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("250 characters");
    }

    #endregion

    #endregion
}

