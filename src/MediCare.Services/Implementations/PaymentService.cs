using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
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

namespace MediCare.Services.Implementations;

public class PaymentService : IPaymentService
{
    private readonly IUnitOfWork _uow;
    private readonly INotificationService _notificationService;
    private readonly ILogger<PaymentService> _logger;
    private readonly ApplicationDbContext? _db;

    public PaymentService(
        IUnitOfWork uow,
        INotificationService notificationService,
        ILogger<PaymentService> logger,
        ApplicationDbContext? db = null)
    {
        _uow = uow;
        _notificationService = notificationService;
        _logger = logger;
        _db = db;
    }

    public async Task<Result<PaymentReceiptDto>> ProcessCheckoutAsync(PaymentCheckoutRequestDto request, string currentUserId)
    {
        if (request == null)
        {
            return Result<PaymentReceiptDto>.Failure("Payment request cannot be empty.");
        }

        if (request.AppointmentId <= 0)
        {
            return Result<PaymentReceiptDto>.Failure("Valid appointment ID is required.");
        }

        // Card validation
        var cleanCard = Regex.Replace(request.CardNumber ?? string.Empty, @"\s+|-", "");
        if (string.IsNullOrWhiteSpace(cleanCard) || !Regex.IsMatch(cleanCard, @"^\d{13,19}$"))
        {
            return Result<PaymentReceiptDto>.Failure("Invalid card number. Please provide a valid 13-19 digit credit or debit card number.");
        }

        if (!IsValidLuhn(cleanCard))
        {
            return Result<PaymentReceiptDto>.Failure("Invalid card checksum. Please check your card number.");
        }

        if (string.IsNullOrWhiteSpace(request.CardHolderName) || request.CardHolderName.Trim().Length < 2)
        {
            return Result<PaymentReceiptDto>.Failure("Cardholder name is required.");
        }

        if (!int.TryParse(request.ExpiryMonth, out int expMonth) || expMonth < 1 || expMonth > 12)
        {
            return Result<PaymentReceiptDto>.Failure("Invalid expiration month.");
        }

        if (!int.TryParse(request.ExpiryYear, out int expYear))
        {
            return Result<PaymentReceiptDto>.Failure("Invalid expiration year.");
        }

        if (expYear < 100)
        {
            expYear += 2000;
        }

        var now = DateTime.UtcNow;
        if (expYear < now.Year || (expYear == now.Year && expMonth < now.Month))
        {
            return Result<PaymentReceiptDto>.Failure("The card has expired.");
        }

        if (string.IsNullOrWhiteSpace(request.Cvv) || !Regex.IsMatch(request.Cvv.Trim(), @"^\d{3,4}$"))
        {
            return Result<PaymentReceiptDto>.Failure("Invalid CVV. Please enter a valid 3 or 4 digit security code.");
        }

        // Fetch appointment
        var appointment = await _uow.Appointments.GetByIdWithDetailsAsync(request.AppointmentId);
        if (appointment == null)
        {
            return Result<PaymentReceiptDto>.Failure("Appointment not found.");
        }

        // IDOR check: Only the patient who booked the appointment can process payment
        if (appointment.Patient?.UserId != currentUserId)
        {
            _logger.LogWarning("Security IDOR: User {UserId} attempted payment for appointment {ApptId} owned by patient user {PatientUserId}",
                currentUserId, request.AppointmentId, appointment.Patient?.UserId);
            return Result<PaymentReceiptDto>.Failure("Forbidden: You are not authorized to pay for this appointment.");
        }

        if (appointment.Status == AppointmentStatus.Cancelled || appointment.Status == AppointmentStatus.Rejected)
        {
            return Result<PaymentReceiptDto>.Failure("Cannot process payment for a cancelled or rejected appointment.");
        }

        if (appointment.PaymentStatus == PaymentStatus.Paid)
        {
            return await GetReceiptAsync(appointment.Id, currentUserId);
        }

        var lockKey = $"Payment_{appointment.Id}";
        await using var appLock = await _uow.AcquireApplicationLockAsync(lockKey, TimeSpan.FromSeconds(10));

        if (appointment.PaymentStatus == PaymentStatus.Paid)
        {
            return await GetReceiptAsync(appointment.Id, currentUserId);
        }

        decimal discountAmount = 0m;
        string? appliedPromo = null;
        if (!string.IsNullOrWhiteSpace(request.PromoCode))
        {
            var promoResult = await ValidatePromoCodeAsync(request.PromoCode, appointment.ConsultationFee, currentUserId);
            if (!promoResult.IsSuccess || promoResult.Value is not { IsValid: true })
            {
                return Result<PaymentReceiptDto>.Failure(promoResult.Error ?? "Invalid promo code.");
            }
            discountAmount = promoResult.Value.DiscountAmount;
            appliedPromo = promoResult.Value.Code;
        }

        var originalFee = appointment.ConsultationFee;
        var finalAmount = Math.Max(0m, originalFee - discountAmount);

        var prevPaymentStatus = appointment.PaymentStatus;
        var prevPaidAmount = appointment.PaidAmount;
        var prevDiscountAmount = appointment.DiscountAmount;
        var prevPromoCode = appointment.PromoCode;
        var prevTxnRef = appointment.TransactionReference;
        var prevPaidAt = appointment.PaidAt;

        // Atomically mark paid
        appointment.PaymentStatus = PaymentStatus.Paid;
        appointment.PaidAmount = finalAmount;
        appointment.DiscountAmount = discountAmount;
        appointment.PromoCode = appliedPromo;
        var paymentTimeUtc = DateTime.UtcNow;
        appointment.PaidAt = paymentTimeUtc;
        var txnRef = $"TXN-{paymentTimeUtc:yyyyMMddHHmmss}-{appointment.Id:D4}";
        appointment.TransactionReference = txnRef;
        appointment.PaymentProvider = "Card (Legacy)";

        _uow.Appointments.Update(appointment);

        try
        {
            await _uow.CommitAsync();
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Concurrency conflict during payment checkout for appointment {ApptId}. Another concurrent request already processed payment.", appointment.Id);
            return await GetReceiptAsync(appointment.Id, currentUserId);
        }
        catch (DbUpdateException ex)
        {
            appointment.PaymentStatus = prevPaymentStatus;
            appointment.PaidAmount = prevPaidAmount;
            appointment.DiscountAmount = prevDiscountAmount;
            appointment.PromoCode = prevPromoCode;
            appointment.TransactionReference = prevTxnRef;
            appointment.PaidAt = prevPaidAt;

            if (ex.InnerException?.Message.Contains("IX_Appointments_Patient_PromoCode_Paid") == true ||
                ex.Message.Contains("IX_Appointments_Patient_PromoCode_Paid"))
            {
                _logger.LogWarning(ex, "Unique index violation: Patient {PatientId} attempted to reuse promo code {Promo}", appointment.PatientId, appliedPromo);
                return Result<PaymentReceiptDto>.Failure("You have already used this promo code on another appointment.");
            }
            throw;
        }

        if (_db != null)
        {
            var attempt = new PaymentAttempt
            {
                AppointmentId = appointment.Id,
                PatientId = appointment.PatientId,
                MerchantReference = txnRef,
                ProviderTransactionId = txnRef,
                AmountCents = checked((long)Math.Round(finalAmount * 100m, MidpointRounding.AwayFromZero)),
                DiscountAmount = discountAmount,
                PromoCode = appliedPromo,
                Currency = "EGP",
                Status = "Succeeded",
                PaymentMethodType = "Card",
                PaymentMethodDetail = "Bank Card",
                TermsVersion = request.TermsVersion ?? PaymentTermsConstants.ActiveVersion,
                TermsAcceptedAt = paymentTimeUtc,
                CreatedAt = paymentTimeUtc,
                CompletedAt = paymentTimeUtc
            };
            _db.PaymentAttempts.Add(attempt);
            await _db.SaveChangesAsync();
        }

        try
        {
            await _notificationService.SendNotificationAsync(
                currentUserId,
                "Payment Received",
                $"Payment of {finalAmount:F2} EGP for appointment #{appointment.Id} was completed successfully. Ref: {txnRef}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send notification for payment {TxnRef}", txnRef);
        }

        var receipt = new PaymentReceiptDto
        {
            AppointmentId = appointment.Id,
            TransactionReference = txnRef,
            AmountPaid = finalAmount,
            OriginalAmount = originalFee,
            DiscountAmount = discountAmount,
            AppliedPromoCode = appliedPromo,
            PaidAt = paymentTimeUtc,
            PatientName = appointment.Patient?.User?.FullName ?? "Patient",
            DoctorName = $"Dr. {appointment.Doctor?.User?.FullName ?? "Physician"}",
            Specialization = appointment.Doctor?.Specialization?.Name ?? "General Practice",
            FormattedDate = appointment.AppointmentDate.ToString("yyyy-MM-dd"),
            FormattedTime = $"{appointment.StartTime:hh\\:mm} - {appointment.EndTime:hh\\:mm}",
            PaymentMethod = appointment.PaymentProvider ?? "Legacy simulated checkout"
        };

        return Result<PaymentReceiptDto>.Success(receipt);
    }

