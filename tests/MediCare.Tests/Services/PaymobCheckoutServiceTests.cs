using System.Security.Cryptography;
using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using MediCare.Data.Context;
using MediCare.Data.Entities;
using MediCare.Data.Enums;
using MediCare.Services.Common;
using MediCare.Services.Contracts;
using MediCare.Services.Implementations;
using MediCare.Services.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace MediCare.Tests.Services;

public class PaymobCheckoutServiceTests
{
    private const string HmacSecret = "test-hmac-secret";
    private const string OrderedValues = "25002026-10-16T12:30:00ZEGPfalsefalse123456789123456truefalsefalsefalsetruefalse4567892346false2346MasterCardcardtrue";

    private const string TransactionJson = """
        {"amount_cents":2500,"created_at":"2026-10-16T12:30:00Z","currency":"EGP","error_occured":false,"has_parent_transaction":false,"id":123456789,"integration_id":123456,"is_3d_secure":true,"is_auth":false,"is_capture":false,"is_refunded":false,"is_standalone_payment":true,"is_voided":false,"order":{"id":456789},"owner":2346,"pending":false,"source_data":{"pan":"2346","sub_type":"MasterCard","type":"card"},"success":true}
        """;

    [Fact]
    public void VerifyTransactionHmac_AcceptsValidPaymobSignature()
    {
        using var document = JsonDocument.Parse(TransactionJson);
        var expected = Convert.ToHexString(HMACSHA512.HashData(Encoding.UTF8.GetBytes(HmacSecret), Encoding.UTF8.GetBytes(OrderedValues))).ToLowerInvariant();

        PaymobCheckoutService.VerifyTransactionHmac(document.RootElement, HmacSecret, expected).Should().BeTrue();
    }

    [Fact]
    public void VerifyTransactionHmac_RejectsTamperedOrMissingSignature()
    {
        using var document = JsonDocument.Parse(TransactionJson);

        PaymobCheckoutService.VerifyTransactionHmac(document.RootElement, HmacSecret, "invalid").Should().BeFalse();
        PaymobCheckoutService.VerifyTransactionHmac(document.RootElement, HmacSecret, null).Should().BeFalse();
    }

