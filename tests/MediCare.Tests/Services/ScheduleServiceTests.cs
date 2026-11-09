using System.Linq.Expressions;
using FluentAssertions;
using MediCare.Data.Entities;
using MediCare.Data.Enums;
using MediCare.Data.UnitOfWork;
using MediCare.Services.DTOs;
using MediCare.Services.Implementations;
using MediCare.Services.Validators;
using Moq;
using Xunit;

namespace MediCare.Tests.Services;

public class ScheduleServiceTests
{
    private readonly Mock<IUnitOfWork> _mockUow;
    private readonly WorkingHoursValidator _workingHoursValidator;
    private readonly DoctorLeaveValidator _doctorLeaveValidator;
    private readonly ScheduleService _sut;

    private readonly List<WorkingHours> _workingHoursStore = new();
    private readonly List<DoctorLeave> _doctorLeavesStore = new();
    private readonly List<Appointment> _appointmentsStore = new();

    public ScheduleServiceTests()
    {
        _mockUow = new Mock<IUnitOfWork>();
        _workingHoursValidator = new WorkingHoursValidator();
        _doctorLeaveValidator = new DoctorLeaveValidator();

        _mockUow.Setup(u => u.Appointments.FindAsync(It.IsAny<Expression<Func<Appointment, bool>>>()))
            .ReturnsAsync((Expression<Func<Appointment, bool>> pred) =>
                _appointmentsStore.Where(pred.Compile()).ToList());

        _mockUow.Setup(u => u.WorkingHours.FindAsync(It.IsAny<Expression<Func<WorkingHours, bool>>>()))
            .ReturnsAsync((Expression<Func<WorkingHours, bool>> pred) =>
                _workingHoursStore.Where(pred.Compile()).ToList());

        _mockUow.Setup(u => u.WorkingHours.GetByIdAsync(It.IsAny<int>()))
            .ReturnsAsync((int id) => _workingHoursStore.FirstOrDefault(w => w.Id == id));

        _mockUow.Setup(u => u.WorkingHours.AddAsync(It.IsAny<WorkingHours>()))
            .Callback<WorkingHours>(w =>
            {
                if (w.Id == 0) w.Id = _workingHoursStore.Count + 1;
                _workingHoursStore.Add(w);
            })
            .Returns(Task.CompletedTask);

        _mockUow.Setup(u => u.WorkingHours.Delete(It.IsAny<WorkingHours>()))
            .Callback<WorkingHours>(w => _workingHoursStore.Remove(w));

        _mockUow.Setup(u => u.DoctorLeaves.FindAsync(It.IsAny<Expression<Func<DoctorLeave, bool>>>()))
            .ReturnsAsync((Expression<Func<DoctorLeave, bool>> pred) =>
                _doctorLeavesStore.Where(pred.Compile()).ToList());

        _mockUow.Setup(u => u.DoctorLeaves.GetByIdAsync(It.IsAny<int>()))
            .ReturnsAsync((int id) => _doctorLeavesStore.FirstOrDefault(l => l.Id == id));

        _mockUow.Setup(u => u.DoctorLeaves.AddAsync(It.IsAny<DoctorLeave>()))
            .Callback<DoctorLeave>(l =>
            {
                if (l.Id == 0) l.Id = _doctorLeavesStore.Count + 1;
                _doctorLeavesStore.Add(l);
            })
            .Returns(Task.CompletedTask);

        _mockUow.Setup(u => u.DoctorLeaves.Delete(It.IsAny<DoctorLeave>()))
            .Callback<DoctorLeave>(l => _doctorLeavesStore.Remove(l));

        _mockUow.Setup(u => u.CommitAsync()).ReturnsAsync(1);

        _sut = new ScheduleService(_mockUow.Object, _workingHoursValidator, _doctorLeaveValidator);
    }

    #region Working Hours Tests

