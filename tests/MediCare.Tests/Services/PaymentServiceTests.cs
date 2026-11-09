using MediCare.Data.Context;
using Microsoft.EntityFrameworkCore;
using FluentAssertions;
using MediCare.Data.Entities;
using MediCare.Data.Enums;
using MediCare.Data.UnitOfWork;
using MediCare.Services.Contracts;
using MediCare.Services.DTOs;
using MediCare.Services.Implementations;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace MediCare.Tests.Services;

public class PaymentServiceTests
{
    private readonly Mock<IUnitOfWork> _mockUow;
    private readonly Mock<INotificationService> _mockNotif;
    private readonly Mock<ILogger<PaymentService>> _mockLogger;
    private readonly PaymentService _sut;

    public PaymentServiceTests()
    {
        _mockUow = new Mock<IUnitOfWork>();
        _mockNotif = new Mock<INotificationService>();
        _mockLogger = new Mock<ILogger<PaymentService>>();
        _sut = new PaymentService(_mockUow.Object, _mockNotif.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task ProcessCheckoutAsync_InvalidCardNumber_ReturnsFailure()
    {
        var request = new PaymentCheckoutRequestDto
        {
            AppointmentId = 1,
            CardNumber = "1234",
            CardHolderName = "John Doe",
            ExpiryMonth = "12",
            ExpiryYear = "2029",
            Cvv = "123"
        };

        var result = await _sut.ProcessCheckoutAsync(request, "patient-user-1");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Invalid card number");
    }

    [Fact]
    public async Task ProcessCheckoutAsync_ExpiredCard_ReturnsFailure()
    {
        var request = new PaymentCheckoutRequestDto
        {
            AppointmentId = 1,
            CardNumber = "4242424242424242", // Valid Luhn length
            CardHolderName = "John Doe",
            ExpiryMonth = "01",
            ExpiryYear = "2020", // Expired
            Cvv = "123"
        };

        var result = await _sut.ProcessCheckoutAsync(request, "patient-user-1");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("expired");
    }

    [Fact]
    public async Task ProcessCheckoutAsync_IdorAttemptByDifferentUser_ReturnsForbidden()
    {
        var appointment = new Appointment
        {
            Id = 42,
            DoctorId = 1,
            PatientId = 2,
            ConsultationFee = 350,
            PaymentStatus = PaymentStatus.Unpaid,
            Status = AppointmentStatus.Confirmed,
            Patient = new Patient { Id = 2, UserId = "legitimate-patient-id" }
        };

        _mockUow.Setup(u => u.Appointments.GetByIdWithDetailsAsync(42))
            .ReturnsAsync(appointment);

        var request = new PaymentCheckoutRequestDto
        {
            AppointmentId = 42,
            CardNumber = "4242424242424242",
            CardHolderName = "Attacker Name",
            ExpiryMonth = "12",
            ExpiryYear = "2029",
            Cvv = "123"
        };

        // Act - Attacker attempts payment
        var result = await _sut.ProcessCheckoutAsync(request, "attacker-user-id");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Forbidden");
        appointment.PaymentStatus.Should().Be(PaymentStatus.Unpaid);
        _mockUow.Verify(u => u.CommitAsync(), Times.Never);
    }

    [Fact]
    public async Task ProcessCheckoutAsync_ValidCardAndOwner_AtomicallyMarksPaidAndReturnsReceipt()
    {
        var patientUser = new ApplicationUser { FullName = "Ali Hassan" };
        var doctorUser = new ApplicationUser { FullName = "Sarah Ibrahim" };
        var spec = new Specialization { Name = "Dermatology" };

        var appointment = new Appointment
        {
            Id = 77,
            DoctorId = 5,
            PatientId = 9,
            AppointmentDate = DateTime.Today.AddDays(2),
            StartTime = new TimeSpan(10, 0, 0),
            EndTime = new TimeSpan(10, 30, 0),
            ConsultationFee = 400,
            PaymentStatus = PaymentStatus.Unpaid,
            Status = AppointmentStatus.Confirmed,
            Patient = new Patient { Id = 9, UserId = "ali-patient-user", User = patientUser },
            Doctor = new Doctor { Id = 5, UserId = "sarah-doctor-user", User = doctorUser, Specialization = spec }
        };

        _mockUow.Setup(u => u.Appointments.GetByIdWithDetailsAsync(77))
            .ReturnsAsync(appointment);

        _mockUow.Setup(u => u.CommitAsync())
            .ReturnsAsync(1);

        var request = new PaymentCheckoutRequestDto
        {
            AppointmentId = 77,
            CardNumber = "4242424242424242",
            CardHolderName = "Ali Hassan",
            ExpiryMonth = "11",
            ExpiryYear = "2028",
            Cvv = "999"
        };

        // Act
        var result = await _sut.ProcessCheckoutAsync(request, "ali-patient-user");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.AmountPaid.Should().Be(400);
        result.Value.AppointmentId.Should().Be(77);
        result.Value.TransactionReference.Should().StartWith("TXN-");
        result.Value.PatientName.Should().Be("Ali Hassan");
        result.Value.DoctorName.Should().Be("Dr. Sarah Ibrahim");

        appointment.PaymentStatus.Should().Be(PaymentStatus.Paid);
        _mockUow.Verify(u => u.Appointments.Update(appointment), Times.Once);
        _mockUow.Verify(u => u.CommitAsync(), Times.Once);
    }

    [Fact]
    public async Task GetReceiptAsync_UnpaidAppointment_ReturnsFailure()
    {
        var appointment = new Appointment
        {
            Id = 50,
            PaymentStatus = PaymentStatus.Unpaid,
            Patient = new Patient { UserId = "patient-user" },
            Doctor = new Doctor { UserId = "doctor-user" }
        };

        _mockUow.Setup(u => u.Appointments.GetByIdWithDetailsAsync(50))
            .ReturnsAsync(appointment);

        var result = await _sut.GetReceiptAsync(50, "patient-user");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not been completed");
    }

    [Fact]
    public async Task GetReceiptAsync_IdorUnauthorizedUser_ReturnsForbidden()
    {
        var appointment = new Appointment
        {
            Id = 50,
            PaymentStatus = PaymentStatus.Paid,
            Patient = new Patient { UserId = "owner-patient" },
            Doctor = new Doctor { UserId = "doctor-id" }
        };

        _mockUow.Setup(u => u.Appointments.GetByIdWithDetailsAsync(50))
            .ReturnsAsync(appointment);

        var result = await _sut.GetReceiptAsync(50, "random-stranger");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Forbidden");
    }

    [Fact]
    public async Task ValidatePromoCodeAsync_ValidCodeDEPI2026_Returns20PercentDiscount()
    {
        // Act
        var result = await _sut.ValidatePromoCodeAsync("depi2026", 100m);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.DiscountAmount.Should().Be(20m);
        result.Value.FinalAmount.Should().Be(80m);
        result.Value.Code.Should().Be("DEPI2026");
    }

    [Fact]
    public async Task ValidatePromoCodeAsync_InvalidCode_ReturnsFailure()
    {
        // Act
        var result = await _sut.ValidatePromoCodeAsync("INVALID_CODE_123", 100m);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Invalid or expired");
    }

    [Fact]
    public async Task ProcessCheckoutAsync_WithValidPromoCode_AppliesDiscount()
    {
        // Arrange
        var appointment = new Appointment
        {
            Id = 55,
            ConsultationFee = 200m,
            PaymentStatus = PaymentStatus.Unpaid,
            Patient = new Patient
            {
                UserId = "pat-1",
                User = new ApplicationUser { FullName = "Mahmoud" }
            },
            Doctor = new Doctor
            {
                UserId = "doc-1",
                User = new ApplicationUser { FullName = "Dr. Tarek" },
                Specialization = new Specialization { Name = "Neurology" }
            }
        };

        _mockUow.Setup(u => u.Appointments.GetByIdWithDetailsAsync(55)).ReturnsAsync(appointment);

        var request = new PaymentCheckoutRequestDto
        {
            AppointmentId = 55,
            CardNumber = "4242424242424242",
            CardHolderName = "Mahmoud Ali",
            ExpiryMonth = "12",
            ExpiryYear = "2029",
            Cvv = "123",
            PromoCode = "DEPI2026"
        };

        // Act
        var result = await _sut.ProcessCheckoutAsync(request, "pat-1");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.AmountPaid.Should().Be(160m); // 200 - 20% (40) = 160
        result.Value.OriginalAmount.Should().Be(200m);
        result.Value.DiscountAmount.Should().Be(40m);
        result.Value.AppliedPromoCode.Should().Be("DEPI2026");
    }

    [Fact]
    public async Task ProcessCheckoutAsync_NullRequest_ReturnsFailure()
    {
        var result = await _sut.ProcessCheckoutAsync(null!, "user-1");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("cannot be empty");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task ProcessCheckoutAsync_InvalidAppointmentId_ReturnsFailure(int apptId)
    {
        var request = new PaymentCheckoutRequestDto { AppointmentId = apptId };
        var result = await _sut.ProcessCheckoutAsync(request, "user-1");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Valid appointment ID is required");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("A")]
    public async Task ProcessCheckoutAsync_InvalidCardholderName_ReturnsFailure(string? name)
    {
        var request = new PaymentCheckoutRequestDto
        {
            AppointmentId = 1,
            CardNumber = "4242424242424242",
            CardHolderName = name!,
            ExpiryMonth = "12",
            ExpiryYear = "2029",
            Cvv = "123"
        };

        var result = await _sut.ProcessCheckoutAsync(request, "user-1");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Cardholder name is required");
    }

    [Theory]
    [InlineData("13", "2029", "month")]
    [InlineData("0", "2029", "month")]
    [InlineData("invalid", "2029", "month")]
    [InlineData("12", "invalid", "year")]
    public async Task ProcessCheckoutAsync_InvalidExpiryDates_ReturnsFailure(string month, string year, string errorField)
    {
        var request = new PaymentCheckoutRequestDto
        {
            AppointmentId = 1,
            CardNumber = "4242424242424242",
            CardHolderName = "John Doe",
            ExpiryMonth = month,
            ExpiryYear = year,
            Cvv = "123"
        };

        var result = await _sut.ProcessCheckoutAsync(request, "user-1");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNull();
        result.Error!.ToLower().Should().Contain(errorField);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("12")]
    [InlineData("12345")]
    [InlineData("abc")]
    public async Task ProcessCheckoutAsync_InvalidCvv_ReturnsFailure(string? cvv)
    {
        var request = new PaymentCheckoutRequestDto
        {
            AppointmentId = 1,
            CardNumber = "4242424242424242",
            CardHolderName = "John Doe",
            ExpiryMonth = "12",
            ExpiryYear = "2029",
            Cvv = cvv!
        };

        var result = await _sut.ProcessCheckoutAsync(request, "user-1");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Invalid CVV");
    }

    [Fact]
    public async Task ProcessCheckoutAsync_WhenAppointmentNotFound_ReturnsFailure()
    {
        _mockUow.Setup(u => u.Appointments.GetByIdWithDetailsAsync(999))
            .ReturnsAsync((Appointment?)null);

        var request = new PaymentCheckoutRequestDto
        {
            AppointmentId = 999,
            CardNumber = "4242424242424242",
            CardHolderName = "John Doe",
            ExpiryMonth = "12",
            ExpiryYear = "2029",
            Cvv = "123"
        };

        var result = await _sut.ProcessCheckoutAsync(request, "user-1");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Appointment not found");
    }

    [Theory]
    [InlineData(AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.Rejected)]
    public async Task ProcessCheckoutAsync_WhenCancelledOrRejected_ReturnsFailure(AppointmentStatus status)
    {
        var appointment = new Appointment
        {
            Id = 1,
            Status = status,
            Patient = new Patient { UserId = "user-1" }
        };

        _mockUow.Setup(u => u.Appointments.GetByIdWithDetailsAsync(1)).ReturnsAsync(appointment);

        var request = new PaymentCheckoutRequestDto
        {
            AppointmentId = 1,
            CardNumber = "4242424242424242",
            CardHolderName = "John Doe",
            ExpiryMonth = "12",
            ExpiryYear = "2029",
            Cvv = "123"
        };

        var result = await _sut.ProcessCheckoutAsync(request, "user-1");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Cannot process payment for a cancelled or rejected");
    }

    [Fact]
    public async Task ProcessCheckoutAsync_WhenAlreadyPaid_ReturnsExistingReceiptIdempotently()
    {
        var appointment = new Appointment
        {
            Id = 1,
            PaymentStatus = PaymentStatus.Paid,
            ConsultationFee = 250m,
            PaidAmount = 250m,
            TransactionReference = "TXN-20261010120000-0001",
            Patient = new Patient { UserId = "user-1" }
        };

        _mockUow.Setup(u => u.Appointments.GetByIdWithDetailsAsync(1)).ReturnsAsync(appointment);

        var request = new PaymentCheckoutRequestDto
        {
            AppointmentId = 1,
            CardNumber = "4242424242424242",
            CardHolderName = "John Doe",
            ExpiryMonth = "12",
            ExpiryYear = "2029",
            Cvv = "123"
        };

        var result = await _sut.ProcessCheckoutAsync(request, "user-1");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.TransactionReference.Should().Be("TXN-20261010120000-0001");
        _mockUow.Verify(u => u.CommitAsync(), Times.Never);
    }

    [Fact]
    public async Task ProcessCheckoutAsync_TwoDigitYear_ParsesCorrectlyAndSucceeds()
    {
        var appointment = new Appointment
        {
            Id = 1,
            ConsultationFee = 300,
            PaymentStatus = PaymentStatus.Unpaid,
            Status = AppointmentStatus.Confirmed,
            Patient = new Patient { UserId = "user-1", User = new ApplicationUser { FullName = "User 1" } }
        };

        _mockUow.Setup(u => u.Appointments.GetByIdWithDetailsAsync(1)).ReturnsAsync(appointment);

        var request = new PaymentCheckoutRequestDto
        {
            AppointmentId = 1,
            CardNumber = "4242424242424242",
            CardHolderName = "John Doe",
            ExpiryMonth = "12",
            ExpiryYear = "29", // Two digit year
            Cvv = "123"
        };

        var result = await _sut.ProcessCheckoutAsync(request, "user-1");

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ProcessCheckoutAsync_NotificationException_DoesNotFailPayment()
    {
        var appointment = new Appointment
        {
            Id = 1,
            ConsultationFee = 300,
            PaymentStatus = PaymentStatus.Unpaid,
            Status = AppointmentStatus.Confirmed,
            Patient = new Patient { UserId = "user-1", User = new ApplicationUser { FullName = "User 1" } }
        };

        _mockUow.Setup(u => u.Appointments.GetByIdWithDetailsAsync(1)).ReturnsAsync(appointment);
        _mockNotif.Setup(n => n.SendNotificationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("Notification gateway network error"));

        var request = new PaymentCheckoutRequestDto
        {
            AppointmentId = 1,
            CardNumber = "4242424242424242",
            CardHolderName = "John Doe",
            ExpiryMonth = "12",
            ExpiryYear = "2029",
            Cvv = "123"
        };

        var result = await _sut.ProcessCheckoutAsync(request, "user-1");

        result.IsSuccess.Should().BeTrue();
        appointment.PaymentStatus.Should().Be(PaymentStatus.Paid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task GetReceiptAsync_InvalidAppointmentId_ReturnsFailure(int id)
    {
        var result = await _sut.GetReceiptAsync(id, "user-1");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Valid appointment ID is required");
    }

    [Fact]
    public async Task GetReceiptAsync_AppointmentNotFound_ReturnsFailure()
    {
        _mockUow.Setup(u => u.Appointments.GetByIdWithDetailsAsync(100)).ReturnsAsync((Appointment?)null);

        var result = await _sut.GetReceiptAsync(100, "user-1");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Appointment not found");
    }

    [Fact]
    public async Task GetReceiptAsync_WhenUserIsDoctor_ReturnsSuccess()
    {
        var appointment = new Appointment
        {
            Id = 1,
            PaymentStatus = PaymentStatus.Paid,
            ConsultationFee = 450,
            Patient = new Patient { UserId = "pat-10", User = new ApplicationUser { FullName = "Patient Ten" } },
            Doctor = new Doctor { UserId = "doc-20", User = new ApplicationUser { FullName = "Doctor Twenty" } },
            CreatedAt = new DateTime(2026, 10, 1)
        };

        _mockUow.Setup(u => u.Appointments.GetByIdWithDetailsAsync(1)).ReturnsAsync(appointment);

        var result = await _sut.GetReceiptAsync(1, "doc-20");

        result.IsSuccess.Should().BeTrue();
        result.Value!.AmountPaid.Should().Be(450);
        result.Value.DoctorName.Should().Be("Dr. Doctor Twenty");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ValidatePromoCodeAsync_EmptyOrWhitespace_ReturnsFailure(string? code)
    {
        var result = await _sut.ValidatePromoCodeAsync(code!, 200m);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("enter a valid promo code");
    }

    [Fact]
    public async Task ValidatePromoCodeAsync_Medicare50_Applies50Discount()
    {
        var result = await _sut.ValidatePromoCodeAsync("medicare50", 200m);

        result.IsSuccess.Should().BeTrue();
        result.Value!.DiscountAmount.Should().Be(50m);
        result.Value.FinalAmount.Should().Be(150m);
    }

    [Fact]
    public async Task ValidatePromoCodeAsync_Welcome10_Applies10PercentDiscount()
    {
        var result = await _sut.ValidatePromoCodeAsync("welcome10", 300m);

        result.IsSuccess.Should().BeTrue();
        result.Value!.DiscountAmount.Should().Be(30m);
        result.Value.FinalAmount.Should().Be(270m);
    }

    [Fact]
    public async Task ProcessCheckoutAsync_WithPromoCode_PersistsPaymentFieldsOnAppointment()
    {
        // Arrange
        var appointment = new Appointment
        {
            Id = 88,
            ConsultationFee = 300m,
            PaymentStatus = PaymentStatus.Unpaid,
            Patient = new Patient
            {
                Id = 1,
                UserId = "pat-owner",
                User = new ApplicationUser { FullName = "Hassan Test" }
            },
            Doctor = new Doctor
            {
                Id = 2,
                UserId = "doc-test",
                User = new ApplicationUser { FullName = "Doctor Test" },
                Specialization = new Specialization { Name = "Cardiology" }
            }
        };

        _mockUow.Setup(u => u.Appointments.GetByIdWithDetailsAsync(88)).ReturnsAsync(appointment);
        _mockUow.Setup(u => u.CommitAsync()).ReturnsAsync(1);

        var request = new PaymentCheckoutRequestDto
        {
            AppointmentId = 88,
            CardNumber = "4242424242424242",
            CardHolderName = "Hassan Test",
            ExpiryMonth = "12",
            ExpiryYear = "2029",
            Cvv = "123",
            PromoCode = "DEPI2026"
        };

        // Act
        var result = await _sut.ProcessCheckoutAsync(request, "pat-owner");

        // Assert
        result.IsSuccess.Should().BeTrue();
        appointment.PaymentStatus.Should().Be(PaymentStatus.Paid);
        appointment.PaidAmount.Should().Be(240m); // 300 - 20% (60)
        appointment.DiscountAmount.Should().Be(60m);
        appointment.PromoCode.Should().Be("DEPI2026");
        appointment.TransactionReference.Should().NotBeNullOrWhiteSpace();
        appointment.TransactionReference.Should().StartWith("TXN-");
        appointment.PaidAt.Should().NotBeNull();
        result.Value!.AmountPaid.Should().Be(240m);
        result.Value.DiscountAmount.Should().Be(60m);
        result.Value.AppliedPromoCode.Should().Be("DEPI2026");
    }

    [Fact]
    public async Task GetReceiptAsync_AfterDiscountedPayment_ReturnsPersistedValuesAccurately()
    {
        // Arrange
        var paidAtUtc = new DateTime(2026, 10, 5, 14, 30, 0, DateTimeKind.Utc);
        var appointment = new Appointment
        {
            Id = 99,
            ConsultationFee = 500m,
            PaidAmount = 400m,
            DiscountAmount = 100m,
            PromoCode = "DEPI2026",
            TransactionReference = "TXN-20261005143000-0099",
            PaidAt = paidAtUtc,
            PaymentStatus = PaymentStatus.Paid,
            Patient = new Patient
            {
                UserId = "pat-owner",
                User = new ApplicationUser { FullName = "Patient Name" }
            },
            Doctor = new Doctor
            {
                UserId = "doc-owner",
                User = new ApplicationUser { FullName = "Doctor Name" },
                Specialization = new Specialization { Name = "Surgery" }
            }
        };

        _mockUow.Setup(u => u.Appointments.GetByIdWithDetailsAsync(99)).ReturnsAsync(appointment);

        // Act
        var result = await _sut.GetReceiptAsync(99, "pat-owner");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.AmountPaid.Should().Be(400m, "Receipt must show discounted amount that was paid, not consultation fee");
        result.Value.OriginalAmount.Should().Be(500m);
        result.Value.DiscountAmount.Should().Be(100m);
        result.Value.AppliedPromoCode.Should().Be("DEPI2026");
        result.Value.TransactionReference.Should().Be("TXN-20261005143000-0099");
        result.Value.PaidAt.Should().Be(paidAtUtc);
    }

    [Fact]
    public async Task GetReceiptAsync_ReceiptValuesRemainImmutable_WhenAppointmentStatusAndUpdatedAtChange()
    {
        // Arrange
        var initialPaidAt = new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc);
        var initialTxnRef = "TXN-20261001100000-0101";

        var appointment = new Appointment
        {
            Id = 101,
            ConsultationFee = 250m,
            PaidAmount = 200m,
            DiscountAmount = 50m,
            PromoCode = "MEDICARE50",
            TransactionReference = initialTxnRef,
            PaidAt = initialPaidAt,
            PaymentStatus = PaymentStatus.Paid,
            Status = AppointmentStatus.Confirmed,
            CreatedAt = initialPaidAt,
            UpdatedAt = initialPaidAt,
            Patient = new Patient
            {
                UserId = "pat-owner",
                User = new ApplicationUser { FullName = "Patient" }
            },
            Doctor = new Doctor
            {
                UserId = "doc-owner",
                User = new ApplicationUser { FullName = "Doctor" },
                Specialization = new Specialization { Name = "General" }
            }
        };

        _mockUow.Setup(u => u.Appointments.GetByIdWithDetailsAsync(101)).ReturnsAsync(appointment);

        // Act 1: Initial receipt
        var receipt1 = await _sut.GetReceiptAsync(101, "pat-owner");

        // Simulate doctor completing the visit a week later
        appointment.Status = AppointmentStatus.Completed;
        appointment.UpdatedAt = initialPaidAt.AddDays(7);

        // Act 2: Subsequent receipt after completion
        var receipt2 = await _sut.GetReceiptAsync(101, "pat-owner");

        // Assert
        receipt1.IsSuccess.Should().BeTrue();
        receipt2.IsSuccess.Should().BeTrue();
        receipt2.Value!.TransactionReference.Should().Be(initialTxnRef, "Transaction reference must remain immutable");
        receipt2.Value.PaidAt.Should().Be(initialPaidAt, "PaidAt timestamp must not shift with UpdatedAt");
        receipt2.Value.AmountPaid.Should().Be(200m);
    }

    [Fact]
    public async Task ProcessCheckoutAsync_AlreadyPaidAppointment_IsIdempotentAndReturnsExistingReceiptWithoutRecharging()
    {
        // Arrange
        var paidAtUtc = new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);
        var appointment = new Appointment
        {
            Id = 102,
            ConsultationFee = 300m,
            PaidAmount = 240m,
            DiscountAmount = 60m,
            PromoCode = "DEPI2026",
            TransactionReference = "TXN-20261008120000-0102",
            PaidAt = paidAtUtc,
            PaymentStatus = PaymentStatus.Paid,
            Status = AppointmentStatus.Confirmed,
            Patient = new Patient
            {
                UserId = "pat-owner",
                User = new ApplicationUser { FullName = "Ali" }
            },
            Doctor = new Doctor
            {
                UserId = "doc-user",
                User = new ApplicationUser { FullName = "Dr. Mona" },
                Specialization = new Specialization { Name = "Dental" }
            }
        };

        _mockUow.Setup(u => u.Appointments.GetByIdWithDetailsAsync(102)).ReturnsAsync(appointment);

        var request = new PaymentCheckoutRequestDto
        {
            AppointmentId = 102,
            CardNumber = "4242424242424242",
            CardHolderName = "Ali",
            ExpiryMonth = "12",
            ExpiryYear = "2029",
            Cvv = "123"
        };

        // Act: Patient accidentally submits payment form twice
        var result = await _sut.ProcessCheckoutAsync(request, "pat-owner");

        // Assert
        result.IsSuccess.Should().BeTrue("Duplicate checkout must be idempotent and return existing receipt");
        result.Value!.TransactionReference.Should().Be("TXN-20261008120000-0102");
        result.Value.AmountPaid.Should().Be(240m);
        _mockUow.Verify(u => u.CommitAsync(), Times.Never, "Duplicate payment request must not execute a secondary database commit or charge");
    }

    [Fact]
    public async Task ProcessCheckoutAsync_ParallelConcurrentCheckout_HandlesConcurrencyIdempotently()
    {
        // Arrange
        var appointment = new Appointment
        {
            Id = 103,
            ConsultationFee = 200m,
            PaymentStatus = PaymentStatus.Unpaid,
            Status = AppointmentStatus.Confirmed,
            Patient = new Patient
            {
                UserId = "pat-owner",
                User = new ApplicationUser { FullName = "Concurrent Patient" }
            },
            Doctor = new Doctor
            {
                UserId = "doc-user",
                User = new ApplicationUser { FullName = "Dr. Concurrency" },
                Specialization = new Specialization { Name = "General" }
            }
        };

        _mockUow.Setup(u => u.Appointments.GetByIdWithDetailsAsync(103)).ReturnsAsync(appointment);

        // First commit succeeds, simulated concurrent commit throws DbUpdateConcurrencyException
        int commitCalls = 0;
        _mockUow.Setup(u => u.CommitAsync()).Returns(() =>
        {
            var callIndex = Interlocked.Increment(ref commitCalls);
            if (callIndex == 1)
            {
                return Task.FromResult(1);
            }
            throw new Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException("Row updated concurrently.");
        });

        var request1 = new PaymentCheckoutRequestDto
        {
            AppointmentId = 103,
            CardNumber = "4242424242424242",
            CardHolderName = "Concurrent Patient",
            ExpiryMonth = "12",
            ExpiryYear = "2029",
            Cvv = "123"
        };

        var request2 = new PaymentCheckoutRequestDto
        {
            AppointmentId = 103,
            CardNumber = "4242424242424242",
            CardHolderName = "Concurrent Patient",
            ExpiryMonth = "12",
            ExpiryYear = "2029",
            Cvv = "123"
        };

        // Act: Execute both concurrent requests simultaneously
        var task1 = _sut.ProcessCheckoutAsync(request1, "pat-owner");
        var task2 = _sut.ProcessCheckoutAsync(request2, "pat-owner");
        var results = await Task.WhenAll(task1, task2);

        // Assert: Both succeed, neither crashes, both return the valid receipt
        results.All(r => r.IsSuccess).Should().BeTrue("Both concurrent checkout requests must succeed idempotently");
        results[0].Value!.TransactionReference.Should().Be(results[1].Value!.TransactionReference);
    }

    #region BIZ-01 Promo Code Reuse & Tracking Tests

    [Fact]
    public async Task ValidatePromoCodeAsync_WhenPatientAlreadyUsedPromoOnPaidAppointment_ReturnsFailure()
    {
        // Arrange
        var patient = new Patient { Id = 10, UserId = "patient-user-1" };
        _mockUow.Setup(u => u.Patients.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Patient, bool>>>()))
            .ReturnsAsync(new List<Patient> { patient });

        var paidAppointment = new Appointment
        {
            Id = 50,
            PatientId = 10,
            PromoCode = "DEPI2026",
            PaymentStatus = PaymentStatus.Paid
        };

        _mockUow.Setup(u => u.Appointments.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Appointment, bool>>>()))
            .ReturnsAsync(new List<Appointment> { paidAppointment });

        // Act
        var result = await _sut.ValidatePromoCodeAsync("DEPI2026", 200m, "patient-user-1");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("already used this promo code");
    }

    [Fact]
    public async Task ProcessCheckoutAsync_WhenPatientAlreadyUsedPromoOnPaidAppointment_ReturnsFailure()
    {
        // Arrange
        var patient = new Patient { Id = 10, UserId = "patient-user-1" };
        var appointment = new Appointment
        {
            Id = 51,
            PatientId = 10,
            ConsultationFee = 200m,
            PaymentStatus = PaymentStatus.Unpaid,
            Status = AppointmentStatus.Confirmed,
            Patient = patient,
            Doctor = new Doctor
            {
                UserId = "doc-user",
                User = new ApplicationUser { FullName = "Dr. Test" },
                Specialization = new Specialization { Name = "General" }
            }
        };

        _mockUow.Setup(u => u.Appointments.GetByIdWithDetailsAsync(51)).ReturnsAsync(appointment);
        _mockUow.Setup(u => u.Patients.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Patient, bool>>>()))
            .ReturnsAsync(new List<Patient> { patient });

        var previousPaidAppt = new Appointment
        {
            Id = 40,
            PatientId = 10,
            PromoCode = "DEPI2026",
            PaymentStatus = PaymentStatus.Paid
        };

        _mockUow.Setup(u => u.Appointments.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Appointment, bool>>>()))
            .ReturnsAsync(new List<Appointment> { previousPaidAppt });

        var request = new PaymentCheckoutRequestDto
        {
            AppointmentId = 51,
            CardNumber = "4242424242424242",
            CardHolderName = "Test Patient",
            ExpiryMonth = "12",
            ExpiryYear = "2029",
            Cvv = "123",
            PromoCode = "DEPI2026"
        };

        // Act
        var result = await _sut.ProcessCheckoutAsync(request, "patient-user-1");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("already used this promo code");
        _mockUow.Verify(u => u.CommitAsync(), Times.Never);
    }

    [Fact]
    public async Task ProcessCheckoutAsync_WhenDbUpdateExceptionFromUniqueIndex_ReturnsFriendlyFailure()
    {
        // Arrange
        var patient = new Patient { Id = 10, UserId = "patient-user-1" };
        var appointment = new Appointment
        {
            Id = 52,
            PatientId = 10,
            ConsultationFee = 200m,
            PaymentStatus = PaymentStatus.Unpaid,
            Status = AppointmentStatus.Confirmed,
            Patient = patient,
            Doctor = new Doctor
            {
                UserId = "doc-user",
                User = new ApplicationUser { FullName = "Dr. Test" },
                Specialization = new Specialization { Name = "General" }
            }
        };

        _mockUow.Setup(u => u.Appointments.GetByIdWithDetailsAsync(52)).ReturnsAsync(appointment);
        _mockUow.Setup(u => u.Patients.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Patient, bool>>>()))
            .ReturnsAsync(new List<Patient> { patient });
        _mockUow.Setup(u => u.Appointments.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Appointment, bool>>>()))
            .ReturnsAsync(new List<Appointment>());

        _mockUow.Setup(u => u.CommitAsync()).ThrowsAsync(
            new Microsoft.EntityFrameworkCore.DbUpdateException("Violation of unique index",
                new Exception("Cannot insert duplicate key in object 'dbo.Appointments' with unique index 'IX_Appointments_Patient_PromoCode_Paid'.")));

        var request = new PaymentCheckoutRequestDto
        {
            AppointmentId = 52,
            CardNumber = "4242424242424242",
            CardHolderName = "Test Patient",
            ExpiryMonth = "12",
            ExpiryYear = "2029",
            Cvv = "123",
            PromoCode = "DEPI2026"
        };

        // Act
        var result = await _sut.ProcessCheckoutAsync(request, "patient-user-1");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("already used this promo code");
        appointment.PaymentStatus.Should().Be(PaymentStatus.Unpaid);
    }

