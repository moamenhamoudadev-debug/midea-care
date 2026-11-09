using MediCare.Services.Common;
using MediCare.Services.DTOs;

namespace MediCare.Services.Contracts;

public interface INotificationService
{
    // Legacy / direct notification queries & commands
    Task<Result<List<NotificationDto>>> GetUnreadNotificationsAsync(string userId);
    Task<Result> MarkAsReadAsync(int notificationId, string currentUserId);
    Task<Result> SendNotificationAsync(string userId, string title, string message);
    Task NotifySlotAvailabilityChangedAsync(int doctorId, DateTime date);
    Task NotifyAppointmentStatusChangedAsync(int appointmentId, string newStatus, string targetUserId);

    // Enterprise Phase 8 real-time & persistent notification pipeline
    Task<Result<NotificationDto>> PublishNotificationAsync(CreateNotificationRequestDto request, CancellationToken cancellationToken = default);
    Task<Result<NotificationPagedResultDto>> GetUserNotificationsPagedAsync(string userId, NotificationFilterDto filter, CancellationToken cancellationToken = default);
    Task<Result<int>> GetUnreadCountAsync(string userId, CancellationToken cancellationToken = default);
    Task<Result> MarkAllAsReadAsync(string userId, CancellationToken cancellationToken = default);

    // Domain event specific notification helpers
    Task<Result<NotificationDto>> NotifyPaymentEventAsync(
        string userId,
        string eventType,
        string title,
        string message,
        int paymentAttemptId,
        int? appointmentId,
        string? transactionRef,
        string? actionUrl,
        CancellationToken cancellationToken = default);

    Task<Result<NotificationDto>> NotifyRefundEventAsync(
        string userId,
        string eventType,
        string title,
        string message,
        int refundId,
        int paymentAttemptId,
        string? auditRef,
        string? actionUrl,
        CancellationToken cancellationToken = default);

    Task<Result<NotificationDto>> NotifyAdminFinancialAlertAsync(
        string alertType,
        string title,
        string message,
        int? paymentAttemptId,
        int? refundId,
        string? eventRef,
        string? actionUrl,
        CancellationToken cancellationToken = default);
}
