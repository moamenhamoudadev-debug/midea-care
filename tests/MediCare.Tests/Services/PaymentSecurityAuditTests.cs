using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using MediCare.Data.Context;
using MediCare.Data.Entities;
using MediCare.Data.Enums;
using MediCare.Data.Repositories;
using MediCare.Data.UnitOfWork;
using MediCare.Services.BackgroundServices;
using MediCare.Services.Common;
using MediCare.Services.Contracts;
using MediCare.Services.DTOs;
using MediCare.Services.Factories;
using MediCare.Services.Implementations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace MediCare.Tests.Services;

/// <summary>
/// Phase 9 Comprehensive Security, Concurrency, and Release Readiness Audit Test Suite.
/// Validates HMAC integrity, state transitions, concurrency locks, maker-checker segregation,
/// financial reconciliation calculations, and outbox failure resilience.
/// </summary>
public class PaymentSecurityAuditTests
{
    private const string HmacSecret = "super-secret-hmac-key-2026";
    private const string ValidTransactionJson = """
        {"amount_cents":50000,"created_at":"2026-11-09T12:00:00Z","currency":"EGP","error_occured":false,"has_parent_transaction":false,"id":987654321,"integration_id":888999,"is_3d_secure":true,"is_auth":false,"is_capture":false,"is_refunded":false,"is_standalone_payment":true,"is_voided":false,"order":{"id":112233},"owner":5544,"pending":false,"source_data":{"pan":"50780123","sub_type":"Meeza","type":"card"},"success":true}
        """;

