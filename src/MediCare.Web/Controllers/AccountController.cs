using MediCare.Data.Entities;
using MediCare.Services.Contracts;
using MediCare.Services.DTOs;
using MediCare.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MediCare.Web.Controllers;

[Authorize]
public class AccountController : Controller
{
    private readonly IAuthService _authService;
    private readonly IDoctorService _doctorService;
    private readonly UserManager<ApplicationUser>? _userManager;

    public AccountController(IAuthService authService, IDoctorService doctorService)
        : this(authService, doctorService, null)
    {
    }

    [ActivatorUtilitiesConstructor]
    public AccountController(
        IAuthService authService,
        IDoctorService doctorService,
        UserManager<ApplicationUser>? userManager)
    {
        _authService = authService;
        _doctorService = doctorService;
        _userManager = userManager;
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult RegisterPatient()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Index", "Home");
        }
        return View(new PatientRegisterViewModel());
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("RegistrationRateLimitPolicy")]
    public async Task<IActionResult> RegisterPatient(PatientRegisterViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var dto = new PatientRegisterDto
        {
            FullName = model.FullName,
            Email = model.Email,
            Password = model.Password,
            ConfirmPassword = model.ConfirmPassword,
            PhoneNumber = model.PhoneNumber,
            DateOfBirth = model.DateOfBirth,
            Gender = model.Gender,
            BloodGroup = model.BloodGroup,
            EmergencyContact = model.EmergencyContact,
            Allergies = model.Allergies,
            MedicalHistory = model.MedicalHistory
        };

        var result = await _authService.RegisterPatientAsync(dto);
        if (result.IsSuccess)
        {
            TempData["SuccessMessage"] = "Your patient account has been created successfully! Welcome to MediCare.";
            return RedirectToAction("Index", "Home");
        }

        ModelState.AddModelError(string.Empty, result.Error ?? "Registration failed. Please verify your details.");
        return View(model);
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> RegisterDoctor()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Index", "Home");
        }

        var model = new DoctorRegisterViewModel
        {
            AvailableSpecializations = await _doctorService.GetSpecializationsAsync()
        };
        return View(model);
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("RegistrationRateLimitPolicy")]
    public async Task<IActionResult> RegisterDoctor(DoctorRegisterViewModel model)
    {
        if (!ModelState.IsValid)
        {
            model.AvailableSpecializations = await _doctorService.GetSpecializationsAsync();
            return View(model);
        }

        var dto = new DoctorRegisterDto
        {
            FullName = model.FullName,
            Email = model.Email,
            Password = model.Password,
            ConfirmPassword = model.ConfirmPassword,
            PhoneNumber = model.PhoneNumber,
            SpecializationId = model.SpecializationId,
            LicenseNumber = model.LicenseNumber,
            ConsultationFee = model.ConsultationFee,
            Governorate = model.Governorate,
            Bio = model.Bio
        };

        var result = await _authService.RegisterDoctorAsync(dto);
        if (result.IsSuccess)
        {
            TempData["InfoMessage"] = "Thank you for registering! Your doctor application has been submitted and is currently pending administrator verification. You will be able to log in once your medical credentials are approved.";
            return RedirectToAction("Login");
        }

        ModelState.AddModelError(string.Empty, result.Error ?? "Doctor registration failed.");
        model.AvailableSpecializations = await _doctorService.GetSpecializationsAsync();
        return View(model);
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Index", "Home");
        }
        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("LoginRateLimitPolicy")]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var dto = new LoginDto
        {
            Email = model.Email,
            Password = model.Password,
            RememberMe = model.RememberMe
        };

        var result = await _authService.LoginAsync(dto);
        if (result.IsSuccess)
        {
            TempData["SuccessMessage"] = "You have successfully signed in.";
            if (!string.IsNullOrEmpty(model.ReturnUrl) && (Url?.IsLocalUrl(model.ReturnUrl) ?? false))
            {
                return Redirect(model.ReturnUrl);
            }

            if (_userManager != null && !string.IsNullOrWhiteSpace(model.Email))
            {
                var user = await _userManager.FindByEmailAsync(model.Email.Trim());
                if (user != null)
                {
                    var roles = await _userManager.GetRolesAsync(user);
                    if (roles.Contains("Admin"))
                    {
                        return RedirectToAction("Index", "Admin");
                    }
                    if (roles.Contains("Doctor"))
                    {
                        return RedirectToAction("Appointments", "Doctor");
                    }
                    if (roles.Contains("Patient"))
                    {
                        return RedirectToAction("Index", "Doctors");
                    }
                }
            }

            return RedirectToAction("Index", "Home");
        }

        ModelState.AddModelError(string.Empty, result.Error ?? "Invalid login attempt.");
        return View(model);
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _authService.LogoutAsync();
        TempData["SuccessMessage"] = "You have been logged out.";
        return RedirectToAction("Index", "Home");
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult AccessDenied()
    {
        return View();
    }

    [HttpGet]
    [Authorize]
    public IActionResult ChangePassword()
    {
        return View(new ChangePasswordViewModel());
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Challenge();
        }

        var result = await _authService.ChangePasswordAsync(userId, model.CurrentPassword, model.NewPassword);
        if (result.IsSuccess)
        {
            TempData["SuccessMessage"] = "Your password has been changed successfully. You may now continue using MediCare.";
            if (User.IsInRole("Pharmacist"))
            {
                return RedirectToAction("Verify", "Prescriptions");
            }
            if (User.IsInRole("Doctor"))
            {
                return RedirectToAction("Index", "Doctor");
            }
            if (User.IsInRole("Admin"))
            {
                return RedirectToAction("Index", "Admin");
            }
            return RedirectToAction("Index", "Home");
        }

        ModelState.AddModelError(string.Empty, result.Error ?? "Failed to change password. Please check your current password.");
        return View(model);
    }

    [HttpGet]
    [Authorize(Roles = "Patient")]
    public async Task<IActionResult> Profile()
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Challenge();
        }

        var result = await _authService.GetPatientProfileAsync(userId);
        if (!result.IsSuccess || result.Value == null)
        {
            TempData["ErrorMessage"] = result.Error ?? "Patient profile not found.";
            return RedirectToAction("Index", "Home");
        }

        var model = new PatientProfileViewModel
        {
            Id = result.Value.Id,
            UserId = result.Value.UserId,
            FullName = result.Value.FullName,
            Email = result.Value.Email,
            PhoneNumber = result.Value.PhoneNumber,
            DateOfBirth = result.Value.DateOfBirth,
            Gender = result.Value.Gender,
            BloodGroup = result.Value.BloodGroup,
            EmergencyContact = result.Value.EmergencyContact,
            Allergies = result.Value.Allergies,
            MedicalHistory = result.Value.MedicalHistory
        };

        return View(model);
    }

    [HttpPost]
    [Authorize(Roles = "Patient")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Profile(PatientProfileViewModel model)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Challenge();
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var dto = new PatientUpdateProfileDto
        {
            FullName = model.FullName,
            PhoneNumber = model.PhoneNumber,
            DateOfBirth = model.DateOfBirth,
            Gender = model.Gender,
            BloodGroup = model.BloodGroup,
            EmergencyContact = model.EmergencyContact,
            Allergies = model.Allergies,
            MedicalHistory = model.MedicalHistory
        };

        var result = await _authService.UpdatePatientProfileAsync(userId, dto);
        if (result.IsSuccess)
        {
            TempData["SuccessMessage"] = "Your medical profile has been updated successfully.";
            return RedirectToAction("Profile");
        }

        ModelState.AddModelError(string.Empty, result.Error ?? "Failed to update profile.");
        return View(model);
    }
}
