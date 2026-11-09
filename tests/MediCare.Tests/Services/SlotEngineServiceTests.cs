using FluentAssertions;
using MediCare.Data.Entities;
using MediCare.Data.Enums;
using MediCare.Data.UnitOfWork;
using MediCare.Services.Common;
using MediCare.Services.Implementations;
using Moq;
using Xunit;

namespace MediCare.Tests.Services;

public class SlotEngineServiceTests
{
    private readonly Mock<IUnitOfWork> _uowMock;
    private readonly Mock<IClinicClock> _clockMock;
    private readonly SlotEngineService _service;

    public SlotEngineServiceTests()
    {
        _uowMock = new Mock<IUnitOfWork>();
        _clockMock = new Mock<IClinicClock>();
        _service = new SlotEngineService(_uowMock.Object, _clockMock.Object);
    }

    [Fact]
    public void ComputeSlots_ShouldGenerateConsecutive30MinSlots_WhenNoBookingsOrLeaves()
    {
        // Arrange: Monday 09:00 - 11:00 (4 x 30min slots: 09:00, 09:30, 10:00, 10:30)
        var date = new DateTime(2026, 11, 16); // Monday
        var workingHours = new List<WorkingHours>
        {
            new() { DoctorId = 1, DayOfWeek = DayOfWeek.Monday, StartTime = new TimeSpan(9, 0, 0), EndTime = new TimeSpan(11, 0, 0) }
        };
        var currentClock = new DateTime(2026, 11, 15, 8, 0, 0); // Before appointment date

        // Act
        var slots = _service.ComputeSlots(
            date,
            30,
            workingHours,
            new List<DoctorLeave>(),
            new List<Appointment>(),
            currentClock);

        // Assert
        slots.Should().HaveCount(4);
        slots.Should().OnlyContain(s => s.IsAvailable);
        slots[0].StartTime.Should().Be(new TimeSpan(9, 0, 0));
        slots[0].EndTime.Should().Be(new TimeSpan(9, 30, 0));
        slots[3].StartTime.Should().Be(new TimeSpan(10, 30, 0));
        slots[3].EndTime.Should().Be(new TimeSpan(11, 0, 0));
    }

    [Fact]
    public void ComputeSlots_ShouldReturnEmpty_WhenDoctorIsOnLeave()
    {
        // Arrange
        var date = new DateTime(2026, 11, 16); // Monday
        var workingHours = new List<WorkingHours>
        {
            new() { DoctorId = 1, DayOfWeek = DayOfWeek.Monday, StartTime = new TimeSpan(9, 0, 0), EndTime = new TimeSpan(17, 0, 0) }
        };
        var leaves = new List<DoctorLeave>
        {
            new() { DoctorId = 1, StartDate = new DateTime(2026, 11, 15), EndDate = new DateTime(2026, 11, 17), Reason = "Annual leave" }
        };

        // Act
        var slots = _service.ComputeSlots(
            date,
            30,
            workingHours,
            leaves,
            new List<Appointment>(),
            DateTime.MinValue);

        // Assert
        slots.Should().BeEmpty();
    }

    [Fact]
    public void ComputeSlots_ShouldMarkPastSlotsAsUnavailable_WhenSlotTimeIsBeforeCurrentClock()
    {
        // Arrange: Today is Nov 16, current time is 10:15 AM
        var date = new DateTime(2026, 11, 16);
        var workingHours = new List<WorkingHours>
        {
            new() { DoctorId = 1, DayOfWeek = DayOfWeek.Monday, StartTime = new TimeSpan(9, 0, 0), EndTime = new TimeSpan(11, 0, 0) }
        };
        var currentClock = new DateTime(2026, 11, 16, 10, 15, 0);

        // Act
        var slots = _service.ComputeSlots(
            date,
            30,
            workingHours,
            new List<DoctorLeave>(),
            new List<Appointment>(),
            currentClock);

        // Assert: 09:00 (past), 09:30 (past), 10:00 (past: 10:00 <= 10:15), 10:30 (future: 10:30 > 10:15)
        slots.Should().HaveCount(4);
        slots[0].IsAvailable.Should().BeFalse(); // 09:00
        slots[1].IsAvailable.Should().BeFalse(); // 09:30
        slots[2].IsAvailable.Should().BeFalse(); // 10:00
        slots[3].IsAvailable.Should().BeTrue();  // 10:30
    }