    #endregion

    #region Phase 5: Patient Payment History Tests

    private static ApplicationDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task GetPaymentHistoryAsync_WhenUnauthenticated_ReturnsFailure()
    {
        await using var db = CreateInMemoryDb();
        var sut = new PaymentService(_mockUow.Object, _mockNotif.Object, _mockLogger.Object, db);

        var result = await sut.GetPaymentHistoryAsync(new PaymentHistoryFilterDto(), "");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Authentication required");
    }

    [Fact]
    public async Task GetPaymentHistoryAsync_WhenPatientNotFound_ReturnsFailure()
    {
        await using var db = CreateInMemoryDb();
        var sut = new PaymentService(_mockUow.Object, _mockNotif.Object, _mockLogger.Object, db);

        var result = await sut.GetPaymentHistoryAsync(new PaymentHistoryFilterDto(), "unknown-user-id", isAdmin: false);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Patient profile not found");
    }

    [Fact]
    public async Task GetPaymentHistoryAsync_ReturnsOnlyPatientTransactions_PreventingIdor()
    {
        await using var db = CreateInMemoryDb();

        var user1 = new ApplicationUser { Id = "patient-user-1", FullName = "Patient One" };
        var patient1 = new Patient { Id = 10, UserId = user1.Id, User = user1 };

        var user2 = new ApplicationUser { Id = "patient-user-2", FullName = "Patient Two" };
        var patient2 = new Patient { Id = 20, UserId = user2.Id, User = user2 };

        var docUser = new ApplicationUser { Id = "doc-1", FullName = "Dr. Clinic" };
        var spec = new Specialization { Id = 1, Name = "Pediatrics" };
        var doctor = new Doctor { Id = 1, UserId = docUser.Id, User = docUser, Specialization = spec };

        var appt1 = new Appointment { Id = 101, PatientId = patient1.Id, DoctorId = doctor.Id, Doctor = doctor, Patient = patient1, ConsultationFee = 350m, PaymentStatus = PaymentStatus.Paid };
        var appt2 = new Appointment { Id = 102, PatientId = patient2.Id, DoctorId = doctor.Id, Doctor = doctor, Patient = patient2, ConsultationFee = 500m, PaymentStatus = PaymentStatus.Paid };

        var attempt1 = new PaymentAttempt
        {
            Id = 1,
            AppointmentId = appt1.Id,
            Appointment = appt1,
            PatientId = patient1.Id,
            MerchantReference = "mc-patient1-ref",
            AmountCents = 35000,
            Currency = "EGP",
            Status = "Succeeded",
            PaymentMethodType = "Card",
            PaymentMethodDetail = "Visa •••• 1234"
        };

        var attempt2 = new PaymentAttempt
        {
            Id = 2,
            AppointmentId = appt2.Id,
            Appointment = appt2,
            PatientId = patient2.Id,
            MerchantReference = "mc-patient2-ref",
            AmountCents = 50000,
            Currency = "EGP",
            Status = "Succeeded",
            PaymentMethodType = "MobileWallet",
            PaymentMethodDetail = "Vodafone Cash (010...)"
        };

        db.Users.AddRange(user1, user2, docUser);
        db.Patients.AddRange(patient1, patient2);
        db.Doctors.Add(doctor);
        db.Appointments.AddRange(appt1, appt2);
        db.PaymentAttempts.AddRange(attempt1, attempt2);
        await db.SaveChangesAsync();

        var sut = new PaymentService(_mockUow.Object, _mockNotif.Object, _mockLogger.Object, db);

        // Act
        var result = await sut.GetPaymentHistoryAsync(new PaymentHistoryFilterDto(), user1.Id, isAdmin: false);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.PagedHistory.TotalCount.Should().Be(1);
        result.Value.PagedHistory.Items.Should().ContainSingle();
        result.Value.PagedHistory.Items[0].MerchantReference.Should().Be("mc-patient1-ref");
        result.Value.PagedHistory.Items[0].AmountPaid.Should().Be(350m);
        result.Value.Summary.TotalPaid.Should().Be(350m);
        result.Value.Summary.SucceededCount.Should().Be(1);
    }