    [Fact]
    public void CheckoutRequest_DoesNotBindCardholderData()
    {
        var request = JsonSerializer.Deserialize<PaymentCheckoutRequestDto>("""
            {"appointmentId":7,"cardNumber":"4111111111111111","cardHolderName":"Test","expiryMonth":"12","expiryYear":"2030","cvv":"123","promoCode":"WELCOME10"}
            """, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        request.Should().NotBeNull();
        request!.AppointmentId.Should().Be(7);
        request.PromoCode.Should().Be("WELCOME10");
        request.CardNumber.Should().BeEmpty();
        request.Cvv.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateCheckoutAsync_RejectsAnotherPatientsAppointment_WithoutAttemptOrProviderRequest()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new ApplicationDbContext(options);

        var owner = new ApplicationUser
        {
            Id = "patient-owner",
            FullName = "Appointment Owner",
            Email = "owner@example.test",
            PhoneNumber = "+201000000001"
        };
        var patient = new Patient { UserId = owner.Id, User = owner, Gender = "Not specified" };
        var doctorUser = new ApplicationUser { Id = "doctor-user", FullName = "Test Doctor" };
        var specialization = new Specialization { Name = "General Practice" };
        var doctor = new Doctor
        {
            UserId = doctorUser.Id,
            User = doctorUser,
            Specialization = specialization,
            LicenseNumber = "TEST-ONLY",
            ConsultationFee = 500m
        };
        var appointment = new Appointment
        {
            Patient = patient,
            Doctor = doctor,
            AppointmentDate = DateTime.UtcNow.Date.AddDays(1),
            StartTime = new TimeSpan(9, 0, 0),
            EndTime = new TimeSpan(9, 30, 0),
            Status = AppointmentStatus.Confirmed,
            ConsultationFee = 500m,
            PaymentStatus = PaymentStatus.Unpaid
        };
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();

        var providerRequestCount = 0;
        using var httpClient = new HttpClient(new StubHttpMessageHandler((_, _) =>
        {
            providerRequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"client_secret":"test-client-secret","intention_order_id":98765}""")
            });
        }));
        var paymentService = new Mock<IPaymentService>();
        var service = new PaymobCheckoutService(
            db,
            httpClient,
            Options.Create(new PaymobSettings
            {
                SecretKey = "sk_test_unit_test",
                PublicKey = "egy_pk_test_unit_test",
                HmacSecret = "unit-test-hmac",
                IntegrationId = "123456",
                PublicBaseUrl = "https://demo.example.test/"
            }),
            paymentService.Object,
            NullLogger<PaymobCheckoutService>.Instance);

        var unauthorized = await service.CreateCheckoutAsync(appointment.Id, "different-patient", null);

        unauthorized.IsSuccess.Should().BeFalse();
        unauthorized.Error.Should().Contain("Forbidden");
        (await db.PaymentAttempts.CountAsync()).Should().Be(0);
        providerRequestCount.Should().Be(0, "unauthorized ownership must be checked before any provider request");

        var ownerResult = await service.CreateCheckoutAsync(appointment.Id, owner.Id, null);

        ownerResult.IsSuccess.Should().BeTrue("the appointment owner should still be able to start hosted checkout");
        ownerResult.Value!.CheckoutUrl.Should().StartWith("https://accept.paymob.com/unifiedcheckout/");
        (await db.PaymentAttempts.CountAsync()).Should().Be(1);
        var attempt = await db.PaymentAttempts.FirstAsync();
        attempt.TermsVersion.Should().Be(MediCare.Services.Common.PaymentTermsConstants.ActiveVersion);
        attempt.TermsAcceptedAt.Should().NotBeNull();
        providerRequestCount.Should().Be(1, "the provider HTTP call is intercepted by a test-only handler");
    }

    [Fact]
    public async Task ProcessWebhookAsync_WhenAppointmentWasCancelledPriorToPayment_PreservesCancellationAndFlagsForReconciliation()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        await using var db = new ApplicationDbContext(options);

        var patientUser = new ApplicationUser { Id = "patient-conflict", FullName = "Conflict Patient", Email = "conflict@test.clinic", PhoneNumber = "+201000000099" };
        var patient = new Patient { UserId = patientUser.Id, User = patientUser, Gender = "Female" };
        var doctorUser = new ApplicationUser { Id = "doctor-conflict", FullName = "Doctor Conflict" };
        var specialization = new Specialization { Name = "Cardiology" };
        var doctor = new Doctor { UserId = doctorUser.Id, User = doctorUser, Specialization = specialization, LicenseNumber = "DOC-CONFLICT", ConsultationFee = 350m };
        var appointment = new Appointment
        {
            Patient = patient,
            Doctor = doctor,
            AppointmentDate = DateTime.UtcNow.Date.AddDays(2),
            StartTime = new TimeSpan(10, 0, 0),
            EndTime = new TimeSpan(10, 30, 0),
            Status = AppointmentStatus.Cancelled,
            ConsultationFee = 350m,
            PaymentStatus = PaymentStatus.Unpaid,
            Notes = "Patient had cancelled prior to bank confirmation."
        };
        db.Appointments.Add(appointment);

        var attempt = new PaymentAttempt
        {
            Appointment = appointment,
            PatientId = patient.Id,
            MerchantReference = "mc-conflict-test",
            ProviderOrderId = "456789",
            AmountCents = 2500,
            Currency = "EGP",
            Status = "Pending",
            ExpiresAt = DateTime.UtcNow.AddMinutes(30)
        };
        db.PaymentAttempts.Add(attempt);
        await db.SaveChangesAsync();

        var service = new PaymobCheckoutService(
            db,
            new HttpClient(),
            Options.Create(new PaymobSettings
            {
                SecretKey = "sk_test_unit",
                PublicKey = "egy_pk_test_unit",
                HmacSecret = HmacSecret,
                IntegrationId = "123456",
                PublicBaseUrl = "https://demo.example.test/"
            }),
            new Mock<IPaymentService>().Object,
            NullLogger<PaymobCheckoutService>.Instance);

        var payloadJson = """
            {"obj":{"amount_cents":2500,"created_at":"2026-10-16T12:30:00Z","currency":"EGP","error_occured":false,"has_parent_transaction":false,"id":123456789,"integration_id":123456,"is_3d_secure":true,"is_auth":false,"is_capture":false,"is_refunded":false,"is_standalone_payment":true,"is_voided":false,"order":{"id":456789,"merchant_order_id":"mc-conflict-test"},"owner":2346,"pending":false,"source_data":{"pan":"2346","sub_type":"MasterCard","type":"card"},"success":true}}
            """;
        using var doc = JsonDocument.Parse(payloadJson);
        var expectedHmac = Convert.ToHexString(HMACSHA512.HashData(Encoding.UTF8.GetBytes(HmacSecret), Encoding.UTF8.GetBytes(OrderedValues))).ToLowerInvariant();

        var success = await service.ProcessWebhookAsync(doc.RootElement, expectedHmac);

        success.Should().BeTrue("the webhook must be acknowledged to avoid Paymob repeated redeliveries");
        var updatedAppt = await db.Appointments.FindAsync(appointment.Id);
        updatedAppt!.Status.Should().Be(AppointmentStatus.Cancelled, "the appointment status must NOT be flipped to confirmed if already cancelled");
        updatedAppt.PaymentStatus.Should().Be(PaymentStatus.Paid);
        updatedAppt.Notes.Should().Contain("PAYMENT RECEIVED POST-CANCELLATION");
        var updatedAttempt = await db.PaymentAttempts.FindAsync(attempt.Id);
        updatedAttempt!.Status.Should().Be("Succeeded");
        updatedAttempt.ReconciliationStatus.Should().Be("RequiresRefund");
        updatedAttempt.ReconciliationReason.Should().Contain("post-cancellation");
        updatedAttempt.PaymentMethodType.Should().Be("Card");
        updatedAttempt.PaymentMethodDetail.Should().Contain("MasterCard");
    }

    [Theory]
    [InlineData("card", "vi", "4242", "Card", "Visa •••• 4242")]
    [InlineData("card", "mc", "5100", "Card", "MasterCard •••• 5100")]
    [InlineData("card", "meeza", "5078", "Card", "Meeza Card •••• 5078")]
    [InlineData("card", "bank_card", "50780123", "Card", "Meeza Card •••• 0123")]
    [InlineData("wallet", "vodafone", "01012345678", "MobileWallet", "Vodafone Cash (01012345678)")]
    [InlineData("wallet", "orange", "01212345678", "MobileWallet", "Orange Cash (01212345678)")]
    [InlineData("wallet", "etisalat", "01112345678", "MobileWallet", "e& money (01112345678)")]
    [InlineData("wallet", "e&", "01112345678", "MobileWallet", "e& money (01112345678)")]
    [InlineData("wallet", "we", "01512345678", "MobileWallet", "WE Pay (01512345678)")]
    public void ParsePaymentMethod_AccuratelyDistinguishesCardsAndWallets(string type, string subType, string pan, string expectedType, string expectedDetail)
    {
        var json = $"{{\"source_data\": {{\"type\": \"{type}\", \"sub_type\": \"{subType}\", \"pan\": \"{pan}\"}}}}";
        using var doc = JsonDocument.Parse(json);
        var (methodType, methodDetail) = PaymobCheckoutService.ParsePaymentMethod(doc.RootElement);

        methodType.Should().Be(expectedType);
        methodDetail.Should().Be(expectedDetail);
    }

    [Fact]
    public async Task CreateCheckoutAsync_WhenWalletsDisabled_SendsCardIntegrationOnly()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new ApplicationDbContext(options);

        var owner = new ApplicationUser { Id = "p-cardonly", FullName = "Card Only", Email = "card@test.eg", PhoneNumber = "+201000000001" };
        var patient = new Patient { UserId = owner.Id, User = owner, Gender = "Male" };
        var doctorUser = new ApplicationUser { Id = "d-cardonly", FullName = "Dr Card" };
        var specialization = new Specialization { Name = "Dermatology" };
        var doctor = new Doctor { UserId = doctorUser.Id, User = doctorUser, Specialization = specialization, LicenseNumber = "DOC-CARD", ConsultationFee = 400m };
        var appointment = new Appointment
        {
            Patient = patient, Doctor = doctor, AppointmentDate = DateTime.UtcNow.Date.AddDays(1),
            StartTime = new TimeSpan(11, 0, 0), EndTime = new TimeSpan(11, 30, 0), Status = AppointmentStatus.Confirmed,
            ConsultationFee = 400m, PaymentStatus = PaymentStatus.Unpaid
        };
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();

        string? capturedBody = null;
        using var httpClient = new HttpClient(new StubHttpMessageHandler(async (req, _) =>
        {
            capturedBody = await req.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"client_secret":"cs-card-only","intention_order_id":112233}""")
            };
        }));

        var service = new PaymobCheckoutService(
            db, httpClient,
            Options.Create(new PaymobSettings
            {
                SecretKey = "sk_test_card", PublicKey = "egy_pk_test_card", HmacSecret = "hmac",
                IntegrationId = "6048804", WalletIntegrationId = "6048805", EnableMobileWallets = false,
                PublicBaseUrl = "https://demo.example.test/"
            }),
            new Mock<IPaymentService>().Object, NullLogger<PaymobCheckoutService>.Instance);

        var result = await service.CreateCheckoutAsync(appointment.Id, owner.Id, null);
        result.IsSuccess.Should().BeTrue();

        capturedBody.Should().NotBeNull();
        using var bodyDoc = JsonDocument.Parse(capturedBody!);
        var methods = bodyDoc.RootElement.GetProperty("payment_methods");
        methods.GetArrayLength().Should().Be(1, "when wallets are disabled, only card integration ID is passed");
        methods[0].GetInt32().Should().Be(6048804);
    }

    [Fact]
    public async Task CreateCheckoutAsync_WhenWalletsEnabled_SendsBothCardAndWalletIntegrations()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new ApplicationDbContext(options);

        var owner = new ApplicationUser { Id = "p-dual", FullName = "Dual User", Email = "dual@test.eg", PhoneNumber = "+201000000002" };
        var patient = new Patient { UserId = owner.Id, User = owner, Gender = "Female" };
        var doctorUser = new ApplicationUser { Id = "d-dual", FullName = "Dr Dual" };
        var specialization = new Specialization { Name = "Pediatrics" };
        var doctor = new Doctor { UserId = doctorUser.Id, User = doctorUser, Specialization = specialization, LicenseNumber = "DOC-DUAL", ConsultationFee = 300m };
        var appointment = new Appointment
        {
            Patient = patient, Doctor = doctor, AppointmentDate = DateTime.UtcNow.Date.AddDays(1),
            StartTime = new TimeSpan(12, 0, 0), EndTime = new TimeSpan(12, 30, 0), Status = AppointmentStatus.Confirmed,
            ConsultationFee = 300m, PaymentStatus = PaymentStatus.Unpaid
        };
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();

        string? capturedBody = null;
        using var httpClient = new HttpClient(new StubHttpMessageHandler(async (req, _) =>
        {
            capturedBody = await req.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"client_secret":"cs-dual","intention_order_id":445566}""")
            };
        }));

        var service = new PaymobCheckoutService(
            db, httpClient,
            Options.Create(new PaymobSettings
            {
                SecretKey = "sk_test_dual", PublicKey = "egy_pk_test_dual", HmacSecret = "hmac",
                IntegrationId = "6048804", WalletIntegrationId = "6048805", EnableMobileWallets = true,
                PublicBaseUrl = "https://demo.example.test/"
            }),
            new Mock<IPaymentService>().Object, NullLogger<PaymobCheckoutService>.Instance);

        var result = await service.CreateCheckoutAsync(appointment.Id, owner.Id, null);
        result.IsSuccess.Should().BeTrue();

        capturedBody.Should().NotBeNull();
        using var bodyDoc = JsonDocument.Parse(capturedBody!);
        var methods = bodyDoc.RootElement.GetProperty("payment_methods");
        methods.GetArrayLength().Should().Be(2, "when wallets are enabled and ID is set, both card and wallet integration IDs are passed");
        methods[0].GetInt32().Should().Be(6048804);
        methods[1].GetInt32().Should().Be(6048805);
    }

    [Fact]
    public void PaymentTermsConstants_HasCanonicalHashAndActiveVersion()
    {
        PaymentTermsConstants.ActiveVersion.Should().Be("v1.0-2026.11");
        PaymentTermsConstants.CanonicalContentHash.Should().StartWith("sha256:");
        PaymentTermsConstants.CanonicalContentHash.Length.Should().Be(71);
    }
    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }
}
