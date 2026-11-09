using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediCare.Data.Context;
using MediCare.Data.Entities;
using MediCare.Data.Enums;
using MediCare.Services.Common;
using MediCare.Services.Contracts;
using MediCare.Services.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MediCare.Services.Implementations;

public sealed class PaymobCheckoutService : IPaymobCheckoutService
{
    private readonly ApplicationDbContext _db;
    private readonly HttpClient _http;
    private readonly PaymobSettings _settings;
    private readonly IPaymentService _paymentService;
    private readonly INotificationService? _notificationService;
    private readonly ILogger<PaymobCheckoutService> _logger;

    public PaymobCheckoutService(
        ApplicationDbContext db,
        HttpClient http,
        IOptions<PaymobSettings> settings,
        IPaymentService paymentService,
        ILogger<PaymobCheckoutService> logger)
        : this(db, http, settings, paymentService, null, logger)
    {
    }

    [Microsoft.Extensions.DependencyInjection.ActivatorUtilitiesConstructor]
    public PaymobCheckoutService(
        ApplicationDbContext db,
        HttpClient http,
        IOptions<PaymobSettings> settings,
        IPaymentService paymentService,
        INotificationService? notificationService,
        ILogger<PaymobCheckoutService> logger)
    {
        _db = db;
        _http = http;
        _settings = settings.Value;
        _paymentService = paymentService;
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task<Result<PaymentCheckoutSessionDto>> CreateCheckoutAsync(int appointmentId, string userId, string? promoCode, string? termsVersion = null, CancellationToken cancellationToken = default)
    {
        if (appointmentId <= 0) return Result<PaymentCheckoutSessionDto>.Failure("Valid appointment ID is required.");
        if (!IsConfigured()) return Result<PaymentCheckoutSessionDto>.Failure("Paymob test checkout is not configured. Add Paymob test credentials and a public callback URL.");

        var appointment = await _db.Appointments.Include(x => x.Patient).ThenInclude(x => x.User).FirstOrDefaultAsync(x => x.Id == appointmentId, cancellationToken);
        if (appointment == null) return Result<PaymentCheckoutSessionDto>.Failure("Appointment not found.");
        if (!string.Equals(appointment.Patient.UserId, userId, StringComparison.Ordinal)) return Result<PaymentCheckoutSessionDto>.Failure("Forbidden: You are not authorized to pay for this appointment.");
        if (appointment.Status is AppointmentStatus.Cancelled or AppointmentStatus.Rejected) return Result<PaymentCheckoutSessionDto>.Failure("Cannot pay for a cancelled or rejected appointment.");
        if (appointment.PaymentStatus == PaymentStatus.Paid) return Result<PaymentCheckoutSessionDto>.Failure("This appointment has already been paid.");

        var nowUtc = DateTime.UtcNow;
        var staleAttempts = await _db.PaymentAttempts
            .Where(x => x.PatientId == appointment.PatientId && x.Status == "Pending" && x.ExpiresAt <= nowUtc)
            .ToListAsync(cancellationToken);
        foreach (var stale in staleAttempts)
        {
            stale.Status = "Expired";
            stale.CompletedAt = nowUtc;
        }
        if (staleAttempts.Count > 0) await _db.SaveChangesAsync(cancellationToken);

        var discount = 0m;
        string? normalizedPromo = null;
        if (!string.IsNullOrWhiteSpace(promoCode))
        {
            var promo = await _paymentService.ValidatePromoCodeAsync(promoCode, appointment.ConsultationFee, userId);
            if (!promo.IsSuccess || promo.Value is not { IsValid: true }) return Result<PaymentCheckoutSessionDto>.Failure(promo.Error ?? "Invalid promo code.");
            discount = promo.Value.DiscountAmount;
            normalizedPromo = promo.Value.Code;
        }

        var user = appointment.Patient.User;
        if (string.IsNullOrWhiteSpace(user.Email) || string.IsNullOrWhiteSpace(user.PhoneNumber))
            return Result<PaymentCheckoutSessionDto>.Failure("Add an email address and phone number to your account before checkout.");

        var amount = Math.Max(0m, appointment.ConsultationFee - discount);
        if (amount <= 0m) return Result<PaymentCheckoutSessionDto>.Failure("The payable amount must be greater than zero for hosted checkout.");

        var pendingAttempt = await _db.PaymentAttempts.FirstOrDefaultAsync(x => x.AppointmentId == appointment.Id && x.Status == "Pending", cancellationToken);
        if (pendingAttempt != null && pendingAttempt.ExpiresAt > nowUtc)
        {
            var requestedAmountCents = checked((long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero));
            if (pendingAttempt.AmountCents != requestedAmountCents || pendingAttempt.DiscountAmount != discount ||
                !string.Equals(pendingAttempt.PromoCode, normalizedPromo, StringComparison.Ordinal))
                return Result<PaymentCheckoutSessionDto>.Failure("A different promo or amount is attached to the current checkout session. Finish it or wait for it to expire before changing the payment.");
            if (!string.IsNullOrWhiteSpace(pendingAttempt.CheckoutUrl))
                return Result<PaymentCheckoutSessionDto>.Success(new PaymentCheckoutSessionDto { CheckoutUrl = pendingAttempt.CheckoutUrl, MerchantReference = pendingAttempt.MerchantReference });
            return Result<PaymentCheckoutSessionDto>.Failure("A payment session is already being prepared. Please retry in a few minutes.");
        }
        if (pendingAttempt != null)
        {
            pendingAttempt.Status = "Expired";
            pendingAttempt.CompletedAt = nowUtc;
            await _db.SaveChangesAsync(cancellationToken);
        }

        var reference = $"mc-{appointment.Id}-{Guid.NewGuid():N}";
        var attempt = new PaymentAttempt
        {
            AppointmentId = appointment.Id,
            PatientId = appointment.PatientId,
            MerchantReference = reference,
            AmountCents = checked((long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero)),
            DiscountAmount = discount,
            PromoCode = normalizedPromo,
            Currency = "EGP",
            TermsVersion = PaymentTermsConstants.ActiveVersion,
            TermsAcceptedAt = nowUtc,
            ExpiresAt = nowUtc.AddMinutes(30)
        };
        _db.PaymentAttempts.Add(attempt);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _db.Entry(attempt).State = EntityState.Detached;
            return Result<PaymentCheckoutSessionDto>.Failure("A payment session is already being prepared for this appointment. Please retry shortly.");
        }

        try
        {
            var fullName = user.FullName.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            var callback = $"{_settings.PublicBaseUrl.TrimEnd('/')}/api/payment/paymob/webhook";
            var returnUrl = $"{_settings.PublicBaseUrl.TrimEnd('/')}/api/payment/paymob/return";
            var paymentMethods = new List<int>();
            if (int.TryParse(_settings.IntegrationId, NumberStyles.None, CultureInfo.InvariantCulture, out var cardIntegrationId))
            {
                paymentMethods.Add(cardIntegrationId);
            }
            if (_settings.EnableMobileWallets &&
                !string.IsNullOrWhiteSpace(_settings.WalletIntegrationId) &&
                int.TryParse(_settings.WalletIntegrationId, NumberStyles.None, CultureInfo.InvariantCulture, out var walletIntegrationId))
            {
                paymentMethods.Add(walletIntegrationId);
            }
            if (paymentMethods.Count == 0)
            {
                return Result<PaymentCheckoutSessionDto>.Failure("No valid payment methods configured.");
            }
            var requestBody = new
            {
                amount = attempt.AmountCents,
                currency = "EGP",
                payment_methods = paymentMethods.ToArray(),
                items = new[] { new { name = $"MediCare appointment #{appointment.Id}", amount = attempt.AmountCents, description = "Clinic consultation", quantity = 1 } },
                billing_data = new
                {
                    apartment = "NA", first_name = fullName.FirstOrDefault() ?? "Patient", last_name = fullName.Length > 1 ? fullName[1] : "Patient",
                    street = "NA", building = "NA", phone_number = user.PhoneNumber, city = "Cairo", country = "EG",
                    email = user.Email, floor = "NA", state = "Cairo", postal_code = "NA", extra_description = "MediCare consultation"
                },
                special_reference = reference,
                expiration = 1800,
                notification_url = callback,
                redirection_url = returnUrl,
                extras = new { appointmentId = appointment.Id }
            };
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://accept.paymob.com/v1/intention/");
            request.Headers.Authorization = new AuthenticationHeaderValue("Token", _settings.SecretKey);
            request.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            using var response = await _http.SendAsync(request, cancellationToken);
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                if ((int)response.StatusCode is >= 400 and < 500)
                {
                    attempt.Status = "Failed";
                    attempt.CompletedAt = DateTime.UtcNow;
                    await _db.SaveChangesAsync(CancellationToken.None);
                }
                throw new InvalidOperationException($"Paymob returned HTTP {(int)response.StatusCode}.");
            }
            using var json = JsonDocument.Parse(responseText);
            if (!json.RootElement.TryGetProperty("client_secret", out var secretElement) || string.IsNullOrWhiteSpace(secretElement.GetString()))
                throw new InvalidOperationException("Paymob did not return a checkout client secret.");
            if (!json.RootElement.TryGetProperty("intention_order_id", out var intentionOrderId))
                throw new InvalidOperationException("Paymob did not return an order ID for callback correlation.");

            var checkoutUrl = "https://accept.paymob.com/unifiedcheckout/?publicKey=" + Uri.EscapeDataString(_settings.PublicKey) + "&clientSecret=" + Uri.EscapeDataString(secretElement.GetString()!);
            attempt.ProviderOrderId = intentionOrderId.ToString();
            attempt.CheckoutUrl = checkoutUrl;
            await _db.SaveChangesAsync(cancellationToken);
            return Result<PaymentCheckoutSessionDto>.Success(new PaymentCheckoutSessionDto { CheckoutUrl = checkoutUrl, MerchantReference = reference });
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or JsonException or FormatException or DbUpdateException)
        {
            // Keep the pre-created attempt pending after network, 5xx, or parse errors: Paymob may have accepted the intention
            // even if our response was lost, and a callback may still arrive.
            _logger.LogWarning("Paymob checkout setup did not complete for merchant reference {MerchantReference}; attempt remains reconcilable. Error type: {ErrorType}", reference, ex.GetType().Name);
            return Result<PaymentCheckoutSessionDto>.Failure("Could not start Paymob checkout. Please try again.");
        }
    }

    public async Task<bool> ProcessWebhookAsync(JsonElement payload, string? hmac, CancellationToken cancellationToken = default)
    {
        if (payload.ValueKind != JsonValueKind.Object) return false;
        if (string.IsNullOrWhiteSpace(hmac) && payload.TryGetProperty("hmac", out var bodyHmac)) hmac = bodyHmac.GetString();
        if (string.IsNullOrWhiteSpace(_settings.HmacSecret) || !TryGetTransaction(payload, out var transaction) || !VerifyTransactionHmac(transaction, _settings.HmacSecret, hmac)) return false;

        // This endpoint acknowledges authenticated refund/void events but does not implement or alter refund state.
        if (IsTrue(transaction, "is_refund") || IsTrue(transaction, "is_void") || IsTrue(transaction, "is_refunded") || IsTrue(transaction, "is_voided")) return true;
        if (IsTrue(transaction, "is_auth") && !IsTrue(transaction, "is_capture")) return true;

        if (!transaction.TryGetProperty("integration_id", out var integrationElement) ||
            !integrationElement.TryGetInt32(out var callbackIntegration)) return false;

        var isConfiguredCard = int.TryParse(_settings.IntegrationId, NumberStyles.None, CultureInfo.InvariantCulture, out var configuredIntegration) && callbackIntegration == configuredIntegration;
        var isConfiguredWallet = _settings.EnableMobileWallets &&
                                 !string.IsNullOrWhiteSpace(_settings.WalletIntegrationId) &&
                                 int.TryParse(_settings.WalletIntegrationId, NumberStyles.None, CultureInfo.InvariantCulture, out var configuredWallet) &&
                                 callbackIntegration == configuredWallet;

        if (!isConfiguredCard && !isConfiguredWallet) return false;

        if (!transaction.TryGetProperty("currency", out var currencyElement) || currencyElement.GetString() != "EGP" ||
            !transaction.TryGetProperty("amount_cents", out var amountElement) || !amountElement.TryGetInt64(out var amountCents) ||
            !transaction.TryGetProperty("id", out var transactionIdElement)) return false;

        if (!transaction.TryGetProperty("order", out var order) || order.ValueKind != JsonValueKind.Object || !order.TryGetProperty("merchant_order_id", out var refElement)) return false;
        var reference = refElement.GetString();
        var providerTransactionId = transactionIdElement.ToString();
        if (string.IsNullOrWhiteSpace(reference) || string.IsNullOrWhiteSpace(providerTransactionId)) return false;
        if (!order.TryGetProperty("id", out var signedOrderId)) return false;
        var providerOrderId = signedOrderId.ToString();

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
        var attempt = await _db.PaymentAttempts.Include(x => x.Appointment).ThenInclude(x => x.Patient)
            .FirstOrDefaultAsync(x => x.ProviderOrderId == providerOrderId, cancellationToken);
        if (attempt == null || attempt.MerchantReference != reference || attempt.AmountCents != amountCents || attempt.Currency != "EGP") return false;
        if (attempt.Status is "Succeeded" or "Failed") return attempt.ProviderTransactionId == providerTransactionId;
        if (attempt.Status != "Pending" || attempt.Appointment.PaymentStatus == PaymentStatus.Paid) return false;

        var (methodType, methodDetail) = ParsePaymentMethod(transaction);

        if (!transaction.TryGetProperty("success", out var successElement) || !transaction.TryGetProperty("pending", out var pendingElement)) return false;
        if (pendingElement.ValueKind == JsonValueKind.True)
        {
            if (_notificationService != null && attempt.Appointment?.Patient != null)
            {
                try
                {
                    var amountEgp = attempt.AmountCents / 100m;
                    await _notificationService.NotifyPaymentEventAsync(
                        attempt.Appointment.Patient.UserId,
                        "PaymentPending",
                        "Payment Pending Verification",
                        $"Your payment of {amountEgp:N2} EGP via {methodDetail} is pending confirmation from Paymob.",
                        attempt.Id,
                        attempt.AppointmentId,
                        providerTransactionId,
                        $"/Appointments/Details/{attempt.AppointmentId}",
                        cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to send pending payment notification for attempt {AttemptId}", attempt.Id);
                }
            }
            return true;
        }

        if (successElement.ValueKind != JsonValueKind.True)
        {
            attempt.Status = "Failed";
            attempt.PaymentMethodType = methodType;
            attempt.PaymentMethodDetail = methodDetail;
            attempt.ReconciliationStatus = "None";
            attempt.ProviderTransactionId = providerTransactionId;
            attempt.ProviderOrderId = order.TryGetProperty("id", out var failedOrderId) ? failedOrderId.ToString() : null;
            attempt.CompletedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            if (_notificationService != null && attempt.Appointment?.Patient != null)
            {
                try
                {
                    var amountEgp = attempt.AmountCents / 100m;
                    await _notificationService.NotifyPaymentEventAsync(
                        attempt.Appointment.Patient.UserId,
                        "PaymentFailed",
                        "Payment Declined",
                        $"Your payment attempt of {amountEgp:N2} EGP via {methodDetail} was unsuccessful or declined.",
                        attempt.Id,
                        attempt.AppointmentId,
                        providerTransactionId,
                        $"/Appointments/Details/{attempt.AppointmentId}",
                        cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to send payment failure notification for attempt {AttemptId}", attempt.Id);
                }
            }

            return true;
        }

        attempt.Status = "Succeeded";
        attempt.PaymentMethodType = methodType;
        attempt.PaymentMethodDetail = methodDetail;
        attempt.ProviderTransactionId = providerTransactionId;
        attempt.ProviderOrderId = providerOrderId;
        attempt.CompletedAt = DateTime.UtcNow;
        attempt.Appointment.PaymentStatus = PaymentStatus.Paid;
        attempt.Appointment.PaidAmount = attempt.AmountCents / 100m;
        attempt.Appointment.DiscountAmount = attempt.DiscountAmount;
        attempt.Appointment.PromoCode = attempt.PromoCode;
        attempt.Appointment.TransactionReference = providerTransactionId;
        attempt.Appointment.PaymentProvider = $"Paymob Test ({methodDetail})";
        attempt.Appointment.PaidAt = attempt.CompletedAt;

        // If appointment was cancelled or rejected prior to payment callback arrival, preserve cancellation state
        // and record structured financial reconciliation flags
        if (attempt.Appointment.Status is AppointmentStatus.Cancelled or AppointmentStatus.Rejected)
        {
            attempt.ReconciliationStatus = "RequiresRefund";
            attempt.ReconciliationReason = "Payment received post-cancellation; requires administrative refund or rebooking";
            _logger.LogWarning("Financial reconciliation alert: Payment of {AmountCents} cents succeeded for already {Status} appointment {ApptId}. ProviderTxn: {TxnId}. Marked as RequiresRefund.",
                attempt.AmountCents, attempt.Appointment.Status, attempt.AppointmentId, providerTransactionId);
            const string reconciliationFlag = "[PAYMENT RECEIVED POST-CANCELLATION: Appointment was cancelled prior to payment completion. Requires administrative refund or rebooking.]";
            attempt.Appointment.Notes = string.IsNullOrWhiteSpace(attempt.Appointment.Notes)
                ? reconciliationFlag
                : $"{attempt.Appointment.Notes}\n{reconciliationFlag}";
        }
        else
        {
            attempt.ReconciliationStatus = "None";
        }

        await _db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        if (_notificationService != null && attempt.Appointment?.Patient != null)
        {
            var amountEgp = attempt.AmountCents / 100m;
            var patientUserId = attempt.Appointment.Patient.UserId;

            if (attempt.ReconciliationStatus == "RequiresRefund")
            {
                // Reconciliation Required Alert for patient
                try
                {
                    await _notificationService.NotifyPaymentEventAsync(
                        patientUserId,
                        "ReconciliationAlert",
                        "Payment Under Administrative Review",
                        $"Payment of {amountEgp:N2} EGP for appointment #{attempt.AppointmentId} was received after cancellation. Our clinic staff is reviewing it for reconciliation or refund.",
                        attempt.Id,
                        attempt.AppointmentId,
                        providerTransactionId,
                        "/Payment/History",
                        cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to send reconciliation notification for attempt {AttemptId}", attempt.Id);
                }

                // Administrative Financial Alert
                try
                {
                    await _notificationService.NotifyAdminFinancialAlertAsync(
                        "PaymentReconciliationAlert",
                        "Reconciliation Alert: Post-Cancellation Payment",
                        $"Post-cancellation payment of {amountEgp:N2} EGP received for appointment #{attempt.AppointmentId} (Txn: {providerTransactionId}). Status marked as RequiresRefund.",
                        attempt.Id,
                        null,
                        $"AdminReconciliation_Attempt_{attempt.Id}",
                        "/Admin/Payments",
                        cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to send admin financial alert for attempt {AttemptId}", attempt.Id);
                }
            }
            else
            {
                // Normal Payment Success
                try
                {
                    await _notificationService.NotifyPaymentEventAsync(
                        patientUserId,
                        "PaymentSuccess",
                        "Payment Successful",
                        $"Payment of {amountEgp:N2} EGP for appointment #{attempt.AppointmentId} via {methodDetail} succeeded. Ref: {providerTransactionId}.",
                        attempt.Id,
                        attempt.AppointmentId,
                        providerTransactionId,
                        $"/Payment/Receipt/{attempt.AppointmentId}",
                        cancellationToken);

                    await _notificationService.PublishNotificationAsync(new CreateNotificationRequestDto
                    {
                        UserId = patientUserId,
                        Title = "Appointment Confirmed",
                        Message = $"Your appointment #{attempt.AppointmentId} on {attempt.Appointment.AppointmentDate:yyyy-MM-dd} at {attempt.Appointment.StartTime:hh\\:mm} is now confirmed.",
                        Type = "AppointmentConfirmed",
                        AppointmentId = attempt.AppointmentId,
                        PaymentAttemptId = attempt.Id,
                        EventReference = $"ApptConfirmed_Paid_{attempt.AppointmentId}",
                        ActionUrl = $"/Appointments/Details/{attempt.AppointmentId}"
                    }, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to dispatch payment success notifications for attempt {AttemptId}", attempt.Id);
                }
            }
        }

        return true;
    }

    public static (string MethodType, string MethodDetail) ParsePaymentMethod(JsonElement transaction)
    {
        string methodType = "Card";
        string methodDetail = "Bank Card";

        if (transaction.TryGetProperty("source_data", out var sourceData) && sourceData.ValueKind == JsonValueKind.Object)
        {
            var rawType = sourceData.TryGetProperty("type", out var typeEl) ? typeEl.GetString()?.ToLowerInvariant() : null;
            var rawSubType = sourceData.TryGetProperty("sub_type", out var subTypeEl) ? subTypeEl.GetString() : null;
            var pan = sourceData.TryGetProperty("pan", out var panEl) ? panEl.GetString() : null;

            if (rawType is "wallet" or "mobile_wallet")
            {
                methodType = "MobileWallet";
                var sub = rawSubType?.ToLowerInvariant() ?? string.Empty;
                if (sub.Contains("vodafone")) methodDetail = "Vodafone Cash";
                else if (sub.Contains("orange")) methodDetail = "Orange Cash";
                else if (sub.Contains("etisalat") || sub.Contains("e&") || sub.Contains("eand")) methodDetail = "e& money";
                else if (sub.Contains("we")) methodDetail = "WE Pay";
                else if (!string.IsNullOrWhiteSpace(rawSubType)) methodDetail = rawSubType;
                else methodDetail = "Egyptian Mobile Wallet";

                if (!string.IsNullOrWhiteSpace(pan))
                {
                    methodDetail = $"{methodDetail} ({pan})";
                }
            }
            else
            {
                methodType = "Card";
                var sub = rawSubType?.ToLowerInvariant() ?? string.Empty;
                if (sub is "vi" || sub.Contains("visa")) methodDetail = "Visa";
                else if (sub is "mc" || sub.Contains("master")) methodDetail = "MasterCard";
                else if (sub.Contains("meeza") || (pan != null && (pan.StartsWith("50780", StringComparison.Ordinal) || pan.StartsWith("50781", StringComparison.Ordinal)))) methodDetail = "Meeza Card";
                else if (!string.IsNullOrWhiteSpace(rawSubType)) methodDetail = rawSubType;
                else methodDetail = "Bank Card";

                if (!string.IsNullOrWhiteSpace(pan))
                {
                    var last4 = pan.Length >= 4 ? pan[^4..] : pan;
                    methodDetail = $"{methodDetail} •••• {last4}";
                }
            }
        }

        return (methodType, methodDetail);
    }

    public static bool VerifyTransactionHmac(JsonElement transaction, string secret, string? suppliedHmac)
    {
        if (transaction.ValueKind != JsonValueKind.Object || string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(suppliedHmac)) return false;
        try
        {
            string[] fields = ["amount_cents", "created_at", "currency", "error_occured", "has_parent_transaction", "id", "integration_id", "is_3d_secure", "is_auth", "is_capture", "is_refunded", "is_standalone_payment", "is_voided", "order.id", "owner", "pending", "source_data.pan", "source_data.sub_type", "source_data.type", "success"];
            var values = new StringBuilder();
            foreach (var field in fields)
            {
                JsonElement value;
                if (field == "order.id")
                {
                    if (!transaction.TryGetProperty("order", out var order) || order.ValueKind != JsonValueKind.Object || !order.TryGetProperty("id", out value)) return false;
                }
                else if (field.StartsWith("source_data.", StringComparison.Ordinal))
                {
                    if (!transaction.TryGetProperty("source_data", out var source) || source.ValueKind != JsonValueKind.Object || !source.TryGetProperty(field[12..], out value)) return false;
                }
                else if (!transaction.TryGetProperty(field, out value)) return false;
                values.Append(JsonScalar(value));
            }
            var computed = Convert.ToHexString(HMACSHA512.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(values.ToString()))).ToLowerInvariant();
            return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(computed), Encoding.ASCII.GetBytes(suppliedHmac.ToLowerInvariant()));
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException)
        {
            return false;
        }
    }

    private bool IsConfigured() =>
        (_settings.SecretKey.StartsWith("sk_test_", StringComparison.Ordinal) || _settings.SecretKey.StartsWith("egy_sk_test_", StringComparison.Ordinal)) &&
        !string.IsNullOrWhiteSpace(_settings.PublicKey) &&
        _settings.PublicKey.StartsWith("egy_pk_test_", StringComparison.Ordinal) &&
        !string.IsNullOrWhiteSpace(_settings.HmacSecret) &&
        int.TryParse(_settings.IntegrationId, NumberStyles.None, CultureInfo.InvariantCulture, out _) &&
        Uri.TryCreate(_settings.PublicBaseUrl, UriKind.Absolute, out var url) &&
        url.Scheme == Uri.UriSchemeHttps && !url.IsLoopback && url.AbsolutePath == "/" &&
        string.IsNullOrEmpty(url.Query) && string.IsNullOrEmpty(url.Fragment);

    private static bool TryGetTransaction(JsonElement payload, out JsonElement transaction)
    {
        transaction = default;
        return payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("obj", out transaction) && transaction.ValueKind == JsonValueKind.Object;
    }

    private static bool IsTrue(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;

    private static string JsonScalar(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.Null => string.Empty,
        _ => throw new JsonException("Unexpected Paymob HMAC field type.")
    };
}