    [Fact]
    public async Task GetPaymentHistoryAsync_WhenAdmin_ReturnsAllTransactions()
    {
        await using var db = CreateInMemoryDb();

        var user1 = new ApplicationUser { Id = "p-1", FullName = "P One" };
        var p1 = new Patient { Id = 1, UserId = user1.Id, User = user1 };
        var user2 = new ApplicationUser { Id = "p-2", FullName = "P Two" };
        var p2 = new Patient { Id = 2, UserId = user2.Id, User = user2 };
        var doc = new Doctor { Id = 1, UserId = "d-1", User = new ApplicationUser { FullName = "Dr. One" }, Specialization = new Specialization { Name = "Cardio" } };

        var a1 = new Appointment { Id = 1, PatientId = p1.Id, DoctorId = doc.Id, Doctor = doc, Patient = p1, ConsultationFee = 200m, PaymentStatus = PaymentStatus.Paid };
        var a2 = new Appointment { Id = 2, PatientId = p2.Id, DoctorId = doc.Id, Doctor = doc, Patient = p2, ConsultationFee = 300m, PaymentStatus = PaymentStatus.Paid };

        db.Patients.AddRange(p1, p2);
        db.Appointments.AddRange(a1, a2);
        db.PaymentAttempts.AddRange(
            new PaymentAttempt { Id = 1, AppointmentId = 1, Appointment = a1, PatientId = 1, MerchantReference = "ref-1", AmountCents = 20000, Status = "Succeeded" },
            new PaymentAttempt { Id = 2, AppointmentId = 2, Appointment = a2, PatientId = 2, MerchantReference = "ref-2", AmountCents = 30000, Status = "Succeeded" }
        );
        await db.SaveChangesAsync();

        var sut = new PaymentService(_mockUow.Object, _mockNotif.Object, _mockLogger.Object, db);

        var result = await sut.GetPaymentHistoryAsync(new PaymentHistoryFilterDto(), "admin-user", isAdmin: true);

        result.IsSuccess.Should().BeTrue();
        result.Value!.PagedHistory.TotalCount.Should().Be(2);
        result.Value.Summary.TotalPaid.Should().Be(500m);
    }

