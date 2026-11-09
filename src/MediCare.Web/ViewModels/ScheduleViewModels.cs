using System.ComponentModel.DataAnnotations;
using MediCare.Services.DTOs;

namespace MediCare.Web.ViewModels;

public class DoctorScheduleViewModel
{
    public int DoctorId { get; set; }
    public string DoctorName { get; set; } = string.Empty;
    public List<WorkingHoursDto> WorkingHours { get; set; } = new();
    public WorkingHoursInputModel NewWorkingHours { get; set; } = new();
}

public class WorkingHoursInputModel
{
    public int? Id { get; set; }

    [Required(ErrorMessage = "Day of week is required.")]
    public DayOfWeek DayOfWeek { get; set; } = DayOfWeek.Monday;

    [Required(ErrorMessage = "Start time is required.")]
    public string StartTime { get; set; } = "09:00";

    [Required(ErrorMessage = "End time is required.")]
    public string EndTime { get; set; } = "17:00";
}

public class DoctorLeavesViewModel
{
    public int DoctorId { get; set; }
    public string DoctorName { get; set; } = string.Empty;
    public List<DoctorLeaveDto> Leaves { get; set; } = new();
    public DoctorLeaveInputModel NewLeave { get; set; } = new();
    public List<LeaveConflictWarningDto> ConflictWarnings { get; set; } = new();
}

public class DoctorLeaveInputModel
{
    [Required(ErrorMessage = "Start date is required.")]
    [DataType(DataType.Date)]
    public DateTime StartDate { get; set; } = DateTime.Today;

    [Required(ErrorMessage = "End date is required.")]
    [DataType(DataType.Date)]
    public DateTime EndDate { get; set; } = DateTime.Today;

    [MaxLength(250, ErrorMessage = "Reason cannot exceed 250 characters.")]
    public string? Reason { get; set; }
}

public class RescheduleAppointmentViewModel
{
    public int AppointmentId { get; set; }
    public int DoctorId { get; set; }
    public string DoctorName { get; set; } = string.Empty;
    public string SpecializationName { get; set; } = string.Empty;
    public DateTime CurrentDate { get; set; }
    public TimeSpan CurrentStartTime { get; set; }
    public string FormattedCurrentTime => $"{DateTime.Today.Add(CurrentStartTime):hh:mm tt}";

    [Required(ErrorMessage = "Please select a new appointment date.")]
    [DataType(DataType.Date)]
    public DateTime NewAppointmentDate { get; set; } = DateTime.Today.AddDays(1);

    [Required(ErrorMessage = "Please select a new appointment time.")]
    public string NewStartTime { get; set; } = "10:00";

    [MaxLength(250, ErrorMessage = "Reschedule reason cannot exceed 250 characters.")]
    public string? Reason { get; set; }

    public DoctorDetailDto? DoctorDetails { get; set; }
}
