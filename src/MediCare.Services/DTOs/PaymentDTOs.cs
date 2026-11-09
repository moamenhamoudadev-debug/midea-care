namespace MediCare.Services.DTOs;

using System.Text.Json.Serialization;

public class PaymentCheckoutRequestDto
{
    public int AppointmentId { get; set; }
    // Kept for source compatibility with legacy callers but excluded from API JSON binding. The hosted checkout never collects card details in MediCare.
    [JsonIgnore] public string CardNumber { get; set; } = string.Empty;
    [JsonIgnore] public string CardHolderName { get; set; } = string.Empty;
    [JsonIgnore] public string ExpiryMonth { get; set; } = string.Empty;
    [JsonIgnore] public string ExpiryYear { get; set; } = string.Empty;
    [JsonIgnore] public string Cvv { get; set; } = string.Empty;
    [JsonIgnore] public string PaymentMethod { get; set; } = "CreditCard";
    public string? PromoCode { get; set; }
    public bool AcceptTerms { get; set; }
    public string? TermsVersion { get; set; }
}

public class PaymentCheckoutSessionDto
{
    public string CheckoutUrl { get; set; } = string.Empty;
    public string MerchantReference { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
}

public class PaymentReceiptDto
{
    public int AppointmentId { get; set; }
    public string TransactionReference { get; set; } = string.Empty;
    public decimal AmountPaid { get; set; }
    public decimal OriginalAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public string? AppliedPromoCode { get; set; }
    public DateTime PaidAt { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public string DoctorName { get; set; } = string.Empty;
    public string Specialization { get; set; } = string.Empty;
    public string FormattedDate { get; set; } = string.Empty;
    public string FormattedTime { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = "Online Card Payment";
    public string? PaymentMethodType { get; set; }
    public string? PaymentMethodDetail { get; set; }
    public string? TermsVersion { get; set; }
    public string? ProviderOrderId { get; set; }
    public string? ReconciliationStatus { get; set; }
    public bool IsRefunded { get; set; } = false;
    public decimal RefundedAmount { get; set; }
    public List<PatientRefundDto> Refunds { get; set; } = new();
    public string Status { get; set; } = "Paid";
}

public class PromoCodeValidationDto
{
    public bool IsValid { get; set; }
    public string Code { get; set; } = string.Empty;
    public decimal DiscountAmount { get; set; }
    public decimal FinalAmount { get; set; }
    public string Description { get; set; } = string.Empty;
}

public class PaymentHistoryFilterDto
{
    public string? SearchTerm { get; set; }
    public string? Status { get; set; }
    public string? PaymentMethod { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

public class PaymentHistoryItemDto
{
    public int Id { get; set; }
    public int AppointmentId { get; set; }
    public string MerchantReference { get; set; } = string.Empty;
    public string? ProviderTransactionId { get; set; }
    public string? ProviderOrderId { get; set; }
    public decimal AmountPaid { get; set; }
    public decimal OriginalAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public string? PromoCode { get; set; }
    public string Currency { get; set; } = "EGP";
    public string Status { get; set; } = "Pending";
    public string DisplayStatus { get; set; } = "Pending";
    public bool IsRefunded { get; set; }
    public decimal RefundedAmount { get; set; }
    public decimal RemainingRefundableAmount { get; set; }
    public List<PatientRefundDto> Refunds { get; set; } = new();
    public bool CanRequestRefund { get; set; }
    public string? RefundStatus { get; set; }
    public string? ReconciliationStatus { get; set; }
    public string? ReconciliationReason { get; set; }
    public string? PaymentMethodType { get; set; }
    public string? PaymentMethodDetail { get; set; }
    public string DoctorName { get; set; } = string.Empty;
    public string Specialization { get; set; } = string.Empty;
    public DateTime AppointmentDate { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public bool CanViewReceipt { get; set; }
    public string? TermsVersion { get; set; }
}

public class PaymentHistorySummaryDto
{
    public decimal TotalPaid { get; set; }
    public int SucceededCount { get; set; }
    public int PendingCount { get; set; }
    public int RefundedCount { get; set; }
    public int RequiresRefundCount { get; set; }
}

public class PaymentHistoryViewModel
{
    public PaymentHistoryFilterDto Filter { get; set; } = new();
    public MediCare.Services.Common.PagedResult<PaymentHistoryItemDto> PagedHistory { get; set; } = new();
    public PaymentHistorySummaryDto Summary { get; set; } = new();
}


public class AdminFinancialDashboardDto
{
    public decimal GrossSuccessfulPayments { get; set; }
    public decimal TotalRefundedAmount { get; set; }
    public decimal NetRevenue => Math.Max(0m, GrossSuccessfulPayments - TotalRefundedAmount);
    public int SucceededCount { get; set; }
    public int FailedCount { get; set; }
    public int PendingCount { get; set; }
    public int RequiresRefundCount { get; set; }
    public int TotalAttemptCount => SucceededCount + FailedCount + PendingCount;
    public decimal SuccessRatePercent => TotalAttemptCount > 0 ? Math.Round((decimal)SucceededCount / TotalAttemptCount * 100m, 1) : 0m;
    public decimal FailureRatePercent => TotalAttemptCount > 0 ? Math.Round((decimal)FailedCount / TotalAttemptCount * 100m, 1) : 0m;

    public decimal CardGrossRevenue { get; set; }
    public int CardTransactionCount { get; set; }
    public decimal WalletGrossRevenue { get; set; }
    public int WalletTransactionCount { get; set; }

    public List<FinancialRevenueTimeSeriesDto> RevenueTimeSeries { get; set; } = new();
    public MediCare.Services.Common.PagedResult<PaymentHistoryItemDto> PagedTransactions { get; set; } = new();
    public PaymentHistoryFilterDto ActiveFilter { get; set; } = new();
    public List<ReconciliationQueueItemDto> ReconciliationQueue { get; set; } = new();
    public List<AdminRefundQueueItemDto> RefundQueue { get; set; } = new();
    public int PendingRefundReviewCount { get; set; }
    public decimal PendingRefundAmount { get; set; }
}

public class FinancialRevenueTimeSeriesDto
{
    public string PeriodLabel { get; set; } = string.Empty;
    public decimal GrossRevenue { get; set; }
    public decimal RefundedAmount { get; set; }
    public decimal NetRevenue => Math.Max(0m, GrossRevenue - RefundedAmount);
    public int TransactionCount { get; set; }
}

public class ReconciliationQueueItemDto
{
    public int Id { get; set; }
    public int AppointmentId { get; set; }
    public string MerchantReference { get; set; } = string.Empty;
    public string? ProviderTransactionId { get; set; }
    public string? ProviderOrderId { get; set; }
    public decimal AmountPaid { get; set; }
    public string Currency { get; set; } = "EGP";
    public string Status { get; set; } = string.Empty;
    public string ReconciliationStatus { get; set; } = string.Empty;
    public string? ReconciliationReason { get; set; }
    public string? PaymentMethodDetail { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public string DoctorName { get; set; } = string.Empty;
    public string AppointmentStatus { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ReconciledAt { get; set; }
    public bool IsPostCancellationPayment { get; set; }
}

public class AdminResolveReconciliationDto
{
    public int AttemptId { get; set; }
    public string Resolution { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
}