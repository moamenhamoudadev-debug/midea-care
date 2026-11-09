using MediCare.Services.Common;
using MediCare.Services.DTOs;

namespace MediCare.Services.Contracts;

public interface IPaymentService
{
    Task<Result<PaymentReceiptDto>> GetReceiptAsync(int appointmentId, string currentUserId);
    Task<Result<PromoCodeValidationDto>> ValidatePromoCodeAsync(string code, decimal currentAmount);
    Task<Result<PromoCodeValidationDto>> ValidatePromoCodeAsync(string code, decimal currentAmount, string? currentUserId);
    Task<Result<PaymentHistoryViewModel>> GetPaymentHistoryAsync(PaymentHistoryFilterDto filter, string currentUserId, bool isAdmin = false, CancellationToken cancellationToken = default);
    Task<Result<AdminFinancialDashboardDto>> GetAdminFinancialDashboardAsync(PaymentHistoryFilterDto filter, CancellationToken cancellationToken = default);
    Task<Result> ResolveReconciliationAsync(AdminResolveReconciliationDto dto, string adminUserId, CancellationToken cancellationToken = default);
}