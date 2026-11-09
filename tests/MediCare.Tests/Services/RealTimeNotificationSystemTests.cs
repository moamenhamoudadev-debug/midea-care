using System.Linq.Expressions;
using System.Text.Json;
using FluentAssertions;
using MediCare.Data.Entities;
using MediCare.Data.Repositories;
using MediCare.Data.UnitOfWork;
using MediCare.Services.BackgroundServices;
using MediCare.Services.Contracts;
using MediCare.Services.DTOs;
using MediCare.Services.Factories;
using MediCare.Services.Implementations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace MediCare.Tests.Services;

public class RealTimeNotificationSystemTests
{
    private readonly Mock<IUnitOfWork> _uowMock;
    private readonly Mock<IRepository<Notification>> _notificationRepoMock;
    private readonly Mock<IRepository<NotificationOutbox>> _outboxRepoMock;
    private readonly Mock<IRealtimeNotifier> _realtimeNotifierMock;
    private readonly Mock<IEmailService> _emailServiceMock;
    private readonly Mock<ILogger<NotificationService>> _loggerMock;
    private readonly NotificationFactory _factory;
    private readonly NotificationService _service;
    private readonly List<Notification> _inMemoryNotifications = new();
    private readonly List<NotificationOutbox> _inMemoryOutbox = new();