    public async Task<Result<PaymentReceiptDto>> GetReceiptAsync(int appointmentId, string currentUserId)
    {
        if (appointmentId <= 0)
        {
            return Result<PaymentReceiptDto>.Failure("Valid appointment ID is required.");
        }

        var appointment = await _uow.Appointments.GetByIdWithDetailsAsync(appointmentId);
        if (appointment == null)
        {
            return Result<PaymentReceiptDto>.Failure("Appointment not found.");
        }

        // Access check: appointment patient, doctor, or authorized
        bool isPatient = appointment.Patient?.UserId == currentUserId;
        bool isDoctor = appointment.Doctor?.UserId == currentUserId;

        if (!isPatient && !isDoctor)
        {
            _logger.LogWarning("Security IDOR: User {UserId} attempted unauthorized access to receipt for appointment {ApptId}",
                currentUserId, appointmentId);
            return Result<PaymentReceiptDto>.Failure("Forbidden: You are not authorized to access this receipt.");
        }

        if (appointment.PaymentStatus != PaymentStatus.Paid && appointment.PaymentStatus != PaymentStatus.Refunded)
        {
            return Result<PaymentReceiptDto>.Failure("Payment has not been completed for this appointment yet.");
        }

        var paidDate = appointment.PaidAt ?? appointment.UpdatedAt ?? appointment.CreatedAt;
        var txnRef = appointment.TransactionReference ?? $"TXN-{paidDate:yyyyMMddHHmmss}-{appointment.Id:D4}";
        bool isRefunded = appointment.PaymentStatus == PaymentStatus.Refunded;

        var receipt = new PaymentReceiptDto
        {
            AppointmentId = appointment.Id,
            TransactionReference = txnRef,
            AmountPaid = appointment.PaidAmount ?? appointment.ConsultationFee,
            OriginalAmount = appointment.ConsultationFee,
            DiscountAmount = appointment.DiscountAmount ?? 0m,
            AppliedPromoCode = appointment.PromoCode,
            PaidAt = paidDate,
            PatientName = appointment.Patient?.User?.FullName ?? "Patient",
            DoctorName = $"Dr. {appointment.Doctor?.User?.FullName ?? "Physician"}",
            Specialization = appointment.Doctor?.Specialization?.Name ?? "General Practice",
            FormattedDate = appointment.AppointmentDate.ToString("yyyy-MM-dd"),
            FormattedTime = $"{appointment.StartTime:hh\\:mm} - {appointment.EndTime:hh\\:mm}",
            PaymentMethod = appointment.PaymentProvider ?? "Legacy simulated checkout",
            IsRefunded = isRefunded,
            Status = isRefunded ? "Refunded" : "Paid"
        };

        if (_db != null)
        {
            var attempt = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
                _db.PaymentAttempts.Include(x => x.Refunds).OrderByDescending(x => x.CreatedAt),
                x => x.AppointmentId == appointment.Id && (x.Status == "Succeeded" || x.Status == "PartiallyRefunded" || x.Status == "Refunded"));
            if (attempt != null)
            {
                receipt.TermsVersion = attempt.TermsVersion;
                receipt.PaymentMethodType = attempt.PaymentMethodType;
                receipt.PaymentMethodDetail = attempt.PaymentMethodDetail;
                receipt.ProviderOrderId = attempt.ProviderOrderId;
                receipt.ReconciliationStatus = attempt.ReconciliationStatus;

                var succeededRefunds = attempt.Refunds
                    .Where(r => r.Status == "Succeeded")
                    .OrderByDescending(r => r.RequestedAtUtc)
                    .Select(r => new PatientRefundDto
                    {
                        RefundId = r.Id,
                        AuditReference = r.AuditReference,
                        AmountEgp = (decimal)r.AmountCents / 100m,
                        Status = r.Status,
                        ProcessedAtUtc = r.ProcessedAtUtc
                    }).ToList();

                receipt.Refunds = succeededRefunds;
                receipt.RefundedAmount = succeededRefunds.Sum(r => r.AmountEgp);
                if (succeededRefunds.Count > 0)
                {
                    receipt.IsRefunded = true;
                    receipt.Status = receipt.RefundedAmount >= receipt.AmountPaid ? "Refunded" : "Partially Refunded";
                }
            }
        }

