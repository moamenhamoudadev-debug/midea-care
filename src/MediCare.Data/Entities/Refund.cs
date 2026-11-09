namespace MediCare.Data.Entities;

public class Refund : BaseAuditableEntity
{
    public int PaymentAttemptId { get; set; }
    public string ProviderTransactionId { get; set; } = string.Empty;
    public string? ProviderRefundId { get; set; }
    public long AmountCents { get; set; }
    public string Currency { get; set; } = "EGP";
    public string Reason { get; set; } = string.Empty;
    public string Status { get; set; } = "Requested"; // Requested, UnderReview, Approved, Processing, Succeeded, Failed, RequiresReconciliation, Rejected
    public string RequestedByAdminId { get; set; } = string.Empty;
    public string? ApprovedByAdminId { get; set; }
    public DateTime RequestedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAtUtc { get; set; }
    public string? FailureReason { get; set; }
    public string AuditReference { get; set; } = string.Empty;
    public bool IsPartialRefund { get; set; }

    public virtual PaymentAttempt PaymentAttempt { get; set; } = null!;
}