    [Fact]
    public async Task GetWorkingHoursAsync_WhenNoHours_ReturnsEmptyList()
    {
        var result = await _sut.GetWorkingHoursAsync(1);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task GetWorkingHoursAsync_ReturnsOrderedListByDayAndStartTime()
    {
        _workingHoursStore.AddRange(new[]
        {
            new WorkingHours { Id = 1, DoctorId = 1, DayOfWeek = DayOfWeek.Tuesday, StartTime = TimeSpan.FromHours(14), EndTime = TimeSpan.FromHours(18) },
            new WorkingHours { Id = 2, DoctorId = 1, DayOfWeek = DayOfWeek.Monday, StartTime = TimeSpan.FromHours(10), EndTime = TimeSpan.FromHours(12) },
            new WorkingHours { Id = 3, DoctorId = 1, DayOfWeek = DayOfWeek.Monday, StartTime = TimeSpan.FromHours(8), EndTime = TimeSpan.FromHours(10) },
            new WorkingHours { Id = 4, DoctorId = 2, DayOfWeek = DayOfWeek.Monday, StartTime = TimeSpan.FromHours(8), EndTime = TimeSpan.FromHours(10) }
        });

        var result = await _sut.GetWorkingHoursAsync(1);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(3);
        result.Value![0].Id.Should().Be(3); // Monday 8am
        result.Value[1].Id.Should().Be(2);  // Monday 10am
        result.Value[2].Id.Should().Be(1);  // Tuesday 2pm
    }

    [Fact]
    public async Task GetWorkingHoursByIdAsync_WhenNotFound_ReturnsFailure()
    {
        var result = await _sut.GetWorkingHoursByIdAsync(999, doctorId: 1);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not found or unauthorized");
    }

    [Fact]
    public async Task GetWorkingHoursByIdAsync_WhenBelongsToAnotherDoctor_ReturnsFailure()
    {
        _workingHoursStore.Add(new WorkingHours
        {
            Id = 5,
            DoctorId = 2,
            DayOfWeek = DayOfWeek.Monday,
            StartTime = TimeSpan.FromHours(9),
            EndTime = TimeSpan.FromHours(12)
        });

        var result = await _sut.GetWorkingHoursByIdAsync(5, doctorId: 1);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not found or unauthorized");
    }

    [Fact]
    public async Task GetWorkingHoursByIdAsync_WhenFoundAndAuthorized_ReturnsDto()
    {
        _workingHoursStore.Add(new WorkingHours
        {
            Id = 5,
            DoctorId = 1,
            DayOfWeek = DayOfWeek.Wednesday,
            StartTime = TimeSpan.FromHours(9),
            EndTime = TimeSpan.FromHours(12)
        });

        var result = await _sut.GetWorkingHoursByIdAsync(5, doctorId: 1);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Id.Should().Be(5);
        result.Value.DayOfWeek.Should().Be(DayOfWeek.Wednesday);
        result.Value.StartTime.Should().Be(TimeSpan.FromHours(9));
        result.Value.EndTime.Should().Be(TimeSpan.FromHours(12));
    }

    [Fact]
    public async Task AddWorkingHoursAsync_WhenValidationFails_ReturnsFailure()
    {
        var dto = new WorkingHoursDto
        {
            DayOfWeek = DayOfWeek.Monday,
            StartTime = TimeSpan.FromHours(14),
            EndTime = TimeSpan.FromHours(12) // End before start
        };

        var result = await _sut.AddWorkingHoursAsync(dto, doctorId: 1);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("End time must be after start time");
    }

    [Theory]
    [InlineData(9, 11, 10, 12)] // Starts inside existing
    [InlineData(11, 13, 10, 12)] // Ends inside existing
    [InlineData(9, 13, 10, 12)]  // Encompasses existing
    [InlineData(10, 12, 10, 12)] // Exact duplicate
    public async Task AddWorkingHoursAsync_WhenOverlappingOnSameDay_ReturnsFailure(
        int newStart, int newEnd, int existStart, int existEnd)
    {
        _workingHoursStore.Add(new WorkingHours
        {
            Id = 1,
            DoctorId = 1,
            DayOfWeek = DayOfWeek.Monday,
            StartTime = TimeSpan.FromHours(existStart),
            EndTime = TimeSpan.FromHours(existEnd)
        });

        var dto = new WorkingHoursDto
        {
            DayOfWeek = DayOfWeek.Monday,
            StartTime = TimeSpan.FromHours(newStart),
            EndTime = TimeSpan.FromHours(newEnd)
        };

        var result = await _sut.AddWorkingHoursAsync(dto, doctorId: 1);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("overlap with an existing schedule");
    }

    [Fact]
    public async Task AddWorkingHoursAsync_WhenSameTimesOnDifferentDay_Succeeds()
    {
        _workingHoursStore.Add(new WorkingHours
        {
            Id = 1,
            DoctorId = 1,
            DayOfWeek = DayOfWeek.Monday,
            StartTime = TimeSpan.FromHours(9),
            EndTime = TimeSpan.FromHours(12)
        });

        var dto = new WorkingHoursDto
        {
            DayOfWeek = DayOfWeek.Tuesday,
            StartTime = TimeSpan.FromHours(9),
            EndTime = TimeSpan.FromHours(12)
        };

        var result = await _sut.AddWorkingHoursAsync(dto, doctorId: 1);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeGreaterThan(0);
        _workingHoursStore.Should().HaveCount(2);
    }

    [Fact]
    public async Task AddWorkingHoursAsync_WhenNonOverlappingOnSameDay_Succeeds()
    {
        _workingHoursStore.Add(new WorkingHours
        {
            Id = 1,
            DoctorId = 1,
            DayOfWeek = DayOfWeek.Monday,
            StartTime = TimeSpan.FromHours(8),
            EndTime = TimeSpan.FromHours(11)
        });

        var dto = new WorkingHoursDto
        {
            DayOfWeek = DayOfWeek.Monday,
            StartTime = TimeSpan.FromHours(14),
            EndTime = TimeSpan.FromHours(18)
        };

        var result = await _sut.AddWorkingHoursAsync(dto, doctorId: 1);

        result.IsSuccess.Should().BeTrue();
        _workingHoursStore.Should().HaveCount(2);
        _mockUow.Verify(u => u.CommitAsync(), Times.Once);
    }

    [Fact]
    public async Task UpdateWorkingHoursAsync_WhenValidationFails_ReturnsFailure()
    {
        var dto = new WorkingHoursDto
        {
            Id = 1,
            DayOfWeek = DayOfWeek.Monday,
            StartTime = TimeSpan.FromHours(10),
            EndTime = TimeSpan.FromHours(8)
        };

        var result = await _sut.UpdateWorkingHoursAsync(dto, doctorId: 1);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateWorkingHoursAsync_WhenNotFoundOrUnauthorized_ReturnsFailure()
    {
        var dto = new WorkingHoursDto
        {
            Id = 99,
            DayOfWeek = DayOfWeek.Monday,
            StartTime = TimeSpan.FromHours(9),
            EndTime = TimeSpan.FromHours(12)
        };

        var result = await _sut.UpdateWorkingHoursAsync(dto, doctorId: 1);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not found or unauthorized");
    }

    [Fact]
    public async Task UpdateWorkingHoursAsync_WhenOverlapsAnotherEntryOnSameDay_ReturnsFailure()
    {
        _workingHoursStore.AddRange(new[]
        {
            new WorkingHours { Id = 1, DoctorId = 1, DayOfWeek = DayOfWeek.Monday, StartTime = TimeSpan.FromHours(8), EndTime = TimeSpan.FromHours(11) },
            new WorkingHours { Id = 2, DoctorId = 1, DayOfWeek = DayOfWeek.Monday, StartTime = TimeSpan.FromHours(13), EndTime = TimeSpan.FromHours(17) }
        });

        var dto = new WorkingHoursDto
        {
            Id = 2,
            DayOfWeek = DayOfWeek.Monday,
            StartTime = TimeSpan.FromHours(10), // Collides with entry #1
            EndTime = TimeSpan.FromHours(14)
        };

        var result = await _sut.UpdateWorkingHoursAsync(dto, doctorId: 1);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("overlap");
    }

    [Fact]
    public async Task UpdateWorkingHoursAsync_WhenRetainingOwnSchedule_DoesNotSelfCollide()
    {
        var entity = new WorkingHours
        {
            Id = 1,
            DoctorId = 1,
            DayOfWeek = DayOfWeek.Monday,
            StartTime = TimeSpan.FromHours(8),
            EndTime = TimeSpan.FromHours(12)
        };
        _workingHoursStore.Add(entity);

        var dto = new WorkingHoursDto
        {
            Id = 1,
            DayOfWeek = DayOfWeek.Monday,
            StartTime = TimeSpan.FromHours(8),
            EndTime = TimeSpan.FromHours(13) // Extended 1 hour
        };

        var result = await _sut.UpdateWorkingHoursAsync(dto, doctorId: 1);

        result.IsSuccess.Should().BeTrue();
        entity.EndTime.Should().Be(TimeSpan.FromHours(13));
        _mockUow.Verify(u => u.CommitAsync(), Times.Once);
    }

    [Fact]
    public async Task DeleteWorkingHoursAsync_WhenNotFoundOrUnauthorized_ReturnsFailure()
    {
        var result = await _sut.DeleteWorkingHoursAsync(999, doctorId: 1);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not found or unauthorized");
    }

    [Fact]
    public async Task DeleteWorkingHoursAsync_WhenAuthorized_DeletesAndCommits()
    {
        var entity = new WorkingHours { Id = 1, DoctorId = 1, DayOfWeek = DayOfWeek.Monday };
        _workingHoursStore.Add(entity);

        var result = await _sut.DeleteWorkingHoursAsync(1, doctorId: 1);

        result.IsSuccess.Should().BeTrue();
        _workingHoursStore.Should().BeEmpty();
        _mockUow.Verify(u => u.CommitAsync(), Times.Once);
    }

    #endregion

    #region Doctor Leaves Tests

    [Fact]
    public async Task GetDoctorLeavesAsync_ReturnsLeavesOrderedDescendingByStartDate()
    {
        _doctorLeavesStore.AddRange(new[]
        {
            new DoctorLeave { Id = 1, DoctorId = 1, StartDate = new DateTime(2026, 11, 1), EndDate = new DateTime(2026, 11, 5) },
            new DoctorLeave { Id = 2, DoctorId = 1, StartDate = new DateTime(2026, 12, 10), EndDate = new DateTime(2026, 12, 15) },
            new DoctorLeave { Id = 3, DoctorId = 2, StartDate = new DateTime(2026, 12, 1), EndDate = new DateTime(2026, 12, 5) }
        });

        var result = await _sut.GetDoctorLeavesAsync(1);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value![0].Id.Should().Be(2); // December
        result.Value[1].Id.Should().Be(1); // November
    }

    [Fact]
    public async Task CheckLeaveConflictsAsync_ReturnsActiveAppointmentWarnings()
    {
        var startDate = new DateTime(2026, 11, 10);
        var endDate = new DateTime(2026, 11, 12);

        var conflictingAppointments = new List<Appointment>
        {
            new Appointment
            {
                Id = 101,
                AppointmentDate = new DateTime(2026, 11, 11),
                StartTime = new TimeSpan(10, 0, 0),
                Status = AppointmentStatus.Confirmed,
                Patient = new Patient { User = new ApplicationUser { FullName = "Ahmed Patient" } }
            }
        };

        _mockUow.Setup(u => u.Appointments.GetDoctorActiveAppointmentsInDateRangeAsync(1, startDate, endDate))
            .ReturnsAsync(conflictingAppointments);

        var result = await _sut.CheckLeaveConflictsAsync(1, startDate, endDate);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value![0].AppointmentId.Should().Be(101);
        result.Value[0].PatientName.Should().Be("Ahmed Patient");
        result.Value[0].Status.Should().Be("Confirmed");
    }

    [Fact]
    public async Task AddDoctorLeaveAsync_WhenValidationFails_ReturnsFailure()
    {
        var dto = new DoctorLeaveDto
        {
            StartDate = DateTime.Today.AddDays(10),
            EndDate = DateTime.Today.AddDays(5), // End before start
            Reason = "Vacation"
        };

        var result = await _sut.AddDoctorLeaveAsync(dto, doctorId: 1);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("on or after the start date");
    }

    [Fact]
    public async Task AddDoctorLeaveAsync_WhenValidWithoutConflicts_AddsLeaveSuccessfully()
    {
        var dto = new DoctorLeaveDto
        {
            StartDate = DateTime.Today.AddDays(5),
            EndDate = DateTime.Today.AddDays(10),
            Reason = " Annual Medical Conference "
        };

        _mockUow.Setup(u => u.Appointments.GetDoctorActiveAppointmentsInDateRangeAsync(
                1, dto.StartDate, dto.EndDate))
            .ReturnsAsync(new List<Appointment>());

        var result = await _sut.AddDoctorLeaveAsync(dto, doctorId: 1);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.AffectedAppointments.Should().BeEmpty();
        _doctorLeavesStore.Should().HaveCount(1);
        _doctorLeavesStore[0].Reason.Should().Be("Annual Medical Conference");
        _mockUow.Verify(u => u.CommitAsync(), Times.Once);
    }

    [Fact]
    public async Task AddDoctorLeaveAsync_WhenActiveAppointmentsExist_ReturnsFailure()
    {
        var dto = new DoctorLeaveDto
        {
            StartDate = DateTime.Today.AddDays(2),
            EndDate = DateTime.Today.AddDays(3),
            Reason = "Emergency Leave"
        };

        var conflictingAppointments = new List<Appointment>
        {
            new Appointment
            {
                Id = 202,
                AppointmentDate = DateTime.Today.AddDays(2),
                StartTime = new TimeSpan(11, 0, 0),
                Status = AppointmentStatus.Pending,
                Patient = new Patient { User = new ApplicationUser { FullName = "Hassan Ali" } }
            }
        };

        _mockUow.Setup(u => u.Appointments.GetDoctorActiveAppointmentsInDateRangeAsync(
                1, dto.StartDate, dto.EndDate))
            .ReturnsAsync(conflictingAppointments);

        var result = await _sut.AddDoctorLeaveAsync(dto, doctorId: 1);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Cannot schedule leave");
        result.Error.Should().Contain("1 active appointment(s)");
        _doctorLeavesStore.Should().BeEmpty();
        _mockUow.Verify(u => u.CommitAsync(), Times.Never);
    }

    [Fact]
    public async Task AddDoctorLeaveAsync_WhenOverlappingLeaveExists_ReturnsFailure()
    {
        _doctorLeavesStore.Add(new DoctorLeave
        {
            Id = 1,
            DoctorId = 1,
            StartDate = DateTime.Today.AddDays(5),
            EndDate = DateTime.Today.AddDays(10),
            Reason = "Vacation"
        });

        var dto = new DoctorLeaveDto
        {
            StartDate = DateTime.Today.AddDays(8),
            EndDate = DateTime.Today.AddDays(12),
            Reason = "Conference"
        };

        var result = await _sut.AddDoctorLeaveAsync(dto, doctorId: 1);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("already has a scheduled leave overlapping this date range");
        _doctorLeavesStore.Should().HaveCount(1);
        _mockUow.Verify(u => u.CommitAsync(), Times.Never);
    }

    [Fact]
    public async Task AddDoctorLeaveAsync_ExecutesInsideTransactionWithMultiKeyLocks()
    {
        var dto = new DoctorLeaveDto
        {
            StartDate = DateTime.Today.AddDays(5),
            EndDate = DateTime.Today.AddDays(7),
            Reason = "Medical Leave"
        };

        var mockTx = new Mock<IDatabaseTransaction>();
        _mockUow.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockTx.Object);

        List<string>? acquiredKeys = null;
        _mockUow.Setup(u => u.AcquireApplicationLocksAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<string>, TimeSpan, CancellationToken>((keys, _, _) => acquiredKeys = keys.ToList())
            .ReturnsAsync(Mock.Of<IAsyncDisposable>());

        _mockUow.Setup(u => u.Appointments.GetDoctorActiveAppointmentsInDateRangeAsync(1, dto.StartDate, dto.EndDate))
            .ReturnsAsync(new List<Appointment>());

        var result = await _sut.AddDoctorLeaveAsync(dto, doctorId: 1);

        result.IsSuccess.Should().BeTrue();
        _mockUow.Verify(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        acquiredKeys.Should().NotBeNull();
        acquiredKeys.Should().HaveCount(3); // Day 5, Day 6, Day 7
        acquiredKeys!.All(k => k.StartsWith("DoctorBooking_1_")).Should().BeTrue();
        mockTx.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteDoctorLeaveAsync_WhenNotFoundOrUnauthorized_ReturnsFailure()
    {
        var result = await _sut.DeleteDoctorLeaveAsync(999, doctorId: 1);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not found or unauthorized");
    }

    [Fact]
    public async Task DeleteDoctorLeaveAsync_WhenAuthorized_DeletesAndCommits()
    {
        var leave = new DoctorLeave { Id = 1, DoctorId = 1, StartDate = DateTime.Today.AddDays(1), EndDate = DateTime.Today.AddDays(3) };
        _doctorLeavesStore.Add(leave);

        var result = await _sut.DeleteDoctorLeaveAsync(1, doctorId: 1);

        result.IsSuccess.Should().BeTrue();
        _doctorLeavesStore.Should().BeEmpty();
        _mockUow.Verify(u => u.CommitAsync(), Times.Once);
    }

    #region BIZ-11 Working Hours Conflict Tests

    [Fact]
    public async Task DeleteWorkingHoursAsync_WhenFutureActiveAppointmentExists_ReturnsFailure()
    {
        // Doctor 1 has Monday 09:00 - 17:00 shift
        _workingHoursStore.Add(new WorkingHours
        {
            Id = 10,
            DoctorId = 1,
            DayOfWeek = DayOfWeek.Monday,
            StartTime = TimeSpan.FromHours(9),
            EndTime = TimeSpan.FromHours(17)
        });

        var nextMonday = DateTime.Today.AddDays(((int)DayOfWeek.Monday - (int)DateTime.Today.DayOfWeek + 7) % 7);
        if (nextMonday <= DateTime.Today) nextMonday = nextMonday.AddDays(7);

        _appointmentsStore.Add(new Appointment
        {
            Id = 501,
            DoctorId = 1,
            AppointmentDate = nextMonday,
            StartTime = TimeSpan.FromHours(10),
            EndTime = TimeSpan.FromHours(10.5),
            Status = AppointmentStatus.Confirmed
        });

        var result = await _sut.DeleteWorkingHoursAsync(10, doctorId: 1);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Cannot delete working hours");
        result.Error.Should().Contain("active appointment");
        _workingHoursStore.Should().Contain(w => w.Id == 10);
    }

    [Fact]
    public async Task UpdateWorkingHoursAsync_WhenAppointmentFallsOutsideNewHours_ReturnsFailure()
    {
        // Doctor 1 has Monday 09:00 - 17:00 shift
        _workingHoursStore.Add(new WorkingHours
        {
            Id = 11,
            DoctorId = 1,
            DayOfWeek = DayOfWeek.Monday,
            StartTime = TimeSpan.FromHours(9),
            EndTime = TimeSpan.FromHours(17)
        });

        var nextMonday = DateTime.Today.AddDays(((int)DayOfWeek.Monday - (int)DateTime.Today.DayOfWeek + 7) % 7);
        if (nextMonday <= DateTime.Today) nextMonday = nextMonday.AddDays(7);

        // Appointment is at 14:00 - 14:30
        _appointmentsStore.Add(new Appointment
        {
            Id = 502,
            DoctorId = 1,
            AppointmentDate = nextMonday,
            StartTime = TimeSpan.FromHours(14),
            EndTime = TimeSpan.FromHours(14.5),
            Status = AppointmentStatus.Confirmed
        });

        // Try to shrink shift to 09:00 - 12:00
        var dto = new WorkingHoursDto
        {
            Id = 11,
            DoctorId = 1,
            DayOfWeek = DayOfWeek.Monday,
            StartTime = TimeSpan.FromHours(9),
            EndTime = TimeSpan.FromHours(12)
        };

        var result = await _sut.UpdateWorkingHoursAsync(dto, doctorId: 1);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("fall outside the new shift time window");
    }

    [Fact]
    public async Task UpdateWorkingHoursAsync_WhenChangingDayOfWeekWithFutureAppointments_ReturnsFailure()
    {
        // Doctor 1 has Monday 09:00 - 17:00 shift
        _workingHoursStore.Add(new WorkingHours
        {
            Id = 12,
            DoctorId = 1,
            DayOfWeek = DayOfWeek.Monday,
            StartTime = TimeSpan.FromHours(9),
            EndTime = TimeSpan.FromHours(17)
        });

        var nextMonday = DateTime.Today.AddDays(((int)DayOfWeek.Monday - (int)DateTime.Today.DayOfWeek + 7) % 7);
        if (nextMonday <= DateTime.Today) nextMonday = nextMonday.AddDays(7);

        _appointmentsStore.Add(new Appointment
        {
            Id = 503,
            DoctorId = 1,
            AppointmentDate = nextMonday,
            StartTime = TimeSpan.FromHours(10),
            EndTime = TimeSpan.FromHours(10.5),
            Status = AppointmentStatus.Confirmed
        });

        // Try to move shift to Tuesday
        var dto = new WorkingHoursDto
        {
            Id = 12,
            DoctorId = 1,
            DayOfWeek = DayOfWeek.Tuesday,
            StartTime = TimeSpan.FromHours(9),
            EndTime = TimeSpan.FromHours(17)
        };

        var result = await _sut.UpdateWorkingHoursAsync(dto, doctorId: 1);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Cannot change day of week");
    }

    #endregion

    #endregion
}
