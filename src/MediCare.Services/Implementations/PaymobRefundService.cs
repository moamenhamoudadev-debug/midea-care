using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediCare.Data.Context;
using MediCare.Data.Entities;
using MediCare.Data.Enums;
using MediCare.Data.UnitOfWork;
using MediCare.Services.Common;
using MediCare.Services.Contracts;
using MediCare.Services.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MediCare.Services.Implementations;

public sealed class PaymobRefundService : IPaymobRefundService
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> _attemptLocks = new();
    private readonly ApplicationDbContext _db;
    private readonly HttpClient _http;
    private readonly PaymobSettings _settings;
    private readonly INotificationService _notificationService;
    private readonly ILogger<PaymobRefundService> _logger;

    public PaymobRefundService(
        ApplicationDbContext db,
        HttpClient http,
        IOptions<PaymobSettings> settings,
        INotificationService notificationService,
        ILogger<PaymobRefundService> logger)
    {
        _db = db;
        _http = http;
        _settings = settings.Value;
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task<RefundResultDto> RequestRefundAsync(CreateRefundRequestDto request, string adminUserId, CancellationToken cancellationToken = default)
    {
        if (request == null)
            return new RefundResultDto { Success = false, Message = "Invalid refund request." };

        if (request.PaymentAttemptId <= 0)
            return new RefundResultDto { Success = false, Message = "Valid payment attempt ID is required." };

        if (string.IsNullOrWhiteSpace(adminUserId))
            return new RefundResultDto { Success = false, Message = "Admin user identity is required for audit trails." };

        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length < 5)
            return new RefundResultDto { Success = false, Message = "Refund reason is mandatory (minimum 5 characters)." };

        var semaphore = _attemptLocks.GetOrAdd(request.PaymentAttemptId, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);

        try
        {
            await using var appLock = await ApplicationLockManager.AcquireLockAsync(
                _db,
                $"Refund_PaymentAttempt_{request.PaymentAttemptId}",
                TimeSpan.FromSeconds(15),
                cancellationToken);

            var attempt = await _db.PaymentAttempts
                .Include(a => a.Appointment)
                    .ThenInclude(ap => ap.Patient)
                        .ThenInclude(p => p.User)
                .Include(a => a.Refunds)
                .FirstOrDefaultAsync(a => a.Id == request.PaymentAttemptId, cancellationToken);

            if (attempt == null)
                return new RefundResultDto { Success = false, Message = "Payment transaction record not found." };

            if (attempt.Status != "Succeeded" && attempt.Status != "PartiallyRefunded")
                return new RefundResultDto { Success = false, Message = $"Cannot refund transaction with status '{attempt.Status}'. Only successful transactions can be refunded." };

            var committedCents = attempt.Refunds
                .Where(r => r.Status != "Rejected" && r.Status != "Failed")
                .Sum(r => r.AmountCents);

            var remainingCents = attempt.AmountCents - committedCents;
            if (remainingCents <= 0)
                return new RefundResultDto { Success = false, Message = "No refundable balance remaining on this transaction. All charges have already been refunded or are awaiting processing." };

            long requestedCents;
            if (request.IsFullRefund)
            {
                requestedCents = remainingCents;
            }
            else
            {
                if (request.AmountEgp <= 0m)
                    return new RefundResultDto { Success = false, Message = "Refund amount must be greater than zero." };

                requestedCents = checked((long)Math.Round(request.AmountEgp * 100m, MidpointRounding.AwayFromZero));
            }

            if (requestedCents <= 0)
                return new RefundResultDto { Success = false, Message = "Refund amount must be greater than 0." };

            if (requestedCents > remainingCents)
            {
                var remainingEgp = (decimal)remainingCents / 100m;
                var reqEgp = (decimal)requestedCents / 100m;
                return new RefundResultDto
                {
                    Success = false,
                    RemainingRefundableEgp = remainingEgp,
                    Message = $"Requested refund ({reqEgp:N2} EGP) exceeds remaining refundable balance ({remainingEgp:N2} EGP)."
                };
            }

            var threshold = _settings.MakerCheckerThresholdEgp;
            var amountEgp = (decimal)requestedCents / 100m;
            var requiresCheckerApproval = amountEgp >= threshold;
            var isPartial = requestedCents < remainingCents;

            var auditRef = $"REF-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}"[..20].ToUpperInvariant();
            var refund = new Refund
            {
                PaymentAttemptId = attempt.Id,
                ProviderTransactionId = attempt.ProviderTransactionId ?? attempt.MerchantReference,
                AmountCents = requestedCents,
                Currency = attempt.Currency ?? "EGP",
                Reason = request.Reason.Trim(),
                RequestedByAdminId = adminUserId,
                RequestedAtUtc = DateTime.UtcNow,
                AuditReference = auditRef,
                IsPartialRefund = isPartial,
                Status = requiresCheckerApproval ? "UnderReview" : "Approved"
            };

            _db.Refunds.Add(refund);
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Refund requested: ID {RefundId}, Audit {AuditRef}, Amount {AmountCents} cents, Requester {AdminId}, RequiresChecker {RequiresChecker}",
                refund.Id, refund.AuditReference, refund.AmountCents, adminUserId, requiresCheckerApproval);

            if (requiresCheckerApproval)
            {
                // Patient Notification: Refund Under Review
                var patientUserId = attempt.Appointment?.Patient?.UserId;
                if (!string.IsNullOrWhiteSpace(patientUserId))
                {
                    try
                    {
                        await _notificationService.NotifyRefundEventAsync(
                            patientUserId,
                            "RefundUnderReview",
                            "Refund Under Review",
                            $"Your refund request of {amountEgp:N2} EGP (Ref: {refund.AuditReference}) has been received and is currently under administrative review.",
                            refund.Id,
                            attempt.Id,
                            refund.AuditReference,
                            "/Payment/History",
                            cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to send under review notification for refund {AuditRef}", refund.AuditReference);
                    }
                }

                // Administrative Alert: Maker-Checker Approval Required
                try
                {
                    await _notificationService.NotifyAdminFinancialAlertAsync(
                        "HighValueRefundReview",
                        "Maker-Checker Dual Authorization Required",
                        $"High-value refund request of {amountEgp:N2} EGP (Audit: {refund.AuditReference}) by {adminUserId} requires secondary administrator approval.",
                        attempt.Id,
                        refund.Id,
                        $"AdminRefundApproval_{refund.Id}",
                        "/Admin/Payments",
                        cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to send admin alert for refund {AuditRef}", refund.AuditReference);
                }

                return new RefundResultDto
                {
                    Success = true,
                    RefundId = refund.Id,
                    AuditReference = refund.AuditReference,
                    Status = refund.Status,
                    AmountEgp = amountEgp,
                    AmountCents = refund.AmountCents,
                    RequiresCheckerApproval = true,
                    RemainingRefundableEgp = (decimal)(remainingCents - requestedCents) / 100m,
                    Message = $"Refund request {refund.AuditReference} of {amountEgp:N2} EGP submitted for Maker-Checker review pursuant to segregation of duties policy."
                };
            }

            // Auto-approved (under threshold): execute immediately with Maker acting as authorized single approver
            refund.ApprovedByAdminId = adminUserId;
            return await ExecuteRefundCoreAsync(refund, attempt, adminUserId, cancellationToken);
        }
        finally
        {
            semaphore.Release();
        }
    }

    public async Task<RefundResultDto> ApproveAndExecuteRefundAsync(int refundId, string approvingAdminId, string? note = null, CancellationToken cancellationToken = default)
    {
        if (refundId <= 0)
            return new RefundResultDto { Success = false, Message = "Valid refund ID is required." };

        if (string.IsNullOrWhiteSpace(approvingAdminId))
            return new RefundResultDto { Success = false, Message = "Approver admin identity is required." };

        var refund = await _db.Refunds
            .Include(r => r.PaymentAttempt)
                .ThenInclude(a => a.Appointment)
                    .ThenInclude(ap => ap.Patient)
                        .ThenInclude(p => p.User)
            .Include(r => r.PaymentAttempt)
                .ThenInclude(a => a.Refunds)
            .FirstOrDefaultAsync(r => r.Id == refundId, cancellationToken);

        if (refund == null)
            return new RefundResultDto { Success = false, Message = "Refund request record not found." };

        if (refund.Status != "UnderReview" && refund.Status != "Requested")
            return new RefundResultDto { Success = false, Message = $"Cannot approve refund with status '{refund.Status}'." };

        // Segregation of Duties (Maker-Checker enforcement)
        if (string.Equals(refund.RequestedByAdminId, approvingAdminId, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Segregation of duties violation: Admin {AdminId} attempted to self-approve refund {RefundId}", approvingAdminId, refundId);
            return new RefundResultDto
            {
                Success = false,
                RefundId = refund.Id,
                AuditReference = refund.AuditReference,
                Status = refund.Status,
                Message = "Segregation of duties violation: An administrator cannot approve their own refund request. A secondary checker must review and approve."
            };
        }

        var semaphore = _attemptLocks.GetOrAdd(refund.PaymentAttemptId, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);

        try
        {
            await using var appLock = await ApplicationLockManager.AcquireLockAsync(
                _db,
                $"Refund_PaymentAttempt_{refund.PaymentAttemptId}",
                TimeSpan.FromSeconds(15),
                cancellationToken);

            refund.ApprovedByAdminId = approvingAdminId;
            refund.Status = "Approved";
            if (!string.IsNullOrWhiteSpace(note))
            {
                refund.Reason = $"{refund.Reason} | Checker Note: {note.Trim()}";
            }
            await _db.SaveChangesAsync(cancellationToken);

            return await ExecuteRefundCoreAsync(refund, refund.PaymentAttempt, approvingAdminId, cancellationToken);
        }
        finally
        {
            semaphore.Release();
        }
    }

    public async Task<RefundResultDto> RejectRefundAsync(int refundId, string rejectingAdminId, string reason, CancellationToken cancellationToken = default)
    {
        if (refundId <= 0)
            return new RefundResultDto { Success = false, Message = "Valid refund ID is required." };

        if (string.IsNullOrWhiteSpace(rejectingAdminId))
            return new RefundResultDto { Success = false, Message = "Admin user identity is required." };

        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 5)
            return new RefundResultDto { Success = false, Message = "Rejection reason is mandatory (minimum 5 characters)." };

        var refund = await _db.Refunds
            .Include(r => r.PaymentAttempt)
                .ThenInclude(a => a.Appointment)
                    .ThenInclude(ap => ap.Patient)
            .FirstOrDefaultAsync(r => r.Id == refundId, cancellationToken);

        if (refund == null)
            return new RefundResultDto { Success = false, Message = "Refund record not found." };

        if (refund.Status != "UnderReview" && refund.Status != "Requested")
            return new RefundResultDto { Success = false, Message = $"Cannot reject refund with status '{refund.Status}'." };

        var semaphore = _attemptLocks.GetOrAdd(refund.PaymentAttemptId, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);

        try
        {
            await using var appLock = await ApplicationLockManager.AcquireLockAsync(
                _db,
                $"Refund_PaymentAttempt_{refund.PaymentAttemptId}",
                TimeSpan.FromSeconds(15),
                cancellationToken);

            refund.Status = "Rejected";
            refund.FailureReason = $"Rejected by admin {rejectingAdminId}: {reason.Trim()}";
            refund.ProcessedAtUtc = DateTime.UtcNow;

            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Refund {RefundId} ({AuditRef}) rejected by admin {AdminId}. Reason: {Reason}",
                refund.Id, refund.AuditReference, rejectingAdminId, reason);

            // Notify patient of rejection
            var patientUserId = refund.PaymentAttempt?.Appointment?.Patient?.UserId;
            if (!string.IsNullOrWhiteSpace(patientUserId))
            {
                try
                {
                    var amountEgp = (decimal)refund.AmountCents / 100m;
                    await _notificationService.NotifyRefundEventAsync(
                        patientUserId,
                        "RefundRejected",
                        "Refund Request Declined",
                        $"Your refund request of {amountEgp:N2} EGP (Ref: {refund.AuditReference}) was reviewed and declined. Reason: {reason.Trim()}",
                        refund.Id,
                        refund.PaymentAttemptId,
                        refund.AuditReference,
                        "/Payment/History",
                        cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to send rejection notification for refund {AuditRef}", refund.AuditReference);
                }
            }

            return new RefundResultDto
            {
                Success = true,
                RefundId = refund.Id,
                AuditReference = refund.AuditReference,
                Status = refund.Status,
                AmountEgp = (decimal)refund.AmountCents / 100m,
                AmountCents = refund.AmountCents,
                Message = $"Refund request {refund.AuditReference} has been rejected."
            };
        }
        finally
        {
            semaphore.Release();
        }
    }

    public async Task<List<AdminRefundQueueItemDto>> GetRefundQueueAsync(string? statusFilter = null, string? currentAdminId = null, CancellationToken cancellationToken = default)
    {
        var query = _db.Refunds
            .Include(r => r.PaymentAttempt)
                .ThenInclude(p => p.Appointment)
                    .ThenInclude(a => a.Patient)
                        .ThenInclude(pt => pt.User)
            .Include(r => r.PaymentAttempt)
                .ThenInclude(p => p.Appointment)
                    .ThenInclude(a => a.Doctor)
                        .ThenInclude(d => d.User)
            .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(statusFilter) && !string.Equals(statusFilter, "All", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(r => r.Status == statusFilter);
        }

        var threshold = _settings.MakerCheckerThresholdEgp;

        var items = await query
            .OrderByDescending(r => r.RequestedAtUtc)
            .Select(r => new AdminRefundQueueItemDto
            {
                RefundId = r.Id,
                AuditReference = r.AuditReference,
                PaymentAttemptId = r.PaymentAttemptId,
                TransactionReference = r.PaymentAttempt.MerchantReference,
                ProviderTransactionId = r.ProviderTransactionId,
                AppointmentId = r.PaymentAttempt.AppointmentId,
                PatientName = r.PaymentAttempt.Appointment.Patient.User.FullName ?? "Patient",
                DoctorName = "Dr. " + (r.PaymentAttempt.Appointment.Doctor.User.FullName ?? "Physician"),
                AmountEgp = (decimal)r.AmountCents / 100m,
                OriginalAmountEgp = (decimal)r.PaymentAttempt.AmountCents / 100m,
                IsPartial = r.IsPartialRefund,
                Reason = r.Reason,
                Status = r.Status,
                RequestedByAdminId = r.RequestedByAdminId,
                ApprovedByAdminId = r.ApprovedByAdminId,
                RequestedAtUtc = r.RequestedAtUtc,
                ProcessedAtUtc = r.ProcessedAtUtc,
                FailureReason = r.FailureReason,
                RequiresCheckerApproval = ((decimal)r.AmountCents / 100m) >= threshold,
                CanCurrentAdminApprove = currentAdminId != null &&
                                         !string.Equals(r.RequestedByAdminId, currentAdminId, StringComparison.OrdinalIgnoreCase) &&
                                         (r.Status == "UnderReview" || r.Status == "Requested")
            })
            .ToListAsync(cancellationToken);

        return items;
    }

    public async Task<decimal> GetRemainingRefundableAmountAsync(int paymentAttemptId, CancellationToken cancellationToken = default)
    {
        var attempt = await _db.PaymentAttempts
            .Include(a => a.Refunds)
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == paymentAttemptId, cancellationToken);

        if (attempt == null || (attempt.Status != "Succeeded" && attempt.Status != "PartiallyRefunded"))
            return 0m;

        var committedCents = attempt.Refunds
            .Where(r => r.Status != "Rejected" && r.Status != "Failed")
            .Sum(r => r.AmountCents);

        var remainingCents = Math.Max(0L, attempt.AmountCents - committedCents);
        return (decimal)remainingCents / 100m;
    }

    private async Task<RefundResultDto> ExecuteRefundCoreAsync(
        Refund refund,
        PaymentAttempt attempt,
        string actingAdminId,
        CancellationToken cancellationToken)
    {
        refund.Status = "Processing";
        refund.ProcessedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        var amountEgp = (decimal)refund.AmountCents / 100m;

        try
        {
            var providerTxnId = attempt.ProviderTransactionId ?? attempt.MerchantReference;
            object payload = long.TryParse(providerTxnId, NumberStyles.None, CultureInfo.InvariantCulture, out var txnNum) && txnNum > 0
                ? new { auth_token = _settings.SecretKey, transaction_id = txnNum, amount_cents = refund.AmountCents }
                : new { auth_token = _settings.SecretKey, transaction_id = providerTxnId, amount_cents = refund.AmountCents };

            using var request = new HttpRequestMessage(HttpMethod.Post, "https://accept.paymob.com/api/acceptance/void_refund/refund");
            if (!string.IsNullOrWhiteSpace(_settings.SecretKey))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Token", _settings.SecretKey);
            }
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var response = await _http.SendAsync(request, cancellationToken);
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                string? providerRefundId = null;
                bool isSuccess = true;

                try
                {
                    using var doc = JsonDocument.Parse(responseText);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("id", out var idProp))
                    {
                        providerRefundId = idProp.ToString();
                    }
                    if (root.TryGetProperty("success", out var successProp) && successProp.ValueKind == JsonValueKind.False)
                    {
                        isSuccess = false;
                    }
                }
                catch (JsonException)
                {
                    // Fallback to text parsing if needed
                }

                if (isSuccess)
                {
                    refund.Status = "Succeeded";
                    refund.ProviderRefundId = providerRefundId ?? $"PMR-{Guid.NewGuid():N}"[..12].ToUpperInvariant();
                    refund.ProcessedAtUtc = DateTime.UtcNow;
                    refund.FailureReason = null;

                    // Update attempt & appointment financial states
                    var totalSucceededCents = attempt.Refunds
                        .Where(r => r.Status == "Succeeded" || r.Id == refund.Id)
                        .Sum(r => r.AmountCents);

                    if (totalSucceededCents >= attempt.AmountCents)
                    {
                        attempt.Status = "Refunded";
                        attempt.ReconciliationStatus = "Refunded";
                        attempt.Appointment.PaymentStatus = PaymentStatus.Refunded;
                    }
                    else
                    {
                        attempt.Status = "PartiallyRefunded";
                        attempt.ReconciliationStatus = "PartiallyRefunded";
                    }

                    await _db.SaveChangesAsync(cancellationToken);

                    _logger.LogInformation("Refund {AuditRef} succeeded via Paymob. Provider Refund ID: {ProviderRefundId}",
                        refund.AuditReference, refund.ProviderRefundId);

                    // Send Patient Notification: Refund Succeeded
                    try
                    {
                        var patientUserId = attempt.Appointment?.Patient?.UserId;
                        if (!string.IsNullOrWhiteSpace(patientUserId))
                        {
                            var notifyMessage = $"Your refund of {amountEgp:N2} EGP has been processed successfully (Ref: {refund.AuditReference}). Funds typically reflect in your account within 5 to 14 business days depending on your bank.";
                            await _notificationService.SendNotificationAsync(patientUserId, "Payment Refund Processed", notifyMessage);
                            await _notificationService.NotifyRefundEventAsync(
                                patientUserId,
                                "RefundSucceeded",
                                "Refund Processed Successfully",
                                notifyMessage,
                                refund.Id,
                                attempt.Id,
                                refund.AuditReference,
                                "/Payment/History",
                                cancellationToken);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to send patient notification for refund {AuditRef}", refund.AuditReference);
                    }

                    return new RefundResultDto
                    {
                        Success = true,
                        RefundId = refund.Id,
                        AuditReference = refund.AuditReference,
                        Status = refund.Status,
                        AmountEgp = amountEgp,
                        AmountCents = refund.AmountCents,
                        ProviderRefundId = refund.ProviderRefundId,
                        Message = $"Refund of {amountEgp:N2} EGP executed successfully with payment provider. Reference: {refund.AuditReference}."
                    };
                }
                else
                {
                    refund.Status = "Failed";
                    refund.FailureReason = $"Provider returned unsuccessful refund response: {responseText}";
                    await _db.SaveChangesAsync(cancellationToken);

                    // Notify patient and alert admins of failure
                    try
                    {
                        var patientUserId = attempt.Appointment?.Patient?.UserId;
                        if (!string.IsNullOrWhiteSpace(patientUserId))
                        {
                            await _notificationService.NotifyRefundEventAsync(
                                patientUserId,
                                "RefundFailed",
                                "Refund Processing Issue",
                                $"Your refund request of {amountEgp:N2} EGP could not be processed immediately by the provider. Our administrative team has been notified.",
                                refund.Id,
                                attempt.Id,
                                refund.AuditReference,
                                "/Payment/History",
                                cancellationToken);
                        }

                        await _notificationService.NotifyAdminFinancialAlertAsync(
                            "RefundProcessingFailure",
                            "Refund Execution Failure Alert",
                            $"Refund attempt of {amountEgp:N2} EGP (Ref: {refund.AuditReference}) was declined by payment gateway: {responseText}",
                            attempt.Id,
                            refund.Id,
                            $"AdminRefundFail_{refund.Id}",
                            "/Admin/Payments",
                            cancellationToken);
                    }
                    catch (Exception notifEx)
                    {
                        _logger.LogWarning(notifEx, "Failed to send refund failure notifications for {AuditRef}", refund.AuditReference);
                    }

                    return new RefundResultDto
                    {
                        Success = false,
                        RefundId = refund.Id,
                        AuditReference = refund.AuditReference,
                        Status = refund.Status,
                        AmountEgp = amountEgp,
                        AmountCents = refund.AmountCents,
                        Message = $"Payment provider rejected the refund: {responseText}"
                    };
                }
            }
            else
            {
                refund.Status = "Failed";
                refund.FailureReason = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}: {responseText}";
                await _db.SaveChangesAsync(cancellationToken);

                try
                {
                    await _notificationService.NotifyAdminFinancialAlertAsync(
                        "RefundProcessingFailure",
                        "Refund Execution HTTP Failure",
                        $"Refund attempt of {amountEgp:N2} EGP (Ref: {refund.AuditReference}) received HTTP error: {response.StatusCode}",
                        attempt.Id,
                        refund.Id,
                        $"AdminRefundHttpErr_{refund.Id}",
                        "/Admin/Payments",
                        cancellationToken);
                }
                catch (Exception notifEx)
                {
                    _logger.LogWarning(notifEx, "Failed to send refund HTTP error alert for {AuditRef}", refund.AuditReference);
                }

                return new RefundResultDto
                {
                    Success = false,
                    RefundId = refund.Id,
                    AuditReference = refund.AuditReference,
                    Status = refund.Status,
                    AmountEgp = amountEgp,
                    AmountCents = refund.AmountCents,
                    Message = $"Refund execution failed: {refund.FailureReason}"
                };
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogError(ex, "Network timeout/exception while dispatching refund {AuditRef}", refund.AuditReference);
            refund.Status = "RequiresReconciliation";
            refund.FailureReason = "Payment provider gateway timeout during refund dispatch. Manual reconciliation required.";
            attempt.ReconciliationStatus = "ReconciliationRequired";
            attempt.ReconciliationReason = $"Refund #{refund.AuditReference} timed out against Paymob; verify transaction status at provider portal before retrying.";
            await _db.SaveChangesAsync(cancellationToken);

            try
            {
                await _notificationService.NotifyAdminFinancialAlertAsync(
                    "RefundDiscrepancy",
                    "Financial Discrepancy: Refund Timeout",
                    $"Refund #{refund.AuditReference} of {amountEgp:N2} EGP for attempt #{attempt.Id} timed out. Flagged as RequiresReconciliation.",
                    attempt.Id,
                    refund.Id,
                    $"AdminRefundTimeout_{refund.Id}",
                    "/Admin/Payments",
                    cancellationToken);
            }
            catch (Exception notifEx)
            {
                _logger.LogWarning(notifEx, "Failed to send refund timeout alert for {AuditRef}", refund.AuditReference);
            }

            return new RefundResultDto
            {
                Success = false,
                RefundId = refund.Id,
                AuditReference = refund.AuditReference,
                Status = refund.Status,
                AmountEgp = amountEgp,
                AmountCents = refund.AmountCents,
                Message = "Refund request timed out while communicating with payment provider. Flagged as RequiresReconciliation for financial audit."
            };
        }
    }
}
