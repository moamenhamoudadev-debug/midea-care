using System.Security.Claims;
using System.Text;
using FluentAssertions;
using MediCare.Data.Entities;
using MediCare.Services.Common;
using MediCare.Services.Contracts;
using MediCare.Services.DTOs;
using MediCare.Web.Controllers;
using MediCare.Web.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.WebUtilities;
using Moq;
using Xunit;

namespace MediCare.Tests.Controllers;

public class PasswordResetControllerTests
{
    private readonly Mock<IAuthService> _authServiceMock;
    private readonly Mock<IDoctorService> _doctorServiceMock;
    private readonly Mock<UserManager<ApplicationUser>> _userManagerMock;
    private readonly AccountController _controller;

    public PasswordResetControllerTests()
    {
        _authServiceMock = new Mock<IAuthService>();
        _doctorServiceMock = new Mock<IDoctorService>();

        var userStore = new Mock<IUserStore<ApplicationUser>>();
        _userManagerMock = new Mock<UserManager<ApplicationUser>>(
            userStore.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        _controller = new AccountController(_authServiceMock.Object, _doctorServiceMock.Object, _userManagerMock.Object);

        var httpContext = new DefaultHttpContext();
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };
        _controller.TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());

        var urlHelperMock = new Mock<IUrlHelper>();
        urlHelperMock.Setup(u => u.Action(It.IsAny<UrlActionContext>()))
            .Returns((UrlActionContext ctx) => $"https://medicare.example.com/{ctx.Controller}/{ctx.Action}?code=encoded-code&email=test@example.com");
        _controller.Url = urlHelperMock.Object;
    }

    private void SetUserContext(string? userId, string role, bool isAuthenticated = true)
    {
        var claims = new List<Claim>();
        if (!string.IsNullOrEmpty(userId))
        {
            claims.Add(new Claim(ClaimTypes.NameIdentifier, userId));
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var identity = new ClaimsIdentity(claims, isAuthenticated ? "TestAuth" : null);
        var httpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };
        _controller.TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());

        var urlHelperMock = new Mock<IUrlHelper>();
        urlHelperMock.Setup(u => u.Action(It.IsAny<UrlActionContext>()))
            .Returns((UrlActionContext ctx) => $"https://medicare.example.com/{ctx.Controller}/{ctx.Action}?code=encoded-code&email=test@example.com");
        _controller.Url = urlHelperMock.Object;
    }

    [Fact]
    public void ForgotPassword_Get_WhenUnauthenticated_ReturnsViewWithEmptyModel()
    {
        // Act
        var result = _controller.ForgotPassword();

        // Assert
        var viewResult = result.Should().BeOfType<ViewResult>().Subject;
        var model = viewResult.Model.Should().BeOfType<ForgotPasswordViewModel>().Subject;
        model.Email.Should().BeEmpty();
    }

    [Fact]
    public void ForgotPassword_Get_WhenAuthenticated_RedirectsToHomeIndex()
    {
        // Arrange
        SetUserContext("patient-1", "Patient", isAuthenticated: true);

        // Act
        var result = _controller.ForgotPassword();

        // Assert
        var redirect = result.Should().BeOfType<RedirectToActionResult>().Subject;
        redirect.ActionName.Should().Be("Index");
        redirect.ControllerName.Should().Be("Home");
    }

    [Fact]
    public async Task ForgotPassword_Post_WithValidExistingEmail_GeneratesTokenSendsEmailAndRedirectsToConfirmation()
    {
        // Arrange
        const string email = "valid@medicare.com";
        const string rawToken = "raw-identity-token-123+456/789==";
        var model = new ForgotPasswordViewModel { Email = email };

        _authServiceMock.Setup(s => s.GeneratePasswordResetTokenAsync(email))
            .ReturnsAsync(Result<string?>.Success(rawToken));
        _authServiceMock.Setup(s => s.SendPasswordResetEmailAsync(email, It.IsAny<string>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync(Result.Success());

        // Act
        var result = await _controller.ForgotPassword(model);

        // Assert
        var redirect = result.Should().BeOfType<RedirectToActionResult>().Subject;
        redirect.ActionName.Should().Be(nameof(AccountController.ForgotPasswordConfirmation));

        _authServiceMock.Verify(s => s.GeneratePasswordResetTokenAsync(email), Times.Once);
        _authServiceMock.Verify(s => s.SendPasswordResetEmailAsync(email, It.IsAny<string>(), It.IsAny<TimeSpan?>()), Times.Once);
    }

    [Fact]
    public async Task ForgotPassword_Post_WithNonExistentEmail_DoesNotSendEmail_AndRedirectsToConfirmation_IdenticalAntiEnumeration()
    {
        // Arrange
        const string nonExistentEmail = "unknown@random.org";
        var model = new ForgotPasswordViewModel { Email = nonExistentEmail };

        // AuthService returns Success(null) for non-existent users
        _authServiceMock.Setup(s => s.GeneratePasswordResetTokenAsync(nonExistentEmail))
            .ReturnsAsync(Result<string?>.Success(null));

        // Act
        var result = await _controller.ForgotPassword(model);

        // Assert: Response is completely indistinguishable from existing user
        var redirect = result.Should().BeOfType<RedirectToActionResult>().Subject;
        redirect.ActionName.Should().Be(nameof(AccountController.ForgotPasswordConfirmation));

        _authServiceMock.Verify(s => s.GeneratePasswordResetTokenAsync(nonExistentEmail), Times.Once);
        _authServiceMock.Verify(s => s.SendPasswordResetEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>()), Times.Never);
    }

    [Fact]
    public async Task ForgotPassword_Post_WithInvalidModelState_ReturnsViewWithModel()
    {
        // Arrange
        var model = new ForgotPasswordViewModel { Email = "" };
        _controller.ModelState.AddModelError("Email", "Email address is required.");

        // Act
        var result = await _controller.ForgotPassword(model);

        // Assert
        var viewResult = result.Should().BeOfType<ViewResult>().Subject;
        viewResult.Model.Should().Be(model);
        _authServiceMock.Verify(s => s.GeneratePasswordResetTokenAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void ForgotPasswordConfirmation_Get_ReturnsView()
    {
        // Act
        var result = _controller.ForgotPasswordConfirmation();

        // Assert
        result.Should().BeOfType<ViewResult>();
    }

    [Fact]
    public void ResetPassword_Get_WithCodeAndEmail_PopulatesModelAndReturnsView()
    {
        // Arrange
        const string code = "sample-token-code";
        const string email = "user@medicare.com";

        // Act
        var result = _controller.ResetPassword(code, email);

        // Assert
        var viewResult = result.Should().BeOfType<ViewResult>().Subject;
        var model = viewResult.Model.Should().BeOfType<ResetPasswordViewModel>().Subject;
        model.Code.Should().Be(code);
        model.Email.Should().Be(email);
        _controller.ModelState.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ResetPassword_Get_WithoutCode_AddsModelError()
    {
        // Act
        var result = _controller.ResetPassword(code: null, email: "user@medicare.com");

        // Assert
        var viewResult = result.Should().BeOfType<ViewResult>().Subject;
        _controller.ModelState.IsValid.Should().BeFalse();
        _controller.ModelState[string.Empty]?.Errors.Should().Contain(e => e.ErrorMessage.Contains("A valid password reset token is required"));
    }

    [Fact]
    public async Task ResetPassword_Post_WithBase64UrlEncodedToken_DecodesTokenAndRedirectsToConfirmation()
    {
        // Arrange
        const string rawToken = "super-secret-identity-token-12345";
        var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(rawToken));
        const string email = "user@medicare.com";
        const string newPassword = "NewStrongPassword@2026";

        var model = new ResetPasswordViewModel
        {
            Code = encodedToken,
            Email = email,
            Password = newPassword,
            ConfirmPassword = newPassword
        };

        string? passedToken = null;
        _authServiceMock.Setup(s => s.ResetPasswordAsync(It.IsAny<ResetPasswordDto>()))
            .Callback<ResetPasswordDto>(dto => passedToken = dto.Token)
            .ReturnsAsync(Result.Success());

        // Act
        var result = await _controller.ResetPassword(model);

        // Assert
        var redirect = result.Should().BeOfType<RedirectToActionResult>().Subject;
        redirect.ActionName.Should().Be(nameof(AccountController.ResetPasswordConfirmation));

        passedToken.Should().Be(rawToken);
    }

    [Fact]
    public async Task ResetPassword_Post_WithRawUnencodedToken_FallsBackGracefullyAndSucceeds()
    {
        // Arrange
        const string rawToken = "raw token with spaces & symbols";
        const string email = "user@medicare.com";
        const string newPassword = "NewStrongPassword@2026";

        var model = new ResetPasswordViewModel
        {
            Code = rawToken,
            Email = email,
            Password = newPassword,
            ConfirmPassword = newPassword
        };

        string? passedToken = null;
        _authServiceMock.Setup(s => s.ResetPasswordAsync(It.IsAny<ResetPasswordDto>()))
            .Callback<ResetPasswordDto>(dto => passedToken = dto.Token)
            .ReturnsAsync(Result.Success());

        // Act
        var result = await _controller.ResetPassword(model);

        // Assert
        var redirect = result.Should().BeOfType<RedirectToActionResult>().Subject;
        redirect.ActionName.Should().Be(nameof(AccountController.ResetPasswordConfirmation));

        passedToken.Should().Be(rawToken);
    }

    [Fact]
    public async Task ResetPassword_Post_WhenServiceFails_AddsModelErrorAndReturnsView()
    {
        // Arrange
        var model = new ResetPasswordViewModel
        {
            Code = "token",
            Email = "user@medicare.com",
            Password = "NewStrongPassword@2026",
            ConfirmPassword = "NewStrongPassword@2026"
        };

        _authServiceMock.Setup(s => s.ResetPasswordAsync(It.IsAny<ResetPasswordDto>()))
            .ReturnsAsync(Result.Failure("The password reset token is invalid or has expired."));

        // Act
        var result = await _controller.ResetPassword(model);

        // Assert
        var viewResult = result.Should().BeOfType<ViewResult>().Subject;
        viewResult.Model.Should().Be(model);
        _controller.ModelState[string.Empty]?.Errors.Should().Contain(e => e.ErrorMessage.Contains("invalid or has expired"));
    }

    [Fact]
    public async Task ResetPassword_Post_WithInvalidModelState_ReturnsViewWithoutCallingService()
    {
        // Arrange
        var model = new ResetPasswordViewModel { Email = "" };
        _controller.ModelState.AddModelError("Password", "Password is required.");

        // Act
        var result = await _controller.ResetPassword(model);

        // Assert
        var viewResult = result.Should().BeOfType<ViewResult>().Subject;
        viewResult.Model.Should().Be(model);
        _authServiceMock.Verify(s => s.ResetPasswordAsync(It.IsAny<ResetPasswordDto>()), Times.Never);
    }

    [Fact]
    public void ResetPasswordConfirmation_Get_ReturnsView()
    {
        // Act
        var result = _controller.ResetPasswordConfirmation();

        // Assert
        result.Should().BeOfType<ViewResult>();
    }
}