    [Theory]
    [InlineData(AppointmentStatus.Pending)]
    [InlineData(AppointmentStatus.Confirmed)]
    public void ComputeSlots_ShouldMarkActiveAppointmentsAsUnavailable(AppointmentStatus status)
    {
        // Arrange: Appointment exists at 09:30 - 10:00
        var date = new DateTime(2026, 11, 16);
        var workingHours = new List<WorkingHours>
        {
            new() { DoctorId = 1, DayOfWeek = DayOfWeek.Monday, StartTime = new TimeSpan(9, 0, 0), EndTime = new TimeSpan(11, 0, 0) }
        };
        var bookings = new List<Appointment>
        {
            new()
            {
                DoctorId = 1,
                AppointmentDate = date,
                StartTime = new TimeSpan(9, 30, 0),
                EndTime = new TimeSpan(10, 0, 0),
                Status = status
            }
        };

        // Act
        var slots = _service.ComputeSlots(
            date,
            30,
            workingHours,
            new List<DoctorLeave>(),
            bookings,
            new DateTime(2026, 11, 15));

        // Assert
        slots.Should().HaveCount(4);
        slots.First(s => s.StartTime == new TimeSpan(9, 0, 0)).IsAvailable.Should().BeTrue();
        slots.First(s => s.StartTime == new TimeSpan(9, 30, 0)).IsAvailable.Should().BeFalse();
        slots.First(s => s.StartTime == new TimeSpan(10, 0, 0)).IsAvailable.Should().BeTrue();
        slots.First(s => s.StartTime == new TimeSpan(10, 30, 0)).IsAvailable.Should().BeTrue();
    }

    [Theory]
    [InlineData(AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.Rejected)]
    public void ComputeSlots_ShouldKeepSlotAvailable_WhenExistingAppointmentIsCancelledOrRejected(AppointmentStatus status)
    {
        // Arrange: Appointment at 09:30 is Cancelled or Rejected
        var date = new DateTime(2026, 11, 16);
        var workingHours = new List<WorkingHours>
        {
            new() { DoctorId = 1, DayOfWeek = DayOfWeek.Monday, StartTime = new TimeSpan(9, 0, 0), EndTime = new TimeSpan(11, 0, 0) }
        };
        var bookings = new List<Appointment>
        {
            new()
            {
                DoctorId = 1,
                AppointmentDate = date,
                StartTime = new TimeSpan(9, 30, 0),
                EndTime = new TimeSpan(10, 0, 0),
                Status = status
            }
        };

        // Act
        var slots = _service.ComputeSlots(
            date,
            30,
            workingHours,
            new List<DoctorLeave>(),
            bookings,
            new DateTime(2026, 11, 15));

        // Assert: slot at 09:30 must be available for rebooking
        slots.First(s => s.StartTime == new TimeSpan(9, 30, 0)).IsAvailable.Should().BeTrue();
    }

