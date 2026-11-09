using Microsoft.AspNetCore.Mvc.ViewFeatures;
using System.Security.Claims;
using FluentAssertions;
using MediCare.Data.Context;
using MediCare.Data.Entities;
using MediCare.Data.Enums;
using MediCare.Services.Contracts;
using MediCare.Services.DTOs;
using MediCare.Web.Controllers;
using MediCare.Web.Controllers.Api;
using MediCare.Web.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace MediCare.Tests.Controllers;

public class PaymentControllerTests
{
    private static ApplicationDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ApplicationDbContext(options);
    }

    private static (ApplicationUser User, Patient Patient, Doctor Doctor, Appointment Appointment, PaymentAttempt Attempt) SeedData(ApplicationDbContext db, string status = "Succeeded", PaymentStatus paymentStatus = PaymentStatus.Paid)
    {
        var user = new ApplicationUser { Id = "patient-1", FullName = "Sarah Connor", Email = "sarah@test.eg" };
        var patient = new Patient { UserId = user.Id, User = user, Gender = "Female" };
        var docUser = new ApplicationUser { Id = "doc-1", FullName = "Tarek Ali" };
        var spec = new Specialization { Name = "Cardiology" };
        var doctor = new Doctor { UserId = docUser.Id, User = docUser, Specialization = spec, LicenseNumber = "MOH-101", ConsultationFee = 500m };
        var appt = new Appointment
        {
            Patient = patient,
            Doctor = doctor,
            AppointmentDate = DateTime.UtcNow.Date.AddDays(1),
            StartTime = new TimeSpan(10, 0, 0),
            EndTime = new TimeSpan(10, 30, 0),
            Status = AppointmentStatus.Confirmed,
            PaymentStatus = paymentStatus,
            ConsultationFee = 500m,
            PaidAmount = 500m,
            TransactionReference = "TXN-TEST-1234",
            PaymentProvider = "Paymob Test (Visa •••• 4242)"
        };
        db.Appointments.Add(appt);

        var attempt = new PaymentAttempt
        {
            Appointment = appt,
            PatientId = patient.Id,
            MerchantReference = "mc-1-abcd1234",
            ProviderOrderId = "ORD-9999",
            ProviderTransactionId = "TXN-TEST-1234",
            AmountCents = 50000,
            Currency = "EGP",
            Status = status,
            PaymentMethodType = "Card",
            PaymentMethodDetail = "Visa •••• 4242",
            TermsVersion = "v1.0-2026.11",
            TermsAcceptedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(30)
        };
        db.PaymentAttempts.Add(attempt);
        db.SaveChanges();

        return (user, patient, doctor, appt, attempt);
    }

    [Fact]
    public async Task Result_WhenPaymentSucceeded_ReturnsSuccessStateViewModel()
    {
        await using var db = CreateInMemoryDb();
        var (user, _, _, appt, attempt) = SeedData(db, "Succeeded", PaymentStatus.Paid);

        var controller = new PaymentController(
            new Mock<IPaymentService>().Object,
            db,
            NullLogger<PaymentController>.Instance);

        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Role, "Patient")
        ], "TestAuth"));
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var actionResult = await controller.Result(attempt.MerchantReference, null, null, null, "true", null, null, null);
        var viewResult = actionResult.Should().BeOfType<ViewResult>().Subject;
        var model = viewResult.Model.Should().BeOfType<PaymentResultViewModel>().Subject;

        model.State.Should().Be("Success");
        model.MerchantReference.Should().Be(attempt.MerchantReference);
        model.DoctorName.Should().Contain("Tarek Ali");
        model.AmountPaid.Should().Be(500m);
    }

    [Fact]
    public async Task Result_WhenPendingGatewayCallback_ReturnsProcessingStateViewModel()
    {
        await using var db = CreateInMemoryDb();
        var (user, _, _, appt, attempt) = SeedData(db, "Pending", PaymentStatus.Unpaid);

        var controller = new PaymentController(
            new Mock<IPaymentService>().Object,
            db,
            NullLogger<PaymentController>.Instance);

        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Role, "Patient")
        ], "TestAuth"));
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var actionResult = await controller.Result(attempt.MerchantReference, null, null, null, "true", "true", null, null);
        var viewResult = actionResult.Should().BeOfType<ViewResult>().Subject;
        var model = viewResult.Model.Should().BeOfType<PaymentResultViewModel>().Subject;

        model.State.Should().Be("Processing");
    }

    [Fact]
    public async Task Result_WhenUnauthorizedUserAttemptsAccess_ReturnsForbid()
    {
        await using var db = CreateInMemoryDb();
        var (_, _, _, _, attempt) = SeedData(db);

        var controller = new PaymentController(
            new Mock<IPaymentService>().Object,
            db,
            NullLogger<PaymentController>.Instance);

        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "attacker-user"),
            new Claim(ClaimTypes.Role, "Patient")
        ], "TestAuth"));
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var actionResult = await controller.Result(attempt.MerchantReference, null, null, null, null, null, null, null);
        actionResult.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task Result_WhenAttemptNotFound_ReturnsNotFoundStateViewModel()
    {
        await using var db = CreateInMemoryDb();

        var controller = new PaymentController(
            new Mock<IPaymentService>().Object,
            db,
            NullLogger<PaymentController>.Instance);

        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "patient-1"),
            new Claim(ClaimTypes.Role, "Patient")
        ], "TestAuth"));
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var actionResult = await controller.Result("non-existent-reference", null, null, null, null, null, null, null);
        var viewResult = actionResult.Should().BeOfType<ViewResult>().Subject;
        var model = viewResult.Model.Should().BeOfType<PaymentResultViewModel>().Subject;

        model.State.Should().Be("NotFound");
    }

    [Fact]
    public async Task GetPaymentStatus_WhenAuthorizedAndPaid_ReturnsSuccessStatus()
    {
        await using var db = CreateInMemoryDb();
        var (user, _, _, _, attempt) = SeedData(db, "Succeeded", PaymentStatus.Paid);

        var apiController = new PaymentApiController(
            new Mock<IPaymentService>().Object,
            new Mock<IPaymobCheckoutService>().Object,
            NullLogger<PaymentApiController>.Instance,
            db);

        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Role, "Patient")
        ], "TestAuth"));
        apiController.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var actionResult = await apiController.GetPaymentStatus(attempt.MerchantReference);
        var okResult = actionResult.Should().BeOfType<OkObjectResult>().Subject;
        var value = okResult.Value;
        value.Should().NotBeNull();
    }

    [Fact]
    public async Task History_WhenAuthenticatedPatient_ReturnsViewWithHistoryViewModel()
    {
        await using var db = CreateInMemoryDb();
        var (user, _, _, _, _) = SeedData(db);

        var mockPaymentService = new Mock<IPaymentService>();
        var expectedVm = new PaymentHistoryViewModel
        {
            PagedHistory = new MediCare.Services.Common.PagedResult<PaymentHistoryItemDto>
            {
                TotalCount = 1,
                Items = new List<PaymentHistoryItemDto> { new() { Id = 1, MerchantReference = "test-ref", AmountPaid = 500m } }
            },
            Summary = new PaymentHistorySummaryDto { TotalPaid = 500m, SucceededCount = 1 }
        };

        mockPaymentService
            .Setup(s => s.GetPaymentHistoryAsync(It.IsAny<PaymentHistoryFilterDto>(), user.Id, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MediCare.Services.Common.Result<PaymentHistoryViewModel>.Success(expectedVm));

        var controller = new PaymentController(
            mockPaymentService.Object,
            db,
            NullLogger<PaymentController>.Instance);

        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Role, "Patient")
        ], "TestAuth"));
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var actionResult = await controller.History(new PaymentHistoryFilterDto());
        var viewResult = actionResult.Should().BeOfType<ViewResult>().Subject;
        var model = viewResult.Model.Should().BeOfType<PaymentHistoryViewModel>().Subject;

        model.PagedHistory.TotalCount.Should().Be(1);
        model.Summary.TotalPaid.Should().Be(500m);
    }

    [Fact]
    public async Task History_WhenUnauthenticated_ReturnsChallenge()
    {
        await using var db = CreateInMemoryDb();

        var controller = new PaymentController(
            new Mock<IPaymentService>().Object,
            db,
            NullLogger<PaymentController>.Instance);

        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

        var actionResult = await controller.History(new PaymentHistoryFilterDto());
        actionResult.Should().BeOfType<ChallengeResult>();
    }

    [Fact]
    public async Task GetPaymentHistoryApi_WhenAuthenticated_ReturnsOkWithData()
    {
        await using var db = CreateInMemoryDb();
        var (user, _, _, _, _) = SeedData(db);

        var mockPaymentService = new Mock<IPaymentService>();
        mockPaymentService
            .Setup(s => s.GetPaymentHistoryAsync(It.IsAny<PaymentHistoryFilterDto>(), user.Id, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MediCare.Services.Common.Result<PaymentHistoryViewModel>.Success(new PaymentHistoryViewModel()));

        var apiController = new PaymentApiController(
            mockPaymentService.Object,
            new Mock<IPaymobCheckoutService>().Object,
            NullLogger<PaymentApiController>.Instance,
            db);

        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Role, "Patient")
        ], "TestAuth"));
        apiController.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var actionResult = await apiController.GetPaymentHistory(new PaymentHistoryFilterDto());
        actionResult.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task AdminPayments_WhenAdmin_ReturnsViewWithFinancialDashboardDto()
    {
        var mockPaymentService = new Mock<IPaymentService>();
        var expectedDto = new AdminFinancialDashboardDto
        {
            GrossSuccessfulPayments = 1500m,
            TotalRefundedAmount = 200m,
            SucceededCount = 3
        };

        mockPaymentService
            .Setup(s => s.GetAdminFinancialDashboardAsync(It.IsAny<PaymentHistoryFilterDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MediCare.Services.Common.Result<AdminFinancialDashboardDto>.Success(expectedDto));

        var controller = new AdminController(
            new Mock<IAdminService>().Object,
            NullLogger<AdminController>.Instance,
            mockPaymentService.Object);

        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "admin-user"),
            new Claim(ClaimTypes.Role, "Admin")
        ], "TestAuth"));
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var actionResult = await controller.Payments(new PaymentHistoryFilterDto());
        var viewResult = actionResult.Should().BeOfType<ViewResult>().Subject;
        var model = viewResult.Model.Should().BeOfType<AdminFinancialDashboardDto>().Subject;

        model.GrossSuccessfulPayments.Should().Be(1500m);
        model.NetRevenue.Should().Be(1300m);
        model.SucceededCount.Should().Be(3);
    }

    [Fact]
    public async Task AdminReconcile_WhenValidInput_RedirectsToPaymentsWithSuccess()
    {
        var mockPaymentService = new Mock<IPaymentService>();
        mockPaymentService
            .Setup(s => s.ResolveReconciliationAsync(It.IsAny<AdminResolveReconciliationDto>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MediCare.Services.Common.Result.Success());

        var controller = new AdminController(
            new Mock<IAdminService>().Object,
            NullLogger<AdminController>.Instance,
            mockPaymentService.Object);

        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "admin-user"),
            new Claim(ClaimTypes.Role, "Admin")
        ], "TestAuth"));
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        controller.TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());

        var dto = new AdminResolveReconciliationDto { AttemptId = 12, Resolution = "Refund", Notes = "Patient cancellation refund approved" };
        var actionResult = await controller.Reconcile(dto);

        var redirectResult = actionResult.Should().BeOfType<RedirectToActionResult>().Subject;
        redirectResult.ActionName.Should().Be("Payments");
        controller.TempData["SuccessMessage"].Should().NotBeNull();
    }

    [Fact]
    public async Task AdminReconcile_WhenServiceFails_RedirectsToPaymentsWithError()
    {
        var mockPaymentService = new Mock<IPaymentService>();
        mockPaymentService
            .Setup(s => s.ResolveReconciliationAsync(It.IsAny<AdminResolveReconciliationDto>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MediCare.Services.Common.Result.Failure("Invalid resolution"));

        var controller = new AdminController(
            new Mock<IAdminService>().Object,
            NullLogger<AdminController>.Instance,
            mockPaymentService.Object);

        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "admin-user"),
            new Claim(ClaimTypes.Role, "Admin")
        ], "TestAuth"));
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        controller.TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());

        var dto = new AdminResolveReconciliationDto { AttemptId = 12, Resolution = "Invalid", Notes = "test" };
        var actionResult = await controller.Reconcile(dto);

        var redirectResult = actionResult.Should().BeOfType<RedirectToActionResult>().Subject;
        redirectResult.ActionName.Should().Be("Payments");
        controller.TempData["ErrorMessage"].Should().Be("Invalid resolution");
    }
}