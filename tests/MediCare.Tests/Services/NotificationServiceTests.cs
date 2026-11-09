using FluentAssertions;
using MediCare.Data.Entities;
using MediCare.Data.Repositories;
using MediCare.Data.UnitOfWork;
using MediCare.Services.Contracts;
using MediCare.Services.DTOs;
using MediCare.Services.Factories;
using MediCare.Services.Implementations;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace MediCare.Tests.Services;

public class NotificationServiceTests
{
    private readonly Mock<IUnitOfWork> _uowMock;
    private readonly Mock<IRepository<Notification>> _notificationRepoMock;
    private readonly Mock<IRealtimeNotifier> _realtimeNotifierMock;
    private readonly Mock<ILogger<NotificationService>> _loggerMock;
    private readonly NotificationFactory _factory;
    private readonly NotificationService _service;

    public NotificationServiceTests()
    {
        _uowMock = new Mock<IUnitOfWork>();
        _notificationRepoMock = new Mock<IRepository<Notification>>();
        _realtimeNotifierMock = new Mock<IRealtimeNotifier>();
        _loggerMock = new Mock<ILogger<NotificationService>>();
        _factory = new NotificationFactory();

        _uowMock.Setup(u => u.Notifications).Returns(_notificationRepoMock.Object);

        _service = new NotificationService(
            _uowMock.Object,
            _factory,
            _realtimeNotifierMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task SendNotificationAsync_ShouldPersistToDatabaseFirst_BeforeInvokingRealtimeNotifier()
    {
        // Arrange
        var executionSequence = new List<string>();

        _notificationRepoMock
            .Setup(r => r.AddAsync(It.IsAny<Notification>()))
            .Returns(Task.CompletedTask)
            .Callback(() => executionSequence.Add("DB_AddAsync"));

        _uowMock
            .Setup(u => u.CommitAsync())
            .ReturnsAsync(1)
            .Callback(() => executionSequence.Add("DB_CommitAsync"));

        _realtimeNotifierMock
            .Setup(n => n.PushNotificationToUserAsync("user_123", It.IsAny<NotificationDto>()))
            .Returns(Task.CompletedTask)
            .Callback(() => executionSequence.Add("Realtime_Push"));

        // Act
        var result = await _service.SendNotificationAsync("user_123", "Alert Title", "Alert Body");

        // Assert: Verify strict persist-first execution order
        result.IsSuccess.Should().BeTrue();
        executionSequence.Should().ContainInOrder("DB_AddAsync", "DB_CommitAsync", "Realtime_Push");
    }

    [Fact]
    public async Task MarkAsReadAsync_ShouldSucceed_WhenNotificationBelongsToUser()
    {
        // Arrange
        var notification = new Notification
        {
            Id = 42,
            UserId = "user_123",
            Title = "Appointment Booked",
            Message = "You have an appointment",
            IsRead = false
        };

        _notificationRepoMock.Setup(r => r.GetByIdAsync(42)).ReturnsAsync(notification);
        _uowMock.Setup(u => u.CommitAsync()).ReturnsAsync(1);

        // Act
        var result = await _service.MarkAsReadAsync(42, "user_123");

        // Assert
        result.IsSuccess.Should().BeTrue();
        notification.IsRead.Should().BeTrue();
        _notificationRepoMock.Verify(r => r.Update(notification), Times.Once);
        _uowMock.Verify(u => u.CommitAsync(), Times.Once);
    }

    [Fact]
    public async Task MarkAsReadAsync_ShouldFail_WhenNotificationBelongsToAnotherUser()
    {
        // Arrange: Notification belongs to user_owner, but user_attacker tries to mark it
        var notification = new Notification
        {
            Id = 42,
            UserId = "user_owner",
            Title = "Confidential Alert",
            IsRead = false
        };

        _notificationRepoMock.Setup(r => r.GetByIdAsync(42)).ReturnsAsync(notification);

        // Act
        var result = await _service.MarkAsReadAsync(42, "user_attacker");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Access denied");
        notification.IsRead.Should().BeFalse();
        _uowMock.Verify(u => u.CommitAsync(), Times.Never);
    }
}
