using FluentValidation;
using MediCare.Data.Entities;
using MediCare.Data.UnitOfWork;
using MediCare.Services.Common;
using MediCare.Services.Contracts;
using MediCare.Services.DTOs;
using MediCare.Services.Validators;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MediCare.Services.Implementations;

public class AuthService : IAuthService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IUnitOfWork _uow;
    private readonly IValidator<PatientRegisterDto> _patientValidator;
    private readonly IValidator<DoctorRegisterDto> _doctorValidator;
    private readonly IValidator<LoginDto> _loginValidator;
    private readonly IValidator<PatientUpdateProfileDto> _updateProfileValidator;
    private readonly IValidator<ResetPasswordDto> _resetPasswordValidator;
    private readonly IEmailService? _emailService;
    private readonly IOptions<DataProtectionTokenProviderOptions>? _tokenProviderOptions;
    private readonly ILogger<AuthService>? _logger;

    public AuthService(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IUnitOfWork uow,
        IValidator<PatientRegisterDto> patientValidator,
        IValidator<DoctorRegisterDto> doctorValidator,
        IValidator<LoginDto> loginValidator,
        IValidator<PatientUpdateProfileDto>? updateProfileValidator = null,
        IValidator<ResetPasswordDto>? resetPasswordValidator = null,
        IEmailService? emailService = null,
        IOptions<DataProtectionTokenProviderOptions>? tokenProviderOptions = null,
        ILogger<AuthService>? logger = null)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _uow = uow;
        _patientValidator = patientValidator;
        _doctorValidator = doctorValidator;
        _loginValidator = loginValidator;
        _updateProfileValidator = updateProfileValidator ?? new PatientUpdateProfileValidator();
        _resetPasswordValidator = resetPasswordValidator ?? new ResetPasswordValidator();
        _emailService = emailService;
        _tokenProviderOptions = tokenProviderOptions;
        _logger = logger;
    }

    public async Task<Result<string>> RegisterPatientAsync(PatientRegisterDto dto)
    {
        var validation = await _patientValidator.ValidateAsync(dto);
        if (!validation.IsValid)
        {
            return Result<string>.Failure(string.Join("; ", validation.Errors.Select(e => e.ErrorMessage)));
        }

        var existingUser = await _userManager.FindByEmailAsync(dto.Email);
        if (existingUser != null)
        {
            return Result<string>.Failure("An account with this email address already exists.");
        }

        var user = new ApplicationUser
        {
            UserName = dto.Email,
            Email = dto.Email,
            PhoneNumber = dto.PhoneNumber,
            FullName = dto.FullName.Trim(),
            CreatedAt = DateTime.UtcNow
        };

        var createResult = await _userManager.CreateAsync(user, dto.Password);
        if (!createResult.Succeeded)
        {
            return Result<string>.Failure(string.Join("; ", createResult.Errors.Select(e => e.Description)));
        }

        await _userManager.AddToRoleAsync(user, "Patient");

        var patient = new Patient
        {
            UserId = user.Id,
            DateOfBirth = dto.DateOfBirth,
            Gender = dto.Gender,
            BloodGroup = dto.BloodGroup,
            EmergencyContact = dto.EmergencyContact,
            Allergies = dto.Allergies?.Trim(),
            MedicalHistory = dto.MedicalHistory?.Trim()
        };

        await _uow.Patients.AddAsync(patient);
        await _uow.CommitAsync();

        // Patient accounts are immediately active per sprint requirements
        await _signInManager.SignInAsync(user, isPersistent: false);

        return Result<string>.Success(user.Id);
    }

    public async Task<Result<string>> RegisterDoctorAsync(DoctorRegisterDto dto)
    {
        var validation = await _doctorValidator.ValidateAsync(dto);
        if (!validation.IsValid)
        {
            return Result<string>.Failure(string.Join("; ", validation.Errors.Select(e => e.ErrorMessage)));
        }

        var existingUser = await _userManager.FindByEmailAsync(dto.Email);
        if (existingUser != null)
        {
            return Result<string>.Failure("An account with this email address already exists.");
        }

        var spec = await _uow.Specializations.GetByIdAsync(dto.SpecializationId);
        if (spec == null)
        {
            return Result<string>.Failure("The selected medical specialization is invalid.");
        }

        var existingLicense = (await _uow.Doctors.FindAsync(d => d.LicenseNumber == dto.LicenseNumber)).Any();
        if (existingLicense)
        {
            return Result<string>.Failure("A doctor with this license number is already registered.");
        }

        var user = new ApplicationUser
        {
            UserName = dto.Email,
            Email = dto.Email,
            PhoneNumber = dto.PhoneNumber,
            FullName = dto.FullName.Trim(),
            CreatedAt = DateTime.UtcNow
        };

        var createResult = await _userManager.CreateAsync(user, dto.Password);
        if (!createResult.Succeeded)
        {
            return Result<string>.Failure(string.Join("; ", createResult.Errors.Select(e => e.Description)));
        }

        await _userManager.AddToRoleAsync(user, "Doctor");

        var doctor = new Doctor
        {
            UserId = user.Id,
            SpecializationId = dto.SpecializationId,
            LicenseNumber = dto.LicenseNumber.Trim(),
            ConsultationFee = dto.ConsultationFee,
            Governorate = string.IsNullOrWhiteSpace(dto.Governorate) ? "Cairo" : dto.Governorate.Trim(),
            Bio = dto.Bio?.Trim(),
            IsApproved = false, // Pending admin approval
            SlotDurationMinutes = 30
        };

        await _uow.Doctors.AddAsync(doctor);
        await _uow.CommitAsync();

        // Doctor is not signed in automatically; status is Pending until approved
        return Result<string>.Success(user.Id);
    }

    public async Task<Result> LoginAsync(LoginDto dto)
    {
        var validation = await _loginValidator.ValidateAsync(dto);
        if (!validation.IsValid)
        {
            return Result.Failure(string.Join("; ", validation.Errors.Select(e => e.ErrorMessage)));
        }

        var user = await _userManager.FindByEmailAsync(dto.Email);
        if (user == null)
        {
            return Result.Failure("Invalid email or password.");
        }

        // Verify credentials and increment lockout counter without issuing a session cookie yet
        var checkResult = await _signInManager.CheckPasswordSignInAsync(
            user,
            dto.Password,
            lockoutOnFailure: true);

        if (checkResult.IsLockedOut)
        {
            return Result.Failure("This account has been locked out due to multiple failed login attempts. Please try again after 15 minutes.");
        }

        if (!checkResult.Succeeded)
        {
            if (await _userManager.IsInRoleAsync(user, "Admin"))
            {
                _logger?.LogWarning("SECURITY ALERT: Failed login attempt for administrator account {Email} (UserId: {UserId}). IsLockedOut={IsLockedOut}", user.Email, user.Id, checkResult.IsLockedOut);
            }

            return Result.Failure("Invalid email or password.");
        }

        // Credentials verified. If the user is a doctor, verify administrator approval before issuing session cookie.
        if (await _userManager.IsInRoleAsync(user, "Doctor"))
        {
            var doctor = (await _uow.Doctors.FindAsync(d => d.UserId == user.Id)).FirstOrDefault();
            if (doctor != null && !doctor.IsApproved)
            {
                return Result.Failure("Your doctor account is pending administrator approval. You will receive access once verified.");
            }
        }

        // Complete sign-in and issue authentication cookie
        await _signInManager.SignInAsync(user, dto.RememberMe);

        return Result.Success();
    }

    public async Task LogoutAsync()
    {
        await _signInManager.SignOutAsync();
    }

    public async Task<Result<PatientProfileDto>> GetPatientProfileAsync(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Result<PatientProfileDto>.Failure("User ID is required.");
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return Result<PatientProfileDto>.Failure("Patient user account not found.");
        }

        var patients = await _uow.Patients.FindAsync(p => p.UserId == userId);
        var patient = patients.FirstOrDefault();
        if (patient == null)
        {
            return Result<PatientProfileDto>.Failure("Patient profile details not found.");
        }

        var dto = new PatientProfileDto
        {
            Id = patient.Id,
            UserId = user.Id,
            FullName = user.FullName,
            Email = user.Email ?? string.Empty,
            PhoneNumber = user.PhoneNumber,
            DateOfBirth = patient.DateOfBirth,
            Gender = patient.Gender,
            BloodGroup = patient.BloodGroup,
            EmergencyContact = patient.EmergencyContact,
            Allergies = patient.Allergies,
            MedicalHistory = patient.MedicalHistory
        };

        return Result<PatientProfileDto>.Success(dto);
    }

    public async Task<Result> UpdatePatientProfileAsync(string userId, PatientUpdateProfileDto dto)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Result.Failure("User ID is required.");
        }

        var validation = await _updateProfileValidator.ValidateAsync(dto);
        if (!validation.IsValid)
        {
            return Result.Failure(string.Join("; ", validation.Errors.Select(e => e.ErrorMessage)));
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return Result.Failure("Patient user account not found.");
        }

        var patients = await _uow.Patients.FindAsync(p => p.UserId == userId);
        var patient = patients.FirstOrDefault();
        if (patient == null)
        {
            return Result.Failure("Patient profile details not found.");
        }

        user.FullName = dto.FullName.Trim();
        user.PhoneNumber = dto.PhoneNumber?.Trim();
        var updateResult = await _userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            return Result.Failure(string.Join("; ", updateResult.Errors.Select(e => e.Description)));
        }

        patient.DateOfBirth = dto.DateOfBirth;
        patient.Gender = dto.Gender;
        patient.BloodGroup = dto.BloodGroup;
        patient.EmergencyContact = dto.EmergencyContact?.Trim();
        patient.Allergies = dto.Allergies?.Trim();
        patient.MedicalHistory = dto.MedicalHistory?.Trim();

        _uow.Patients.Update(patient);
        await _uow.CommitAsync();

        return Result.Success();
    }

    public async Task<Result> ChangePasswordAsync(string userId, string currentPassword, string newPassword)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Result.Failure("User ID is required.");
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return Result.Failure("User account not found.");
        }

        var result = await _userManager.ChangePasswordAsync(user, currentPassword, newPassword);
        if (!result.Succeeded)
        {
            return Result.Failure(string.Join("; ", result.Errors.Select(e => e.Description)));
        }

        // If user has the MustChangePassword claim, remove it upon successful password change
        var claims = await _userManager.GetClaimsAsync(user);
        var mustChangeClaim = claims.FirstOrDefault(c => c.Type == "MustChangePassword");
        if (mustChangeClaim != null)
        {
            await _userManager.RemoveClaimAsync(user, mustChangeClaim);
        }

        // Refresh sign-in cookie so updated claims take effect immediately
        await _signInManager.RefreshSignInAsync(user);

        _logger?.LogInformation("User {Email} (UserId {UserId}) successfully updated password and cleared MustChangePassword claim", user.Email, user.Id);
        return Result.Success();
    }

    public async Task<Result<string?>> GeneratePasswordResetTokenAsync(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return Result<string?>.Failure("Email address is required.");
        }

        var normalizedEmail = email.Trim();
        var user = await _userManager.FindByEmailAsync(normalizedEmail);
        if (user == null)
        {
            // Strict anti-enumeration defense: return Success(null) so caller performs uniform handling
            _logger?.LogInformation("Password reset requested for non-existent email address (anti-enumeration active)");
            return Result<string?>.Success(null);
        }

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        return Result<string?>.Success(token);
    }

    public async Task<Result> SendPasswordResetEmailAsync(string email, string resetUrl, TimeSpan? tokenLifespan = null)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return Result.Failure("Email address is required.");
        }

        if (string.IsNullOrWhiteSpace(resetUrl))
        {
            return Result.Failure("Reset URL is required.");
        }

        if (_emailService == null)
        {
            _logger?.LogWarning("Email service not available; skipping password reset email dispatch for {Email}", email);
            return Result.Success();
        }

        var normalizedEmail = email.Trim();
        var lifespan = tokenLifespan ?? _tokenProviderOptions?.Value?.TokenLifespan ?? TimeSpan.FromHours(2);
        var lifespanHours = Math.Max(1, (int)Math.Round(lifespan.TotalHours));
        var hoursTextEn = lifespanHours == 1 ? "1 hour" : $"{lifespanHours} hours";
        var hoursTextAr = lifespanHours == 1 ? "ساعة واحدة" : (lifespanHours == 2 ? "ساعتين" : $"{lifespanHours} ساعات");

        var safeUrl = System.Net.WebUtility.HtmlEncode(resetUrl);
        var safeEmail = System.Net.WebUtility.HtmlEncode(normalizedEmail);

        var emailSubject = "MediCare - Password Reset Request | طلب إعادة تعيين كلمة المرور";
        var emailBody = $@"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>MediCare - Password Reset</title>
