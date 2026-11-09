using FluentValidation;
using MediCare.Services.Common;
using MediCare.Services.DTOs;

namespace MediCare.Services.Validators;

public class PatientUpdateProfileValidator : AbstractValidator<PatientUpdateProfileDto>
{
    private static readonly string[] AllowedBloodGroups = { "A+", "A-", "B+", "B-", "AB+", "AB-", "O+", "O-" };
    private static readonly string[] AllowedGenders = { "Male", "Female" };

    public PatientUpdateProfileValidator(IClinicClock? clinicClock = null)
    {
        var clock = clinicClock ?? new ClinicClock();

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

        RuleFor(x => x.DateOfBirth)
            .NotEmpty().WithMessage("Date of Birth is required.")
            .LessThan(_ => clock.Today).WithMessage("Date of Birth must be in the past.");

        RuleFor(x => x.Gender)
            .NotEmpty().WithMessage("Gender is required.")
            .Must(g => AllowedGenders.Contains(g)).WithMessage("Gender must be either 'Male' or 'Female'.");

        RuleFor(x => x.BloodGroup)
            .Must(bg => string.IsNullOrEmpty(bg) || AllowedBloodGroups.Contains(bg))
            .WithMessage("Blood Group must be a valid blood type (e.g. A+, O-, B+).");

        RuleFor(x => x.EmergencyContact)
            .MaximumLength(50).WithMessage("Emergency contact cannot exceed 50 characters.")
            .Matches(@"^\+?[0-9]{10,15}$").When(x => !string.IsNullOrWhiteSpace(x.EmergencyContact))
            .WithMessage("Emergency contact must be a valid phone number with 10 to 15 digits.");

        RuleFor(x => x.Allergies)
            .MaximumLength(500).WithMessage("Allergies cannot exceed 500 characters.");

        RuleFor(x => x.MedicalHistory)
            .MaximumLength(1000).WithMessage("Medical History cannot exceed 1000 characters.");
    }
}
