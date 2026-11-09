using System.Linq.Expressions;
using FluentAssertions;
using FluentValidation;
using MediCare.Data.Entities;
using MediCare.Data.UnitOfWork;
using MediCare.Services.DTOs;
using MediCare.Services.Implementations;
using MediCare.Services.Validators;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace MediCare.Tests.Services;

public class AuthServiceTests
{
    private readonly Mock<UserManager<ApplicationUser>> _mockUserManager;
    private readonly Mock<SignInManager<ApplicationUser>> _mockSignInManager;
    private readonly Mock<IUnitOfWork> _mockUow;
    private readonly Mock<ILogger<AuthService>> _mockLogger;
    private readonly IValidator<PatientRegisterDto> _patientValidator;
    private readonly IValidator<DoctorRegisterDto> _doctorValidator;
    private readonly IValidator<LoginDto> _loginValidator;
    private readonly AuthService _sut;

    public AuthServiceTests()
    {
        var userStore = new Mock<IUserStore<ApplicationUser>>();
        _mockUserManager = new Mock<UserManager<ApplicationUser>>(
            userStore.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        var contextAccessor = new Mock<IHttpContextAccessor>();
        var claimsFactory = new Mock<IUserClaimsPrincipalFactory<ApplicationUser>>();
        _mockSignInManager = new Mock<SignInManager<ApplicationUser>>(
            _mockUserManager.Object, contextAccessor.Object, claimsFactory.Object, null!, null!, null!, null!);

        _mockUow = new Mock<IUnitOfWork>();
        _mockLogger = new Mock<ILogger<AuthService>>();
        _patientValidator = new PatientRegisterValidator();
        _doctorValidator = new DoctorRegisterValidator();
        _loginValidator = new LoginValidator();

        _sut = new AuthService(
            _mockUserManager.Object,
            _mockSignInManager.Object,
            _mockUow.Object,
            _patientValidator,
            _doctorValidator,
            _loginValidator,
            logger: _mockLogger.Object);
    }

    [Fact]
    public async Task RegisterPatientAsync_WithValidDto_CreatesUserAndAssignsPatientRole()
    {
        // Arrange
        var dto = new PatientRegisterDto
        {
            FullName = "Khaled Omar",
            Email = "khaled@test.com",
            Password = "P@ssword123!",
            ConfirmPassword = "P@ssword123!",
            PhoneNumber = "+201012345678",
            DateOfBirth = new DateTime(1990, 5, 20),
            Gender = "Male",
            BloodGroup = "A+"
        };

        _mockUserManager.Setup(m => m.FindByEmailAsync(dto.Email))
            .ReturnsAsync((ApplicationUser?)null);

        _mockUserManager.Setup(m => m.CreateAsync(It.IsAny<ApplicationUser>(), dto.Password))
            .ReturnsAsync(IdentityResult.Success);

        _mockUserManager.Setup(m => m.AddToRoleAsync(It.IsAny<ApplicationUser>(), "Patient"))
            .ReturnsAsync(IdentityResult.Success);

        _mockUow.Setup(u => u.Patients.AddAsync(It.IsAny<Patient>()))
            .Returns(Task.CompletedTask);

        _mockUow.Setup(u => u.CommitAsync())
            .ReturnsAsync(1);

        // Act
        var result = await _sut.RegisterPatientAsync(dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _mockUserManager.Verify(m => m.CreateAsync(It.Is<ApplicationUser>(u => u.Email == dto.Email), dto.Password), Times.Once);
        _mockUserManager.Verify(m => m.AddToRoleAsync(It.IsAny<ApplicationUser>(), "Patient"), Times.Once);
        _mockUow.Verify(u => u.Patients.AddAsync(It.IsAny<Patient>()), Times.Once);
        _mockSignInManager.Verify(s => s.SignInAsync(It.IsAny<ApplicationUser>(), false, null), Times.Once);
    }

    [Fact]
    public async Task RegisterDoctorAsync_WithValidDto_SetsIsApprovedToFalseAndDoesNotSignIn()
    {
        // Arrange
        var dto = new DoctorRegisterDto
        {
            FullName = "Dr. Ahmed Taha",
            Email = "ahmed.taha@clinic.com",
            Password = "P@ssword123!",
            ConfirmPassword = "P@ssword123!",
            SpecializationId = 2,
            LicenseNumber = "LIC-EGY-4455",
            ConsultationFee = 300,
            Bio = "Cardiologist"
        };

        _mockUserManager.Setup(m => m.FindByEmailAsync(dto.Email))
            .ReturnsAsync((ApplicationUser?)null);

        _mockUow.Setup(u => u.Specializations.GetByIdAsync(2))
            .ReturnsAsync(new Specialization { Id = 2, Name = "Cardiology" });

        _mockUow.Setup(u => u.Doctors.FindAsync(It.IsAny<Expression<Func<Doctor, bool>>>()))
            .ReturnsAsync(new List<Doctor>());

        _mockUserManager.Setup(m => m.CreateAsync(It.IsAny<ApplicationUser>(), dto.Password))
            .ReturnsAsync(IdentityResult.Success);

        _mockUserManager.Setup(m => m.AddToRoleAsync(It.IsAny<ApplicationUser>(), "Doctor"))
            .ReturnsAsync(IdentityResult.Success);

        Doctor? capturedDoctor = null;
        _mockUow.Setup(u => u.Doctors.AddAsync(It.IsAny<Doctor>()))
            .Callback<Doctor>(d => capturedDoctor = d)
            .Returns(Task.CompletedTask);

        _mockUow.Setup(u => u.CommitAsync())
            .ReturnsAsync(1);

        // Act
        var result = await _sut.RegisterDoctorAsync(dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        capturedDoctor.Should().NotBeNull();
        capturedDoctor!.IsApproved.Should().BeFalse("new doctors must require administrator approval");
        capturedDoctor.LicenseNumber.Should().Be("LIC-EGY-4455");

        // Doctor must NOT be signed in automatically
        _mockSignInManager.Verify(s => s.SignInAsync(It.IsAny<ApplicationUser>(), It.IsAny<bool>(), null), Times.Never);
    }

    [Fact]
    public async Task LoginAsync_WhenDoctorIsNotApproved_ReturnsPendingApprovalMessage()
    {
        // Arrange
        var dto = new LoginDto
        {
            Email = "pending.doctor@clinic.com",
            Password = "P@ssword123!"
        };

        var user = new ApplicationUser { Id = "doc-guid-1", UserName = dto.Email, Email = dto.Email };

        _mockUserManager.Setup(m => m.FindByEmailAsync(dto.Email))
            .ReturnsAsync(user);

        _mockSignInManager.Setup(s => s.CheckPasswordSignInAsync(user, dto.Password, true))
            .ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.Success);

        _mockUserManager.Setup(m => m.IsInRoleAsync(user, "Doctor"))
            .ReturnsAsync(true);

        _mockUow.Setup(u => u.Doctors.FindAsync(It.IsAny<Expression<Func<Doctor, bool>>>()))
            .ReturnsAsync(new List<Doctor> { new() { UserId = user.Id, IsApproved = false } });

        // Act
        var result = await _sut.LoginAsync(dto);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("pending administrator approval");
        _mockSignInManager.Verify(s => s.SignInAsync(It.IsAny<ApplicationUser>(), It.IsAny<bool>(), null), Times.Never);
    }

    [Fact]
    public async Task LoginAsync_WhenDoctorIsNotApproved_AndWrongPasswordProvided_ReturnsGenericErrorWithoutExposingApprovalStatus()
    {
        // Arrange
        var dto = new LoginDto
        {
            Email = "pending.doctor@clinic.com",
            Password = "WrongPassword123!"
        };

        var user = new ApplicationUser { Id = "doc-guid-1", UserName = dto.Email, Email = dto.Email };

        _mockUserManager.Setup(m => m.FindByEmailAsync(dto.Email))
            .ReturnsAsync(user);

        _mockSignInManager.Setup(s => s.CheckPasswordSignInAsync(user, dto.Password, true))
            .ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.Failed);

        // Act
        var result = await _sut.LoginAsync(dto);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Invalid email or password.");
        _mockUow.Verify(u => u.Doctors.FindAsync(It.IsAny<Expression<Func<Doctor, bool>>>()), Times.Never,
            "Doctor approval status must never be checked or disclosed before verifying password credentials.");
        _mockSignInManager.Verify(s => s.SignInAsync(It.IsAny<ApplicationUser>(), It.IsAny<bool>(), null), Times.Never);
    }

    [Fact]
    public async Task LoginAsync_WhenAccountIsLockedOut_ReturnsLockoutMessage()
    {
        // Arrange
        var dto = new LoginDto
        {
            Email = "locked.user@clinic.com",
            Password = "WrongPassword!"
        };

        var user = new ApplicationUser { Id = "user-guid-locked", UserName = dto.Email, Email = dto.Email };

        _mockUserManager.Setup(m => m.FindByEmailAsync(dto.Email))
            .ReturnsAsync(user);

        _mockSignInManager.Setup(s => s.CheckPasswordSignInAsync(user, dto.Password, true))
            .ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.LockedOut);

        // Act
        var result = await _sut.LoginAsync(dto);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("locked out");
        _mockSignInManager.Verify(s => s.CheckPasswordSignInAsync(user, dto.Password, true), Times.Once);
        _mockSignInManager.Verify(s => s.SignInAsync(It.IsAny<ApplicationUser>(), It.IsAny<bool>(), null), Times.Never);
    }