</head>
<body style=""margin: 0; padding: 0; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; background-color: #f8fafc; color: #1e293b;"">
    <table role=""presentation"" border=""0"" cellpadding=""0"" cellspacing=""0"" width=""100%"" style=""background-color: #f8fafc; padding: 32px 16px;"">
        <tr>
            <td align=""center"">
                <table role=""presentation"" border=""0"" cellpadding=""0"" cellspacing=""0"" width=""100%"" style=""max-width: 600px; background-color: #ffffff; border-radius: 16px; overflow: hidden; box-shadow: 0 4px 6px -1px rgba(0, 0, 0, 0.05), 0 2px 4px -2px rgba(0, 0, 0, 0.05); border: 1px solid #e2e8f0;"">
                    <!-- Brand Header -->
                    <tr>
                        <td style=""background: linear-gradient(135deg, #0d6efd 0%, #0b5ed7 100%); padding: 32px; text-align: center; color: #ffffff;"">
                            <div style=""display: inline-block; background-color: rgba(255, 255, 255, 0.2); width: 48px; height: 48px; line-height: 48px; border-radius: 12px; font-size: 24px; margin-bottom: 12px;"">
                                &#9829;
                            </div>
                            <h1 style=""margin: 0; font-size: 24px; font-weight: 700; letter-spacing: -0.5px;"">MediCare</h1>
                            <p style=""margin: 4px 0 0 0; font-size: 13px; opacity: 0.9; text-transform: uppercase; letter-spacing: 1px;"">Clinic Management System</p>
                        </td>
                    </tr>

                    <!-- English Content -->
                    <tr>
                        <td style=""padding: 32px 32px 16px 32px;"">
                            <h2 style=""margin: 0 0 16px 0; font-size: 20px; font-weight: 600; color: #0f172a;"">Password Reset Request</h2>
                            <p style=""margin: 0 0 16px 0; font-size: 15px; line-height: 1.6; color: #334155;"">
                                Hello,
                            </p>
                            <p style=""margin: 0 0 20px 0; font-size: 15px; line-height: 1.6; color: #334155;"">
                                We received a request to reset the password for your MediCare account (<strong>{safeEmail}</strong>). To proceed with creating a new password, click the button below:
                            </p>
                            <div style=""text-align: center; margin: 28px 0;"">
                                <a href=""{safeUrl}"" style=""display: inline-block; background-color: #0d6efd; color: #ffffff; text-decoration: none; padding: 14px 28px; border-radius: 8px; font-size: 15px; font-weight: 600; box-shadow: 0 2px 4px rgba(13, 110, 253, 0.2);"">
                                    Reset My Password
                                </a>
                            </div>
                            <p style=""margin: 0 0 12px 0; font-size: 13px; line-height: 1.5; color: #64748b;"">
                                <strong>Security Notice:</strong> This link is single-use and will expire in <strong>{hoursTextEn}</strong>. If you did not request a password reset, no action is needed; your account remains completely secure.
                            </p>
                            <div style=""background-color: #f1f5f9; padding: 12px 16px; border-radius: 8px; font-size: 12px; color: #64748b; word-break: break-all; margin: 16px 0 0 0;"">
                                <span>If the button above does not work, copy and paste this link into your browser:</span><br/>
                                <a href=""{safeUrl}"" style=""color: #0d6efd;"">{safeUrl}</a>
                            </div>
                        </td>
                    </tr>

                    <!-- Divider -->
                    <tr>
                        <td style=""padding: 0 32px;"">
                            <hr style=""border: none; border-top: 1px solid #e2e8f0; margin: 16px 0;"">
                        </td>
                    </tr>

                    <!-- Arabic Content -->
                    <tr>
                        <td dir=""rtl"" style=""padding: 16px 32px 32px 32px; text-align: right;"">
                            <h2 style=""margin: 0 0 16px 0; font-size: 20px; font-weight: 600; color: #0f172a;"">طلب إعادة تعيين كلمة المرور</h2>
                            <p style=""margin: 0 0 16px 0; font-size: 15px; line-height: 1.7; color: #334155;"">
                                مرحباً بك،
                            </p>
                            <p style=""margin: 0 0 20px 0; font-size: 15px; line-height: 1.7; color: #334155;"">
                                تلقينا طلباً لإعادة تعيين كلمة المرور الخاصة بحسابك في ميديكير (<strong>{safeEmail}</strong>). لتعيين كلمة مرور جديدة، يرجى الضغط على الزر أدناه:
                            </p>
                            <div style=""text-align: center; margin: 24px 0;"">
                                <a href=""{safeUrl}"" style=""display: inline-block; background-color: #0d6efd; color: #ffffff; text-decoration: none; padding: 14px 28px; border-radius: 8px; font-size: 15px; font-weight: 600;"">
                                    إعادة تعيين كلمة المرور
                                </a>
                            </div>
                            <p style=""margin: 0; font-size: 13px; line-height: 1.6; color: #64748b;"">
                                <strong>تنبيه أمان:</strong> صلاحية هذا الرابط تنتهي خلال <strong>{hoursTextAr}</strong> للاستخدام لمرة واحدة فقط. إذا لم تكن قد طلبت تغيير كلمة المرور، يمكنك تجاهل هذه الرسالة بأمان فحسابك محمي بالكامل.
                            </p>
                        </td>
                    </tr>

                    <!-- Footer -->
                    <tr>
                        <td style=""background-color: #f8fafc; padding: 24px 32px; text-align: center; border-top: 1px solid #e2e8f0; font-size: 12px; color: #94a3b8;"">
                            <p style=""margin: 0 0 4px 0; font-weight: 500;"">MediCare Healthcare Management Platform</p>
                            <p style=""margin: 0;"">This is an automated notification. Please do not reply directly to this email.</p>
                        </td>
                    </tr>
                </table>
            </td>
        </tr>
    </table>
</body>
</html>";

        try
        {
            var sent = await _emailService.SendEmailAsync(normalizedEmail, emailSubject, emailBody);
            if (!sent)
            {
                _logger?.LogWarning("Email provider returned failure while dispatching password reset email for user account.");
            }
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Exception occurred while dispatching password reset email for user account.");
            return Result.Success(); // Non-blocking: anti-enumeration and client stability
        }
    }

    public async Task<Result> ResetPasswordAsync(ResetPasswordDto dto)
    {
        if (dto == null)
        {
            return Result.Failure("Invalid request.");
        }

        var validation = await _resetPasswordValidator.ValidateAsync(dto);
        if (!validation.IsValid)
        {
            return Result.Failure(string.Join("; ", validation.Errors.Select(e => e.ErrorMessage)));
        }

        var user = await _userManager.FindByEmailAsync(dto.Email.Trim());
        if (user == null)
        {
            // Generic anti-enumeration response
            return Result.Failure("The password reset token is invalid or has expired.");
        }

        var resetResult = await _userManager.ResetPasswordAsync(user, dto.Token, dto.NewPassword);
        if (!resetResult.Succeeded)
        {
            var errors = resetResult.Errors.Select(e =>
                e.Code == "InvalidToken"
                    ? "The password reset token is invalid or has expired."
                    : e.Description);
            return Result.Failure(string.Join("; ", errors));
        }

        // Invalidate any active authentication cookies/sessions
        await _userManager.UpdateSecurityStampAsync(user);

        // If the user has a temporary MustChangePassword claim, clear it
        var claims = await _userManager.GetClaimsAsync(user);
        var mustChangeClaim = claims.FirstOrDefault(c => c.Type == "MustChangePassword");
        if (mustChangeClaim != null)
        {
            await _userManager.RemoveClaimAsync(user, mustChangeClaim);
        }

        _logger?.LogInformation("Password reset successfully completed for user {UserId}", user.Id);
        return Result.Success();
    }
}