    [Fact]
    public async Task GetPaymentHistoryAsync_FiltersBySearchAndStatus_Correctly()
    {
        await using var db = CreateInMemoryDb();

        var user = new ApplicationUser { Id = "pat-filter", FullName = "Filter Patient" };
        var patient = new Patient { Id = 1, UserId = user.Id, User = user };
        var doc = new Doctor { Id = 1, UserId = "doc-f", User = new ApplicationUser { FullName = "Dr. Magdi" }, Specialization = new Specialization { Name = "Surgery" } };

        var appt1 = new Appointment { Id = 10, PatientId = 1, DoctorId = 1, Doctor = doc, Patient = patient, ConsultationFee = 400m, PaymentStatus = PaymentStatus.Paid };
        var appt2 = new Appointment { Id = 11, PatientId = 1, DoctorId = 1, Doctor = doc, Patient = patient, ConsultationFee = 400m, PaymentStatus = PaymentStatus.Unpaid };

        db.Patients.Add(patient);
        db.Appointments.AddRange(appt1, appt2);
        db.PaymentAttempts.AddRange(
            new PaymentAttempt { Id = 1, AppointmentId = 10, Appointment = appt1, PatientId = 1, MerchantReference = "MC-TARGET-001", AmountCents = 40000, Status = "Succeeded", PaymentMethodType = "Card", PaymentMethodDetail = "Visa •••• 4242" },
            new PaymentAttempt { Id = 2, AppointmentId = 11, Appointment = appt2, PatientId = 1, MerchantReference = "MC-OTHER-002", AmountCents = 40000, Status = "Pending", PaymentMethodType = "MobileWallet", PaymentMethodDetail = "Vodafone Cash" }
        );
        await db.SaveChangesAsync();

        var sut = new PaymentService(_mockUow.Object, _mockNotif.Object, _mockLogger.Object, db);

        // Filter by search term
        var searchRes = await sut.GetPaymentHistoryAsync(new PaymentHistoryFilterDto { SearchTerm = "TARGET" }, user.Id);
        searchRes.Value!.PagedHistory.TotalCount.Should().Be(1);
        searchRes.Value.PagedHistory.Items[0].MerchantReference.Should().Be("MC-TARGET-001");

        // Filter by status
        var statusRes = await sut.GetPaymentHistoryAsync(new PaymentHistoryFilterDto { Status = "Pending" }, user.Id);
        statusRes.Value!.PagedHistory.TotalCount.Should().Be(1);
        statusRes.Value.PagedHistory.Items[0].Status.Should().Be("Pending");

        // Filter by payment method
        var methodRes = await sut.GetPaymentHistoryAsync(new PaymentHistoryFilterDto { PaymentMethod = "Card" }, user.Id);
        methodRes.Value!.PagedHistory.TotalCount.Should().Be(1);
        methodRes.Value.PagedHistory.Items[0].PaymentMethodDetail.Should().Contain("Visa");
    }