        return Result<PaymentReceiptDto>.Success(receipt);
    }

    private static bool IsValidLuhn(string number)
    {
        int sum = 0;
        bool alternate = false;
        for (int i = number.Length - 1; i >= 0; i--)
        {
            int n = number[i] - '0';
            if (alternate)
            {
                n *= 2;
                if (n > 9)
                {
                    n -= 9;
                }
            }
            sum += n;
            alternate = !alternate;
        }
        return sum % 10 == 0;
    }

    public Task<Result<PromoCodeValidationDto>> ValidatePromoCodeAsync(string code, decimal currentAmount)
    {
        return ValidatePromoCodeAsync(code, currentAmount, null);
    }

    public async Task<Result<PromoCodeValidationDto>> ValidatePromoCodeAsync(string code, decimal currentAmount, string? currentUserId)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Result<PromoCodeValidationDto>.Failure("Please enter a valid promo code.");
        }

        var normalized = code.Trim().ToUpperInvariant();
        decimal discount = 0m;
        string desc = string.Empty;

        switch (normalized)
        {
            case "DEPI2026":
                discount = Math.Round(currentAmount * 0.20m, 2);
                desc = "DEPI 2026 Scholarship Special: 20% Discount";
                break;
            case "MEDICARE50":
                discount = Math.Min(currentAmount, 50m);
                desc = "Medicare Voucher: 50 EGP Instant Discount";
                break;
            case "WELCOME10":
                discount = Math.Round(currentAmount * 0.10m, 2);
                desc = "New Patient Welcome: 10% Discount";
                break;
            default:
                return Result<PromoCodeValidationDto>.Failure("Invalid or expired promo code.");
        }

        if (!string.IsNullOrEmpty(currentUserId) && _uow.Patients != null)
        {
            var patients = await _uow.Patients.FindAsync(p => p.UserId == currentUserId);
            var patient = patients?.FirstOrDefault();
            if (patient != null && _uow.Appointments != null)
            {
                var existingPaid = await _uow.Appointments.FindAsync(a =>
                    a.PatientId == patient.Id &&
                    a.PromoCode == normalized &&
                    a.PaymentStatus == PaymentStatus.Paid);

                if (existingPaid != null && existingPaid.Any())
                {
                    return Result<PromoCodeValidationDto>.Failure("You have already used this promo code on another appointment.");
                }
            }
        }

        var dto = new PromoCodeValidationDto
        {
            IsValid = true,
            Code = normalized,
            DiscountAmount = discount,
            FinalAmount = Math.Max(0m, currentAmount - discount),
            Description = desc
        };

        return Result<PromoCodeValidationDto>.Success(dto);
    }

