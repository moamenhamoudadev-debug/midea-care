using MediCare.Data.Entities;
using MediCare.Services.DTOs;

namespace MediCare.Services.Factories;

public class NotificationFactory
{
    public Notification CreateNotification(string userId, string title, string message)
    {
        return new Notification
        {
            UserId = userId,
            Title = title,
            Message = message,
            Type = "General",
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        };
    }

    public Notification CreateNotification(CreateNotificationRequestDto request)
    {
        return new Notification
        {
            UserId = request.UserId,
            Title = request.Title,
            Message = request.Message,
            Type = string.IsNullOrWhiteSpace(request.Type) ? "General" : request.Type,
            PaymentAttemptId = request.PaymentAttemptId,
            RefundId = request.RefundId,
            AppointmentId = request.AppointmentId,
            EventReference = request.EventReference,
            ActionUrl = request.ActionUrl,
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        };
    }

    public NotificationDto CreatePayload(Notification entity)
    {
        return new NotificationDto
        {
            Id = entity.Id,
            UserId = entity.UserId,
            Title = entity.Title,
            Message = entity.Message,
            Type = entity.Type ?? "General",
            PaymentAttemptId = entity.PaymentAttemptId,
            RefundId = entity.RefundId,
            AppointmentId = entity.AppointmentId,
            EventReference = entity.EventReference,
            ActionUrl = entity.ActionUrl,
            CreatedAt = entity.CreatedAt,
            ReadAtUtc = entity.ReadAtUtc,
            IsRead = entity.IsRead
        };
    }
}