    [Fact]
    public async Task GetPaymentHistoryAsync_PaginationWorksCorrectly()
    {
        await using var db = CreateInMemoryDb();

        var user = new ApplicationUser { Id = "pat-page", FullName = "Paging Patient" };
        var patient = new Patient { Id = 1, UserId = user.Id, User = user };
        var doc = new Doctor { Id = 1, UserId = "doc-p", User = new ApplicationUser { FullName = "Dr. Clinic" }, Specialization = new Specialization { Name = "General" } };

        db.Patients.Add(patient);
        for (int i = 1; i <= 15; i++)
        {
            var appt = new Appointment { Id = i, PatientId = 1, DoctorId = 1, Doctor = doc, Patient = patient, ConsultationFee = 100m, PaymentStatus = PaymentStatus.Paid };
            db.Appointments.Add(appt);
            db.PaymentAttempts.Add(new PaymentAttempt
            {
                Id = i,
                AppointmentId = i,
                Appointment = appt,
                PatientId = 1,
                MerchantReference = $"MC-PAGE-{i:D2}",
                AmountCents = 10000,
                Status = "Succeeded",
                CreatedAt = DateTime.UtcNow.AddMinutes(i)
            });
        }
        await db.SaveChangesAsync();

        var sut = new PaymentService(_mockUow.Object, _mockNotif.Object, _mockLogger.Object, db);

        var page1 = await sut.GetPaymentHistoryAsync(new PaymentHistoryFilterDto { Page = 1, PageSize = 5 }, user.Id);
        page1.Value!.PagedHistory.TotalCount.Should().Be(15);
        page1.Value.PagedHistory.TotalPages.Should().Be(3);
        page1.Value.PagedHistory.Items.Count.Should().Be(5);
        page1.Value.PagedHistory.HasNextPage.Should().BeTrue();
        page1.Value.PagedHistory.HasPreviousPage.Should().BeFalse();

        var page3 = await sut.GetPaymentHistoryAsync(new PaymentHistoryFilterDto { Page = 3, PageSize = 5 }, user.Id);
        page3.Value!.PagedHistory.Items.Count.Should().Be(5);
        page3.Value.PagedHistory.HasNextPage.Should().BeFalse();
        page3.Value.PagedHistory.HasPreviousPage.Should().BeTrue();
    }