public async Task<Result<PaymentHistoryViewModel>> GetPaymentHistoryAsync(
        PaymentHistoryFilterDto filter,
        string currentUserId,
        bool isAdmin = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(currentUserId))
        {
            return Result<PaymentHistoryViewModel>.Failure("Authentication required.");
        }

        if (_db == null)
        {
            return Result<PaymentHistoryViewModel>.Failure("Database context is required to query payment transaction history.");
        }

        filter ??= new PaymentHistoryFilterDto();

        int? patientId = null;
        if (!isAdmin)
        {
            var patient = await _db.Patients.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == currentUserId, cancellationToken);
            if (patient == null)
            {
                return Result<PaymentHistoryViewModel>.Failure("Patient profile not found for current user.");
            }
            patientId = patient.Id;
        }

        IQueryable<PaymentAttempt> baseQuery = _db.PaymentAttempts.AsNoTracking();
        if (!isAdmin)
        {
            baseQuery = baseQuery.Where(p => p.PatientId == patientId!.Value);
        }

        // Calculate summary metrics across the patient's (or system-wide for admin) payment history
        var summary = new PaymentHistorySummaryDto();
        var totalPaidCents = await baseQuery.Where(p => p.Status == "Succeeded").SumAsync(p => (long?)p.AmountCents, cancellationToken) ?? 0L;
        summary.TotalPaid = (decimal)totalPaidCents / 100m;
        summary.SucceededCount = await baseQuery.CountAsync(p => p.Status == "Succeeded", cancellationToken);
        summary.PendingCount = await baseQuery.CountAsync(p => p.Status == "Pending", cancellationToken);
        summary.RefundedCount = await baseQuery.CountAsync(p => p.Appointment.PaymentStatus == PaymentStatus.Refunded || p.ReconciliationStatus == "Refunded" || p.Status == "Refunded", cancellationToken);
        summary.RequiresRefundCount = await baseQuery.CountAsync(p => p.ReconciliationStatus == "RequiresRefund" || p.ReconciliationStatus == "ReconciliationRequired" || p.ReconciliationStatus == "ManualInterventionRequired", cancellationToken);

        // Apply search and filter criteria
        var filteredQuery = baseQuery;

        if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
        {
            var term = filter.SearchTerm.Trim();
            bool isNumeric = int.TryParse(term, out int searchId);

            filteredQuery = filteredQuery.Where(p =>
                p.MerchantReference.Contains(term) ||
                (p.ProviderTransactionId != null && p.ProviderTransactionId.Contains(term)) ||
                (p.ProviderOrderId != null && p.ProviderOrderId.Contains(term)) ||
                (p.Appointment.Doctor.User.FullName != null && p.Appointment.Doctor.User.FullName.Contains(term)) ||
                (isNumeric && p.AppointmentId == searchId));
        }

        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            var s = filter.Status.Trim();
            if (string.Equals(s, "Succeeded", StringComparison.OrdinalIgnoreCase))
            {
                filteredQuery = filteredQuery.Where(p => p.Status == "Succeeded");
            }
            else if (string.Equals(s, "Pending", StringComparison.OrdinalIgnoreCase))
            {
                filteredQuery = filteredQuery.Where(p => p.Status == "Pending");
            }
            else if (string.Equals(s, "Failed", StringComparison.OrdinalIgnoreCase))
            {
                filteredQuery = filteredQuery.Where(p => p.Status == "Failed");
            }
            else if (string.Equals(s, "Expired", StringComparison.OrdinalIgnoreCase))
            {
                filteredQuery = filteredQuery.Where(p => p.Status == "Expired");
            }
            else if (string.Equals(s, "Refunded", StringComparison.OrdinalIgnoreCase))
            {
                filteredQuery = filteredQuery.Where(p => p.Appointment.PaymentStatus == PaymentStatus.Refunded || p.ReconciliationStatus == "Refunded" || p.Status == "Refunded");
            }
            else if (string.Equals(s, "RequiresRefund", StringComparison.OrdinalIgnoreCase) || string.Equals(s, "ReconciliationRequired", StringComparison.OrdinalIgnoreCase))
            {
                filteredQuery = filteredQuery.Where(p => p.ReconciliationStatus == "RequiresRefund" || p.ReconciliationStatus == "ReconciliationRequired" || p.ReconciliationStatus == "ManualInterventionRequired");
            }
        }

        if (!string.IsNullOrWhiteSpace(filter.PaymentMethod))
        {
            var m = filter.PaymentMethod.Trim();
            if (string.Equals(m, "Card", StringComparison.OrdinalIgnoreCase))
            {
                filteredQuery = filteredQuery.Where(p => p.PaymentMethodType == "Card" || (p.PaymentMethodDetail != null && (p.PaymentMethodDetail.Contains("Card") || p.PaymentMethodDetail.Contains("Visa") || p.PaymentMethodDetail.Contains("Mastercard") || p.PaymentMethodDetail.Contains("Meeza"))));
            }
            else if (string.Equals(m, "MobileWallet", StringComparison.OrdinalIgnoreCase) || string.Equals(m, "Wallet", StringComparison.OrdinalIgnoreCase))
            {
                filteredQuery = filteredQuery.Where(p => p.PaymentMethodType == "MobileWallet" || (p.PaymentMethodDetail != null && (p.PaymentMethodDetail.Contains("Cash") || p.PaymentMethodDetail.Contains("Wallet") || p.PaymentMethodDetail.Contains("Vodafone") || p.PaymentMethodDetail.Contains("Orange") || p.PaymentMethodDetail.Contains("Etisalat") || p.PaymentMethodDetail.Contains("WE"))));
            }
        }

        if (filter.FromDate.HasValue)
        {
            var fromUtc = filter.FromDate.Value.Date;
            filteredQuery = filteredQuery.Where(p => p.CreatedAt >= fromUtc);
        }

        if (filter.ToDate.HasValue)
        {
            var toUtc = filter.ToDate.Value.Date.AddDays(1).AddTicks(-1);
            filteredQuery = filteredQuery.Where(p => p.CreatedAt <= toUtc);
        }

        int totalCount = await filteredQuery.CountAsync(cancellationToken);
        int page = filter.Page < 1 ? 1 : filter.Page;
        int pageSize = filter.PageSize < 1 ? 10 : (filter.PageSize > 100 ? 100 : filter.PageSize);

        var items = await filteredQuery
            .OrderByDescending(p => p.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new PaymentHistoryItemDto
            {
                Id = p.Id,
                AppointmentId = p.AppointmentId,
                MerchantReference = p.MerchantReference,
                ProviderTransactionId = p.ProviderTransactionId,
                ProviderOrderId = p.ProviderOrderId,
                AmountPaid = (decimal)p.AmountCents / 100m,
                OriginalAmount = p.Appointment.ConsultationFee,
                DiscountAmount = p.DiscountAmount,
                PromoCode = p.PromoCode,
                Currency = p.Currency ?? "EGP",
                Status = p.Status,
                DisplayStatus = p.Status,
                IsRefunded = p.Appointment.PaymentStatus == PaymentStatus.Refunded || p.ReconciliationStatus == "Refunded" || p.Status == "Refunded" || p.Refunds.Any(r => r.Status == "Succeeded"),
                RefundStatus = (p.Appointment.PaymentStatus == PaymentStatus.Refunded || p.ReconciliationStatus == "Refunded" || p.Status == "Refunded" || (p.Refunds.Any(r => r.Status == "Succeeded") && p.Refunds.Where(r => r.Status == "Succeeded").Sum(r => (long?)r.AmountCents) >= p.AmountCents)) ? "Refunded" : (p.Refunds.Any(r => r.Status == "Succeeded") ? "Partially Refunded" : null),
                RefundedAmount = (decimal)(p.Refunds.Where(r => r.Status == "Succeeded").Sum(r => (long?)r.AmountCents) ?? 0L) / 100m,
                RemainingRefundableAmount = Math.Max(0m, (decimal)(p.AmountCents - (p.Refunds.Where(r => r.Status != "Rejected" && r.Status != "Failed").Sum(r => (long?)r.AmountCents) ?? 0L)) / 100m),
                CanRequestRefund = (p.Status == "Succeeded" || p.Status == "PartiallyRefunded") &&
                                   (p.AmountCents - (p.Refunds.Where(r => r.Status != "Rejected" && r.Status != "Failed").Sum(r => (long?)r.AmountCents) ?? 0L)) > 0,
                Refunds = p.Refunds
                    .Where(r => r.Status == "Succeeded" || r.Status == "Processing" || r.Status == "UnderReview")
                    .OrderByDescending(r => r.RequestedAtUtc)
                    .Select(r => new PatientRefundDto
                    {
                        RefundId = r.Id,
                        AuditReference = r.AuditReference,
                        AmountEgp = (decimal)r.AmountCents / 100m,
                        Status = r.Status,
                        ProcessedAtUtc = r.ProcessedAtUtc
                    }).ToList(),
                ReconciliationStatus = p.ReconciliationStatus,
                ReconciliationReason = p.ReconciliationReason,
                PaymentMethodType = p.PaymentMethodType ?? "Card",
                PaymentMethodDetail = p.PaymentMethodDetail ?? (p.PaymentMethodType == "MobileWallet" ? "Mobile Wallet" : "Payment Card"),
                DoctorName = "Dr. " + (p.Appointment.Doctor.User.FullName ?? "Physician"),
                Specialization = p.Appointment.Doctor.Specialization.Name ?? "General Practice",
                AppointmentDate = p.Appointment.AppointmentDate,
                StartTime = p.Appointment.StartTime,
                EndTime = p.Appointment.EndTime,
                CreatedAtUtc = p.CreatedAt,
                CompletedAtUtc = p.CompletedAt,
                CanViewReceipt = p.Status == "Succeeded" || p.Appointment.PaymentStatus == PaymentStatus.Paid || p.Appointment.PaymentStatus == PaymentStatus.Refunded,
                TermsVersion = p.TermsVersion
            })
            .ToListAsync(cancellationToken);

        var pagedResult = new PagedResult<PaymentHistoryItemDto>(items, totalCount, page, pageSize);

        var viewModel = new PaymentHistoryViewModel
        {
            Filter = filter,
            PagedHistory = pagedResult,
            Summary = summary
        };

        return Result<PaymentHistoryViewModel>.Success(viewModel);
    }