    [Fact]
    public void ComputeSlots_ShouldRespectCustomSlotDuration_WhenDoctorHasNonDefaultDuration()
    {
        // Arrange: 15-minute slot duration across 1 hour (09:00 - 10:00 -> 4 slots)
        var date = new DateTime(2026, 11, 16);
        var workingHours = new List<WorkingHours>
        {
            new() { DoctorId = 1, DayOfWeek = DayOfWeek.Monday, StartTime = new TimeSpan(9, 0, 0), EndTime = new TimeSpan(10, 0, 0) }
        };

        // Act
        var slots = _service.ComputeSlots(
            date,
            15,
            workingHours,
            new List<DoctorLeave>(),
            new List<Appointment>(),
            new DateTime(2026, 11, 15));

        // Assert
        slots.Should().HaveCount(4);
        slots[0].StartTime.Should().Be(new TimeSpan(9, 0, 0));
        slots[0].EndTime.Should().Be(new TimeSpan(9, 15, 0));
        slots[1].StartTime.Should().Be(new TimeSpan(9, 15, 0));
        slots[1].EndTime.Should().Be(new TimeSpan(9, 30, 0));
    }

    [Fact]
    public void ComputeSlots_ShouldHandleMultipleShiftsOnSameDay()
    {
        // Arrange: Morning shift (09:00 - 10:00) and Evening shift (16:00 - 17:00)
        var date = new DateTime(2026, 11, 16);
        var workingHours = new List<WorkingHours>
        {
            new() { DoctorId = 1, DayOfWeek = DayOfWeek.Monday, StartTime = new TimeSpan(9, 0, 0), EndTime = new TimeSpan(10, 0, 0) },
            new() { DoctorId = 1, DayOfWeek = DayOfWeek.Monday, StartTime = new TimeSpan(16, 0, 0), EndTime = new TimeSpan(17, 0, 0) }
        };

        // Act
        var slots = _service.ComputeSlots(
            date,
            30,
            workingHours,
            new List<DoctorLeave>(),
            new List<Appointment>(),
            new DateTime(2026, 11, 15));

        // Assert: 2 in morning + 2 in evening = 4 total slots
        slots.Should().HaveCount(4);
        slots[0].StartTime.Should().Be(new TimeSpan(9, 0, 0));
        slots[1].StartTime.Should().Be(new TimeSpan(9, 30, 0));
        slots[2].StartTime.Should().Be(new TimeSpan(16, 0, 0));
        slots[3].StartTime.Should().Be(new TimeSpan(16, 30, 0));
    }

    [Fact]
    public void ComputeSlots_ShouldNotGenerateSlot_WhenShiftRemainderIsShorterThanSlotDuration()
    {
        // Arrange: Shift 09:00 - 09:45 with 30-min duration should produce only one slot (09:00-09:30), not partial slot
        var date = new DateTime(2026, 11, 16);
        var workingHours = new List<WorkingHours>
        {
            new() { DoctorId = 1, DayOfWeek = DayOfWeek.Monday, StartTime = new TimeSpan(9, 0, 0), EndTime = new TimeSpan(9, 45, 0) }
        };

        // Act
        var slots = _service.ComputeSlots(
            date,
            30,
            workingHours,
            new List<DoctorLeave>(),
            new List<Appointment>(),
            new DateTime(2026, 11, 15));

        // Assert
        slots.Should().HaveCount(1);
        slots[0].StartTime.Should().Be(new TimeSpan(9, 0, 0));
        slots[0].EndTime.Should().Be(new TimeSpan(9, 30, 0));
    }

    [Fact]
    public void ComputeSlots_ShouldReturnEmpty_WhenNoWorkingHoursConfiguredForDay()
    {
        // Arrange: Working hours only on Tuesday, querying Monday
        var date = new DateTime(2026, 11, 16); // Monday
        var workingHours = new List<WorkingHours>
        {
            new() { DoctorId = 1, DayOfWeek = DayOfWeek.Tuesday, StartTime = new TimeSpan(9, 0, 0), EndTime = new TimeSpan(17, 0, 0) }
        };

        // Act
        var slots = _service.ComputeSlots(
            date,
            30,
            workingHours,
            new List<DoctorLeave>(),
            new List<Appointment>(),
            new DateTime(2026, 11, 15));

        // Assert
        slots.Should().BeEmpty();
    }
}
