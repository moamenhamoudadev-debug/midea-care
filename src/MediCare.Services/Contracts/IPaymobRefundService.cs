using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediCare.Services.DTOs;

namespace MediCare.Services.Contracts;

public interface IPaymobRefundService
{
    Task<RefundResultDto> RequestRefundAsync(CreateRefundRequestDto request, string adminUserId, CancellationToken cancellationToken = default);
    Task<RefundResultDto> ApproveAndExecuteRefundAsync(int refundId, string approvingAdminId, string? note = null, CancellationToken cancellationToken = default);
    Task<RefundResultDto> RejectRefundAsync(int refundId, string rejectingAdminId, string reason, CancellationToken cancellationToken = default);
    Task<List<AdminRefundQueueItemDto>> GetRefundQueueAsync(string? statusFilter = null, string? currentAdminId = null, CancellationToken cancellationToken = default);
    Task<decimal> GetRemainingRefundableAmountAsync(int paymentAttemptId, CancellationToken cancellationToken = default);
}
