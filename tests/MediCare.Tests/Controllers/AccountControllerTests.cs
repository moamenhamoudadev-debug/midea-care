using System.Security.Claims;
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
using Microsoft.AspNetCore.RateLimiting;
using Moq;
using Xunit;

namespace MediCare.Tests.Controllers;

public class AccountControllerTests
{
    private readonly Mock<IAuthService> _authServiceMock;
    private readonly Mock<IDoctorService> _doctorServiceMock;
    private readonly Mock<UserManager<ApplicationUser>> _userManagerMock;
    private readonly AccountController _controller;

    public AccountControllerTests()
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
    }

    // =========================================================================
    // 1. Patient Registration Tests
    // =========================================================================

    [Fact]
    public void RegisterPatient_Get_WhenAlreadyAuthenticated_RedirectsToHome()
    {
        SetUserContext("existing_user", "Patient", isAuthenticated: true);

        var result = _controller.RegisterPatient();

        result.Should().BeOfType<RedirectToActionResult>()
            .Which.ActionName.Should().Be("Index");
    }

    [Fact]
    public void RegisterPatient_Get_WhenAnonymous_ReturnsView()
    {
        SetUserContext(null, "", isAuthenticated: false);

        var result = _controller.RegisterPatient();

        result.Should().BeOfType<ViewResult>();
    }

    [Fact]
    public async Task RegisterPatient_Post_WhenModelStateInvalid_ReturnsViewWithoutCallingService()
    {
        var model = new PatientRegisterViewModel();
        _controller.ModelState.AddModelError("Email", "Email is required");

        var result = await _controller.RegisterPatient(model);

        result.Should().BeOfType<ViewResult>()
            .Which.Model.Should().BeSameAs(model);
        _authServiceMock.Verify(a => a.RegisterPatientAsync(It.IsAny<PatientRegisterDto>()), Times.Never);
    }

    [Fact]
    public async Task RegisterPatient_Post_WhenAuthServiceFails_AddsModelErrorAndReturnsView()
    {
        var model = new PatientRegisterViewModel
        {
            FullName = "Ahmed Aly",
            Email = "duplicate@medicare.com",
            Password = "P@ssword123!",
            ConfirmPassword = "P@ssword123!",
            PhoneNumber = "01011112222"
        };

        _authServiceMock.Setup(a => a.RegisterPatientAsync(It.IsAny<PatientRegisterDto>()))
            .ReturnsAsync(Result<string>.Failure("An account with this email address already exists."));

        var result = await _controller.RegisterPatient(model);

        result.Should().BeOfType<ViewResult>()
            .Which.Model.Should().BeSameAs(model);
        _controller.ModelState.IsValid.Should().BeFalse();
        _controller.ModelState[string.Empty]!.Errors.Should().Contain(e => e.ErrorMessage.Contains("email address already exists"));
    }

    [Fact]
    public async Task RegisterPatient_Post_WhenAuthServiceSucceeds_SetsSuccessMessageAndRedirects()
    {
        var model = new PatientRegisterViewModel
        {
            FullName = "Ahmed Aly",
            Email = "newpatient@medicare.com",
            Password = "P@ssword123!",
            ConfirmPassword = "P@ssword123!",
            PhoneNumber = "01011112222"
        };

        _authServiceMock.Setup(a => a.RegisterPatientAsync(It.IsAny<PatientRegisterDto>()))
            .ReturnsAsync(Result<string>.Success("user_id_123"));

        var result = await _controller.RegisterPatient(model);

        result.Should().BeOfType<RedirectToActionResult>()
            .Which.ActionName.Should().Be("Index");
        _controller.TempData["SuccessMessage"]!.ToString().Should().Contain("patient account has been created");
    }

    // =========================================================================
    // 2. Doctor Registration Tests
    // =========================================================================

    [Fact]
    public async Task RegisterDoctor_Get_WhenAlreadyAuthenticated_RedirectsToHome()
    {
        SetUserContext("doc_user", "Doctor", isAuthenticated: true);

        var result = await _controller.RegisterDoctor();

        result.Should().BeOfType<RedirectToActionResult>()
            .Which.ActionName.Should().Be("Index");
    }

    [Fact]
    public async Task RegisterDoctor_Get_WhenAnonymous_LoadsSpecializationsAndReturnsView()
    {
        SetUserContext(null, "", isAuthenticated: false);
        var specs = new List<SpecializationDto>
        {
            new() { Id = 1, Name = "Cardiology" }
        };
        _doctorServiceMock.Setup(d => d.GetSpecializationsAsync()).ReturnsAsync(specs);

        var result = await _controller.RegisterDoctor();

        var viewResult = result.Should().BeOfType<ViewResult>().Subject;
        var model = viewResult.Model.Should().BeOfType<DoctorRegisterViewModel>().Subject;
        model.AvailableSpecializations.Should().HaveCount(1);
    }

    [Fact]
    public async Task RegisterDoctor_Post_WhenModelStateInvalid_ReloadsSpecializationsAndReturnsView()
    {
        var model = new DoctorRegisterViewModel();
        _controller.ModelState.AddModelError("LicenseNumber", "License is required");

        var specs = new List<SpecializationDto>
        {
            new() { Id = 1, Name = "Cardiology" }
        };
        _doctorServiceMock.Setup(d => d.GetSpecializationsAsync()).ReturnsAsync(specs);

        var result = await _controller.RegisterDoctor(model);

        var viewResult = result.Should().BeOfType<ViewResult>().Subject;
        viewResult.Model.Should().BeSameAs(model);
        model.AvailableSpecializations.Should().HaveCount(1);
        _authServiceMock.Verify(a => a.RegisterDoctorAsync(It.IsAny<DoctorRegisterDto>()), Times.Never);
    }

    [Fact]
    public async Task RegisterDoctor_Post_WhenAuthServiceFails_AddsModelErrorAndReturnsView()
    {
        var model = new DoctorRegisterViewModel
        {
            FullName = "Dr. Tarek",
            Email = "tarek@medicare.com",
            LicenseNumber = "LIC-12345",
            SpecializationId = 1
        };

        var specs = new List<SpecializationDto>
        {
            new() { Id = 1, Name = "Cardiology" }
        };
        _doctorServiceMock.Setup(d => d.GetSpecializationsAsync()).ReturnsAsync(specs);

        _authServiceMock.Setup(a => a.RegisterDoctorAsync(It.IsAny<DoctorRegisterDto>()))
            .ReturnsAsync(Result<string>.Failure("Doctor license already registered."));

        var result = await _controller.RegisterDoctor(model);

        var viewResult = result.Should().BeOfType<ViewResult>().Subject;
        viewResult.Model.Should().BeSameAs(model);
        _controller.ModelState.IsValid.Should().BeFalse();
        _controller.ModelState[string.Empty]!.Errors.Should().Contain(e => e.ErrorMessage.Contains("license already registered"));
    }

    [Fact]
    public async Task RegisterDoctor_Post_WhenAuthServiceSucceeds_SetsInfoMessageAndRedirectsToLogin()
    {
        var model = new DoctorRegisterViewModel
        {
            FullName = "Dr. Tarek",
            Email = "tarek@medicare.com",
            LicenseNumber = "LIC-12345",
            SpecializationId = 1
        };

        _authServiceMock.Setup(a => a.RegisterDoctorAsync(It.IsAny<DoctorRegisterDto>()))
            .ReturnsAsync(Result<string>.Success("doc_id_99"));

        var result = await _controller.RegisterDoctor(model);

        result.Should().BeOfType<RedirectToActionResult>()
            .Which.ActionName.Should().Be("Login");
        _controller.TempData["InfoMessage"]!.ToString().Should().Contain("pending administrator verification");
    }

    // =========================================================================
    // 3. Login Tests
    // =========================================================================

    [Fact]
    public void Login_Get_WhenAlreadyAuthenticated_RedirectsToHome()
    {
        SetUserContext("existing_user", "Patient", isAuthenticated: true);

        var result = _controller.Login();

        result.Should().BeOfType<RedirectToActionResult>()
            .Which.ActionName.Should().Be("Index");
    }

    [Fact]
    public void Login_Get_WhenAnonymous_ReturnsViewWithReturnUrl()
    {
        SetUserContext(null, "", isAuthenticated: false);

        var result = _controller.Login("/Appointments/Book/1");

        var viewResult = result.Should().BeOfType<ViewResult>().Subject;
        var model = viewResult.Model.Should().BeOfType<LoginViewModel>().Subject;
        model.ReturnUrl.Should().Be("/Appointments/Book/1");
    }

    [Fact]
    public async Task Login_Post_WhenModelStateInvalid_ReturnsViewWithoutCallingService()
    {
        var model = new LoginViewModel();
        _controller.ModelState.AddModelError("Email", "Email is required");

        var result = await _controller.Login(model);

        result.Should().BeOfType<ViewResult>()
            .Which.Model.Should().BeSameAs(model);
        _authServiceMock.Verify(a => a.LoginAsync(It.IsAny<LoginDto>()), Times.Never);
    }

    [Fact]
    public async Task Login_Post_WhenAuthServiceFails_AddsModelErrorAndReturnsView()
    {
        var model = new LoginViewModel
        {
            Email = "user@medicare.com",
            Password = "WrongPassword!"
        };

        _authServiceMock.Setup(a => a.LoginAsync(It.IsAny<LoginDto>()))
            .ReturnsAsync(Result.Failure("Invalid email or password."));

        var result = await _controller.Login(model);

        result.Should().BeOfType<ViewResult>()
            .Which.Model.Should().BeSameAs(model);
        _controller.ModelState.IsValid.Should().BeFalse();
        _controller.ModelState[string.Empty]!.Errors.Should().Contain(e => e.ErrorMessage.Contains("Invalid email or password"));
    }

    [Fact]
    public async Task Login_Post_WhenAuthServiceSucceeds_WithoutReturnUrl_WhenUserNotFound_RedirectsToHome()
    {
        var model = new LoginViewModel
        {
            Email = "user@medicare.com",
            Password = "CorrectPassword123!"
        };

        _authServiceMock.Setup(a => a.LoginAsync(It.IsAny<LoginDto>()))
            .ReturnsAsync(Result.Success());
        _userManagerMock.Setup(m => m.FindByEmailAsync("user@medicare.com"))
            .ReturnsAsync((ApplicationUser?)null);

        var result = await _controller.Login(model);

        var redirect = result.Should().BeOfType<RedirectToActionResult>().Subject;
        redirect.ActionName.Should().Be("Index");
        redirect.ControllerName.Should().Be("Home");
        _controller.TempData["SuccessMessage"]!.ToString().Should().Contain("successfully signed in");
    }

    [Fact]
    public async Task Login_Post_WhenAuthServiceSucceeds_WithoutReturnUrl_WhenRoleIsAdmin_RedirectsToAdminDashboard()
    {
        var model = new LoginViewModel
        {
            Email = "admin@medicare.com",
            Password = "AdminPassword123!"
        };
        var user = new ApplicationUser { Email = model.Email };

        _authServiceMock.Setup(a => a.LoginAsync(It.IsAny<LoginDto>()))
            .ReturnsAsync(Result.Success());
        _userManagerMock.Setup(m => m.FindByEmailAsync(model.Email))
            .ReturnsAsync(user);
        _userManagerMock.Setup(m => m.GetRolesAsync(user))
            .ReturnsAsync(new List<string> { "Admin" });

        var result = await _controller.Login(model);

        var redirect = result.Should().BeOfType<RedirectToActionResult>().Subject;
        redirect.ActionName.Should().Be("Index");
        redirect.ControllerName.Should().Be("Admin");
    }

    [Fact]
    public async Task Login_Post_WhenAuthServiceSucceeds_WithoutReturnUrl_WhenRoleIsDoctor_RedirectsToDoctorAppointments()
    {
        var model = new LoginViewModel
        {
            Email = "doctor@medicare.com",
            Password = "DoctorPassword123!"
        };
        var user = new ApplicationUser { Email = model.Email };

        _authServiceMock.Setup(a => a.LoginAsync(It.IsAny<LoginDto>()))
            .ReturnsAsync(Result.Success());
        _userManagerMock.Setup(m => m.FindByEmailAsync(model.Email))
            .ReturnsAsync(user);
        _userManagerMock.Setup(m => m.GetRolesAsync(user))
            .ReturnsAsync(new List<string> { "Doctor" });

        var result = await _controller.Login(model);

        var redirect = result.Should().BeOfType<RedirectToActionResult>().Subject;
        redirect.ActionName.Should().Be("Appointments");
        redirect.ControllerName.Should().Be("Doctor");
    }

    [Fact]
    public async Task Login_Post_WhenAuthServiceSucceeds_WithoutReturnUrl_WhenRoleIsPatient_RedirectsToDoctorsDirectory()
    {
        var model = new LoginViewModel
        {
            Email = "patient@medicare.com",
            Password = "PatientPassword123!"
        };
        var user = new ApplicationUser { Email = model.Email };

        _authServiceMock.Setup(a => a.LoginAsync(It.IsAny<LoginDto>()))
            .ReturnsAsync(Result.Success());
        _userManagerMock.Setup(m => m.FindByEmailAsync(model.Email))
            .ReturnsAsync(user);
        _userManagerMock.Setup(m => m.GetRolesAsync(user))
            .ReturnsAsync(new List<string> { "Patient" });

        var result = await _controller.Login(model);

        var redirect = result.Should().BeOfType<RedirectToActionResult>().Subject;
        redirect.ActionName.Should().Be("Index");
        redirect.ControllerName.Should().Be("Doctors");
    }

    [Fact]
    public async Task Login_Post_WhenAuthServiceSucceeds_WithoutReturnUrl_WhenRoleIsUnknown_RedirectsToHome()
    {
        var model = new LoginViewModel
        {
            Email = "pharmacist@medicare.com",
            Password = "PharmPassword123!"
        };
        var user = new ApplicationUser { Email = model.Email };

        _authServiceMock.Setup(a => a.LoginAsync(It.IsAny<LoginDto>()))
            .ReturnsAsync(Result.Success());
        _userManagerMock.Setup(m => m.FindByEmailAsync(model.Email))
            .ReturnsAsync(user);
        _userManagerMock.Setup(m => m.GetRolesAsync(user))
            .ReturnsAsync(new List<string> { "Pharmacist" });

        var result = await _controller.Login(model);

        var redirect = result.Should().BeOfType<RedirectToActionResult>().Subject;
        redirect.ActionName.Should().Be("Index");
        redirect.ControllerName.Should().Be("Home");
    }

    [Fact]
    public async Task Login_Post_WhenAuthServiceSucceeds_WithValidLocalReturnUrl_RedirectsToReturnUrl()
    {
        var model = new LoginViewModel
        {
            Email = "patient@medicare.com",
            Password = "PatientPassword123!",
            ReturnUrl = "/Appointments/Book/5"
        };
        var urlHelperMock = new Mock<IUrlHelper>();
        urlHelperMock.Setup(u => u.IsLocalUrl("/Appointments/Book/5")).Returns(true);
        _controller.Url = urlHelperMock.Object;

        _authServiceMock.Setup(a => a.LoginAsync(It.IsAny<LoginDto>()))
            .ReturnsAsync(Result.Success());

        var result = await _controller.Login(model);

        var redirect = result.Should().BeOfType<RedirectResult>().Subject;
        redirect.Url.Should().Be("/Appointments/Book/5");
    }

    [Fact]
    public async Task Login_Post_WhenAuthServiceSucceeds_WithMaliciousReturnUrl_IgnoresAndRedirectsByRole()
    {
        var model = new LoginViewModel
        {
            Email = "patient@medicare.com",
            Password = "PatientPassword123!",
            ReturnUrl = "https://evil.com/phishing"
        };
        var user = new ApplicationUser { Email = model.Email };

        var urlHelperMock = new Mock<IUrlHelper>();
        urlHelperMock.Setup(u => u.IsLocalUrl("https://evil.com/phishing")).Returns(false);
        _controller.Url = urlHelperMock.Object;

        _authServiceMock.Setup(a => a.LoginAsync(It.IsAny<LoginDto>()))
            .ReturnsAsync(Result.Success());
        _userManagerMock.Setup(m => m.FindByEmailAsync(model.Email))
            .ReturnsAsync(user);
        _userManagerMock.Setup(m => m.GetRolesAsync(user))
            .ReturnsAsync(new List<string> { "Patient" });

        var result = await _controller.Login(model);

        var redirect = result.Should().BeOfType<RedirectToActionResult>().Subject;
        redirect.ActionName.Should().Be("Index");
        redirect.ControllerName.Should().Be("Doctors");
    }

    // =========================================================================
    // 4. Logout Tests
    // =========================================================================

    [Fact]
    public async Task Logout_Post_CallsLogoutAsync_SetsSuccessMessageAndRedirectsToHome()
    {
        SetUserContext("doc_user", "Doctor");

        _authServiceMock.Setup(a => a.LogoutAsync())
            .Returns(Task.CompletedTask);

        var result = await _controller.Logout();

        _authServiceMock.Verify(a => a.LogoutAsync(), Times.Once);
        result.Should().BeOfType<RedirectToActionResult>()
            .Which.ActionName.Should().Be("Index");
        _controller.TempData["SuccessMessage"]!.ToString().Should().Contain("logged out");
    }

    // =========================================================================
    // 5. Rate Limiting Security Tests
    // =========================================================================

    [Fact]
    public void RegisterPatient_Post_HasStrictRateLimitingAndAntiforgeryAttributes()
    {
        var method = typeof(AccountController).GetMethod(nameof(AccountController.RegisterPatient), new[] { typeof(PatientRegisterViewModel) });
        method.Should().NotBeNull();

        var rateLimitAttr = method!.GetCustomAttributes(typeof(EnableRateLimitingAttribute), false).FirstOrDefault() as EnableRateLimitingAttribute;
        rateLimitAttr.Should().NotBeNull("RegisterPatient POST must be protected by [EnableRateLimiting]");
        rateLimitAttr!.PolicyName.Should().Be("RegistrationRateLimitPolicy");

        var antiforgery = method.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), false);
        antiforgery.Should().NotBeEmpty("RegisterPatient POST must have [ValidateAntiForgeryToken]");
    }

    [Fact]
    public void RegisterDoctor_Post_HasStrictRateLimitingAndAntiforgeryAttributes()
    {
        var method = typeof(AccountController).GetMethod(nameof(AccountController.RegisterDoctor), new[] { typeof(DoctorRegisterViewModel) });
        method.Should().NotBeNull();

        var rateLimitAttr = method!.GetCustomAttributes(typeof(EnableRateLimitingAttribute), false).FirstOrDefault() as EnableRateLimitingAttribute;
        rateLimitAttr.Should().NotBeNull("RegisterDoctor POST must be protected by [EnableRateLimiting]");
        rateLimitAttr!.PolicyName.Should().Be("RegistrationRateLimitPolicy");

        var antiforgery = method.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), false);
        antiforgery.Should().NotBeEmpty("RegisterDoctor POST must have [ValidateAntiForgeryToken]");
    }
}
