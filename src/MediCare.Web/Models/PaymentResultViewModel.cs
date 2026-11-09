namespace MediCare.Web.Models;

public class PaymentResultViewModel
{
    public string State { get; set; } = "Processing"; // "Success", "Processing", "Failed", "Expired", "NotFound"
    public string MerchantReference { get; set; } = string.Empty;
    public string? ProviderTransactionId { get; set; }
    public string? ProviderOrderId { get; set; }
    public int AppointmentId { get; set; }
    public decimal AmountPaid { get; set; }
    public decimal OriginalAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public string? PromoCode { get; set; }
    public string Currency { get; set; } = "EGP";
    public string? PaymentMethodType { get; set; }
    public string? PaymentMethodDetail { get; set; }
    public string? DoctorName { get; set; }
    public string? Specialization { get; set; }
    public string? ClinicLocation { get; set; }
    public DateTime? AppointmentDate { get; set; }
    public TimeSpan? StartTime { get; set; }
    public TimeSpan? EndTime { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public string? FailureReason { get; set; }
    public string? TermsVersion { get; set; }
    public bool IsPostCancellationPayment { get; set; }
}
