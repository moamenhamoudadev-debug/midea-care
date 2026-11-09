using System.Text.Json;
using MediCare.Services.Common;
using MediCare.Services.DTOs;

namespace MediCare.Services.Contracts;

public interface IPaymobCheckoutService
{
    Task<Result<PaymentCheckoutSessionDto>> CreateCheckoutAsync(int appointmentId, string userId, string? promoCode, string? termsVersion = null, CancellationToken cancellationToken = default);
    Task<bool> ProcessWebhookAsync(JsonElement payload, string? hmac, CancellationToken cancellationToken = default);
}