    #endregion

    #region Phase 6: Admin Financial Dashboard & Reconciliation Tests

    [Fact]
    public async Task GetAdminFinancialDashboardAsync_ExcludesPendingAndFailedFromRevenue_SubtractsRefundsAccurately()
    {
        await using var db = CreateInMemoryDb();

        var pat = new Patient { Id = 1, UserId = "p-1", User = new ApplicationUser { FullName = "Patient One" } };
        var doc = new Doctor { Id = 1, UserId = "d-1", User = new ApplicationUser { FullName = "Dr. One" }, Specialization = new Specialization { Name = "Cardiology" } };

        var a1 = new Appointment { Id = 1, PatientId = 1, DoctorId = 1, Doctor = doc, Patient = pat, ConsultationFee = 500m, PaymentStatus = PaymentStatus.Paid };
        var a2 = new Appointment { Id = 2, PatientId = 1, DoctorId = 1, Doctor = doc, Patient = pat, ConsultationFee = 300m, PaymentStatus = PaymentStatus.Paid };
        var a3 = new Appointment { Id = 3, PatientId = 1, DoctorId = 1, Doctor = doc, Patient = pat, ConsultationFee = 200m, PaymentStatus = PaymentStatus.Refunded };
        var a4 = new Appointment { Id = 4, PatientId = 1, DoctorId = 1, Doctor = doc, Patient = pat, ConsultationFee = 1000m, PaymentStatus = PaymentStatus.Unpaid };
        var a5 = new Appointment { Id = 5, PatientId = 1, DoctorId = 1, Doctor = doc, Patient = pat, ConsultationFee = 400m, PaymentStatus = PaymentStatus.Unpaid };

        db.Patients.Add(pat);
        db.Doctors.Add(doc);
        db.Appointments.AddRange(a1, a2, a3, a4, a5);
        db.PaymentAttempts.AddRange(
            new PaymentAttempt { Id = 1, AppointmentId = 1, Appointment = a1, PatientId = 1, MerchantReference = "ref-1", AmountCents = 50000, Status = "Succeeded", PaymentMethodType = "Card", PaymentMethodDetail = "Visa •••• 1111" },
            new PaymentAttempt { Id = 2, AppointmentId = 2, Appointment = a2, PatientId = 1, MerchantReference = "ref-2", AmountCents = 30000, Status = "Succeeded", PaymentMethodType = "MobileWallet", PaymentMethodDetail = "Vodafone Cash" },
            new PaymentAttempt { Id = 3, AppointmentId = 3, Appointment = a3, PatientId = 1, MerchantReference = "ref-3", AmountCents = 20000, Status = "Succeeded", ReconciliationStatus = "Refunded", PaymentMethodType = "Card", PaymentMethodDetail = "MasterCard •••• 2222" },
            new PaymentAttempt { Id = 4, AppointmentId = 4, Appointment = a4, PatientId = 1, MerchantReference = "ref-4", AmountCents = 100000, Status = "Pending", PaymentMethodType = "Card" },
            new PaymentAttempt { Id = 5, AppointmentId = 5, Appointment = a5, PatientId = 1, MerchantReference = "ref-5", AmountCents = 40000, Status = "Failed", PaymentMethodType = "Card" }
        );
        await db.SaveChangesAsync();

        var sut = new PaymentService(_mockUow.Object, _mockNotif.Object, _mockLogger.Object, db);

        var result = await sut.GetAdminFinancialDashboardAsync(new PaymentHistoryFilterDto());

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value!;

        // Gross collected = 500 + 300 + 200 = 1000 EGP
        dto.GrossSuccessfulPayments.Should().Be(1000m);

        // Refunded = 200 EGP
        dto.TotalRefundedAmount.Should().Be(200m);

        // Net revenue = 1000 - 200 = 800 EGP
        dto.NetRevenue.Should().Be(800m);

        // Counts
        dto.SucceededCount.Should().Be(3);
        dto.PendingCount.Should().Be(1);
        dto.FailedCount.Should().Be(1);
        dto.TotalAttemptCount.Should().Be(5);
        dto.SuccessRatePercent.Should().Be(60.0m);
        dto.FailureRatePercent.Should().Be(20.0m);
    }

