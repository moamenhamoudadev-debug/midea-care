using System.Security.Claims;
using FluentAssertions;
using FluentValidation;
using MediCare.Data.Entities;
using MediCare.Data.UnitOfWork;
using MediCare.Services.Contracts;
using MediCare.Services.DTOs;
using MediCare.Services.Implementations;
using MediCare.Services.Validators;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace MediCare.Tests.Services;

public class PasswordResetServiceTests
{
    private readonly Mock<UserManager<ApplicationUser>> _mockUserManager;
    private readonly Mock<SignInManager<ApplicationUser>> _mockSignInManager;
    private readonly Mock<IUnitOfWork> _mockUow;
    private readonly Mock<IEmailService> _mockEmailService;
    private readonly Mock<ILogger<AuthService>> _mockLogger;
    private readonly IValidator<ResetPasswordDto> _resetPasswordValidator;
    private readonly IOptions<DataProtectionTokenProviderOptions> _tokenOptions;
    private readonly AuthService _sut;

    public PasswordResetServiceTests()
    {
        var userStore = new Mock<IUserStore<ApplicationUser>>();
        _mockUserManager = new Mock<UserManager<ApplicationUser>>(
            userStore.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        var contextAccessor = new Mock<IHttpContextAccessor>();
        var claimsFactory = new Mock<IUserClaimsPrincipalFactory<ApplicationUser>>();
        _mockSignInManager = new Mock<SignInManager<ApplicationUser>>(
            _mockUserManager.Object, contextAccessor.Object, claimsFactory.Object, null!, null!, null!, null!);

        _mockUow = new Mock<IUnitOfWork>();
        _mockEmailService = new Mock<IEmailService>();
        _mockLogger = new Mock<ILogger<AuthService>>();
        _resetPasswordValidator = new ResetPasswordValidator();
        _tokenOptions = Options.Create(new DataProtectionTokenProviderOptions
        {
            TokenLifespan = TimeSpan.FromHours(2)
        });

        _sut = new AuthService(
            _mockUserManager.Object,
            _mockSignInManager.Object,
            _mockUow.Object,
            new PatientRegisterValidator(),
            new DoctorRegisterValidator(),
            new LoginValidator(),
            new PatientUpdateProfileValidator(),
            _resetPasswordValidator,
            _mockEmailService.Object,
            _tokenOptions,
            _mockLogger.Object);
    }

    [Fact]
    public async Task GeneratePasswordResetTokenAsync_WithExistingUser_ReturnsSuccessWithGeneratedToken()
    {
        // Arrange
        const string email = "patient@medicare.com";
        var user = new ApplicationUser { Id = "user-123", Email = email, UserName = email };

        _mockUserManager.Setup(m => m.FindByEmailAsync(email))
            .ReturnsAsync(user);
        _mockUserManager.Setup(m => m.GeneratePasswordResetTokenAsync(user))
            .ReturnsAsync("valid-identity-reset-token-xyz");

        // Act
        var result = await _sut.GeneratePasswordResetTokenAsync(email);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("valid-identity-reset-token-xyz");
        _mockUserManager.Verify(m => m.GeneratePasswordResetTokenAsync(user), Times.Once);
    }

    [Fact]
    public async Task GeneratePasswordResetTokenAsync_WithNonExistentUser_ReturnsSuccessWithNull_AntiEnumeration()
    {
        // Arrange
        const string unknownEmail = "unknown@randomdomain.com";
        _mockUserManager.Setup(m => m.FindByEmailAsync(unknownEmail))
            .ReturnsAsync((ApplicationUser?)null);

        // Act
        var result = await _sut.GeneratePasswordResetTokenAsync(unknownEmail);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
        _mockUserManager.Verify(m => m.GeneratePasswordResetTokenAsync(It.IsAny<ApplicationUser>()), Times.Never);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task GeneratePasswordResetTokenAsync_WithEmptyEmail_ReturnsFailure(string? email)
    {
        // Act
        var result = await _sut.GeneratePasswordResetTokenAsync(email!);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Email address is required");
    }

    [Fact]
    public async Task SendPasswordResetEmailAsync_WithValidInputs_DispatchesBilingualEmailWithBrandAndLifespan()
    {
        // Arrange
        const string email = "user@medicare.com";
        const string resetUrl = "https://medicare.example.com/Account/ResetPassword?code=test-code&email=user@medicare.com";

        string? capturedBody = null;
        string? capturedSubject = null;
        _mockEmailService.Setup(m => m.SendEmailAsync(email, It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string, string>((to, sub, body) =>
            {
                capturedSubject = sub;
                capturedBody = body;
            })
            .ReturnsAsync(true);

        // Act
        var result = await _sut.SendPasswordResetEmailAsync(email, resetUrl);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _mockEmailService.Verify(m => m.SendEmailAsync(email, It.IsAny<string>(), It.IsAny<string>()), Times.Once);

        capturedSubject.Should().Contain("Password Reset Request");
        capturedSubject.Should().Contain("إعادة تعيين كلمة المرور");

        capturedBody.Should().NotBeNull();
        capturedBody.Should().Contain("MediCare");
        capturedBody.Should().Contain("Reset My Password");
        capturedBody.Should().Contain("إعادة تعيين كلمة المرور");
        capturedBody.Should().Contain("2 hours");
        capturedBody.Should().Contain("ساعتين");
        capturedBody.Should().Contain(System.Net.WebUtility.HtmlEncode(resetUrl));
    }

    [Fact]
    public async Task SendPasswordResetEmailAsync_WhenEmailProviderReturnsFalse_ReturnsSuccessNonBlocking()
    {
        // Arrange
        const string email = "patient@medicare.com";
        const string resetUrl = "https://medicare.example.com/Account/ResetPassword?code=xyz";

        _mockEmailService.Setup(m => m.SendEmailAsync(email, It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(false);

        // Act
        var result = await _sut.SendPasswordResetEmailAsync(email, resetUrl);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task SendPasswordResetEmailAsync_WhenEmailProviderThrows_ReturnsSuccessNonBlocking()
    {
        // Arrange
        const string email = "patient@medicare.com";
        const string resetUrl = "https://medicare.example.com/Account/ResetPassword?code=xyz";

        _mockEmailService.Setup(m => m.SendEmailAsync(email, It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("SMTP connection timeout"));

        // Act
        var result = await _sut.SendPasswordResetEmailAsync(email, resetUrl);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ResetPasswordAsync_WithValidTokenAndPassword_ResetsPasswordAndInvalidatesSecurityStamp()
    {
        // Arrange
        const string email = "patient@medicare.com";
        const string token = "valid-token-123";
        const string newPassword = "StrongPassword@123";

        var user = new ApplicationUser { Id = "user-123", Email = email, UserName = email };

        _mockUserManager.Setup(m => m.FindByEmailAsync(email))
            .ReturnsAsync(user);
        _mockUserManager.Setup(m => m.ResetPasswordAsync(user, token, newPassword))
            .ReturnsAsync(IdentityResult.Success);
        _mockUserManager.Setup(m => m.UpdateSecurityStampAsync(user))
            .ReturnsAsync(IdentityResult.Success);
        _mockUserManager.Setup(m => m.GetClaimsAsync(user))
            .ReturnsAsync(new List<Claim>());

        var dto = new ResetPasswordDto
        {
            Email = email,
            Token = token,
            NewPassword = newPassword,
            ConfirmPassword = newPassword
        };

        // Act
        var result = await _sut.ResetPasswordAsync(dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _mockUserManager.Verify(m => m.ResetPasswordAsync(user, token, newPassword), Times.Once);
        _mockUserManager.Verify(m => m.UpdateSecurityStampAsync(user), Times.Once);
    }

    [Fact]
    public async Task ResetPasswordAsync_WithNonExistentUser_ReturnsGenericFailure_AntiEnumeration()
    {
        // Arrange
        const string unknownEmail = "unknown@random.org";
        _mockUserManager.Setup(m => m.FindByEmailAsync(unknownEmail))
            .ReturnsAsync((ApplicationUser?)null);

        var dto = new ResetPasswordDto
        {
            Email = unknownEmail,
            Token = "any-token",
            NewPassword = "StrongPassword@123",
            ConfirmPassword = "StrongPassword@123"
        };

        // Act
        var result = await _sut.ResetPasswordAsync(dto);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("The password reset token is invalid or has expired.");
        _mockUserManager.Verify(m => m.ResetPasswordAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ResetPasswordAsync_WithInvalidOrExpiredToken_ReturnsGenericTokenFailure()
    {
        // Arrange
        const string email = "patient@medicare.com";
        var user = new ApplicationUser { Id = "user-123", Email = email };

        _mockUserManager.Setup(m => m.FindByEmailAsync(email))
            .ReturnsAsync(user);
        _mockUserManager.Setup(m => m.ResetPasswordAsync(user, "expired-token", "StrongPassword@123"))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Code = "InvalidToken", Description = "Invalid token." }));

        var dto = new ResetPasswordDto
        {
            Email = email,
            Token = "expired-token",
            NewPassword = "StrongPassword@123",
            ConfirmPassword = "StrongPassword@123"
        };

        // Act
        var result = await _sut.ResetPasswordAsync(dto);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("The password reset token is invalid or has expired.");
        _mockUserManager.Verify(m => m.UpdateSecurityStampAsync(It.IsAny<ApplicationUser>()), Times.Never);
    }

    [Fact]
    public async Task ResetPasswordAsync_WithPasswordPolicyViolation_ReturnsDetailedIdentityError()
    {
        // Arrange
        const string email = "patient@medicare.com";
        var user = new ApplicationUser { Id = "user-123", Email = email };

        _mockUserManager.Setup(m => m.FindByEmailAsync(email))
            .ReturnsAsync(user);
        _mockUserManager.Setup(m => m.ResetPasswordAsync(user, "valid-token", "WeakPassword@1"))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Code = "PasswordRequiresUniqueChars", Description = "Passwords must use at least 1 different characters." }));

        var dto = new ResetPasswordDto
        {
            Email = email,
            Token = "valid-token",
            NewPassword = "WeakPassword@1",
            ConfirmPassword = "WeakPassword@1"
        };

        // Act
        var result = await _sut.ResetPasswordAsync(dto);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Passwords must use at least 1 different characters.");
    }

    [Fact]
    public async Task ResetPasswordAsync_WithMismatchedPasswords_FailsValidation()
    {
        // Arrange
        var dto = new ResetPasswordDto
        {
            Email = "patient@medicare.com",
            Token = "token",
            NewPassword = "StrongPassword@123",
            ConfirmPassword = "DifferentPassword@456"
        };

        // Act
        var result = await _sut.ResetPasswordAsync(dto);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Passwords do not match");
    }

    [Fact]
    public async Task ResetPasswordAsync_WhenUserHasMustChangePasswordClaim_RemovesClaimUponReset()
    {
        // Arrange
        const string email = "doctor@medicare.com";
        const string token = "valid-token";
        const string newPassword = "DoctorPassword@123";

        var user = new ApplicationUser { Id = "doctor-123", Email = email };
        var claim = new Claim("MustChangePassword", "True");

        _mockUserManager.Setup(m => m.FindByEmailAsync(email))
            .ReturnsAsync(user);
        _mockUserManager.Setup(m => m.ResetPasswordAsync(user, token, newPassword))
            .ReturnsAsync(IdentityResult.Success);
        _mockUserManager.Setup(m => m.UpdateSecurityStampAsync(user))
            .ReturnsAsync(IdentityResult.Success);
        _mockUserManager.Setup(m => m.GetClaimsAsync(user))
            .ReturnsAsync(new List<Claim> { claim });
        _mockUserManager.Setup(m => m.RemoveClaimAsync(user, claim))
            .ReturnsAsync(IdentityResult.Success);

        var dto = new ResetPasswordDto
        {
            Email = email,
            Token = token,
            NewPassword = newPassword,
            ConfirmPassword = newPassword
        };

        // Act
        var result = await _sut.ResetPasswordAsync(dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _mockUserManager.Verify(m => m.RemoveClaimAsync(user, claim), Times.Once);
    }
}
