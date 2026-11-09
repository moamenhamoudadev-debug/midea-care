namespace MediCare.Data.Entities;

public class DoctorLeave : BaseAuditableEntity
{
    public int DoctorId { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string? Reason { get; set; }

    public virtual Doctor Doctor { get; set; } = null!;
}
