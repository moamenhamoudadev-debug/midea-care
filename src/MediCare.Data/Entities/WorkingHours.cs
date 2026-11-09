namespace MediCare.Data.Entities;

public class WorkingHours : BaseAuditableEntity
{
    public int DoctorId { get; set; }
    public DayOfWeek DayOfWeek { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }

    public virtual Doctor Doctor { get; set; } = null!;
}
