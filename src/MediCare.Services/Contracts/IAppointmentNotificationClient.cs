using MediCare.Services.DTOs;

namespace MediCare.Services.Contracts;

/// <summary>
/// Strongly-typed SignalR client interface for real-time notifications and calendar sync.
/// </summary>
public interface IAppointmentNotificationClient
{
    Task ReceiveNotification(NotificationDto notification);
    Task UnreadCountChanged(int unreadCount);
    Task AppointmentStatusChanged(int appointmentId, string status);
    Task SlotAvailabilityChanged(int doctorId, DateTime date);
}