    [Fact]
    public async Task LoginAsync_LockoutCounterIncrements_AndReturnsLockoutAfterFifthFailure()
    {
        // Arrange
        var dto = new LoginDto
        {
            Email = "bruteforce.target@clinic.com",
            Password = "WrongPasswordAttempt!"
        };

        var user = new ApplicationUser { Id = "bruteforce-user-id", UserName = dto.Email, Email = dto.Email };

        _mockUserManager.Setup(m => m.FindByEmailAsync(dto.Email))
            .ReturnsAsync(user);

        // Simulate 4 failed attempts followed by lockout on 5th attempt
        _mockSignInManager.SetupSequence(s => s.CheckPasswordSignInAsync(user, dto.Password, true))
            .ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.Failed)
            .ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.Failed)
            .ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.Failed)
            .ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.Failed)
            .ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.LockedOut);

        // Act & Assert: Attempts 1 to 4 return generic failure
        for (int attempt = 1; attempt <= 4; attempt++)
        {
            var attemptResult = await _sut.LoginAsync(dto);
            attemptResult.IsSuccess.Should().BeFalse();
            attemptResult.Error.Should().Be("Invalid email or password.");
        }

        // 5th attempt triggers lockout
        var fifthResult = await _sut.LoginAsync(dto);
        fifthResult.IsSuccess.Should().BeFalse();
        fifthResult.Error.Should().Contain("locked out due to multiple failed login attempts");
        fifthResult.Error.Should().Contain("15 minutes");

        // Verify lockoutOnFailure: true was enforced every time
        _mockSignInManager.Verify(s => s.CheckPasswordSignInAsync(user, dto.Password, true), Times.Exactly(5));
        _mockSignInManager.Verify(s => s.SignInAsync(It.IsAny<ApplicationUser>(), It.IsAny<bool>(), null), Times.Never);
    }

    [Fact]
    public async Task LoginAsync_WhenAdminLoginFails_LogsSecurityAlertWithoutPassword()
    {
        // Arrange
        var dto = new LoginDto
        {
            Email = "admin@medicare.com",
            Password = "WrongAdminPassword123!"
        };

        var user = new ApplicationUser { Id = "admin-guid-1", UserName = dto.Email, Email = dto.Email };

        _mockUserManager.Setup(m => m.FindByEmailAsync(dto.Email))
            .ReturnsAsync(user);

        _mockUserManager.Setup(m => m.IsInRoleAsync(user, "Admin"))
            .ReturnsAsync(true);

        _mockSignInManager.Setup(s => s.CheckPasswordSignInAsync(user, dto.Password, true))
            .ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.Failed);

        // Act
        var result = await _sut.LoginAsync(dto);

        // Assert
        result.IsSuccess.Should().BeFalse();
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("SECURITY ALERT") && v.ToString()!.Contains("admin@medicare.com") && !v.ToString()!.Contains("WrongAdminPassword123!")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
        _mockSignInManager.Verify(s => s.SignInAsync(It.IsAny<ApplicationUser>(), It.IsAny<bool>(), null), Times.Never);
    }

    [Fact]
    public async Task LoginAsync_WhenCredentialsValidAndDoctorApproved_SignsInSuccessfully()
    {
        // Arrange
        var dto = new LoginDto
        {
            Email = "approved.doctor@clinic.com",
            Password = "ValidPassword123!",
            RememberMe = true
        };

        var user = new ApplicationUser { Id = "doc-approved-1", UserName = dto.Email, Email = dto.Email };

        _mockUserManager.Setup(m => m.FindByEmailAsync(dto.Email))
            .ReturnsAsync(user);

        _mockSignInManager.Setup(s => s.CheckPasswordSignInAsync(user, dto.Password, true))
            .ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.Success);

        _mockUserManager.Setup(m => m.IsInRoleAsync(user, "Doctor"))
            .ReturnsAsync(true);

        _mockUow.Setup(u => u.Doctors.FindAsync(It.IsAny<Expression<Func<Doctor, bool>>>()))
            .ReturnsAsync(new List<Doctor> { new() { UserId = user.Id, IsApproved = true } });

        // Act
        var result = await _sut.LoginAsync(dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _mockSignInManager.Verify(s => s.SignInAsync(user, dto.RememberMe, null), Times.Once);
    }

    [Fact]
    public async Task GetPatientProfileAsync_WhenUserAndPatientExist_ReturnsSuccessWithProfileDto()
    {
        // Arrange
        const string userId = "patient-user-guid";
        var user = new ApplicationUser
        {
            Id = userId,
            FullName = "Khaled Omar",
            Email = "khaled@medicare.com",
            PhoneNumber = "+201099112233"
        };
        var patient = new Patient
        {
            Id = 42,
            UserId = userId,
            DateOfBirth = new DateTime(1990, 5, 20),
            Gender = "Male",
            BloodGroup = "O+",
            EmergencyContact = "+201099112234",
            Allergies = "Penicillin",
            MedicalHistory = "Mild hypertension"
        };

        _mockUserManager.Setup(m => m.FindByIdAsync(userId))
            .ReturnsAsync(user);

        _mockUow.Setup(u => u.Patients.FindAsync(It.IsAny<Expression<Func<Patient, bool>>>()))
            .ReturnsAsync(new List<Patient> { patient });

        // Act
        var result = await _sut.GetPatientProfileAsync(userId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Id.Should().Be(42);
        result.Value.FullName.Should().Be("Khaled Omar");
        result.Value.Email.Should().Be("khaled@medicare.com");
        result.Value.BloodGroup.Should().Be("O+");
        result.Value.Allergies.Should().Be("Penicillin");
        result.Value.MedicalHistory.Should().Be("Mild hypertension");
    }

    [Fact]
    public async Task UpdatePatientProfileAsync_WithValidDto_UpdatesIdentityAndPatientEntities()
    {
        // Arrange
        const string userId = "patient-user-guid";
        var user = new ApplicationUser
        {
            Id = userId,
            FullName = "Old Name",
            PhoneNumber = "+201000000000"
        };
        var patient = new Patient
        {
            Id = 42,
            UserId = userId,
            DateOfBirth = new DateTime(1990, 1, 1),
            Gender = "Male",
            BloodGroup = "A+",
            Allergies = null,
            MedicalHistory = null
        };

        var updateDto = new PatientUpdateProfileDto
        {
            FullName = "Updated Name",
            PhoneNumber = "+201011112222",
            DateOfBirth = new DateTime(1992, 3, 15),
            Gender = "Male",
            BloodGroup = "B+",
            EmergencyContact = "+201099998888",
            Allergies = "Aspirin, Peanuts",
            MedicalHistory = "Asthma diagnosed 2021"
        };

        _mockUserManager.Setup(m => m.FindByIdAsync(userId))
            .ReturnsAsync(user);

        _mockUserManager.Setup(m => m.UpdateAsync(user))
            .ReturnsAsync(IdentityResult.Success);

        _mockUow.Setup(u => u.Patients.FindAsync(It.IsAny<Expression<Func<Patient, bool>>>()))
            .ReturnsAsync(new List<Patient> { patient });

        _mockUow.Setup(u => u.CommitAsync())
            .ReturnsAsync(1);

        // Act
        var result = await _sut.UpdatePatientProfileAsync(userId, updateDto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        user.FullName.Should().Be("Updated Name");
        user.PhoneNumber.Should().Be("+201011112222");
        patient.BloodGroup.Should().Be("B+");
        patient.Allergies.Should().Be("Aspirin, Peanuts");
        patient.MedicalHistory.Should().Be("Asthma diagnosed 2021");

        _mockUow.Verify(u => u.Patients.Update(patient), Times.Once);
        _mockUow.Verify(u => u.CommitAsync(), Times.Once);
    }

    [Fact]
    public async Task UpdatePatientProfileAsync_WithInvalidDobInFuture_ReturnsValidationError()
    {
        // Arrange
        const string userId = "patient-user-guid";
        var updateDto = new PatientUpdateProfileDto
        {
            FullName = "Valid Name",
            DateOfBirth = DateTime.Today.AddDays(5), // Future date
            Gender = "Male"
        };

        // Act
        var result = await _sut.UpdatePatientProfileAsync(userId, updateDto);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Date of Birth must be in the past");
        _mockUow.Verify(u => u.CommitAsync(), Times.Never);
    }

    [Fact]
    public void AccountController_LoginPost_HasLoginRateLimitPolicyConfigured()
    {
        var method = typeof(MediCare.Web.Controllers.AccountController).GetMethods()
            .First(m => m.Name == "Login" && m.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.HttpPostAttribute), false).Any());

        var rateLimitAttr = method.GetCustomAttributes(typeof(Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute), false)
            .FirstOrDefault() as Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute;

        rateLimitAttr.Should().NotBeNull("Login POST action must have [EnableRateLimiting]");
        rateLimitAttr!.PolicyName.Should().Be("LoginRateLimitPolicy");
    }

    [Fact]
    public void AccountController_LoginGet_DoesNotHaveRateLimiting()
    {
        var method = typeof(MediCare.Web.Controllers.AccountController).GetMethods()
            .First(m => m.Name == "Login" && m.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.HttpGetAttribute), false).Any());

        var rateLimitAttr = method.GetCustomAttributes(typeof(Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute), false)
            .FirstOrDefault();

        rateLimitAttr.Should().BeNull("Login GET action must NOT have rate limiting so initial page loads are never blocked");
    }

    [Fact]
    public async Task ChangePasswordAsync_WhenSuccessful_RemovesMustChangePasswordClaimAndRefreshesSignIn()
    {
        // Arrange
        const string userId = "pharmacist-guid-1";
        var user = new ApplicationUser { Id = userId, Email = "pharm@medicare.com", UserName = "pharm@medicare.com" };

        _mockUserManager.Setup(m => m.FindByIdAsync(userId)).ReturnsAsync(user);
        _mockUserManager.Setup(m => m.ChangePasswordAsync(user, "OldPass123!", "NewPass123!"))
            .ReturnsAsync(IdentityResult.Success);

        var claim = new System.Security.Claims.Claim("MustChangePassword", "true");
        _mockUserManager.Setup(m => m.GetClaimsAsync(user))
            .ReturnsAsync(new List<System.Security.Claims.Claim> { claim });
        _mockUserManager.Setup(m => m.RemoveClaimAsync(user, claim))
            .ReturnsAsync(IdentityResult.Success);

        _mockSignInManager.Setup(s => s.RefreshSignInAsync(user))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _sut.ChangePasswordAsync(userId, "OldPass123!", "NewPass123!");

        // Assert
        result.IsSuccess.Should().BeTrue();
        _mockUserManager.Verify(m => m.RemoveClaimAsync(user, claim), Times.Once);
        _mockSignInManager.Verify(s => s.RefreshSignInAsync(user), Times.Once);
    }

    [Fact]
    public async Task ChangePasswordAsync_WhenPasswordRequirementsFail_ReturnsFailure()
    {
        // Arrange
        const string userId = "pharmacist-guid-1";
        var user = new ApplicationUser { Id = userId, Email = "pharm@medicare.com" };

        _mockUserManager.Setup(m => m.FindByIdAsync(userId)).ReturnsAsync(user);
        _mockUserManager.Setup(m => m.ChangePasswordAsync(user, "OldPass123!", "weak"))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Password too short." }));

        // Act
        var result = await _sut.ChangePasswordAsync(userId, "OldPass123!", "weak");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Password too short");
        _mockUserManager.Verify(m => m.RemoveClaimAsync(It.IsAny<ApplicationUser>(), It.IsAny<System.Security.Claims.Claim>()), Times.Never);
    }

    [Fact]
    public async Task UpdatePatientProfileAsync_WhenUserIdEmpty_ReturnsFailure()
    {
        var dto = new PatientUpdateProfileDto { FullName = "Name" };
        var result = await _sut.UpdatePatientProfileAsync("", dto);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("User ID is required");
    }

    [Fact]
    public async Task UpdatePatientProfileAsync_WhenUserNotFound_ReturnsFailure()
    {
        _mockUserManager.Setup(m => m.FindByIdAsync("user-404")).ReturnsAsync((ApplicationUser?)null);

        var dto = new PatientUpdateProfileDto
        {
            FullName = "Ali Hassan",
            PhoneNumber = "01012345678",
            DateOfBirth = new DateTime(1995, 1, 1),
            Gender = "Male"
        };
        var result = await _sut.UpdatePatientProfileAsync("user-404", dto);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Patient user account not found");
    }

    [Fact]
    public async Task UpdatePatientProfileAsync_WhenPatientNotFound_ReturnsFailure()
    {
        var user = new ApplicationUser { Id = "user-1", FullName = "Ali" };
        _mockUserManager.Setup(m => m.FindByIdAsync("user-1")).ReturnsAsync(user);
        _mockUow.Setup(u => u.Patients.FindAsync(It.IsAny<Expression<Func<Patient, bool>>>()))
            .ReturnsAsync(new List<Patient>());

        var dto = new PatientUpdateProfileDto
        {
            FullName = "Ali Hassan",
            PhoneNumber = "01012345678",
            DateOfBirth = new DateTime(1995, 1, 1),
            Gender = "Male"
        };
        var result = await _sut.UpdatePatientProfileAsync("user-1", dto);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Patient profile details not found");
    }

    [Fact]
    public async Task UpdatePatientProfileAsync_WhenValid_UpdatesAndCommits()
    {
        var user = new ApplicationUser { Id = "user-1", FullName = "Old Name" };
        var patient = new Patient { Id = 10, UserId = "user-1", DateOfBirth = new DateTime(1990, 1, 1) };

        _mockUserManager.Setup(m => m.FindByIdAsync("user-1")).ReturnsAsync(user);
        _mockUserManager.Setup(m => m.UpdateAsync(user)).ReturnsAsync(IdentityResult.Success);
        _mockUow.Setup(u => u.Patients.FindAsync(It.IsAny<Expression<Func<Patient, bool>>>()))
            .ReturnsAsync(new List<Patient> { patient });
        _mockUow.Setup(u => u.CommitAsync()).ReturnsAsync(1);

        var dto = new PatientUpdateProfileDto
        {
            FullName = "New Updated Name",
            PhoneNumber = "01012345678",
            DateOfBirth = new DateTime(1995, 5, 20),
            Gender = "Male",
            BloodGroup = "A+",
            EmergencyContact = "01099999999",
            Allergies = "None",
            MedicalHistory = "Clean"
        };

        var result = await _sut.UpdatePatientProfileAsync("user-1", dto);

        result.IsSuccess.Should().BeTrue();
        user.FullName.Should().Be("New Updated Name");
        patient.BloodGroup.Should().Be("A+");
        _mockUow.Verify(u => u.CommitAsync(), Times.Once);
    }
}
