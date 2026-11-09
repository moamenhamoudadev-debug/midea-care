namespace MediCare.Services.DTOs;

public class WorkingHoursDto
{
    public int Id { get; set; }
    public int DoctorId { get; set; }
    public DayOfWeek DayOfWeek { get; set; }
    public string DayName => DayOfWeek.ToString();
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public string FormattedTime => $"{DateTime.Today.Add(StartTime):hh:mm tt} - {DateTime.Today.Add(EndTime):hh:mm tt}";
}

public class DoctorLeaveDto
{
    public int Id { get; set; }
    public int DoctorId { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string? Reason { get; set; }
}

public class LeaveConflictWarningDto
{
    public int AppointmentId { get; set; }
    public DateTime AppointmentDate { get; set; }
    public TimeSpan StartTime { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string FormattedSlot => $"{AppointmentDate:yyyy-MM-dd} at {DateTime.Today.Add(StartTime):hh:mm tt}";
}

public class DoctorLeaveCreateResultDto
{
    public int LeaveId { get; set; }
    public List<LeaveConflictWarningDto> AffectedAppointments { get; set; } = new();
    public bool HasConflicts => AffectedAppointments.Count > 0;
}
