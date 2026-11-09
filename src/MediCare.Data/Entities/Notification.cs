namespace MediCare.Data.Entities;

public class Notification : BaseAuditableEntity
{
    public string UserId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public bool IsRead { get; set; } = false;

    public string Type { get; set; } = "General";
    public int? PaymentAttemptId { get; set; }
    public int? RefundId { get; set; }
    public int? AppointmentId { get; set; }
    public DateTime? ReadAtUtc { get; set; }
    public string? EventReference { get; set; }
    public string? ActionUrl { get; set; }

    public virtual ApplicationUser User { get; set; } = null!;
    public virtual PaymentAttempt? PaymentAttempt { get; set; }
    public virtual Refund? Refund { get; set; }
    public virtual Appointment? Appointment { get; set; }
}
