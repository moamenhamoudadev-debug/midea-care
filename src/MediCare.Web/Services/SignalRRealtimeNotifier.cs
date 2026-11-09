using MediCare.Services.Contracts;
using MediCare.Services.DTOs;
using MediCare.Web.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace MediCare.Web.Services;

public class SignalRRealtimeNotifier : IRealtimeNotifier
{
    private readonly IHubContext<AppointmentHub, IAppointmentNotificationClient> _hubContext;

    public SignalRRealtimeNotifier(IHubContext<AppointmentHub, IAppointmentNotificationClient> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task PushNotificationToUserAsync(string userId, NotificationDto notification)
    {
        await _hubContext.Clients.Group($"user_{userId}").ReceiveNotification(notification);
        await _hubContext.Clients.User(userId).ReceiveNotification(notification);
    }

    public async Task PushNotificationToAdminsAsync(NotificationDto notification)
    {
        await _hubContext.Clients.Group("admins").ReceiveNotification(notification);
    }

    public async Task PushUnreadCountToUserAsync(string userId, int unreadCount)
    {
        await _hubContext.Clients.Group($"user_{userId}").UnreadCountChanged(unreadCount);
        await _hubContext.Clients.User(userId).UnreadCountChanged(unreadCount);
    }

    public async Task NotifyStatusChangedAsync(string userId, int appointmentId, string status)
    {
        await _hubContext.Clients.Group($"user_{userId}").AppointmentStatusChanged(appointmentId, status);
        await _hubContext.Clients.User(userId).AppointmentStatusChanged(appointmentId, status);
    }

    public async Task NotifySlotChangedAsync(int doctorId, DateTime date)
    {
        await _hubContext.Clients.All.SlotAvailabilityChanged(doctorId, date);
    }
}
