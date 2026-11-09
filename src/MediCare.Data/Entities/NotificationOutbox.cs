namespace MediCare.Data.Entities;

public class NotificationOutbox : BaseAuditableEntity
{
    public int NotificationId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Channel { get; set; } = "SignalR"; // "SignalR", "Email"
    public string? RecipientEmail { get; set; }
    public string? Subject { get; set; }
    public string? PayloadJson { get; set; }
    public string Status { get; set; } = "Pending"; // "Pending", "Dispatched", "Failed"
    public int RetryCount { get; set; } = 0;
    public int MaxRetries { get; set; } = 3;
    public string? LastError { get; set; }
    public DateTime? ProcessedAtUtc { get; set; }

    public virtual Notification Notification { get; set; } = null!;
}