    [Fact]
    public async Task GetAdminFinancialDashboardAsync_CalculatesCardAndMobileWalletDistributionCorrectly()
    {
        await using var db = CreateInMemoryDb();

        var pat = new Patient { Id = 1, UserId = "p-dist", User = new ApplicationUser { FullName = "Dist Patient" } };
        var doc = new Doctor { Id = 1, UserId = "d-dist", User = new ApplicationUser { FullName = "Dr. Dist" }, Specialization = new Specialization { Name = "General" } };

        var a1 = new Appointment { Id = 1, PatientId = 1, DoctorId = 1, Doctor = doc, Patient = pat, ConsultationFee = 600m, PaymentStatus = PaymentStatus.Paid };
        var a2 = new Appointment { Id = 2, PatientId = 1, DoctorId = 1, Doctor = doc, Patient = pat, ConsultationFee = 400m, PaymentStatus = PaymentStatus.Paid };

        db.Patients.Add(pat);
        db.Doctors.Add(doc);
        db.Appointments.AddRange(a1, a2);
        db.PaymentAttempts.AddRange(
            new PaymentAttempt { Id = 1, AppointmentId = 1, Appointment = a1, PatientId = 1, MerchantReference = "ref-card", AmountCents = 60000, Status = "Succeeded", PaymentMethodType = "Card", PaymentMethodDetail = "Meeza Card •••• 5078" },
            new PaymentAttempt { Id = 2, AppointmentId = 2, Appointment = a2, PatientId = 1, MerchantReference = "ref-wallet", AmountCents = 40000, Status = "Succeeded", PaymentMethodType = "MobileWallet", PaymentMethodDetail = "Orange Cash (012...)" }
        );
        await db.SaveChangesAsync();

        var sut = new PaymentService(_mockUow.Object, _mockNotif.Object, _mockLogger.Object, db);

        var result = await sut.GetAdminFinancialDashboardAsync(new PaymentHistoryFilterDto());

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value!;

        dto.CardGrossRevenue.Should().Be(600m);
        dto.CardTransactionCount.Should().Be(1);
        dto.WalletGrossRevenue.Should().Be(400m);
        dto.WalletTransactionCount.Should().Be(1);
    }