    public RealTimeNotificationSystemTests()
    {
        _uowMock = new Mock<IUnitOfWork>();
        _notificationRepoMock = new Mock<IRepository<Notification>>();
        _outboxRepoMock = new Mock<IRepository<NotificationOutbox>>();
        _realtimeNotifierMock = new Mock<IRealtimeNotifier>();
        _emailServiceMock = new Mock<IEmailService>();
        _loggerMock = new Mock<ILogger<NotificationService>>();
        _factory = new NotificationFactory();

        _uowMock.Setup(u => u.Notifications).Returns(_notificationRepoMock.Object);
        _uowMock.Setup(u => u.NotificationOutbox).Returns(_outboxRepoMock.Object);
        _uowMock.Setup(u => u.CommitAsync()).ReturnsAsync(1);

        _notificationRepoMock.Setup(r => r.AddAsync(It.IsAny<Notification>()))
            .Callback<Notification>(n =>
            {
                if (n.Id == 0) n.Id = _inMemoryNotifications.Count + 1;
                _inMemoryNotifications.Add(n);
            })
            .Returns(Task.CompletedTask);

        _notificationRepoMock.Setup(r => r.FindAsync(It.IsAny<Expression<Func<Notification, bool>>>()))
            .ReturnsAsync((Expression<Func<Notification, bool>> pred) =>
                _inMemoryNotifications.AsQueryable().Where(pred).ToList());

        _notificationRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<int>()))
            .ReturnsAsync((int id) => _inMemoryNotifications.FirstOrDefault(n => n.Id == id));

        _outboxRepoMock.Setup(r => r.AddAsync(It.IsAny<NotificationOutbox>()))
            .Callback<NotificationOutbox>(o =>
            {
                if (o.Id == 0) o.Id = _inMemoryOutbox.Count + 1;
                _inMemoryOutbox.Add(o);
            })
            .Returns(Task.CompletedTask);

        _outboxRepoMock.Setup(r => r.FindAsync(It.IsAny<Expression<Func<NotificationOutbox, bool>>>()))
            .ReturnsAsync((Expression<Func<NotificationOutbox, bool>> pred) =>
                _inMemoryOutbox.AsQueryable().Where(pred).ToList());

        _service = new NotificationService(
            _uowMock.Object,
            _factory,
            _realtimeNotifierMock.Object,
            _emailServiceMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task PublishNotificationAsync_ShouldPersistNotificationAndEnqueueOutbox_BeforeSignalRDispatch()
    {
        // Arrange
        var request = new CreateNotificationRequestDto
        {
            UserId = "user_patient_1",
            Title = "Payment Successful",
            Message = "Your payment of 500.00 EGP was successful.",
            Type = "PaymentSuccess",
            PaymentAttemptId = 101,
            AppointmentId = 55,
            EventReference = "Pay_PaymentSuccess_101",
            ActionUrl = "/Payment/Receipt/55"
        };

        // Act
        var result = await _service.PublishNotificationAsync(request);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Title.Should().Be("Payment Successful");
        result.Value.PaymentAttemptId.Should().Be(101);
        result.Value.AppointmentId.Should().Be(55);

        _inMemoryNotifications.Should().HaveCount(1);
        _inMemoryNotifications[0].Title.Should().Be("Payment Successful");
        _inMemoryNotifications[0].EventReference.Should().Be("Pay_PaymentSuccess_101");

        _inMemoryOutbox.Should().HaveCount(1);
        _inMemoryOutbox[0].Channel.Should().Be("SignalR");
        _inMemoryOutbox[0].Status.Should().Be("Dispatched");

        _realtimeNotifierMock.Verify(r => r.PushNotificationToUserAsync("user_patient_1", It.IsAny<NotificationDto>()), Times.Once);
        _realtimeNotifierMock.Verify(r => r.PushUnreadCountToUserAsync("user_patient_1", 1), Times.Once);
    }

    [Fact]
    public async Task PublishNotificationAsync_WithSameEventReference_IsIdempotentAndDoesNotDuplicate()
    {
        // Arrange
        var request = new CreateNotificationRequestDto
        {
            UserId = "user_patient_1",
            Title = "Payment Received",
            Message = "Payment callback received",
            Type = "PaymentSuccess",
            PaymentAttemptId = 102,
            EventReference = "Pay_PaymentSuccess_102"
        };

        // Act: First publish
        var firstResult = await _service.PublishNotificationAsync(request);

        // Act: Duplicate webhook / retried callback arrival
        var secondResult = await _service.PublishNotificationAsync(request);

        // Assert
        firstResult.IsSuccess.Should().BeTrue();
        secondResult.IsSuccess.Should().BeTrue();
        secondResult.Value!.Id.Should().Be(firstResult.Value!.Id);

        _inMemoryNotifications.Should().HaveCount(1, "Idempotency key must prevent second notification from being inserted into database");
    }

    [Fact]
    public async Task GetUserNotificationsPagedAsync_ShouldFilterByTypeAndReadStatus_WithCorrectPagination()
    {
        // Arrange
        _inMemoryNotifications.Add(new Notification { Id = 1, UserId = "user_1", Type = "PaymentSuccess", IsRead = false, Title = "N1", CreatedAt = DateTime.UtcNow.AddMinutes(-10) });
        _inMemoryNotifications.Add(new Notification { Id = 2, UserId = "user_1", Type = "AppointmentConfirmed", IsRead = true, Title = "N2", CreatedAt = DateTime.UtcNow.AddMinutes(-8) });
        _inMemoryNotifications.Add(new Notification { Id = 3, UserId = "user_1", Type = "PaymentSuccess", IsRead = false, Title = "N3", CreatedAt = DateTime.UtcNow.AddMinutes(-5) });
        _inMemoryNotifications.Add(new Notification { Id = 4, UserId = "user_2", Type = "PaymentSuccess", IsRead = false, Title = "Other User", CreatedAt = DateTime.UtcNow });

        var filter = new NotificationFilterDto
        {
            PageNumber = 1,
            PageSize = 10,
            Type = "PaymentSuccess",
            IsRead = false
        };

        // Act
        var result = await _service.GetUserNotificationsPagedAsync("user_1", filter);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().HaveCount(2);
        result.Value.TotalItems.Should().Be(2);
        result.Value.UnreadCount.Should().Be(2);
        result.Value.Items.All(i => i.Type == "PaymentSuccess" && !i.IsRead).Should().BeTrue();
    }

    [Fact]
    public async Task MarkAsReadAsync_ForOwnNotification_MarksReadAndPushesUpdatedUnreadCount()
    {
        // Arrange
        var notif = new Notification { Id = 10, UserId = "user_test", IsRead = false, Title = "Unread", Message = "Msg", CreatedAt = DateTime.UtcNow };
        _inMemoryNotifications.Add(notif);

        // Act
        var result = await _service.MarkAsReadAsync(10, "user_test");

        // Assert
        result.IsSuccess.Should().BeTrue();
        notif.IsRead.Should().BeTrue();
        notif.ReadAtUtc.Should().NotBeNull();
        _realtimeNotifierMock.Verify(r => r.PushUnreadCountToUserAsync("user_test", 0), Times.Once);
    }

    [Fact]
    public async Task MarkAsReadAsync_ForAnotherUsersNotification_FailsWithSecurityViolation_IDORPrevention()
    {
        // Arrange: Notification owned by victim_user
        var notif = new Notification { Id = 20, UserId = "victim_user", IsRead = false, Title = "Sensitive Notification", Message = "Private", CreatedAt = DateTime.UtcNow };
        _inMemoryNotifications.Add(notif);

        // Act: Attacker attempts to mark victim's notification as read
        var result = await _service.MarkAsReadAsync(20, "attacker_user");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Access denied");
        notif.IsRead.Should().BeFalse("Victim's notification must not be altered by unauthorized user");
    }

    [Fact]
    public async Task MarkAllAsReadAsync_MarksAllUnreadNotificationsAsRead_AndPushesZeroUnreadCount()
    {
        // Arrange
        var n1 = new Notification { Id = 31, UserId = "user_bulk", IsRead = false, Title = "N1", CreatedAt = DateTime.UtcNow };
        var n2 = new Notification { Id = 32, UserId = "user_bulk", IsRead = false, Title = "N2", CreatedAt = DateTime.UtcNow };
        var n3 = new Notification { Id = 33, UserId = "user_bulk", IsRead = true, Title = "N3", CreatedAt = DateTime.UtcNow };
        _inMemoryNotifications.AddRange(new[] { n1, n2, n3 });

        // Act
        var result = await _service.MarkAllAsReadAsync("user_bulk");

        // Assert
        result.IsSuccess.Should().BeTrue();
        n1.IsRead.Should().BeTrue();
        n2.IsRead.Should().BeTrue();
        n1.ReadAtUtc.Should().NotBeNull();
        n2.ReadAtUtc.Should().NotBeNull();
        _realtimeNotifierMock.Verify(r => r.PushUnreadCountToUserAsync("user_bulk", 0), Times.Once);
    }

    [Fact]
    public async Task NotifyPaymentEventAsync_CorrectlyPopulatesPaymentAttemptIdAndActionUrl()
    {
        // Act
        var result = await _service.NotifyPaymentEventAsync(
            "patient_45",
            "PaymentSuccess",
            "Payment Completed",
            "Your 350.00 EGP payment was confirmed.",
            450,
            88,
            "TXN_999888",
            "/Payment/Receipt/88");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.PaymentAttemptId.Should().Be(450);
        result.Value.AppointmentId.Should().Be(88);
        result.Value.ActionUrl.Should().Be("/Payment/Receipt/88");
        result.Value.EventReference.Should().Be("Pay_PaymentSuccess_450");
    }

    [Fact]
    public async Task NotifyRefundEventAsync_CorrectlyPopulatesRefundIdAndType()
    {
        // Act
        var result = await _service.NotifyRefundEventAsync(
            "patient_60",
            "RefundSucceeded",
            "Refund Processed",
            "Your refund of 250.00 EGP has been executed.",
            15,
            450,
            "REF-20261109-ABCDEF",
            "/Payment/History");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.RefundId.Should().Be(15);
        result.Value.PaymentAttemptId.Should().Be(450);
        result.Value.Type.Should().Be("RefundSucceeded");
        result.Value.EventReference.Should().Be("Ref_RefundSucceeded_15");
    }

    [Fact]
    public async Task NotifyAdminFinancialAlertAsync_SendsToAdminsGroupWithCorrectTypeAndAuditReference()
    {
        // Act
        var result = await _service.NotifyAdminFinancialAlertAsync(
            "HighValueRefundReview",
            "Maker-Checker Review Required",
            "Refund of 1,200.00 EGP exceeds threshold and requires checker authorization.",
            450,
            16,
            "AdminRefundReview_16",
            "/Admin/Payments");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.UserId.Should().Be("admins");
        result.Value.Type.Should().Be("HighValueRefundReview");
        result.Value.ActionUrl.Should().Be("/Admin/Payments");

        _realtimeNotifierMock.Verify(r => r.PushNotificationToAdminsAsync(It.IsAny<NotificationDto>()), Times.Once);
    }

    [Fact]
    public async Task NotificationOutboxBackgroundService_ProcessesPendingOutboxEntries_AndUpdatesStatusToDispatched()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(_uowMock.Object);
        services.AddSingleton(_realtimeNotifierMock.Object);
        services.AddSingleton(_emailServiceMock.Object);
        var provider = services.BuildServiceProvider();

        var scopeFactoryMock = new Mock<IServiceScopeFactory>();
        var scopeMock = new Mock<IServiceScope>();
        scopeMock.Setup(s => s.ServiceProvider).Returns(provider);
        scopeFactoryMock.Setup(f => f.CreateScope()).Returns(scopeMock.Object);

        var outboxLoggerMock = new Mock<ILogger<NotificationOutboxBackgroundService>>();
        var backgroundService = new NotificationOutboxBackgroundService(scopeFactoryMock.Object, outboxLoggerMock.Object);

        var payload = new NotificationDto { Id = 99, Title = "Queued Alert", UserId = "user_offline" };
        var outboxItem = new NotificationOutbox
        {
            Id = 1,
            NotificationId = 99,
            UserId = "user_offline",
            Channel = "SignalR",
            PayloadJson = JsonSerializer.Serialize(payload),
            Status = "Pending",
            CreatedAt = DateTime.UtcNow
        };
        _inMemoryOutbox.Add(outboxItem);

        // Act
        var processed = await backgroundService.ProcessPendingOutboxAsync(CancellationToken.None);

        // Assert
        processed.Should().Be(1);
        outboxItem.Status.Should().Be("Dispatched");
        outboxItem.ProcessedAtUtc.Should().NotBeNull();
        _realtimeNotifierMock.Verify(r => r.PushNotificationToUserAsync("user_offline", It.Is<NotificationDto>(p => p.Id == 99)), Times.Once);
    }

    [Fact]
    public async Task NotificationOutboxBackgroundService_WhenEmailProviderFails_IncrementsRetryCountWithoutCrashing()
    {
        // Arrange
        _emailServiceMock.Setup(e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(false);

        var services = new ServiceCollection();
        services.AddSingleton(_uowMock.Object);
        services.AddSingleton(_realtimeNotifierMock.Object);
        services.AddSingleton(_emailServiceMock.Object);
        var provider = services.BuildServiceProvider();

        var scopeFactoryMock = new Mock<IServiceScopeFactory>();
        var scopeMock = new Mock<IServiceScope>();
        scopeMock.Setup(s => s.ServiceProvider).Returns(provider);
        scopeFactoryMock.Setup(f => f.CreateScope()).Returns(scopeMock.Object);

        var outboxLoggerMock = new Mock<ILogger<NotificationOutboxBackgroundService>>();
        var backgroundService = new NotificationOutboxBackgroundService(scopeFactoryMock.Object, outboxLoggerMock.Object);

        var outboxItem = new NotificationOutbox
        {
            Id = 2,
            NotificationId = 100,
            UserId = "user_email_test",
            Channel = "Email",
            RecipientEmail = "patient@test.local",
            Subject = "Test Subject",
            PayloadJson = "Payment confirmed",
            Status = "Pending",
            RetryCount = 0,
            MaxRetries = 3,
            CreatedAt = DateTime.UtcNow
        };
        _inMemoryOutbox.Add(outboxItem);

        // Act
        var processed = await backgroundService.ProcessPendingOutboxAsync(CancellationToken.None);

        // Assert
        processed.Should().Be(1);
        outboxItem.RetryCount.Should().Be(1);
        outboxItem.Status.Should().Be("Pending", "Should remain pending for retry until max retries exceeded");
        outboxItem.LastError.Should().NotBeNullOrEmpty();
    }
}
