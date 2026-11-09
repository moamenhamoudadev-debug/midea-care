using MediCare.Services.DTOs;

namespace MediCare.Services.Contracts;

public interface IRealtimeNotifier
{
    Task PushNotificationToUserAsync(string userId, NotificationDto notification);
    Task PushNotificationToAdminsAsync(NotificationDto notification);
    Task PushUnreadCountToUserAsync(string userId, int unreadCount);
    Task NotifyStatusChangedAsync(string userId, int appointmentId, string status);
    Task NotifySlotChangedAsync(int doctorId, DateTime date);
}
