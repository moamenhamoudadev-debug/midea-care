using System.Text.Json;
using MediCare.Data.Entities;
using MediCare.Data.UnitOfWork;
using MediCare.Services.Common;
using MediCare.Services.Contracts;
using MediCare.Services.DTOs;
using MediCare.Services.Factories;
using Microsoft.Extensions.Logging;

namespace MediCare.Services.Implementations;

/// <summary>
/// Service managing enterprise user notifications.
/// Enforces database persistence first before real-time SignalR or email dispatch,
/// maintains an Outbox for transactional durability, and provides idempotent event deduplication.
/// </summary>
public class NotificationService : INotificationService
{
    private readonly IUnitOfWork _uow;
    private readonly NotificationFactory _factory;
    private readonly IRealtimeNotifier _realtimeNotifier;
    private readonly IEmailService? _emailService;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        IUnitOfWork uow,
        NotificationFactory factory,
        IRealtimeNotifier realtimeNotifier,
        ILogger<NotificationService> logger)
        : this(uow, factory, realtimeNotifier, null, logger)
    {
    }

    public NotificationService(
        IUnitOfWork uow,
        NotificationFactory factory,
        IRealtimeNotifier realtimeNotifier,
        IEmailService? emailService,
        ILogger<NotificationService> logger)
    {
        _uow = uow;
        _factory = factory;
        _realtimeNotifier = realtimeNotifier;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task<Result<List<NotificationDto>>> GetUnreadNotificationsAsync(string userId)
    {
        var entities = (await _uow.Notifications.FindAsync(n => n.UserId == userId && !n.IsRead))
            .OrderByDescending(n => n.CreatedAt)
            .Select(n => _factory.CreatePayload(n))
            .ToList();

        return Result<List<NotificationDto>>.Success(entities);
    }

    public async Task<Result> MarkAsReadAsync(int notificationId, string currentUserId)
    {
        var notification = await _uow.Notifications.GetByIdAsync(notificationId);
        if (notification == null)
        {
            return Result.Failure("Notification not found.");
        }

        if (notification.UserId != currentUserId)
        {
            _logger.LogWarning("Security violation: User {CurrentUserId} attempted to mark notification {NotificationId} owned by {OwnerId}",
                currentUserId, notificationId, notification.UserId);
            return Result.Failure("Access denied: You do not own this notification.");
        }

        notification.IsRead = true;
        notification.ReadAtUtc = DateTime.UtcNow;
        _uow.Notifications.Update(notification);
        await _uow.CommitAsync();

        // Push updated unread counter
        try
        {
            var unreadCount = (await _uow.Notifications.FindAsync(n => n.UserId == currentUserId && !n.IsRead)).Count();
            await _realtimeNotifier.PushUnreadCountToUserAsync(currentUserId, unreadCount);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to push unread counter update to user {UserId}", currentUserId);
        }

        return Result.Success();
    }

    public async Task<Result> MarkAllAsReadAsync(string userId, CancellationToken cancellationToken = default)
    {
        var unreadNotifications = (await _uow.Notifications.FindAsync(n => n.UserId == userId && !n.IsRead)).ToList();
        if (unreadNotifications.Count == 0)
        {
            return Result.Success();
        }

        var utcNow = DateTime.UtcNow;
        foreach (var notif in unreadNotifications)
        {
            notif.IsRead = true;
            notif.ReadAtUtc = utcNow;
            _uow.Notifications.Update(notif);
        }

        await _uow.CommitAsync();

        try
        {
            await _realtimeNotifier.PushUnreadCountToUserAsync(userId, 0);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to push 0 unread counter to user {UserId}", userId);
        }

        return Result.Success();
    }

    public async Task<Result<int>> GetUnreadCountAsync(string userId, CancellationToken cancellationToken = default)
    {
        var count = (await _uow.Notifications.FindAsync(n => n.UserId == userId && !n.IsRead)).Count();
        return Result<int>.Success(count);
    }

    public async Task<Result<NotificationPagedResultDto>> GetUserNotificationsPagedAsync(
        string userId,
        NotificationFilterDto filter,
        CancellationToken cancellationToken = default)
    {
        var allUserNotifications = (await _uow.Notifications.FindAsync(n => n.UserId == userId)).ToList();

        var query = allUserNotifications.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(filter.Type))
        {
            query = query.Where(n => string.Equals(n.Type, filter.Type, StringComparison.OrdinalIgnoreCase));
        }

        if (filter.IsRead.HasValue)
        {
            query = query.Where(n => n.IsRead == filter.IsRead.Value);
        }

        var totalItems = query.Count();
        var unreadCount = allUserNotifications.Count(n => !n.IsRead);

        var pageNumber = Math.Max(1, filter.PageNumber);
        var pageSize = Math.Clamp(filter.PageSize, 1, 50);

        var items = query
            .OrderByDescending(n => n.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(n => _factory.CreatePayload(n))
            .ToList();

        var result = new NotificationPagedResultDto
        {
            Items = items,
            TotalItems = totalItems,
            PageNumber = pageNumber,
            PageSize = pageSize,
            UnreadCount = unreadCount
        };

        return Result<NotificationPagedResultDto>.Success(result);
    }

    public async Task<Result> SendNotificationAsync(string userId, string title, string message)
    {
        try
        {
            // 1. Persist to database FIRST
            var entity = _factory.CreateNotification(userId, title, message);
            await _uow.Notifications.AddAsync(entity);
            await _uow.CommitAsync();

            // 2. Push through real-time notifier
            var payload = _factory.CreatePayload(entity);
            await _realtimeNotifier.PushNotificationToUserAsync(userId, payload);

            // 3. Outbox tracking if available
            if (_uow.NotificationOutbox != null)
            {
                try
                {
                    var outbox = new NotificationOutbox
                    {
                        NotificationId = entity.Id,
                        UserId = userId,
                        Channel = "SignalR",
                        Status = "Dispatched",
                        PayloadJson = JsonSerializer.Serialize(payload),
                        ProcessedAtUtc = DateTime.UtcNow,
                        CreatedAt = DateTime.UtcNow
                    };
                    await _uow.NotificationOutbox.AddAsync(outbox);
                    await _uow.CommitAsync();
                }
                catch
                {
                    // Fail-safe
                }
            }

            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist and dispatch notification to user {UserId}", userId);
            return Result.Failure("Failed to deliver notification.");
        }
    }

    public async Task<Result<NotificationDto>> PublishNotificationAsync(
        CreateNotificationRequestDto request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // 1. Idempotency Check: if EventReference already exists for this UserId, do not duplicate
            if (!string.IsNullOrWhiteSpace(request.EventReference))
            {
                var existing = (await _uow.Notifications.FindAsync(n => n.UserId == request.UserId && n.EventReference == request.EventReference))
                    .FirstOrDefault();

                if (existing != null)
                {
                    _logger.LogInformation("Idempotent notification deduplicated. EventReference {EventRef} already exists for user {UserId}",
                        request.EventReference, request.UserId);
                    return Result<NotificationDto>.Success(_factory.CreatePayload(existing));
                }
            }

            // 2. Persist Notification to database FIRST
            var entity = _factory.CreateNotification(request);
            await _uow.Notifications.AddAsync(entity);
            await _uow.CommitAsync();

            var payload = _factory.CreatePayload(entity);

            // 3. Enqueue SignalR Outbox item (if Outbox repository is available)
            NotificationOutbox? signalROutbox = null;
            if (_uow.NotificationOutbox != null)
            {
                signalROutbox = new NotificationOutbox
                {
                    NotificationId = entity.Id,
                    UserId = request.UserId,
                    Channel = request.UserId == "admins" ? "SignalRAdmin" : "SignalR",
                    Status = "Pending",
                    PayloadJson = JsonSerializer.Serialize(payload),
                    CreatedAt = DateTime.UtcNow
                };
                await _uow.NotificationOutbox.AddAsync(signalROutbox);
            }

            // 4. Optionally Enqueue Email Outbox item
            NotificationOutbox? emailOutbox = null;
            if (_uow.NotificationOutbox != null && request.SendEmail && !string.IsNullOrWhiteSpace(request.RecipientEmail))
            {
                emailOutbox = new NotificationOutbox
                {
                    NotificationId = entity.Id,
                    UserId = request.UserId,
                    Channel = "Email",
                    RecipientEmail = request.RecipientEmail,
                    Subject = string.IsNullOrWhiteSpace(request.EmailSubject) ? request.Title : request.EmailSubject,
                    PayloadJson = request.Message,
                    Status = "Pending",
                    CreatedAt = DateTime.UtcNow
                };
                await _uow.NotificationOutbox.AddAsync(emailOutbox);
            }

            if (_uow.NotificationOutbox != null)
            {
                await _uow.CommitAsync();
            }

            // 5. Attempt Immediate Dispatch (Fail-safe: errors do not reverse DB persistence)
            try
            {
                if (request.UserId == "admins")
                {
                    await _realtimeNotifier.PushNotificationToAdminsAsync(payload);
                }
                else
                {
                    await _realtimeNotifier.PushNotificationToUserAsync(request.UserId, payload);
                    var unread = (await _uow.Notifications.FindAsync(n => n.UserId == request.UserId && !n.IsRead)).Count();
                    await _realtimeNotifier.PushUnreadCountToUserAsync(request.UserId, unread);
                }

                if (signalROutbox != null && _uow.NotificationOutbox != null)
                {
                    signalROutbox.Status = "Dispatched";
                    signalROutbox.ProcessedAtUtc = DateTime.UtcNow;
                    _uow.NotificationOutbox.Update(signalROutbox);
                    await _uow.CommitAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Immediate SignalR dispatch delayed for notification {Id}; Outbox worker will retry", entity.Id);
                if (signalROutbox != null && _uow.NotificationOutbox != null)
                {
                    signalROutbox.LastError = ex.Message;
                    _uow.NotificationOutbox.Update(signalROutbox);
                    await _uow.CommitAsync();
                }
            }

            // 6. Attempt Immediate Email Dispatch if requested (Non-blocking: email failures must NEVER fail operation)
            if (emailOutbox != null && _emailService != null && _uow.NotificationOutbox != null)
            {
                try
                {
                    var emailSent = await _emailService.SendEmailAsync(
                        emailOutbox.RecipientEmail!,
                        emailOutbox.Subject!,
                        $"<div style='font-family:sans-serif;padding:20px;line-height:1.6;color:#333;'>" +
                        $"<h2 style='color:#0d6efd;'>{System.Net.WebUtility.HtmlEncode(request.Title)}</h2>" +
                        $"<p>{System.Net.WebUtility.HtmlEncode(request.Message)}</p>" +
                        (string.IsNullOrWhiteSpace(request.ActionUrl) ? "" : $"<p><a href='{request.ActionUrl}' style='display:inline-block;padding:10px 18px;background-color:#0d6efd;color:#fff;text-decoration:none;border-radius:4px;'>View in MediCare</a></p>") +
                        $"<hr style='border:none;border-top:1px solid #eee;margin:20px 0;'/>" +
                        $"<small style='color:#888;'>This is an automated notification from MediCare Clinic Management System.</small>" +
                        $"</div>");

                    if (emailSent)
                    {
                        emailOutbox.Status = "Dispatched";
                        emailOutbox.ProcessedAtUtc = DateTime.UtcNow;
                    }
                    else
                    {
                        emailOutbox.RetryCount = 1;
                        emailOutbox.LastError = "Email service returned false";
                    }

                    _uow.NotificationOutbox.Update(emailOutbox);
                    await _uow.CommitAsync();
                }
                catch (Exception emailEx)
                {
                    _logger.LogWarning(emailEx, "Immediate email dispatch failed for notification {Id}; Outbox worker will retry", entity.Id);
                    emailOutbox.LastError = emailEx.Message;
                    emailOutbox.RetryCount = 1;
                    _uow.NotificationOutbox.Update(emailOutbox);
                    await _uow.CommitAsync();
                }
            }

            return Result<NotificationDto>.Success(payload);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist and publish notification to user {UserId}", request.UserId);
            return Result<NotificationDto>.Failure("Failed to deliver notification.");
        }
    }

    public async Task<Result<NotificationDto>> NotifyPaymentEventAsync(
        string userId,
        string eventType,
        string title,
        string message,
        int paymentAttemptId,
        int? appointmentId,
        string? transactionRef,
        string? actionUrl,
        CancellationToken cancellationToken = default)
    {
        var idempotencyKey = $"Pay_{eventType}_{paymentAttemptId}";
        var request = new CreateNotificationRequestDto
        {
            UserId = userId,
            Title = title,
            Message = message,
            Type = eventType,
            PaymentAttemptId = paymentAttemptId,
            AppointmentId = appointmentId,
            EventReference = idempotencyKey,
            ActionUrl = actionUrl ?? (appointmentId.HasValue ? $"/Payment/Receipt/{appointmentId.Value}" : "/Payment/History")
        };

        return await PublishNotificationAsync(request, cancellationToken);
    }

    public async Task<Result<NotificationDto>> NotifyRefundEventAsync(
        string userId,
        string eventType,
        string title,
        string message,
        int refundId,
        int paymentAttemptId,
        string? auditRef,
        string? actionUrl,
        CancellationToken cancellationToken = default)
    {
        var idempotencyKey = $"Ref_{eventType}_{refundId}";
        var request = new CreateNotificationRequestDto
        {
            UserId = userId,
            Title = title,
            Message = message,
            Type = eventType,
            RefundId = refundId,
            PaymentAttemptId = paymentAttemptId,
            EventReference = idempotencyKey,
            ActionUrl = actionUrl ?? "/Payment/History"
        };

        return await PublishNotificationAsync(request, cancellationToken);
    }

    public async Task<Result<NotificationDto>> NotifyAdminFinancialAlertAsync(
        string alertType,
        string title,
        string message,
        int? paymentAttemptId,
        int? refundId,
        string? eventRef,
        string? actionUrl,
        CancellationToken cancellationToken = default)
    {
        var idempotencyKey = eventRef ?? $"AdminAlert_{alertType}_{paymentAttemptId ?? refundId ?? 0}_{DateTime.UtcNow:yyyyMMddHHmmss}";
        var request = new CreateNotificationRequestDto
        {
            UserId = "admins",
            Title = title,
            Message = message,
            Type = alertType,
            PaymentAttemptId = paymentAttemptId,
            RefundId = refundId,
            EventReference = idempotencyKey,
            ActionUrl = actionUrl ?? "/Admin/Payments"
        };

        return await PublishNotificationAsync(request, cancellationToken);
    }

    public async Task NotifySlotAvailabilityChangedAsync(int doctorId, DateTime date)
    {
        try
        {
            await _realtimeNotifier.NotifySlotChangedAsync(doctorId, date);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error broadcasting slot change for doctor {DoctorId}", doctorId);
        }
    }

    public async Task NotifyAppointmentStatusChangedAsync(int appointmentId, string newStatus, string targetUserId)
    {
        try
        {
            await _realtimeNotifier.NotifyStatusChangedAsync(targetUserId, appointmentId, newStatus);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error broadcasting status change for appointment {AppointmentId}", appointmentId);
        }
    }
}
