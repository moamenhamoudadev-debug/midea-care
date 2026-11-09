using System;
using System.ComponentModel.DataAnnotations;

namespace MediCare.Services.DTOs;

public class CreateRefundRequestDto
{
    [Required]
    public int PaymentAttemptId { get; set; }

    [Required]
    [Range(0.01, 1000000.00, ErrorMessage = "Refund amount must be greater than zero.")]
    public decimal AmountEgp { get; set; }

    [Required(ErrorMessage = "Refund reason is mandatory for financial auditability.")]
    [StringLength(500, MinimumLength = 5, ErrorMessage = "Refund reason must be between 5 and 500 characters.")]
    public string Reason { get; set; } = string.Empty;

    public bool IsFullRefund { get; set; }
}

public class ApproveRefundRequestDto
{
    [Required]
    public int RefundId { get; set; }

    [StringLength(500)]
    public string? ApproverNote { get; set; }
}

public class RejectRefundRequestDto
{
    [Required]
    public int RefundId { get; set; }

    [Required(ErrorMessage = "Rejection reason is mandatory.")]
    [StringLength(500, MinimumLength = 5, ErrorMessage = "Rejection reason must be between 5 and 500 characters.")]
    public string RejectionReason { get; set; } = string.Empty;
}

public class RefundResultDto
{
    public bool Success { get; set; }
    public int RefundId { get; set; }
    public string AuditReference { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal AmountEgp { get; set; }
    public long AmountCents { get; set; }
    public bool RequiresCheckerApproval { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? ProviderRefundId { get; set; }
    public decimal RemainingRefundableEgp { get; set; }
}

public class AdminRefundQueueItemDto
{
    public int RefundId { get; set; }
    public string AuditReference { get; set; } = string.Empty;
    public int PaymentAttemptId { get; set; }
    public string TransactionReference { get; set; } = string.Empty;
    public string ProviderTransactionId { get; set; } = string.Empty;
    public int AppointmentId { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public string DoctorName { get; set; } = string.Empty;
    public decimal AmountEgp { get; set; }
    public decimal OriginalAmountEgp { get; set; }
    public bool IsPartial { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string RequestedByAdminId { get; set; } = string.Empty;
    public string? ApprovedByAdminId { get; set; }
    public DateTime RequestedAtUtc { get; set; }
    public DateTime? ProcessedAtUtc { get; set; }
    public string? FailureReason { get; set; }
    public bool RequiresCheckerApproval { get; set; }
    public bool CanCurrentAdminApprove { get; set; }
}

public class PatientRefundDto
{
    public int RefundId { get; set; }
    public string AuditReference { get; set; } = string.Empty;
    public decimal AmountEgp { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? ProcessedAtUtc { get; set; }
    public string BankClearanceNotice { get; set; } = "Refund processed. Funds typically reflect in your account within 5 to 14 business days depending on your bank.";
}
