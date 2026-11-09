using System.Security.Claims;
using FluentAssertions;
using MediCare.Services.Common;
using MediCare.Services.Contracts;
using MediCare.Services.DTOs;
using MediCare.Web.Controllers.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace MediCare.Tests.Controllers;

public class PaymentApiControllerTests
{
    private readonly Mock<IPaymentService> _paymentServiceMock;
    private readonly Mock<IPaymobCheckoutService> _paymobMock;
    private readonly Mock<ILogger<PaymentApiController>> _loggerMock;
    private readonly PaymentApiController _controller;

    public PaymentApiControllerTests()
    {
        _paymentServiceMock = new Mock<IPaymentService>();
        _paymobMock = new Mock<IPaymobCheckoutService>();
        _loggerMock = new Mock<ILogger<PaymentApiController>>();
        _controller = new PaymentApiController(_paymentServiceMock.Object, _paymobMock.Object, _loggerMock.Object);
    }

    private void SetUserContext(string? userId, string role = "Patient")
    {
        var claims = new List<Claim>();
        if (!string.IsNullOrEmpty(userId))
        {
            claims.Add(new Claim(ClaimTypes.NameIdentifier, userId));
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var identity = new ClaimsIdentity(claims, "TestAuth");
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
        };
    }

    [Fact]
    public async Task Checkout_ShouldReturnUnauthorized_WhenUserClaimMissing()
    {
        // Arrange
        SetUserContext(null);
        var request = new PaymentCheckoutRequestDto { AppointmentId = 1 };

        // Act
        var result = await _controller.Checkout(request);

        // Assert
        var unauthorizedResult = result.Should().BeOfType<UnauthorizedObjectResult>().Subject;
        unauthorizedResult.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task Checkout_ShouldReturnBadRequest_WhenPaymobIsNotConfigured()
    {
        // Arrange
        SetUserContext("patient_user_123");
        var request = new PaymentCheckoutRequestDto
        {
            AppointmentId = 10,
            AcceptTerms = true,
        };

        _paymobMock.Setup(s => s.CreateCheckoutAsync(10, "patient_user_123", null, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PaymentCheckoutSessionDto>.Failure("Paymob test checkout is not configured."));

        // Act
        var result = await _controller.Checkout(request);

        // Assert
        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Checkout_ShouldReturnHostedUrl_WhenCheckoutSessionIsCreated()
    {
        // Arrange
        SetUserContext("patient_user_123");
        var request = new PaymentCheckoutRequestDto
        {
            AppointmentId = 15,
            AcceptTerms = true,
        };

        _paymobMock.Setup(s => s.CreateCheckoutAsync(15, "patient_user_123", null, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PaymentCheckoutSessionDto>.Success(new PaymentCheckoutSessionDto { CheckoutUrl = "https://accept.paymob.com/unifiedcheckout/?test", MerchantReference = "mc-test" }));

        // Act
        var result = await _controller.Checkout(request);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.StatusCode.Should().Be(200);
        okResult.Value.Should().NotBeNull();
    }

    [Fact]
    public async Task GetReceipt_ShouldReturnUnauthorized_WhenUserClaimMissing()
    {
        // Arrange
        SetUserContext(null);

        // Act
        var result = await _controller.GetReceipt(10);

        // Assert
        var unauthorizedResult = result.Should().BeOfType<UnauthorizedObjectResult>().Subject;
        unauthorizedResult.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task GetReceipt_ShouldReturnBadRequest_WhenReceiptNotFoundOrUnpaid()
    {
        // Arrange
        SetUserContext("patient_user_123");

        _paymentServiceMock.Setup(s => s.GetReceiptAsync(99, "patient_user_123"))
            .ReturnsAsync(Result<PaymentReceiptDto>.Failure("No receipt available. Payment has not been completed."));

        // Act
        var result = await _controller.GetReceipt(99);

        // Assert
        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task GetReceipt_ShouldReturnOk_WhenReceiptFound()
    {
        // Arrange
        SetUserContext("patient_user_123");
        var receipt = new PaymentReceiptDto
        {
            AppointmentId = 25,
            TransactionReference = "TXN_TEST_987654",
            AmountPaid = 300,
            DoctorName = "Dr. Mahmoud",
            PatientName = "Jane Doe",
            PaidAt = DateTime.UtcNow
        };

        _paymentServiceMock.Setup(s => s.GetReceiptAsync(25, "patient_user_123"))
            .ReturnsAsync(Result<PaymentReceiptDto>.Success(receipt));

        // Act
        var result = await _controller.GetReceipt(25);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.StatusCode.Should().Be(200);
    }

    [Fact]
    public void ValidatePromo_ShouldHaveAuthorizePatientAndPromoRateLimitPolicyAttributes()
    {
        // Arrange
        var method = typeof(PaymentApiController).GetMethod(nameof(PaymentApiController.ValidatePromo));

        // Assert
        method.Should().NotBeNull();
        var authAttr = method!.GetCustomAttributes(typeof(AuthorizeAttribute), false).FirstOrDefault() as AuthorizeAttribute;
        authAttr.Should().NotBeNull("ValidatePromo action must be restricted by [Authorize]");
        authAttr!.Roles.Should().Be("Patient", "Only Patients can validate discount codes against pending consultations");

        var rateLimitAttr = method.GetCustomAttributes(typeof(EnableRateLimitingAttribute), false).FirstOrDefault() as EnableRateLimitingAttribute;
        rateLimitAttr.Should().NotBeNull("ValidatePromo action must be protected against brute-force attacks via [EnableRateLimiting]");
        rateLimitAttr!.PolicyName.Should().Be("PromoRateLimitPolicy");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ValidatePromo_ShouldReturnBadRequest_WhenPromoCodeIsNullOrWhitespace(string? code)
    {
        // Arrange
        SetUserContext("patient_user_123");
        var request = new PromoValidationRequest { Code = code!, Amount = 300m };

        // Act
        var result = await _controller.ValidatePromo(request);

        // Assert
        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task ValidatePromo_ShouldReturnBadRequest_WhenServiceValidationFails()
    {
        // Arrange
        SetUserContext("patient_user_123");
        var request = new PromoValidationRequest { Code = "EXPIRED2024", Amount = 300m };

        _paymentServiceMock.Setup(s => s.ValidatePromoCodeAsync("EXPIRED2024", 300m))
            .ReturnsAsync(Result<PromoCodeValidationDto>.Failure("This promotion has expired."));

        // Act
        var result = await _controller.ValidatePromo(request);

        // Assert
        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task ValidatePromo_ShouldReturnOk_WhenServiceValidationSucceeds()
    {
        // Arrange
        SetUserContext("patient_user_123");
        var request = new PromoValidationRequest { Code = "WELCOME10", Amount = 300m };
        var promoResult = new PromoCodeValidationDto
        {
            Code = "WELCOME10",
            IsValid = true,
            DiscountAmount = 30m,
            FinalAmount = 270m,
            Description = "10% Welcome Discount"
        };

        _paymentServiceMock.Setup(s => s.ValidatePromoCodeAsync("WELCOME10", 300m))
            .ReturnsAsync(Result<PromoCodeValidationDto>.Success(promoResult));

        // Act
        var result = await _controller.ValidatePromo(request);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.StatusCode.Should().Be(200);
    }
}
