namespace MediCare.Data.Entities;

public class PaymentAttempt : BaseAuditableEntity
{
    public int AppointmentId { get; set; }
    public int PatientId { get; set; }
    public string MerchantReference { get; set; } = string.Empty;
    public string? ProviderOrderId { get; set; }
    public string? ProviderTransactionId { get; set; }
    public string? CheckoutUrl { get; set; }
    public long AmountCents { get; set; }
    public decimal DiscountAmount { get; set; }
    public string? PromoCode { get; set; }
    public string Currency { get; set; } = "EGP";
    public string Status { get; set; } = "Pending";
    public string? TermsVersion { get; set; }
    public DateTime? TermsAcceptedAt { get; set; }
    public string? ReconciliationStatus { get; set; }
    public string? ReconciliationReason { get; set; }
    public DateTime? ReconciledAt { get; set; }
    public string? PaymentMethodType { get; set; }
    public string? PaymentMethodDetail { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public virtual Appointment Appointment { get; set; } = null!;
    public virtual ICollection<Refund> Refunds { get; set; } = new List<Refund>();
}