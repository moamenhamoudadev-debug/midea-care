using System;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MediCare.Data.Context;
using MediCare.Data.Entities;
using MediCare.Data.Enums;
using MediCare.Services.Common;
using MediCare.Services.Contracts;
using MediCare.Services.DTOs;
using MediCare.Services.Implementations;
using MediCare.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace MediCare.Tests.Services;

public class PaymobRefundServiceTests
{
    private static ApplicationDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"RefundTests_{Guid.NewGuid():N}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static HttpClient CreateMockHttpClient(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        return new HttpClient(new TestHttpMessageHandler(handler));
    }

    private class TestHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;
        public TestHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) => _handler = handler;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_handler(request));
    }

    private static (ApplicationDbContext Db, PaymentAttempt Attempt, Patient Patient) SeedSuccessfulTransaction(ApplicationDbContext db, decimal amountEgp)
    {
        var user = new ApplicationUser
        {
            Id = $"user-{Guid.NewGuid():N}",
            UserName = "patient@test.com",
            Email = "patient@test.com",
            FullName = "Ahmed Ali",
            PhoneNumber = "+201012345678"
        };
        db.Users.Add(user);

        var patient = new Patient
        {
            Id = 100 + Random.Shared.Next(1, 1000),
            UserId = user.Id,
            User = user,
            DateOfBirth = new DateTime(1990, 1, 1),
            EmergencyContact = "+201099999999"
        };
        db.Patients.Add(patient);

        var doctorUser = new ApplicationUser
        {
            Id = $"doc-{Guid.NewGuid():N}",
            UserName = "doctor@test.com",
            Email = "doctor@test.com",
            FullName = "Dr. Sara Ibrahim"
        };
        db.Users.Add(doctorUser);

        var spec = new Specialization { Id = 1, Name = "Cardiology" };
        db.Specializations.Add(spec);

        var doctor = new Doctor
        {
            Id = 200 + Random.Shared.Next(1, 1000),
            UserId = doctorUser.Id,
            User = doctorUser,
            SpecializationId = spec.Id,
            Specialization = spec,
            ConsultationFee = amountEgp,
            IsApproved = true
        };
        db.Doctors.Add(doctor);

        var appointment = new Appointment
        {
            Id = 300 + Random.Shared.Next(1, 1000),
            PatientId = patient.Id,
            Patient = patient,
            DoctorId = doctor.Id,
            Doctor = doctor,
            AppointmentDate = DateTime.UtcNow.Date.AddDays(1),
            StartTime = new TimeSpan(10, 0, 0),
            EndTime = new TimeSpan(10, 30, 0),
            ConsultationFee = amountEgp,
            PaidAmount = amountEgp,
            PaymentStatus = PaymentStatus.Paid,
            Status = AppointmentStatus.Confirmed
        };
        db.Appointments.Add(appointment);

        var attempt = new PaymentAttempt
        {
            Id = 400 + Random.Shared.Next(1, 1000),
            AppointmentId = appointment.Id,
            Appointment = appointment,
            PatientId = patient.Id,
            MerchantReference = $"mc-{appointment.Id}-test",
            ProviderTransactionId = "987654321",
            AmountCents = checked((long)Math.Round(amountEgp * 100m)),
            Currency = "EGP",
            Status = "Succeeded",
            PaymentMethodType = "Card",
            PaymentMethodDetail = "Visa",
            TermsVersion = PaymentTermsConstants.ActiveVersion,
            CreatedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow
        };
        db.PaymentAttempts.Add(attempt);
        db.SaveChanges();

        return (db, attempt, patient);
    }

    [Fact]
    public async Task RequestRefund_FullRefund_UnderThreshold_ExecutesImmediatelyAndSucceeds()
    {
        var db = CreateInMemoryDbContext();
        var (_, attempt, _) = SeedSuccessfulTransaction(db, 300m); // Under 500 EGP threshold

        var httpClient = CreateMockHttpClient(req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"id\": 112233, \"success\": true, \"amount_cents\": 30000}", Encoding.UTF8, "application/json")
        });

        var settings = Options.Create(new PaymobSettings
        {
            SecretKey = "egy_sk_test_123",
            MakerCheckerThresholdEgp = 500m
        });

        var mockNotif = new Mock<INotificationService>();
        var service = new PaymobRefundService(db, httpClient, settings, mockNotif.Object, NullLogger<PaymobRefundService>.Instance);

        var request = new CreateRefundRequestDto
        {
            PaymentAttemptId = attempt.Id,
            IsFullRefund = true,
            Reason = "Patient consultation cancelled within allowable terms window."
        };

        var result = await service.RequestRefundAsync(request, "admin-maker-1");

        result.Success.Should().BeTrue();
        result.Status.Should().Be("Succeeded");
        result.RequiresCheckerApproval.Should().BeFalse();
        result.AmountEgp.Should().Be(300m);

        var savedAttempt = await db.PaymentAttempts.Include(a => a.Appointment).Include(a => a.Refunds).FirstAsync(a => a.Id == attempt.Id);
        savedAttempt.Status.Should().Be("Refunded");
        savedAttempt.Appointment.PaymentStatus.Should().Be(PaymentStatus.Refunded);
        savedAttempt.Refunds.Should().HaveCount(1);
        savedAttempt.Refunds.First().Status.Should().Be("Succeeded");
        savedAttempt.Refunds.First().IsPartialRefund.Should().BeFalse();

        mockNotif.Verify(n => n.SendNotificationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task RequestRefund_PartialRefund_SucceedsAndUpdatesRemainingBalance()
    {
        var db = CreateInMemoryDbContext();
        var (_, attempt, _) = SeedSuccessfulTransaction(db, 400m);

        var httpClient = CreateMockHttpClient(req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"id\": 445566, \"success\": true, \"amount_cents\": 15000}", Encoding.UTF8, "application/json")
        });

        var settings = Options.Create(new PaymobSettings
        {
            SecretKey = "egy_sk_test_123",
            MakerCheckerThresholdEgp = 500m
        });

        var mockNotif = new Mock<INotificationService>();
        var service = new PaymobRefundService(db, httpClient, settings, mockNotif.Object, NullLogger<PaymobRefundService>.Instance);

        var request = new CreateRefundRequestDto
        {
            PaymentAttemptId = attempt.Id,
            AmountEgp = 150m,
            IsFullRefund = false,
            Reason = "Partial courtesy concession approved by clinic administrator."
        };

        var result = await service.RequestRefundAsync(request, "admin-maker-1");

        result.Success.Should().BeTrue();
        result.Status.Should().Be("Succeeded");
        result.AmountEgp.Should().Be(150m);

        var savedAttempt = await db.PaymentAttempts.Include(a => a.Appointment).Include(a => a.Refunds).FirstAsync(a => a.Id == attempt.Id);
        savedAttempt.Status.Should().Be("PartiallyRefunded");
        savedAttempt.Appointment.PaymentStatus.Should().Be(PaymentStatus.Paid);

        var remaining = await service.GetRemainingRefundableAmountAsync(attempt.Id);
        remaining.Should().Be(250m);
    }

    [Fact]
    public async Task RequestRefund_MultiplePartialRefunds_ExhaustingBalance_TransitionsToRefunded()
    {
        var db = CreateInMemoryDbContext();
        var (_, attempt, _) = SeedSuccessfulTransaction(db, 300m);

        var httpClient = CreateMockHttpClient(req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"id\": 101010, \"success\": true}", Encoding.UTF8, "application/json")
        });

        var settings = Options.Create(new PaymobSettings { SecretKey = "egy_sk_test_123", MakerCheckerThresholdEgp = 500m });
        var service = new PaymobRefundService(db, httpClient, settings, Mock.Of<INotificationService>(), NullLogger<PaymobRefundService>.Instance);

        // 1st partial: 100 EGP
        var res1 = await service.RequestRefundAsync(new CreateRefundRequestDto { PaymentAttemptId = attempt.Id, AmountEgp = 100m, Reason = "Partial concession 1" }, "admin-1");
        res1.Success.Should().BeTrue();

        // 2nd partial: 150 EGP
        var res2 = await service.RequestRefundAsync(new CreateRefundRequestDto { PaymentAttemptId = attempt.Id, AmountEgp = 150m, Reason = "Partial concession 2" }, "admin-1");
        res2.Success.Should().BeTrue();

        var remAfter2 = await service.GetRemainingRefundableAmountAsync(attempt.Id);
        remAfter2.Should().Be(50m);

        // 3rd partial: remaining 50 EGP
        var res3 = await service.RequestRefundAsync(new CreateRefundRequestDto { PaymentAttemptId = attempt.Id, AmountEgp = 50m, Reason = "Final residual balance refund" }, "admin-1");
        res3.Success.Should().BeTrue();

        var savedAttempt = await db.PaymentAttempts.Include(a => a.Appointment).FirstAsync(a => a.Id == attempt.Id);
        savedAttempt.Status.Should().Be("Refunded");
        savedAttempt.Appointment.PaymentStatus.Should().Be(PaymentStatus.Refunded);

        var remFinal = await service.GetRemainingRefundableAmountAsync(attempt.Id);
        remFinal.Should().Be(0m);

        // 4th attempt: should be rejected
        var res4 = await service.RequestRefundAsync(new CreateRefundRequestDto { PaymentAttemptId = attempt.Id, AmountEgp = 10m, Reason = "Over refund" }, "admin-1");
        res4.Success.Should().BeFalse();
        res4.Message.Should().Contain("Cannot refund transaction with status 'Refunded'");
    }

    [Fact]
    public async Task RequestRefund_AmountExceedingRemainingBalance_IsRejected()
    {
        var db = CreateInMemoryDbContext();
        var (_, attempt, _) = SeedSuccessfulTransaction(db, 300m);

        var httpClient = CreateMockHttpClient(req => new HttpResponseMessage(HttpStatusCode.OK));
        var settings = Options.Create(new PaymobSettings { SecretKey = "egy_sk_test_123", MakerCheckerThresholdEgp = 500m });
        var service = new PaymobRefundService(db, httpClient, settings, Mock.Of<INotificationService>(), NullLogger<PaymobRefundService>.Instance);

        var request = new CreateRefundRequestDto
        {
            PaymentAttemptId = attempt.Id,
            AmountEgp = 350m, // Exceeds 300
            Reason = "Intentional overcharge refund test"
        };

        var result = await service.RequestRefundAsync(request, "admin-1");

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("exceeds remaining refundable balance");
    }

    [Fact]
    public async Task RequestRefund_OverMakerCheckerThreshold_RequiresCheckerApproval()
    {
        var db = CreateInMemoryDbContext();
        var (_, attempt, _) = SeedSuccessfulTransaction(db, 800m); // > 500 EGP

        var httpClient = CreateMockHttpClient(req => new HttpResponseMessage(HttpStatusCode.OK));
        var settings = Options.Create(new PaymobSettings { SecretKey = "egy_sk_test_123", MakerCheckerThresholdEgp = 500m });
        var service = new PaymobRefundService(db, httpClient, settings, Mock.Of<INotificationService>(), NullLogger<PaymobRefundService>.Instance);

        var request = new CreateRefundRequestDto
        {
            PaymentAttemptId = attempt.Id,
            AmountEgp = 600m,
            Reason = "High-value specialist surgical consultation cancellation."
        };

        var result = await service.RequestRefundAsync(request, "admin-maker-1");

        result.Success.Should().BeTrue();
        result.RequiresCheckerApproval.Should().BeTrue();
        result.Status.Should().Be("UnderReview");
        result.Message.Should().Contain("Maker-Checker review");

        var refund = await db.Refunds.FirstAsync(r => r.Id == result.RefundId);
        refund.Status.Should().Be("UnderReview");
        refund.RequestedByAdminId.Should().Be("admin-maker-1");
        refund.ApprovedByAdminId.Should().BeNull();
    }

    [Fact]
    public async Task ApproveRefund_MakerCannotSelfApprove_ThrowsSegregationOfDutiesError()
    {
        var db = CreateInMemoryDbContext();
        var (_, attempt, _) = SeedSuccessfulTransaction(db, 1000m);

        var httpClient = CreateMockHttpClient(req => new HttpResponseMessage(HttpStatusCode.OK));
        var settings = Options.Create(new PaymobSettings { SecretKey = "egy_sk_test_123", MakerCheckerThresholdEgp = 500m });
        var service = new PaymobRefundService(db, httpClient, settings, Mock.Of<INotificationService>(), NullLogger<PaymobRefundService>.Instance);

        var requestResult = await service.RequestRefundAsync(new CreateRefundRequestDto
        {
            PaymentAttemptId = attempt.Id,
            AmountEgp = 700m,
            Reason = "Patient requested refund due to physician emergency absence."
        }, "admin-maker-1");

        // Same admin tries to approve
        var approveResult = await service.ApproveAndExecuteRefundAsync(requestResult.RefundId, "admin-maker-1");

        approveResult.Success.Should().BeFalse();
        approveResult.Message.Should().Contain("Segregation of duties violation");

        var refund = await db.Refunds.FirstAsync(r => r.Id == requestResult.RefundId);
        refund.Status.Should().Be("UnderReview");
    }

    [Fact]
    public async Task ApproveRefund_IndependentCheckerApproval_ExecutesRefundSuccessfully()
    {
        var db = CreateInMemoryDbContext();
        var (_, attempt, _) = SeedSuccessfulTransaction(db, 1000m);

        var httpClient = CreateMockHttpClient(req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"id\": 999888, \"success\": true, \"amount_cents\": 70000}", Encoding.UTF8, "application/json")
        });

        var settings = Options.Create(new PaymobSettings { SecretKey = "egy_sk_test_123", MakerCheckerThresholdEgp = 500m });
        var service = new PaymobRefundService(db, httpClient, settings, Mock.Of<INotificationService>(), NullLogger<PaymobRefundService>.Instance);

        var requestResult = await service.RequestRefundAsync(new CreateRefundRequestDto
        {
            PaymentAttemptId = attempt.Id,
            AmountEgp = 700m,
            Reason = "Patient requested refund due to physician emergency absence."
        }, "admin-maker-1");

        // Different admin (Checker) approves
        var approveResult = await service.ApproveAndExecuteRefundAsync(requestResult.RefundId, "admin-checker-2", "Verified hospital logs and approved.");

        approveResult.Success.Should().BeTrue();
        approveResult.Status.Should().Be("Succeeded");

        var refund = await db.Refunds.FirstAsync(r => r.Id == requestResult.RefundId);
        refund.Status.Should().Be("Succeeded");
        refund.RequestedByAdminId.Should().Be("admin-maker-1");
        refund.ApprovedByAdminId.Should().Be("admin-checker-2");
        refund.Reason.Should().Contain("Checker Note: Verified hospital logs and approved.");
    }

    [Fact]
    public async Task RejectRefund_CheckerRejection_TransitionsStatusToRejected()
    {
        var db = CreateInMemoryDbContext();
        var (_, attempt, _) = SeedSuccessfulTransaction(db, 800m);

        var httpClient = CreateMockHttpClient(req => new HttpResponseMessage(HttpStatusCode.OK));
        var settings = Options.Create(new PaymobSettings { SecretKey = "egy_sk_test_123", MakerCheckerThresholdEgp = 500m });
        var service = new PaymobRefundService(db, httpClient, settings, Mock.Of<INotificationService>(), NullLogger<PaymobRefundService>.Instance);

        var requestResult = await service.RequestRefundAsync(new CreateRefundRequestDto
        {
            PaymentAttemptId = attempt.Id,
            AmountEgp = 600m,
            Reason = "Late cancellation refund request"
        }, "admin-maker-1");

        var rejectResult = await service.RejectRefundAsync(requestResult.RefundId, "admin-checker-2", "Cancellation was within 2 hours of slot; terms require 24 hours notice.");

        rejectResult.Success.Should().BeTrue();
        rejectResult.Status.Should().Be("Rejected");

        var refund = await db.Refunds.FirstAsync(r => r.Id == requestResult.RefundId);
        refund.Status.Should().Be("Rejected");
        refund.FailureReason.Should().Contain("terms require 24 hours notice");
    }

    [Fact]
    public async Task ExecuteRefund_WhenGatewayTimesOut_TransitionsToRequiresReconciliation()
    {
        var db = CreateInMemoryDbContext();
        var (_, attempt, _) = SeedSuccessfulTransaction(db, 300m);

        var httpClient = CreateMockHttpClient(req => throw new HttpRequestException("Paymob gateway timeout"));
        var settings = Options.Create(new PaymobSettings { SecretKey = "egy_sk_test_123", MakerCheckerThresholdEgp = 500m });
        var service = new PaymobRefundService(db, httpClient, settings, Mock.Of<INotificationService>(), NullLogger<PaymobRefundService>.Instance);

        var result = await service.RequestRefundAsync(new CreateRefundRequestDto
        {
            PaymentAttemptId = attempt.Id,
            IsFullRefund = true,
            Reason = "Gateway timeout resilience test"
        }, "admin-1");

        result.Success.Should().BeFalse();
        result.Status.Should().Be("RequiresReconciliation");

        var refund = await db.Refunds.FirstAsync(r => r.Id == result.RefundId);
        refund.Status.Should().Be("RequiresReconciliation");

        var savedAttempt = await db.PaymentAttempts.FirstAsync(a => a.Id == attempt.Id);
        savedAttempt.ReconciliationStatus.Should().Be("ReconciliationRequired");
    }

    [Fact]
    public async Task AdminController_RefundActions_RedirectToPaymentsWithNotice()
    {
        var mockRefundService = new Mock<IPaymobRefundService>();
        mockRefundService
            .Setup(s => s.RequestRefundAsync(It.IsAny<CreateRefundRequestDto>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RefundResultDto { Success = true, Message = "Refund initiated successfully." });

        mockRefundService
            .Setup(s => s.ApproveAndExecuteRefundAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RefundResultDto { Success = true, Message = "Refund approved and executed." });

        mockRefundService
            .Setup(s => s.RejectRefundAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RefundResultDto { Success = true, Message = "Refund request rejected." });

        var controller = new AdminController(
            Mock.Of<IAdminService>(),
            NullLogger<AdminController>.Instance,
            Mock.Of<IPaymentService>(),
            mockRefundService.Object);

        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "admin-user"),
            new Claim(ClaimTypes.Role, "Admin")
        ], "TestAuth"));
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        controller.TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());

        // 1. Request Refund
        var reqAction = await controller.RequestRefund(new CreateRefundRequestDto { PaymentAttemptId = 1, AmountEgp = 100m, Reason = "Clinical reason" });
        var reqRedirect = reqAction.Should().BeOfType<RedirectToActionResult>().Subject;
        reqRedirect.ActionName.Should().Be("Payments");
        controller.TempData["SuccessMessage"].Should().Be("Refund initiated successfully.");

        // 2. Approve Refund
        var appAction = await controller.ApproveRefund(new ApproveRefundRequestDto { RefundId = 5, ApproverNote = "Approved" });
        var appRedirect = appAction.Should().BeOfType<RedirectToActionResult>().Subject;
        appRedirect.ActionName.Should().Be("Payments");
        controller.TempData["SuccessMessage"].Should().Be("Refund approved and executed.");

        // 3. Reject Refund
        var rejAction = await controller.RejectRefund(new RejectRefundRequestDto { RefundId = 5, RejectionReason = "Ineligible" });
        var rejRedirect = rejAction.Should().BeOfType<RedirectToActionResult>().Subject;
        rejRedirect.ActionName.Should().Be("Payments");
        controller.TempData["SuccessMessage"].Should().Be("Refund request rejected.");
    }
}
