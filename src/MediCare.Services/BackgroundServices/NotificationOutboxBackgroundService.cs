using System.Text.Json;
using MediCare.Data.UnitOfWork;
using MediCare.Services.Contracts;
using MediCare.Services.DTOs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MediCare.Services.BackgroundServices;

/// <summary>
/// Background worker implementing the Transactional Outbox pattern for notifications.
/// Guarantees at-least-once delivery for real-time SignalR notifications and emails,
/// ensuring offline resilience, transient fault retry, and zero notification loss.
/// </summary>
public class NotificationOutboxBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<NotificationOutboxBackgroundService> _logger;
    private readonly TimeSpan _checkInterval;

    public NotificationOutboxBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<NotificationOutboxBackgroundService> logger,
        TimeSpan? checkInterval = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        // Default check interval: 5 seconds
        _checkInterval = checkInterval ?? TimeSpan.FromSeconds(5);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("NotificationOutboxBackgroundService started. Interval: {Interval}", _checkInterval);

        // Warmup delay
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingOutboxAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in NotificationOutboxBackgroundService execution cycle");
            }

            try
            {
                await Task.Delay(_checkInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("NotificationOutboxBackgroundService stopped.");
    }

    public async Task<int> ProcessPendingOutboxAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var realtimeNotifier = scope.ServiceProvider.GetRequiredService<IRealtimeNotifier>();
        var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();

        var pendingItems = (await uow.NotificationOutbox.FindAsync(
            o => o.Status == "Pending" || (o.Status == "Failed" && o.RetryCount < o.MaxRetries)))
            .OrderBy(o => o.CreatedAt)
            .Take(50)
            .ToList();

        if (pendingItems.Count == 0)
        {
            return 0;
        }

        int processedCount = 0;

        foreach (var item in pendingItems)
        {
            if (cancellationToken.IsCancellationRequested) break;

            try
            {
                if (item.Channel == "SignalR")
                {
                    if (!string.IsNullOrWhiteSpace(item.PayloadJson))
                    {
                        var payload = JsonSerializer.Deserialize<NotificationDto>(item.PayloadJson);
                        if (payload != null)
                        {
                            await realtimeNotifier.PushNotificationToUserAsync(item.UserId, payload);
                            var unreadCount = (await uow.Notifications.FindAsync(n => n.UserId == item.UserId && !n.IsRead)).Count();
                            await realtimeNotifier.PushUnreadCountToUserAsync(item.UserId, unreadCount);
                        }
                    }

                    item.Status = "Dispatched";
                    item.ProcessedAtUtc = DateTime.UtcNow;
                    item.LastError = null;
                }
                else if (item.Channel == "SignalRAdmin")
                {
                    if (!string.IsNullOrWhiteSpace(item.PayloadJson))
                    {
                        var payload = JsonSerializer.Deserialize<NotificationDto>(item.PayloadJson);
                        if (payload != null)
                        {
                            await realtimeNotifier.PushNotificationToAdminsAsync(payload);
                        }
                    }

                    item.Status = "Dispatched";
                    item.ProcessedAtUtc = DateTime.UtcNow;
                    item.LastError = null;
                }
                else if (item.Channel == "Email")
                {
                    if (!string.IsNullOrWhiteSpace(item.RecipientEmail))
                    {
                        var htmlBody = $"<div style='font-family:sans-serif;padding:20px;line-height:1.6;color:#333;'>" +
                                       $"<h2 style='color:#0d6efd;'>{System.Net.WebUtility.HtmlEncode(item.Subject ?? "MediCare Notification")}</h2>" +
                                       $"<p>{System.Net.WebUtility.HtmlEncode(item.PayloadJson ?? "")}</p>" +
                                       $"<hr style='border:none;border-top:1px solid #eee;margin:20px 0;'/>" +
                                       $"<small style='color:#888;'>This is an automated notification from MediCare Clinic Management System.</small>" +
                                       $"</div>";

                        var sent = await emailService.SendEmailAsync(item.RecipientEmail, item.Subject ?? "Notification", htmlBody);
                        if (sent)
                        {
                            item.Status = "Dispatched";
                            item.ProcessedAtUtc = DateTime.UtcNow;
                            item.LastError = null;
                        }
                        else
                        {
                            item.RetryCount++;
                            item.LastError = "Email provider returned false";
                            if (item.RetryCount >= item.MaxRetries) item.Status = "Failed";
                        }
                    }
                    else
                    {
                        item.Status = "Failed";
                        item.LastError = "Recipient email missing";
                    }
                }
                else
                {
                    item.Status = "Failed";
                    item.LastError = $"Unknown channel: {item.Channel}";
                }

                uow.NotificationOutbox.Update(item);
                processedCount++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to process outbox item {Id} for user {UserId}", item.Id, item.UserId);
                item.RetryCount++;
                item.LastError = ex.Message;
                if (item.RetryCount >= item.MaxRetries)
                {
                    item.Status = "Failed";
                }
                uow.NotificationOutbox.Update(item);
            }
        }

        await uow.CommitAsync();
        return processedCount;
    }
}
