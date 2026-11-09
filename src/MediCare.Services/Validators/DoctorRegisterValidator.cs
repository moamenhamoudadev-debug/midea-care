using FluentValidation;
using MediCare.Services.DTOs;

namespace MediCare.Services.Validators;

public class DoctorRegisterValidator : AbstractValidator<DoctorRegisterDto>
{
    public DoctorRegisterValidator()
    {
        RuleFor(x => x.FullName)
            .NotEmpty().WithMessage("Full Name is required.")
            .MinimumLength(3).WithMessage("Full Name must be at least 3 characters long.")
            .MaximumLength(150).WithMessage("Full Name cannot exceed 150 characters.")
            .Matches(@"^[a-zA-Z\u0621-\u064A\u0671-\u06D3\u064B-\u065F\s.'\-]+$")
            .WithMessage("Full Name must contain only letters (Arabic or English) and cannot contain numbers.")
            .Must(n => string.IsNullOrEmpty(n) || !n.Any(char.IsDigit))
            .WithMessage("Full Name cannot contain numbers.")
            .Must(n => string.IsNullOrEmpty(n) || n.Count(c => char.IsLetter(c) || (c >= 0x0621 && c <= 0x064A) || (c >= 0x0671 && c <= 0x06D3)) >= 2)
            .WithMessage("Full Name must contain at least 2 letters.");

        RuleFor(x => x.PhoneNumber)
            .Matches(@"^\+?[0-9]{10,15}$").When(x => !string.IsNullOrWhiteSpace(x.PhoneNumber))
            .WithMessage("Phone number must contain between 10 and 15 digits without letters.");

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("A valid email address is required.")
            .MaximumLength(256).WithMessage("Email cannot exceed 256 characters.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.")
            .MinimumLength(8).WithMessage("Password must be at least 8 characters long.")
            .Matches("[A-Z]").WithMessage("Password must contain at least one uppercase letter.")
            .Matches("[a-z]").WithMessage("Password must contain at least one lowercase letter.")
            .Matches("[0-9]").WithMessage("Password must contain at least one digit.")
            .Matches("[^a-zA-Z0-9]").WithMessage("Password must contain at least one special character.");

        RuleFor(x => x.ConfirmPassword)
            .Equal(x => x.Password).WithMessage("Passwords do not match.");

        RuleFor(x => x.SpecializationId)
            .GreaterThan(0).WithMessage("Please select a valid medical specialization.");

        RuleFor(x => x.LicenseNumber)
            .NotEmpty().WithMessage("Medical license number is required.")
            .MaximumLength(50).WithMessage("License number cannot exceed 50 characters.");

        RuleFor(x => x.ConsultationFee)
            .GreaterThanOrEqualTo(0).WithMessage("Consultation fee cannot be negative.")
            .LessThanOrEqualTo(10000).WithMessage("Consultation fee cannot exceed 10,000 EGP.");

        RuleFor(x => x.Governorate)
            .NotEmpty().WithMessage("Governorate is required.")
            .MaximumLength(100).WithMessage("Governorate cannot exceed 100 characters.");

        RuleFor(x => x.Bio)
            .MaximumLength(1000).WithMessage("Biography cannot exceed 1000 characters.");
    }
}