    private static ApplicationDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public void HmacVerification_TamperedAmount_FailsVerification()
    {
        // Arrange
        using var validDoc = JsonDocument.Parse(ValidTransactionJson);
        string[] fields = ["amount_cents", "created_at", "currency", "error_occured", "has_parent_transaction", "id", "integration_id", "is_3d_secure", "is_auth", "is_capture", "is_refunded", "is_standalone_payment", "is_voided", "order.id", "owner", "pending", "source_data.pan", "source_data.sub_type", "source_data.type", "success"];
        var sb = new StringBuilder();
        sb.Append("500002026-11-09T12:00:00ZEGPfalsefalse987654321888999truefalsefalsefalsetruefalse1122335544false50780123Meezacardtrue");
        var validHmac = Convert.ToHexString(HMACSHA512.HashData(Encoding.UTF8.GetBytes(HmacSecret), Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();

        // Tampered payload with modified amount
        const string tamperedJson = """
            {"amount_cents":100,"created_at":"2026-11-09T12:00:00Z","currency":"EGP","error_occured":false,"has_parent_transaction":false,"id":987654321,"integration_id":888999,"is_3d_secure":true,"is_auth":false,"is_capture":false,"is_refunded":false,"is_standalone_payment":true,"is_voided":false,"order":{"id":112233},"owner":5544,"pending":false,"source_data":{"pan":"50780123","sub_type":"Meeza","type":"card"},"success":true}
            """;
        using var tamperedDoc = JsonDocument.Parse(tamperedJson);

        // Act & Assert
        PaymobCheckoutService.VerifyTransactionHmac(tamperedDoc.RootElement, HmacSecret, validHmac).Should().BeFalse(
            "An attacker modifying the amount_cents must fail HMAC verification");
    }

    [Fact]
    public void HmacVerification_EmptySecretOrSignature_ReturnsFalseSafely()
    {
        using var doc = JsonDocument.Parse(ValidTransactionJson);

        PaymobCheckoutService.VerifyTransactionHmac(doc.RootElement, "", "abc").Should().BeFalse();
        PaymobCheckoutService.VerifyTransactionHmac(doc.RootElement, HmacSecret, "").Should().BeFalse();
        PaymobCheckoutService.VerifyTransactionHmac(doc.RootElement, HmacSecret, null).Should().BeFalse();
    }

    [Fact]
    public async Task StateTransitions_FailedAttempt_CannotBeOverwrittenToSucceededByLateCallback()
    {
        // Arrange
        var db = CreateInMemoryDbContext();
        var patientUser = new ApplicationUser { Id = "u1", UserName = "patient1", FullName = "Patient 1" };
        var patient = new Patient { Id = 1, UserId = "u1", User = patientUser, EmergencyContact = "123" };
        var appt = new Appointment
        {
            Id = 10,
            PatientId = 1,
            Patient = patient,
            Status = AppointmentStatus.Confirmed,
            PaymentStatus = PaymentStatus.Unpaid
        };
        var attempt = new PaymentAttempt
        {
            Id = 1,
            PatientId = 1,
            AppointmentId = 10,
            Appointment = appt,
            MerchantReference = "REF-20261109-1",
            AmountCents = 35000,
            Currency = "EGP",
            Status = "Failed", // Already finalized as Failed
            ProviderOrderId = "ORD-999",
            ProviderTransactionId = "TXN-ORIGINAL-FAILED"
        };
        db.Appointments.Add(appt);
        db.PaymentAttempts.Add(attempt);
        await db.SaveChangesAsync();

        var paymentServiceMock = new Mock<IPaymentService>();
        var settings = Options.Create(new PaymobSettings
        {
            SecretKey = "egy_sk_test_1",
            PublicKey = "egy_pk_test_1",
            HmacSecret = HmacSecret,
            IntegrationId = "100",
            PublicBaseUrl = "https://medicare.example.com/"
        });

        var service = new PaymobCheckoutService(db, new HttpClient(), settings, paymentServiceMock.Object, NullLogger<PaymobCheckoutService>.Instance);

        var lateSuccessWebhook = JsonDocument.Parse("""
            {"obj":{"id":999999,"success":true,"pending":false,"amount_cents":35000,"currency":"EGP","order":{"id":"ORD-999","merchant_order_id":"REF-20261109-1"},"source_data":{"type":"card","sub_type":"vi","pan":"4242"}}}
            """).RootElement;

        // Act
        var result = await service.ProcessWebhookAsync(lateSuccessWebhook, null, CancellationToken.None);

        // Assert
        var reloaded = await db.PaymentAttempts.FindAsync(1);
        reloaded!.Status.Should().Be("Failed", "A finalized Failed attempt must remain immutable against late incoming webhooks");
        reloaded.Appointment.PaymentStatus.Should().Be(PaymentStatus.Unpaid);
    }

    [Fact]
    public async Task StateTransitions_SucceededAttempt_CannotBeOverwrittenToFailed()
    {
        // Arrange
        var db = CreateInMemoryDbContext();
        var patientUser = new ApplicationUser { Id = "u2", UserName = "patient2", FullName = "Patient 2" };
        var patient = new Patient { Id = 2, UserId = "u2", User = patientUser, EmergencyContact = "123" };
        var appt = new Appointment
        {
            Id = 20,
            PatientId = 2,
            Patient = patient,
            Status = AppointmentStatus.Confirmed,
            PaymentStatus = PaymentStatus.Paid
        };
        var attempt = new PaymentAttempt
        {
            Id = 2,
            PatientId = 2,
            AppointmentId = 20,
            Appointment = appt,
            MerchantReference = "REF-20261109-2",
            AmountCents = 40000,
            Currency = "EGP",
            Status = "Succeeded", // Already Succeeded
            ProviderOrderId = "ORD-888",
            ProviderTransactionId = "TXN-LEGIT-SUCCESS"
        };
        db.Appointments.Add(appt);
        db.PaymentAttempts.Add(attempt);
        await db.SaveChangesAsync();

        var settings = Options.Create(new PaymobSettings
        {
            SecretKey = "egy_sk_test_1",
            PublicKey = "egy_pk_test_1",
            HmacSecret = HmacSecret,
            IntegrationId = "100",
            PublicBaseUrl = "https://medicare.example.com/"
        });

        var service = new PaymobCheckoutService(db, new HttpClient(), settings, Mock.Of<IPaymentService>(), NullLogger<PaymobCheckoutService>.Instance);

        var lateFailureWebhook = JsonDocument.Parse("""
            {"obj":{"id":888888,"success":false,"pending":false,"amount_cents":40000,"currency":"EGP","order":{"id":"ORD-888","merchant_order_id":"REF-20261109-2"},"source_data":{"type":"card","sub_type":"vi","pan":"4242"}}}
            """).RootElement;

        // Act
        var result = await service.ProcessWebhookAsync(lateFailureWebhook, null, CancellationToken.None);

        // Assert
        var reloaded = await db.PaymentAttempts.FindAsync(2);
        reloaded!.Status.Should().Be("Succeeded", "A finalized Succeeded attempt must never be downgraded to Failed");
        reloaded.Appointment.PaymentStatus.Should().Be(PaymentStatus.Paid);
    }

    [Fact]
    public async Task RefundSecurity_DuplicateSubmission_RejectsExceedingRemainingBalance()
    {
        // Arrange
        var db = CreateInMemoryDbContext();
        var patientUser = new ApplicationUser { Id = "u3", UserName = "patient3", FullName = "Patient 3" };
        var patient = new Patient { Id = 3, UserId = "u3", User = patientUser, EmergencyContact = "123" };
        var appt = new Appointment { Id = 30, PatientId = 3, Patient = patient, PaymentStatus = PaymentStatus.Paid };
        var attempt = new PaymentAttempt
        {
            Id = 3,
            PatientId = 3,
            AppointmentId = 30,
            Appointment = appt,
            AmountCents = 20000, // 200 EGP total
            Currency = "EGP",
            Status = "Succeeded"
        };
        db.Appointments.Add(appt);
        db.PaymentAttempts.Add(attempt);
        await db.SaveChangesAsync();

        var settings = Options.Create(new PaymobSettings
        {
            SecretKey = "egy_sk_test_1",
            MakerCheckerThresholdEgp = 500m
        });

        var notifMock = new Mock<INotificationService>();
        var service = new PaymobRefundService(db, new HttpClient(), settings, notifMock.Object, NullLogger<PaymobRefundService>.Instance);

        // First partial refund of 150 EGP
        var firstReq = new CreateRefundRequestDto
        {
            PaymentAttemptId = 3,
            AmountEgp = 150m,
            IsFullRefund = false,
            Reason = "Patient requested partial adjustment"
        };

        // Seed an approved refund record of 150 EGP to simulate committed balance
        var existingRefund = new Refund
        {
            PaymentAttemptId = 3,
            AmountCents = 15000,
            Currency = "EGP",
            Status = "Approved",
            RequestedByAdminId = "maker-admin",
            AuditReference = "REF-20261109-EXISTING",
            Reason = "First refund"
        };
        db.Refunds.Add(existingRefund);
        await db.SaveChangesAsync();

        // Second refund of 80 EGP (150 + 80 = 230 > 200)
        var secondReq = new CreateRefundRequestDto
        {
            PaymentAttemptId = 3,
            AmountEgp = 80m,
            IsFullRefund = false,
            Reason = "Second refund request"
        };

        // Act
        var result = await service.RequestRefundAsync(secondReq, "maker-admin-2");

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("exceeds remaining refundable balance");
        result.RemainingRefundableEgp.Should().Be(50m, "Only 50 EGP (200 - 150) should remain refundable");
    }

    [Fact]
    public async Task RefundSecurity_MakerChecker_EnforcesDifferentAdminApproval()
    {
        // Arrange
        var db = CreateInMemoryDbContext();
        var patientUser = new ApplicationUser { Id = "u4", UserName = "patient4", FullName = "Patient 4" };
        var patient = new Patient { Id = 4, UserId = "u4", User = patientUser, EmergencyContact = "123" };
        var appt = new Appointment { Id = 40, PatientId = 4, Patient = patient, PaymentStatus = PaymentStatus.Paid };
        var attempt = new PaymentAttempt
        {
            Id = 4,
            PatientId = 4,
            AppointmentId = 40,
            Appointment = appt,
            AmountCents = 80000, // 800 EGP (exceeds 500 threshold)
            Currency = "EGP",
            Status = "Succeeded"
        };
        var highValueRefund = new Refund
        {
            Id = 401,
            PaymentAttemptId = 4,
            PaymentAttempt = attempt,
            AmountCents = 80000,
            Currency = "EGP",
            Status = "UnderReview",
            RequestedByAdminId = "admin-maker-original",
            AuditReference = "REF-20261109-HV-01",
            Reason = "Large refund pending secondary review"
        };
        db.Appointments.Add(appt);
        db.PaymentAttempts.Add(attempt);
        db.Refunds.Add(highValueRefund);
        await db.SaveChangesAsync();

        var settings = Options.Create(new PaymobSettings
        {
            SecretKey = "egy_sk_test_1",
            MakerCheckerThresholdEgp = 500m
        });

        var service = new PaymobRefundService(db, new HttpClient(), settings, Mock.Of<INotificationService>(), NullLogger<PaymobRefundService>.Instance);

        // Act: Original maker attempts to approve their own refund
        var selfApprovalResult = await service.ApproveAndExecuteRefundAsync(401, "admin-maker-original");

        // Assert
        selfApprovalResult.Success.Should().BeFalse();
        selfApprovalResult.Message.Should().Contain("Segregation of duties violation");

        var reloaded = await db.Refunds.FindAsync(401);
        reloaded!.Status.Should().Be("UnderReview", "Self-approved refund must remain UnderReview and not execute");
    }

    [Fact]
    public async Task NotificationOutbox_PermanentFailureHandling_CeasesRetriesWhenMaxRetriesReached()
    {
        // Arrange
        var uowMock = new Mock<IUnitOfWork>();
        var outboxRepoMock = new Mock<IRepository<NotificationOutbox>>();
        var notifRepoMock = new Mock<IRepository<Notification>>();
        var realtimeNotifierMock = new Mock<IRealtimeNotifier>();
        var emailServiceMock = new Mock<IEmailService>();

        uowMock.Setup(u => u.NotificationOutbox).Returns(outboxRepoMock.Object);
        uowMock.Setup(u => u.Notifications).Returns(notifRepoMock.Object);

        // Email service consistently fails
        emailServiceMock.Setup(e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(false);

        var failingItem = new NotificationOutbox
        {
            Id = 50,
            NotificationId = 500,
            UserId = "user_outbox_test",
            Channel = "Email",
            RecipientEmail = "fail@test.local",
            Subject = "Test",
            PayloadJson = "Payload",
            Status = "Pending",
            RetryCount = 2,
            MaxRetries = 3
        };

        outboxRepoMock.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<NotificationOutbox, bool>>>()))
            .ReturnsAsync(new List<NotificationOutbox> { failingItem });

        var services = new ServiceCollection();
        services.AddSingleton(uowMock.Object);
        services.AddSingleton(realtimeNotifierMock.Object);
        services.AddSingleton(emailServiceMock.Object);
        var provider = services.BuildServiceProvider();

        var scopeFactoryMock = new Mock<IServiceScopeFactory>();
        var scopeMock = new Mock<IServiceScope>();
        scopeMock.Setup(s => s.ServiceProvider).Returns(provider);
        scopeFactoryMock.Setup(f => f.CreateScope()).Returns(scopeMock.Object);

        var worker = new NotificationOutboxBackgroundService(scopeFactoryMock.Object, NullLogger<NotificationOutboxBackgroundService>.Instance);

        // Act
        var processed = await worker.ProcessPendingOutboxAsync(CancellationToken.None);

        // Assert
        processed.Should().Be(1);
        failingItem.RetryCount.Should().Be(3);
        failingItem.Status.Should().Be("Failed", "Exceeding max retries must transition item to terminal Failed status to avoid infinite retry loops");
        failingItem.LastError.Should().NotBeNullOrEmpty();
    }
}