public async Task<Result<AdminFinancialDashboardDto>> GetAdminFinancialDashboardAsync(
        PaymentHistoryFilterDto filter,
        CancellationToken cancellationToken = default)
    {
        if (_db == null)
        {
            return Result<AdminFinancialDashboardDto>.Failure("Database context is required to query administrative financial metrics.");
        }

        filter ??= new PaymentHistoryFilterDto();

        // 1. Base aggregations across all transactions
        var baseQuery = _db.PaymentAttempts.AsNoTracking();

        var totalGrossCents = await baseQuery
            .Where(p => p.Status == "Succeeded" || p.Status == "PartiallyRefunded" || p.Status == "Refunded")
            .SumAsync(p => (long?)p.AmountCents, cancellationToken) ?? 0L;

        var refundTableCents = await _db.Refunds
            .Where(r => r.Status == "Succeeded")
            .SumAsync(r => (long?)r.AmountCents, cancellationToken) ?? 0L;

        var legacyRefundedCents = await baseQuery
            .Where(p => !p.Refunds.Any(r => r.Status == "Succeeded") &&
                        (p.Appointment.PaymentStatus == PaymentStatus.Refunded || p.ReconciliationStatus == "Refunded" || p.Status == "Refunded"))
            .SumAsync(p => (long?)p.AmountCents, cancellationToken) ?? 0L;

        var totalRefundedCents = refundTableCents + legacyRefundedCents;

        var succeededCount = await baseQuery.CountAsync(p => p.Status == "Succeeded", cancellationToken);
        var failedCount = await baseQuery.CountAsync(p => p.Status == "Failed" || p.Status == "Expired", cancellationToken);
        var pendingCount = await baseQuery.CountAsync(p => p.Status == "Pending", cancellationToken);
        var requiresRefundCount = await baseQuery.CountAsync(p => 
            p.ReconciliationStatus == "RequiresRefund" || 
            p.ReconciliationStatus == "ReconciliationRequired" || 
            p.ReconciliationStatus == "ManualInterventionRequired", cancellationToken);

        // 2. Payment Method Breakdown (Gross & Count for Card vs MobileWallet)
        var cardQuery = baseQuery.Where(p => p.Status == "Succeeded" && (p.PaymentMethodType == "Card" || (p.PaymentMethodDetail != null && (p.PaymentMethodDetail.Contains("Card") || p.PaymentMethodDetail.Contains("Visa") || p.PaymentMethodDetail.Contains("Mastercard") || p.PaymentMethodDetail.Contains("Meeza")))));
        var cardGrossCents = await cardQuery.SumAsync(p => (long?)p.AmountCents, cancellationToken) ?? 0L;
        var cardCount = await cardQuery.CountAsync(cancellationToken);

        var walletQuery = baseQuery.Where(p => p.Status == "Succeeded" && (p.PaymentMethodType == "MobileWallet" || (p.PaymentMethodDetail != null && (p.PaymentMethodDetail.Contains("Cash") || p.PaymentMethodDetail.Contains("Wallet") || p.PaymentMethodDetail.Contains("Vodafone") || p.PaymentMethodDetail.Contains("Orange") || p.PaymentMethodDetail.Contains("Etisalat") || p.PaymentMethodDetail.Contains("WE")))));
        var walletGrossCents = await walletQuery.SumAsync(p => (long?)p.AmountCents, cancellationToken) ?? 0L;
        var walletCount = await walletQuery.CountAsync(cancellationToken);

        // 3. Time Series Revenue (Last 14 days)
        var timeSeriesCutoff = DateTime.UtcNow.Date.AddDays(-13);
        var recentSucceeded = await baseQuery
            .Where(p => p.Status == "Succeeded" && p.CreatedAt >= timeSeriesCutoff)
            .Select(p => new
            {
                Date = p.CreatedAt.Date,
                Amount = (decimal)p.AmountCents / 100m,
                IsRefunded = p.Appointment.PaymentStatus == PaymentStatus.Refunded || p.ReconciliationStatus == "Refunded" || p.Status == "Refunded"
            })
            .ToListAsync(cancellationToken);

        var timeSeries = new List<FinancialRevenueTimeSeriesDto>();
        for (int i = 13; i >= 0; i--)
        {
            var day = DateTime.UtcNow.Date.AddDays(-i);
            var dayItems = recentSucceeded.Where(x => x.Date == day).ToList();
            timeSeries.Add(new FinancialRevenueTimeSeriesDto
            {
                PeriodLabel = day.ToString("MMM dd"),
                GrossRevenue = dayItems.Sum(x => x.Amount),
                RefundedAmount = dayItems.Where(x => x.IsRefunded).Sum(x => x.Amount),
                TransactionCount = dayItems.Count
            });
        }

        // 4. Reconciliation Queue (Transactions requiring administrative attention)
        var reconciliationItems = await baseQuery
            .Where(p => 
                p.ReconciliationStatus == "RequiresRefund" || 
                p.ReconciliationStatus == "ReconciliationRequired" || 
                p.ReconciliationStatus == "ManualInterventionRequired" ||
                (p.Appointment.Status == AppointmentStatus.Cancelled && p.Status == "Succeeded" && p.ReconciliationStatus != "Refunded" && p.ReconciliationStatus != "Rebooked"))
            .OrderByDescending(p => p.CreatedAt)
            .Take(25)
            .Select(p => new ReconciliationQueueItemDto
            {
                Id = p.Id,
                AppointmentId = p.AppointmentId,
                MerchantReference = p.MerchantReference,
                ProviderTransactionId = p.ProviderTransactionId,
                ProviderOrderId = p.ProviderOrderId,
                AmountPaid = (decimal)p.AmountCents / 100m,
                Currency = p.Currency ?? "EGP",
                Status = p.Status,
                ReconciliationStatus = p.ReconciliationStatus ?? "RequiresRefund",
                ReconciliationReason = p.ReconciliationReason,
                PaymentMethodDetail = p.PaymentMethodDetail,
                PatientName = p.Appointment.Patient.User.FullName ?? "Patient",
                DoctorName = "Dr. " + (p.Appointment.Doctor.User.FullName ?? "Physician"),
                AppointmentStatus = p.Appointment.Status.ToString(),
                CreatedAtUtc = p.CreatedAt,
                ReconciledAt = p.ReconciledAt,
                IsPostCancellationPayment = p.Appointment.Status == AppointmentStatus.Cancelled || p.Appointment.Status == AppointmentStatus.Rejected
            })
            .ToListAsync(cancellationToken);

        // 5. Paged Transactions with Active Filters
        var filteredQuery = baseQuery;

        if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
        {
            var term = filter.SearchTerm.Trim();
            bool isNumeric = int.TryParse(term, out int searchId);

            filteredQuery = filteredQuery.Where(p =>
                p.MerchantReference.Contains(term) ||
                (p.ProviderTransactionId != null && p.ProviderTransactionId.Contains(term)) ||
                (p.ProviderOrderId != null && p.ProviderOrderId.Contains(term)) ||
                (p.Appointment.Doctor.User.FullName != null && p.Appointment.Doctor.User.FullName.Contains(term)) ||
                (p.Appointment.Patient.User.FullName != null && p.Appointment.Patient.User.FullName.Contains(term)) ||
                (isNumeric && p.AppointmentId == searchId));
        }

        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            var s = filter.Status.Trim();
            if (string.Equals(s, "Succeeded", StringComparison.OrdinalIgnoreCase))
            {
                filteredQuery = filteredQuery.Where(p => p.Status == "Succeeded");
            }
            else if (string.Equals(s, "Pending", StringComparison.OrdinalIgnoreCase))
            {
                filteredQuery = filteredQuery.Where(p => p.Status == "Pending");
            }
            else if (string.Equals(s, "Failed", StringComparison.OrdinalIgnoreCase))
            {
                filteredQuery = filteredQuery.Where(p => p.Status == "Failed" || p.Status == "Expired");
            }
            else if (string.Equals(s, "Refunded", StringComparison.OrdinalIgnoreCase))
            {
                filteredQuery = filteredQuery.Where(p => p.Appointment.PaymentStatus == PaymentStatus.Refunded || p.ReconciliationStatus == "Refunded" || p.Status == "Refunded");
            }
            else if (string.Equals(s, "RequiresRefund", StringComparison.OrdinalIgnoreCase) || string.Equals(s, "ReconciliationRequired", StringComparison.OrdinalIgnoreCase))
            {
                filteredQuery = filteredQuery.Where(p => p.ReconciliationStatus == "RequiresRefund" || p.ReconciliationStatus == "ReconciliationRequired" || p.ReconciliationStatus == "ManualInterventionRequired");
            }
        }

        if (!string.IsNullOrWhiteSpace(filter.PaymentMethod))
        {
            var m = filter.PaymentMethod.Trim();
            if (string.Equals(m, "Card", StringComparison.OrdinalIgnoreCase))
            {
                filteredQuery = filteredQuery.Where(p => p.PaymentMethodType == "Card" || (p.PaymentMethodDetail != null && (p.PaymentMethodDetail.Contains("Card") || p.PaymentMethodDetail.Contains("Visa") || p.PaymentMethodDetail.Contains("Mastercard") || p.PaymentMethodDetail.Contains("Meeza"))));
            }
            else if (string.Equals(m, "MobileWallet", StringComparison.OrdinalIgnoreCase) || string.Equals(m, "Wallet", StringComparison.OrdinalIgnoreCase))
            {
                filteredQuery = filteredQuery.Where(p => p.PaymentMethodType == "MobileWallet" || (p.PaymentMethodDetail != null && (p.PaymentMethodDetail.Contains("Cash") || p.PaymentMethodDetail.Contains("Wallet") || p.PaymentMethodDetail.Contains("Vodafone") || p.PaymentMethodDetail.Contains("Orange") || p.PaymentMethodDetail.Contains("Etisalat") || p.PaymentMethodDetail.Contains("WE"))));
            }
        }

        if (filter.FromDate.HasValue)
        {
            var fromUtc = filter.FromDate.Value.Date;
            filteredQuery = filteredQuery.Where(p => p.CreatedAt >= fromUtc);
        }

        if (filter.ToDate.HasValue)
        {
            var toUtc = filter.ToDate.Value.Date.AddDays(1).AddTicks(-1);
            filteredQuery = filteredQuery.Where(p => p.CreatedAt <= toUtc);
        }

        int totalTxnCount = await filteredQuery.CountAsync(cancellationToken);
        int page = filter.Page < 1 ? 1 : filter.Page;
        int pageSize = filter.PageSize < 1 ? 15 : (filter.PageSize > 100 ? 100 : filter.PageSize);

        var pagedItems = await filteredQuery
            .OrderByDescending(p => p.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new PaymentHistoryItemDto
            {
                Id = p.Id,
                AppointmentId = p.AppointmentId,
                MerchantReference = p.MerchantReference,
                ProviderTransactionId = p.ProviderTransactionId,
                ProviderOrderId = p.ProviderOrderId,
                AmountPaid = (decimal)p.AmountCents / 100m,
                OriginalAmount = p.Appointment.ConsultationFee,
                DiscountAmount = p.DiscountAmount,
                PromoCode = p.PromoCode,
                Currency = p.Currency ?? "EGP",
                Status = p.Status,
                DisplayStatus = p.Status,
                IsRefunded = p.Appointment.PaymentStatus == PaymentStatus.Refunded || p.ReconciliationStatus == "Refunded" || p.Status == "Refunded" || p.Refunds.Any(r => r.Status == "Succeeded"),
                RefundStatus = (p.Appointment.PaymentStatus == PaymentStatus.Refunded || p.ReconciliationStatus == "Refunded" || p.Status == "Refunded" || (p.Refunds.Any(r => r.Status == "Succeeded") && p.Refunds.Where(r => r.Status == "Succeeded").Sum(r => (long?)r.AmountCents) >= p.AmountCents)) ? "Refunded" : (p.Refunds.Any(r => r.Status == "Succeeded") ? "Partially Refunded" : null),
                RefundedAmount = (decimal)(p.Refunds.Where(r => r.Status == "Succeeded").Sum(r => (long?)r.AmountCents) ?? 0L) / 100m,
                RemainingRefundableAmount = Math.Max(0m, (decimal)(p.AmountCents - (p.Refunds.Where(r => r.Status != "Rejected" && r.Status != "Failed").Sum(r => (long?)r.AmountCents) ?? 0L)) / 100m),
                CanRequestRefund = (p.Status == "Succeeded" || p.Status == "PartiallyRefunded") &&
                                   (p.AmountCents - (p.Refunds.Where(r => r.Status != "Rejected" && r.Status != "Failed").Sum(r => (long?)r.AmountCents) ?? 0L)) > 0,
                Refunds = p.Refunds
                    .Where(r => r.Status == "Succeeded" || r.Status == "Processing" || r.Status == "UnderReview")
                    .OrderByDescending(r => r.RequestedAtUtc)
                    .Select(r => new PatientRefundDto
                    {
                        RefundId = r.Id,
                        AuditReference = r.AuditReference,
                        AmountEgp = (decimal)r.AmountCents / 100m,
                        Status = r.Status,
                        ProcessedAtUtc = r.ProcessedAtUtc
                    }).ToList(),
                ReconciliationStatus = p.ReconciliationStatus,
                ReconciliationReason = p.ReconciliationReason,
                PaymentMethodType = p.PaymentMethodType ?? "Card",
                PaymentMethodDetail = p.PaymentMethodDetail ?? (p.PaymentMethodType == "MobileWallet" ? "Mobile Wallet" : "Payment Card"),
                DoctorName = "Dr. " + (p.Appointment.Doctor.User.FullName ?? "Physician"),
                Specialization = p.Appointment.Doctor.Specialization.Name ?? "General Practice",
                AppointmentDate = p.Appointment.AppointmentDate,
                StartTime = p.Appointment.StartTime,
                EndTime = p.Appointment.EndTime,
                CreatedAtUtc = p.CreatedAt,
                CompletedAtUtc = p.CompletedAt,
                CanViewReceipt = p.Status == "Succeeded" || p.Appointment.PaymentStatus == PaymentStatus.Paid || p.Appointment.PaymentStatus == PaymentStatus.Refunded,
                TermsVersion = p.TermsVersion
            })
            .ToListAsync(cancellationToken);

        var pagedResult = new PagedResult<PaymentHistoryItemDto>(pagedItems, totalTxnCount, page, pageSize);

        var dto = new AdminFinancialDashboardDto
        {
            GrossSuccessfulPayments = (decimal)totalGrossCents / 100m,
            TotalRefundedAmount = (decimal)totalRefundedCents / 100m,
            SucceededCount = succeededCount,
            FailedCount = failedCount,
            PendingCount = pendingCount,
            RequiresRefundCount = requiresRefundCount,
            CardGrossRevenue = (decimal)cardGrossCents / 100m,
            CardTransactionCount = cardCount,
            WalletGrossRevenue = (decimal)walletGrossCents / 100m,
            WalletTransactionCount = walletCount,
            RevenueTimeSeries = timeSeries,
            PagedTransactions = pagedResult,
            ActiveFilter = filter,
            ReconciliationQueue = reconciliationItems
        };

        return Result<AdminFinancialDashboardDto>.Success(dto);
    }

    public async Task<Result> ResolveReconciliationAsync(
        AdminResolveReconciliationDto dto,
        string adminUserId,
        CancellationToken cancellationToken = default)
    {
        if (dto == null || dto.AttemptId <= 0)
        {
            return Result.Failure("Valid transaction attempt ID is required.");
        }

        if (string.IsNullOrWhiteSpace(dto.Resolution))
        {
            return Result.Failure("Reconciliation resolution decision (Refund, Rebook, or Dismiss) is required.");
        }

        if (string.IsNullOrWhiteSpace(dto.Notes) || dto.Notes.Trim().Length < 5)
        {
            return Result.Failure("Administrative audit notes (minimum 5 characters) explaining the financial decision are mandatory.");
        }

        if (_db == null)
        {
            return Result.Failure("Database context is required for reconciliation operations.");
        }

        var attempt = await _db.PaymentAttempts
            .Include(p => p.Appointment)
            .FirstOrDefaultAsync(p => p.Id == dto.AttemptId, cancellationToken);

        if (attempt == null)
        {
            return Result.Failure("Payment transaction record not found.");
        }

        var resolution = dto.Resolution.Trim();
        var notes = dto.Notes.Trim();
        var nowUtc = DateTime.UtcNow;

        _logger.LogInformation("Security Audit: Admin {AdminId} resolving reconciliation for PaymentAttempt {AttemptId} to '{Resolution}' with notes: {Notes}",
            adminUserId, resolution, attempt.Id, notes);

        if (string.Equals(resolution, "Refund", StringComparison.OrdinalIgnoreCase))
        {
            attempt.ReconciliationStatus = "Refunded";
            attempt.Status = "Refunded";
            attempt.ReconciliationReason = $"[Admin Refund by {adminUserId} on {nowUtc:yyyy-MM-dd HH:mm} UTC]: {notes}";
            attempt.ReconciledAt = nowUtc;
            attempt.UpdatedAt = nowUtc;

            if (attempt.Appointment != null)
            {
                attempt.Appointment.PaymentStatus = PaymentStatus.Refunded;
                attempt.Appointment.Notes = (attempt.Appointment.Notes ?? "") + $"\n[REFUNDED BY ADMIN {adminUserId} on {nowUtc:yyyy-MM-dd HH:mm} UTC]: {notes}";
            }
        }
        else if (string.Equals(resolution, "Rebook", StringComparison.OrdinalIgnoreCase))
        {
            attempt.ReconciliationStatus = "Rebooked";
            attempt.ReconciliationReason = $"[Rebooking Credit Approved by {adminUserId} on {nowUtc:yyyy-MM-dd HH:mm} UTC]: {notes}";
            attempt.ReconciledAt = nowUtc;
            attempt.UpdatedAt = nowUtc;

            if (attempt.Appointment != null)
            {
                attempt.Appointment.Notes = (attempt.Appointment.Notes ?? "") + $"\n[REBOOKING CREDIT APPROVED BY ADMIN {adminUserId} on {nowUtc:yyyy-MM-dd HH:mm} UTC]: {notes}";
            }
        }
        else if (string.Equals(resolution, "Dismiss", StringComparison.OrdinalIgnoreCase) || string.Equals(resolution, "Resolved", StringComparison.OrdinalIgnoreCase))
        {
            attempt.ReconciliationStatus = "Resolved";
            attempt.ReconciliationReason = $"[Reconciliation Dismissed/Resolved by {adminUserId} on {nowUtc:yyyy-MM-dd HH:mm} UTC]: {notes}";
            attempt.ReconciledAt = nowUtc;
            attempt.UpdatedAt = nowUtc;
        }
        else
        {
            return Result.Failure($"Unsupported reconciliation resolution '{dto.Resolution}'. Supported options: Refund, Rebook, Dismiss.");
        }

        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