    [Fact]
    public async Task GetAdminFinancialDashboardAsync_IdentifiesReconciliationQueue_PostCancellationAndUrgentDisputes()
    {
        await using var db = CreateInMemoryDb();

        var pat = new Patient { Id = 1, UserId = "p-q", User = new ApplicationUser { FullName = "Dispute Patient" } };
        var doc = new Doctor { Id = 1, UserId = "d-q", User = new ApplicationUser { FullName = "Dr. Clinic" }, Specialization = new Specialization { Name = "Dermatology" } };

        var cancelledAppt = new Appointment
        {
            Id = 99,
            PatientId = 1,
            DoctorId = 1,
            Doctor = doc,
            Patient = pat,
            ConsultationFee = 350m,
            Status = AppointmentStatus.Cancelled,
            PaymentStatus = PaymentStatus.Paid
        };

        var attempt = new PaymentAttempt
        {
            Id = 10,
            AppointmentId = 99,
            Appointment = cancelledAppt,
            PatientId = 1,
            MerchantReference = "mc-postcancel-ref",
            AmountCents = 35000,
            Status = "Succeeded",
            ReconciliationStatus = "RequiresRefund",
            ReconciliationReason = "Payment completed after appointment was cancelled"
        };

        db.Patients.Add(pat);
        db.Doctors.Add(doc);
        db.Appointments.Add(cancelledAppt);
        db.PaymentAttempts.Add(attempt);
        await db.SaveChangesAsync();

        var sut = new PaymentService(_mockUow.Object, _mockNotif.Object, _mockLogger.Object, db);

        var result = await sut.GetAdminFinancialDashboardAsync(new PaymentHistoryFilterDto());

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value!;

        dto.RequiresRefundCount.Should().Be(1);
        dto.ReconciliationQueue.Should().ContainSingle();
        var queueItem = dto.ReconciliationQueue[0];
        queueItem.MerchantReference.Should().Be("mc-postcancel-ref");
        queueItem.AmountPaid.Should().Be(350m);
        queueItem.IsPostCancellationPayment.Should().BeTrue();
        queueItem.ReconciliationStatus.Should().Be("RequiresRefund");
    }

    [Fact]
    public async Task ResolveReconciliationAsync_RefundDecision_AtomicallyMarksStatusAndAppointmentRefunded()
    {
        await using var db = CreateInMemoryDb();

        var pat = new Patient { Id = 1, UserId = "p-r", User = new ApplicationUser { FullName = "Refund Patient" } };
        var doc = new Doctor { Id = 1, UserId = "d-r", User = new ApplicationUser { FullName = "Dr. R" }, Specialization = new Specialization { Name = "General" } };

        var appt = new Appointment
        {
            Id = 77,
            PatientId = 1,
            DoctorId = 1,
            Doctor = doc,
            Patient = pat,
            ConsultationFee = 450m,
            Status = AppointmentStatus.Cancelled,
            PaymentStatus = PaymentStatus.Paid
        };

        var attempt = new PaymentAttempt
        {
            Id = 55,
            AppointmentId = 77,
            Appointment = appt,
            PatientId = 1,
            MerchantReference = "mc-refund-target",
            AmountCents = 45000,
            Status = "Succeeded",
            ReconciliationStatus = "RequiresRefund"
        };

        db.Patients.Add(pat);
        db.Doctors.Add(doc);
        db.Appointments.Add(appt);
        db.PaymentAttempts.Add(attempt);
        await db.SaveChangesAsync();

        var sut = new PaymentService(_mockUow.Object, _mockNotif.Object, _mockLogger.Object, db);

        var dto = new AdminResolveReconciliationDto
        {
            AttemptId = 55,
            Resolution = "Refund",
            Notes = "Authorized refund to bank card per consumer cancellation rights"
        };

        var result = await sut.ResolveReconciliationAsync(dto, "admin-agent-007");

        result.IsSuccess.Should().BeTrue();

        var updatedAttempt = await db.PaymentAttempts.FindAsync(55);
        updatedAttempt.Should().NotBeNull();
        updatedAttempt!.Status.Should().Be("Refunded");
        updatedAttempt.ReconciliationStatus.Should().Be("Refunded");
        updatedAttempt.ReconciledAt.Should().NotBeNull();
        updatedAttempt.ReconciliationReason.Should().Contain("admin-agent-007");

        var updatedAppt = await db.Appointments.FindAsync(77);
        updatedAppt!.PaymentStatus.Should().Be(PaymentStatus.Refunded);
        updatedAppt.Notes.Should().Contain("REFUNDED BY ADMIN admin-agent-007");
    }

    [Fact]
    public async Task ResolveReconciliationAsync_RebookDecision_AllocatesRebookingCredit()
    {
        await using var db = CreateInMemoryDb();

        var appt = new Appointment { Id = 88, PatientId = 1, DoctorId = 1, ConsultationFee = 300m, PaymentStatus = PaymentStatus.Paid };
        var attempt = new PaymentAttempt { Id = 66, AppointmentId = 88, Appointment = appt, PatientId = 1, MerchantReference = "ref-rebook", AmountCents = 30000, Status = "Succeeded", ReconciliationStatus = "RequiresRefund" };

        db.Appointments.Add(appt);
        db.PaymentAttempts.Add(attempt);
        await db.SaveChangesAsync();

        var sut = new PaymentService(_mockUow.Object, _mockNotif.Object, _mockLogger.Object, db);

        var dto = new AdminResolveReconciliationDto
        {
            AttemptId = 66,
            Resolution = "Rebook",
            Notes = "Rebooked for Thursday consultation with Dr. Tarek"
        };

        var result = await sut.ResolveReconciliationAsync(dto, "admin-lead");

        result.IsSuccess.Should().BeTrue();

        var updatedAttempt = await db.PaymentAttempts.FindAsync(66);
        updatedAttempt!.ReconciliationStatus.Should().Be("Rebooked");
        updatedAttempt.ReconciliationReason.Should().Contain("Rebooking Credit Approved");
    }

    [Fact]
    public async Task ResolveReconciliationAsync_WhenNotesAreMissingOrTooShort_ReturnsFailure()
    {
        await using var db = CreateInMemoryDb();
        var sut = new PaymentService(_mockUow.Object, _mockNotif.Object, _mockLogger.Object, db);

        var dto = new AdminResolveReconciliationDto
        {
            AttemptId = 1,
            Resolution = "Refund",
            Notes = "ok"
        };

        var result = await sut.ResolveReconciliationAsync(dto, "admin");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("minimum 5 characters");
    }

    [Fact]
    public async Task ResolveReconciliationAsync_WhenAttemptNotFound_ReturnsFailure()
    {
        await using var db = CreateInMemoryDb();
        var sut = new PaymentService(_mockUow.Object, _mockNotif.Object, _mockLogger.Object, db);

        var dto = new AdminResolveReconciliationDto
        {
            AttemptId = 9999,
            Resolution = "Refund",
            Notes = "Investigated and attempting refund on missing attempt"
        };

        var result = await sut.ResolveReconciliationAsync(dto, "admin");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    #endregion
}